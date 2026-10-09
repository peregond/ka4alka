using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Kachalka;

// A regional page keeps the source's pagination. Unclassified titles are never
// labelled foreign, and loading their countries does not block ordinary lists.
public sealed class RegionalCatalog(SourceClient client)
{
    record CountrySnapshot(string PageUrl,string Title,int Year,string Country,string[] CountryKeys,DateTime SavedUtc);
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string,Lazy<Task<MediaItem?>>> requests=new();
    static readonly SemaphoreSlim slots=new(2,2);
    static string Key(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string CachePath(MediaItem item)=>Path.Combine(Preferences.DataDir,"regions",Key(item.PageUrl??item.Id.ToString())+".json");
    static string DetailPath(MediaItem item)=>Path.Combine(Preferences.DataDir,"details",Key(item.PageUrl!)+".json");

    public static async Task<MediaItem[]> WithCachedCountries(IEnumerable<MediaItem> items,CancellationToken ct)
    {
        var result=new List<MediaItem>();
        foreach(var item in items)
        {
            ct.ThrowIfCancellationRequested();
            result.Add(await Cached(item,ct)??item);
        }
        return result.ToArray();
    }
    static async Task<MediaItem?> Cached(MediaItem item,CancellationToken ct)
    {
        if(item.CountryKeysComplete&&CatalogRegions.CountryKeys(item.CountryKeys).Length>0)return item;
        var url=CinemaMetadata.CatalogUrl(item.PageUrl,item.Section=="Сериалы"?"/tvseries/":"/movies/");
        if(url==null)return null;
        try
        {
            var path=CachePath(item);
            if(new FileInfo(path).Length<=32*1024)
            {
                var saved=JsonSerializer.Deserialize<CountrySnapshot>(await CacheFiles.ReadAllTextAsync(path,ct));
                if(saved is {CountryKeys:not null}&&saved.PageUrl==url&&saved.Title==item.Title&&saved.Year==item.Year&&DateTime.UtcNow-saved.SavedUtc<TimeSpan.FromHours(saved.CountryKeys.Length>0?168:1))
                    return item with{Country=saved.Country??"",CountryKeys=CatalogRegions.CountryKeys(saved.CountryKeys),CountryKeysComplete=true};
            }
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException or ArgumentException){}
        try
        {
            var path=DetailPath(item);
            if(new FileInfo(path).Length<=4*1024*1024)
            {
                using var json=JsonDocument.Parse(await CacheFiles.ReadAllTextAsync(path,ct));
                var root=json.RootElement;
                if(root.TryGetProperty("CountryKeys",out var keys)&&keys.ValueKind==JsonValueKind.Array)
                {
                    var countries=CatalogRegions.CountryKeys(keys.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()!));
                    var complete=!root.TryGetProperty("CountryKeysComplete",out var completeness)||completeness.ValueKind!=JsonValueKind.False;
                    if(countries.Length>0)return item with{CountryKeys=countries,Country=root.TryGetProperty("Country",out var country)&&country.ValueKind==JsonValueKind.String?country.GetString()??"":"",CountryKeysComplete=complete};
                }
            }
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException or ArgumentException){}
        return null;
    }
    async Task<MediaItem?> Country(MediaItem item,CancellationToken ct,bool forceRefresh)
    {
        if(!forceRefresh)
        {
            var saved=await Cached(item,ct);if(saved is {CountryKeysComplete:true})return saved;
        }
        if(item.CountryKeysComplete&&CatalogRegions.CountryKeys(item.CountryKeys).Length>0&&!forceRefresh)return item;
        var url=CinemaMetadata.CatalogUrl(item.PageUrl,item.Section=="Сериалы"?"/tvseries/":"/movies/");
        if(url==null||item.Year is <1900 or >2100)return null;
        var key=url+"|"+item.Title+"|"+item.Year;
        var entry=requests.GetOrAdd(key,_=>new Lazy<Task<MediaItem?>>(Fetch));
        var pending=entry.Value;
        _=pending.ContinueWith(completed=>
        {
            _=completed.Exception;
            requests.TryRemove(new KeyValuePair<string,Lazy<Task<MediaItem?>>>(key,entry));
        },CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        try{return await pending.WaitAsync(ct);}
        finally{if(pending.IsCompleted)requests.TryRemove(new KeyValuePair<string,Lazy<Task<MediaItem?>>>(key,entry));}
        async Task<MediaItem?> Fetch()
        {
            await slots.WaitAsync(ct);
            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(8));
                var bytes=await client.ReadCinemaDetail(new Uri(url),4*1024*1024,timeout.Token);
                var fresh=ZonaMovieMetadata.Parse(bytes,item);
                if(fresh==null||!CompleteCountries(bytes,item))return null;
                var countries=CatalogRegions.CountryKeys(fresh.CountryKeys);
                var result=item with{Country=fresh.Country,CountryKeys=countries,CountryKeysComplete=true};
                timeout.Token.ThrowIfCancellationRequested();
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(CachePath(item))!);
                    await CacheFiles.WriteAllTextAsync(CachePath(item),JsonSerializer.Serialize(new CountrySnapshot(url,item.Title,item.Year,result.Country,countries,DateTime.UtcNow)),timeout.Token);
                }
                catch(IOException){}
                return result;
            }
            finally{slots.Release();}
        }
    }
    static bool CompleteCountries(byte[] bytes,MediaItem item)
    {
        using var json=JsonDocument.Parse(bytes);var root=json.RootElement;
        var movie=root.GetProperty(item.Section=="Сериалы"?"serial":"movie");
        if(!movie.TryGetProperty("country_id",out var selected)||selected.ValueKind!=JsonValueKind.String)return false;
        var ids=(selected.GetString()??"").Split(' ',StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if(ids.Count==0)return true;
        if(!root.TryGetProperty("countries",out var countries)||countries.ValueKind!=JsonValueKind.Array)return false;
        var known=countries.EnumerateArray().Where(row=>row.ValueKind==JsonValueKind.Object&&row.TryGetProperty("name",out var name)&&name.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(name.GetString())&&row.TryGetProperty("translit",out var key)&&key.ValueKind==JsonValueKind.String&&CatalogRegions.CountryKey(key.GetString())!=null)
            .Select(row=>row.TryGetProperty("id",out var id)?id.ValueKind==JsonValueKind.String?id.GetString():id.ValueKind==JsonValueKind.Number?id.GetRawText():null:null).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return ids.IsSubsetOf(known);
    }
    public async Task<CatalogPage> BrowsePage(string section,int page,string region,string? homeCountry,CancellationToken ct,CatalogSelection? selection=null,bool forceRefresh=false,Action<CatalogPage>? progress=null)
    {
        if(region is not ("native" or "foreign"))throw new ArgumentException("Invalid catalog region",nameof(region));
        var home=CatalogRegions.HomeCountry(homeCountry);
        var filters=(selection??new CatalogSelection()) with{Region=region,HomeCountry=home};
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct);budget.CancelAfter(TimeSpan.FromSeconds(18));
        var source=await new LiveCatalog(client).BrowsePage(section,page,filters,budget.Token,forceRefresh);
        var rows=await WithCachedCountries(source.Items,budget.Token);
        CatalogPage Snapshot()=>source with{Items=rows.Where(item=>CatalogRegions.Matches(item,region,home)).ToArray()};
        progress?.Invoke(Snapshot());
        var work=Enumerable.Range(0,rows.Length).Where(index=>
            !(region=="native"&&CatalogRegions.IsNative(rows[index],home))&&
            (forceRefresh||!rows[index].CountryKeysComplete||CatalogRegions.CountryKeys(rows[index].CountryKeys).Length==0)).ToArray();
        var cursor=-1;var sync=new object();
        async Task Worker()
        {
            while(!budget.IsCancellationRequested)
            {
                var offset=Interlocked.Increment(ref cursor);if(offset>=work.Length)return;
                var index=work[offset];
                try
                {
                    var resolved=await Country(rows[index],budget.Token,forceRefresh);
                    if(resolved!=null)
                    {
                        lock(sync)
                        {
                            if(budget.IsCancellationRequested)return;
                            rows[index]=resolved;progress?.Invoke(Snapshot());
                        }
                    }
                }
                catch(Exception)when(!ct.IsCancellationRequested){}
            }
        }
        await Task.WhenAll(Worker(),Worker());
        ct.ThrowIfCancellationRequested();
        return Snapshot();
    }
}
