using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Kachalka;

// Public metadata is refreshed independently of application releases.
public sealed class SharedCatalog
{
    public static readonly Uri FeedUri=new("https://github.com/peregond/ka4alka/releases/download/catalog-data/catalog.json");
    public const int MaxBytes=32*1024*1024;
    static readonly TimeSpan RefreshInterval=TimeSpan.FromDays(7);
    sealed record SyncState(DateTime LastCheckedUtc);
    readonly SourceClient client;
    readonly Func<DateTime> clock;
    readonly object state=new();
    readonly SemaphoreSlim reader=new(1,1);
    readonly string path=Path.Combine(Preferences.DataDir,"catalog-shared.json");
    readonly string syncPath=Path.Combine(Preferences.DataDir,"catalog-shared-sync.json");
    MediaItem[]? items;
    SeasonalCollection[]? collections;
    DateTime generatedUtc,lastCheckedUtc,retryAfterUtc;
    string? snapshotHash;
    Task? restoreTask;
    Task<bool>? refreshTask;
    int generation;

    public SharedCatalog(SourceClient client,Func<DateTime>? utcNow=null)
    {
        this.client=client;clock=utcNow??(()=>DateTime.UtcNow);
    }
    bool Due(DateTime now)=>now>=retryAfterUtc&&(lastCheckedUtc==default||lastCheckedUtc>now||now-lastCheckedUtc>=RefreshInterval);
    public bool RefreshDue {get{lock(state)return Due(clock());}}
    public DateTime LastCheckedUtc {get{lock(state)return lastCheckedUtc;}}
    public IReadOnlyList<MediaItem> All=>items??BundledCatalog.All;
    public IReadOnlyList<SeasonalCollection> Collections=>collections??BundledCatalog.Collections;
    public IReadOnlyList<SeasonalCollection> ActiveCollections=>Collections.Where(x=>x.Active(clock())).ToArray();
    static SeasonalCollection[] ParseCollections(byte[] bytes)
    {
        using var json=JsonDocument.Parse(bytes);return SeasonalCatalog.Parse(json.RootElement);
    }
    public MediaItem? Find(MediaItem item)=>All.FirstOrDefault(row=>row.Id==item.Id&&row.Section==item.Section);
    public static DateTime BoundaryUtc(DateTime now)
    {
        now=now.Kind==DateTimeKind.Local?now.ToUniversalTime():DateTime.SpecifyKind(now,DateTimeKind.Utc);
        var boundary=now.Date.AddHours(5);return now<boundary?boundary.AddDays(-1):boundary;
    }
    public static (DateTime GeneratedUtc,MediaItem[] Items) Parse(byte[] bytes,DateTime now)
    {
        if(bytes.Length>MaxBytes)throw new InvalidDataException("Общий каталог слишком большой.");
        using var json=JsonDocument.Parse(bytes);var root=json.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=1||!root.GetProperty("generatedAtUtc").TryGetDateTime(out var generated)||generated.Kind!=DateTimeKind.Utc||generated>now.AddMinutes(15)||generated<new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc))throw new InvalidDataException("Неверная дата общего каталога.");
        var rows=root.GetProperty("items");if(rows.ValueKind!=JsonValueKind.Array||rows.GetArrayLength()>15000)throw new InvalidDataException("Общий каталог слишком большой.");
        var parsed=rows.EnumerateArray().Where(row=>row.ValueKind==JsonValueKind.Object).Select(row=>OnlineIndexClient.Media(row,row.TryGetProperty("section",out var section)&&section.GetString()=="movies"?"Фильмы":"Сериалы")).OfType<MediaItem>()
            .Where(item=>item.Year is >=0 and <=2100).DistinctBy(item=>(item.Section,item.Id)).ToArray();
        if(parsed.Count(item=>item.Section=="Фильмы")<2000||parsed.Count(item=>item.Section=="Сериалы")<300)throw new InvalidDataException("Общий каталог неполный.");
        return (generated,parsed.OrderByDescending(item=>Math.Min(item.Year,now.Year)).ToArray());
    }
    public IReadOnlyList<MediaItem> Search(string section,string query)
    {
        CacheFiles.Touch(path);static string Normalize(string value)=>System.Text.RegularExpressions.Regex.Replace(value.Replace('ё','е').Replace('Ё','Е').ToLowerInvariant(),@"[^\p{L}\p{N}]+"," ").Trim();
        var words=Normalize(query).Split(' ',StringSplitOptions.RemoveEmptyEntries);
        return All.Where(item=>item.Section==section&&words.All(word=>Normalize(item.Title+" "+item.OriginalTitle+" "+item.Year).Split(' ').Any(part=>part.StartsWith(word,StringComparison.Ordinal)))).Take(80).ToArray();
    }
    // Navigation cancellation stops only its wait, never the shared disk load or refresh.
    public Task RestoreAsync(CancellationToken ct=default)
    {
        Task restoring;
        lock(state)restoring=restoreTask??=RestoreCoreAsync(generation);
        return restoring.WaitAsync(ct);
    }
    async Task RestoreCoreAsync(int version)
    {
        await Task.Yield();await reader.WaitAsync();
        try
        {
            var now=clock();MediaItem[]? savedItems=null;SeasonalCollection[]? savedCollections=null;DateTime savedGenerated=default,savedChecked=default;string? savedHash=null;
            try
            {
                if(File.Exists(path)&&new FileInfo(path).Length<=MaxBytes)
                {
                    var bytes=await CacheFiles.ReadAllBytesAsync(path,CancellationToken.None);
                    var saved=await Task.Run(()=>
                    {
                        var parsed=Parse(bytes,now);
                        return (parsed.GeneratedUtc,parsed.Items,Collections:ParseCollections(bytes),Hash:Convert.ToHexString(SHA256.HashData(bytes)));
                    });
                    savedCollections=saved.Collections;savedItems=saved.Items;savedGenerated=saved.GeneratedUtc;savedHash=saved.Hash;
                    if(File.Exists(syncPath)&&new FileInfo(syncPath).Length<=1024)
                    {
                        var sync=JsonSerializer.Deserialize<SyncState>(await CacheFiles.ReadAllTextAsync(syncPath));
                        if(sync is {}&&sync.LastCheckedUtc.Kind==DateTimeKind.Utc&&sync.LastCheckedUtc<=now&&sync.LastCheckedUtc>=new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc))savedChecked=sync.LastCheckedUtc;
                    }
                }
            }
            catch(Exception error)when(error is IOException or InvalidDataException or JsonException or InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException){}
            if(savedItems==null)await BundledCatalog.EnsureLoadedAsync();
            lock(state)
            {
                if(version!=generation)return;
                items=savedItems;collections=savedCollections;generatedUtc=savedGenerated;lastCheckedUtc=savedChecked;snapshotHash=savedHash;
            }
        }
        finally{reader.Release();}
    }
    // All callers join one refresh; a force request bypasses the weekly/offline gates.
    public async Task<bool> RefreshAsync(CancellationToken ct=default,bool force=false)
    {
        await RestoreAsync(ct);Task<bool> refreshing;
        lock(state)
        {
            if(refreshTask is {IsCompleted:false})refreshing=refreshTask;
            else
            {
                var now=clock();if(!force&&!Due(now))return false;
                retryAfterUtc=now.AddMinutes(30);refreshing=refreshTask=RefreshCoreAsync(generation);
            }
        }
        return await refreshing.WaitAsync(ct);
    }
    async Task<bool> RefreshCoreAsync(int version)
    {
        await Task.Yield();
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var bytes=await TextCatalogDownload.DownloadAsync((uri,maximum,token)=>client.Read(uri,maximum,token),timeout.Token);
            var fresh=await Task.Run(()=>
            {
                var parsed=Parse(bytes,clock());
                return (parsed.GeneratedUtc,parsed.Items,Collections:ParseCollections(bytes),Hash:Convert.ToHexString(SHA256.HashData(bytes)));
            });
            var freshCollections=fresh.Collections;var hash=fresh.Hash;
            await reader.WaitAsync();
            try
            {
                DateTime checkedUtc;bool changed;
                lock(state)
                {
                    if(version!=generation||fresh.GeneratedUtc<generatedUtc)return false;
                    checkedUtc=clock();changed=hash!=snapshotHash;
                }
                var persisted=false;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await CacheFiles.WriteAllBytesAsync(path,bytes,CancellationToken.None);persisted=true;
                }
                catch(IOException){}catch(UnauthorizedAccessException){}
                // A failed snapshot write must not postpone repairing the old disk copy after restart.
                if(persisted)try{await CacheFiles.WriteAllTextAsync(syncPath,JsonSerializer.Serialize(new SyncState(checkedUtc)));}catch(IOException){}catch(UnauthorizedAccessException){}
                lock(state)
                {
                    if(version!=generation)return false;
                    items=fresh.Items;collections=freshCollections;generatedUtc=fresh.GeneratedUtc;snapshotHash=hash;lastCheckedUtc=checkedUtc;retryAfterUtc=default;
                }
                return changed;
            }
            finally{reader.Release();}
        }
        catch(Exception){return false;}
    }
    public async Task ClearMemoryAsync()
    {
        await reader.WaitAsync();
        try
        {
            lock(state)
            {
                generation++;items=null;collections=null;generatedUtc=default;lastCheckedUtc=default;retryAfterUtc=default;snapshotHash=null;restoreTask=null;refreshTask=null;
            }
        }
        finally{reader.Release();}
    }
    public async Task<CatalogPage?> PageAsync(string section,int page,CancellationToken ct,bool force=false)
    {
        CacheFiles.Touch(path);await RestoreAsync(ct);ct.ThrowIfCancellationRequested();
        // The previous complete snapshot or the embedded seed is available while the feed is checked.
        _=RefreshAsync(force:force);
        var rows=All.Where(item=>item.Section==section).ToArray();var offset=(Math.Clamp(page,1,CatalogPaging.Limit)-1)*CatalogPaging.Size;
        return new(rows.Skip(offset).Take(CatalogPaging.Size).ToArray(),offset+CatalogPaging.Size<rows.Length,[],[]);
    }
}
