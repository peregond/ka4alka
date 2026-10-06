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
    public DownloadService(EngineSettings? settings=null)
    {
        customSettings=settings;
        if(!File.Exists(queuePath))return;
        foreach(var item in JsonSerializer.Deserialize<List<DownloadItem>>(File.ReadAllText(queuePath))??[]){if(!item.Paused)pendingResume.Add(item.Id);item.Paused=true;item.Busy=false;item.Status="На паузе";item.Stats=$"{item.Progress:F1}% · на паузе";item.Hint="";Items.Add(item);}
    }
    ClientEngine Engine => engine ??= new ClientEngine(customSettings ?? new EngineSettingsBuilder {
        CacheDirectory=Path.Combine(Preferences.DataDir,"cache"), MaximumConnections=50, DiskCacheBytes=4*1024*1024,
        AutoSaveLoadFastResume=true,AutoSaveLoadMagnetLinkMetadata=true,AllowPortForwarding=false
    }.ToSettings());
    public void Save()
    {
        Directory.CreateDirectory(Preferences.DataDir);
        File.WriteAllText(queuePath+".tmp",JsonSerializer.Serialize(Items));File.Move(queuePath+".tmp",queuePath,true);
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
    public async Task Add(string source,string folder)
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
            var item=new DownloadItem{Source=source,Folder=folder,Name=name,InfoHash=identity};
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
    public async Task Toggle(DownloadItem item)
    {
        if(item.Busy)return;pendingResume.Remove(item.Id);item.Busy=true;item.Refresh();
        await gate.WaitAsync();
        try{var manager=await Manager(item);if(item.Paused){await manager.StartAsync();item.Paused=false;item.LastStartedUtc=DateTime.UtcNow;}else{await StopManager(manager);item.Paused=true;}Update();Save();await ReleaseIdleEngine();}
        catch(Exception error){item.Status="Ошибка: "+error.Message;item.Refresh();Save();throw;}
        finally{item.Busy=false;item.Refresh();gate.Release();}
    }
    public async Task Remove(DownloadItem item)
    {
        if(item.Busy)return;item.Busy=true;item.Refresh();await gate.WaitAsync();
        try{pendingResume.Remove(item.Id);if(managers.TryGetValue(item.Id,out var manager)){await StopManager(manager);await Engine.RemoveAsync(manager);managers.Remove(item.Id);meters.Remove(item.Id);}Items.Remove(item);Save();await ReleaseIdleEngine();}
        finally{item.Busy=false;item.Refresh();gate.Release();}
    }
    public void Update()
    {
        var now=DateTime.UtcNow;
        foreach(var item in Items){if(!managers.TryGetValue(item.Id,out var m))continue;
            item.Name=m.Torrent?.Name??item.Name;
            var meter=meters[item.Id];var rate=meter.Down.Sample(now,item.LastStartedUtc,m.Monitor.DataBytesReceived);
            var upload=meter.Up.Sample(now,item.LastStartedUtc,m.Monitor.DataBytesSent);
            DownloadPresentation.Apply(item,new(m.State,item.Paused,m.Progress,m.Torrent?.Size,rate,upload,m.OpenConnections,m.Peers.Seeds,item.LastStartedUtc,meter.Down.LastPayloadUtc,m.Error?.Exception?.Message),now);
            item.Refresh();
        }
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
