using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using MonoTorrent;
using MonoTorrent.Client;
namespace Kachalka;
public sealed class DownloadService
{
    ClientEngine? engine;
    readonly Dictionary<string,TorrentManager> managers=[];
    readonly Dictionary<string,(PayloadMeter Down,PayloadMeter Up)> meters=[];
    readonly HashSet<string> pendingResume=[];
    readonly SemaphoreSlim gate=new(1,1);
    readonly string queuePath=Path.Combine(Preferences.DataDir,"queue.json");
    public ObservableCollection<DownloadItem> Items {get;}=[];
    public bool EngineCreated => engine!=null;
    readonly EngineSettings? customSettings;
    public int DownloadLimitKbps {get;private set;}
    public int UploadLimitKbps {get;private set;}
    public DownloadService(EngineSettings? settings=null,int? downloadLimitKbps=null,int? uploadLimitKbps=null)
    {
        customSettings=settings;
        DownloadLimitKbps=Math.Clamp(downloadLimitKbps??settings?.MaximumDownloadRate/1024??0,0,int.MaxValue/1024);
        UploadLimitKbps=Math.Clamp(uploadLimitKbps??settings?.MaximumUploadRate/1024??0,0,int.MaxValue/1024);
        if(!File.Exists(queuePath))return;
        var restored=JsonSerializer.Deserialize<List<DownloadItem>>(File.ReadAllText(queuePath))??[];
        if(DownloadOrdering.AssignMissingAddedUtc(restored,File.GetLastWriteTimeUtc(queuePath)))SaveQueue(restored);
        foreach(var item in restored)
        {
            if(!item.Paused)pendingResume.Add(item.Id);
            item.Paused=true;item.Busy=false;item.Status="На паузе";item.Stats=$"{item.Progress:F1}% · на паузе";item.Hint="";
            item.DownloadRate=0;item.UploadRate=0;item.TotalBytes=KnownTotal(item.Files);Items.Add(item);
        }
    }
    ClientEngine Engine
    {
        get
        {
            if(engine!=null)return engine;
            var builder=customSettings!=null?new EngineSettingsBuilder(customSettings):new EngineSettingsBuilder {
                CacheDirectory=Path.Combine(Preferences.DataDir,"cache"), MaximumConnections=50, DiskCacheBytes=4*1024*1024,
                AutoSaveLoadFastResume=true,AutoSaveLoadMagnetLinkMetadata=true,AllowPortForwarding=false
            };
            builder.MaximumDownloadRate=DownloadLimitKbps*1024;builder.MaximumUploadRate=UploadLimitKbps*1024;
            return engine=new ClientEngine(builder.ToSettings());
        }
    }
    public async Task SetLimitsAsync(int downloadKbps,int uploadKbps)
    {
        if(downloadKbps<0||uploadKbps<0||downloadKbps>int.MaxValue/1024||uploadKbps>int.MaxValue/1024)throw new ArgumentOutOfRangeException(nameof(downloadKbps),"Укажи скорость от 0 до 2097151 КБ/с.");
        await gate.WaitAsync();
        try
        {
            if(engine!=null){var builder=new EngineSettingsBuilder(engine.Settings){MaximumDownloadRate=downloadKbps*1024,MaximumUploadRate=uploadKbps*1024};await engine.UpdateSettingsAsync(builder.ToSettings());}
            DownloadLimitKbps=downloadKbps;UploadLimitKbps=uploadKbps;
        }
        finally{gate.Release();}
    }
    public async Task<int> SetGroupAsync(bool completed,bool paused)
    {
        Update();int changed=0;
        foreach(var item in Items.ToArray().Where(x=>x.Completed==completed))if(!item.Busy&&item.Paused!=paused){await SetPausedAsync(item,paused);changed++;}
        return changed;
    }
    public void Save()=>SaveQueue(Items);
    void SaveQueue(IEnumerable<DownloadItem> items)
    {
        Directory.CreateDirectory(Preferences.DataDir);
        File.WriteAllText(queuePath+".tmp",JsonSerializer.Serialize(items));File.Move(queuePath+".tmp",queuePath,true);
    }
    public int PendingResumeCount=>pendingResume.Count;
    public async Task<int> ResumePendingAsync()
    {
        var restored=0;
        foreach(var id in pendingResume.ToArray())
        {
            pendingResume.Remove(id);
            var item=Items.FirstOrDefault(x=>x.Id==id);
            if(item==null||!item.Paused)continue;
            try{await Toggle(item);restored++;}
            catch(Exception error){item.Status="Не удалось продолжить: "+error.Message;item.Refresh();Save();}
        }
        return restored;
    }
    static string HashKey(InfoHashes hashes)=>hashes.V1?.ToHex()??hashes.V2?.ToHex()??"";
    static string? ExistingHash(DownloadItem item)
    {
        if(!string.IsNullOrWhiteSpace(item.InfoHash))return item.InfoHash;
        try{return item.Source.StartsWith("magnet:",StringComparison.OrdinalIgnoreCase)?HashKey(MagnetLink.Parse(item.Source).InfoHashes):File.Exists(item.Source)?HashKey(Torrent.Load(item.Source).InfoHashes):null;}
        catch{return null;}
    }
    async Task<TorrentManager> Manager(DownloadItem item)
    {
        if(managers.TryGetValue(item.Id,out var current))return current;
        var settings=new TorrentSettingsBuilder{MaximumConnections=30}.ToSettings();
        var manager=item.Source.StartsWith("magnet:",StringComparison.OrdinalIgnoreCase)
          ? await Engine.AddAsync(MagnetLink.Parse(item.Source),item.Folder,settings)
          : await Engine.AddAsync(item.Source,item.Folder,settings);
        managers[item.Id]=manager;meters[item.Id]=(new(),new());return manager;
    }
    public async Task Add(string source,string folder,MediaItem? media=null,string? imageUrl=null,SourceEntry? release=null)
    {
        await gate.WaitAsync();
        try {
            source=source.Trim();
            string name="Получение метаданных…";
            string identity;
            if(source.StartsWith("magnet:",StringComparison.OrdinalIgnoreCase)){MagnetLink magnet;try{magnet=MagnetLink.Parse(source);}catch(Exception e)when(e is ArgumentException or FormatException){throw new FormatException("Некорректная magnet-ссылка. Проверь полный адрес раздачи.",e);}name=string.IsNullOrWhiteSpace(magnet.Name)?name:magnet.Name;identity=HashKey(magnet.InfoHashes);}
            else {
                if(!File.Exists(source)||new FileInfo(source).Length>10*1024*1024)throw new InvalidOperationException("Выбери .torrent-файл размером до 10 МБ.");
                var torrent=await Torrent.LoadAsync(source);name=torrent.Name;identity=HashKey(torrent.InfoHashes);
                var folderCache=Path.Combine(Preferences.DataDir,"torrents");Directory.CreateDirectory(folderCache);
                var cached=Path.Combine(folderCache,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(source)))+".torrent");
                if(!File.Exists(cached))File.Copy(source,cached);source=cached;
            }
            if(Items.Any(x=>x.Source==source||identity.Length>0&&string.Equals(ExistingHash(x),identity,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Эта раздача уже в очереди.");
            Directory.CreateDirectory(folder);
            var item=new DownloadItem
            {
                Source=source,Folder=folder,Name=name,InfoHash=identity,AddedUtc=DateTime.UtcNow,
                ImageUrl=!string.IsNullOrWhiteSpace(media?.ImageUrl)?media.ImageUrl:!string.IsNullOrWhiteSpace(imageUrl)?imageUrl:null,
                MediaTitle=media?.Title,MediaSection=media?.Section,MediaPageUrl=media?.PageUrl,MediaYear=media?.Year??0,
                ReleaseTitle=release?.Title,ReleaseSource=release?.Source,ReleaseId=release?.Id,ReleasePageUrl=release?.PageUrl,ReleaseUrl=release?.TorrentUrl
            };
            TorrentManager? manager=null;
            try
            {
                manager=await Manager(item);
                await manager.StartAsync();item.Paused=false;item.LastStartedUtc=DateTime.UtcNow;meters[item.Id].Down.Sample(item.LastStartedUtc,item.LastStartedUtc,manager.Monitor.DataBytesReceived);meters[item.Id].Up.Sample(item.LastStartedUtc,item.LastStartedUtc,manager.Monitor.DataBytesSent);item.Status="Поиск участников…";item.Refresh();
                Items.Add(item);Save();
            }
            catch
            {
                Items.Remove(item);
                if(manager!=null){try{await manager.StopAsync();await Engine.RemoveAsync(manager);}catch{}managers.Remove(item.Id);meters.Remove(item.Id);}
                await ReleaseIdleEngine();
                throw;
            }
        }finally{gate.Release();}
    }
    public Task Toggle(DownloadItem item)=>SetPausedAsync(item,!item.Paused);
    async Task SetPausedAsync(DownloadItem item,bool paused)
    {
        if(item.Busy)return;pendingResume.Remove(item.Id);item.Busy=true;item.Refresh();
        await gate.WaitAsync();
        try
        {
            if(!Items.Contains(item)||item.Paused==paused)return;
            var manager=await Manager(item);
            if(!paused){await manager.StartAsync();item.Paused=false;item.LastStartedUtc=DateTime.UtcNow;}
            else{await StopManager(manager);item.Paused=true;}
            Update();Save();await ReleaseIdleEngine();
        }
        catch(Exception error){item.DownloadRate=0;item.UploadRate=0;item.Status="Ошибка: "+error.Message;item.Refresh();Save();throw;}
        finally{item.Busy=false;item.Refresh();gate.Release();}
    }
    public async Task Remove(DownloadItem item,bool deleteFiles=false)
    {
        if(item.Busy)return;item.Busy=true;item.Refresh();await gate.WaitAsync();
        try
        {
            pendingResume.Remove(item.Id);
            if(managers.TryGetValue(item.Id,out var manager)){await StopManager(manager);item.Paused=true;SnapshotFiles(item,manager);item.DownloadRate=0;item.UploadRate=0;item.Remaining="";item.PeersText="";item.Indeterminate=false;item.Status="На паузе";await Engine.RemoveAsync(manager);managers.Remove(item.Id);meters.Remove(item.Id);}
            if(deleteFiles)
            {
                var paths=item.Files.SelectMany(f=>new[]{f.FullPath,f.IncompletePath}).Where(p=>p.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                foreach(var path in paths)
                {
                    DownloadFiles.ValidatePath(item.Folder,path);
                    if(Items.Where(x=>x!=item).SelectMany(x=>x.Files).Any(f=>string.Equals(f.FullPath,path,StringComparison.OrdinalIgnoreCase)||string.Equals(f.IncompletePath,path,StringComparison.OrdinalIgnoreCase)))throw new IOException("Файл используется другой задачей. Сначала убери её из очереди.");
                }
                foreach(var path in paths)File.Delete(path);
                foreach(var path in paths)DownloadFiles.RemoveEmptyParents(item.Folder,path);
            }
            Items.Remove(item);Save();await ReleaseIdleEngine();
        }
        catch{Save();await ReleaseIdleEngine();throw;}
        finally{item.Busy=false;item.Refresh();gate.Release();}
    }
    public void Update()
    {
        var now=DateTime.UtcNow;
        foreach(var item in Items){if(!managers.TryGetValue(item.Id,out var m))continue;
            item.Name=m.Torrent?.Name??item.Name;SnapshotFiles(item,m);
            var meter=meters[item.Id];var rate=meter.Down.Sample(now,item.LastStartedUtc,m.Monitor.DataBytesReceived);
            var upload=meter.Up.Sample(now,item.LastStartedUtc,m.Monitor.DataBytesSent);
            DownloadPresentation.Apply(item,new(m.State,item.Paused,m.Progress,m.Torrent?.Size,rate,upload,m.OpenConnections,m.Peers.Seeds,item.LastStartedUtc,meter.Down.LastPayloadUtc,m.Error?.Exception?.Message),now);
            item.Refresh();
        }
    }
    static void SnapshotFiles(DownloadItem item,TorrentManager manager)
    {
        if(manager.Files.Count>0)item.Files=manager.Files.Select(f=>new DownloadFile(f.Path,f.DownloadCompleteFullPath,f.DownloadIncompleteFullPath,f.Length,f.BitField.PercentComplete)).ToList();
    }
    static long? KnownTotal(IReadOnlyList<DownloadFile> files)
    {
        if(files.Count==0)return null;
        long total=0;
        foreach(var file in files)
        {
            if(file.Size<0||total>long.MaxValue-file.Size)return null;
            total+=file.Size;
        }
        return total;
    }
    static async Task StopManager(TorrentManager manager)
    {
        for(int attempt=0;attempt<4;attempt++){await manager.StopAsync();await Task.Delay(50);if(manager.State==TorrentState.Stopped)return;}
        throw new InvalidOperationException("Раздача ещё завершает операцию. Повтори остановку через несколько секунд.");
    }
    public async Task Close()
    {
        await gate.WaitAsync();try{if(engine!=null){await engine.StopAllAsync();Update();Save();engine.Dispose();engine=null;}}finally{gate.Release();}
    }
    async Task ReleaseIdleEngine(){if(engine!=null&&Items.All(x=>x.Paused)){await engine.StopAllAsync();engine.Dispose();engine=null;managers.Clear();meters.Clear();}}
    public static string FormatBytes(long bytes){string[] units=["Б","КБ","МБ","ГБ"];double value=bytes;int i=0;while(value>=1024&&i<3){i++;value/=1024;}return $"{value:F1} {units[i]}";}
}
