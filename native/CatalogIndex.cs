using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kachalka;

public record ReleaseCache(DateTime SavedUtc,SourceEntry[] Items,SourceCheck[]? Sources=null);

// A bounded, persistent index of catalog records seen by the app. It contains
// public metadata only; torrent transfer state remains in DownloadService.
public sealed class CatalogIndex
{
    sealed record Entry(MediaItem Item,DateTime SeenUtc);
    readonly Dictionary<string,Entry> entries=[];
    readonly SemaphoreSlim writer=new(1,1);
    readonly string path;
    const int Limit=3000;
    public long Revision {get;private set;}

    readonly Task<Entry[]> restore;
    bool restored;
    public CatalogIndex(string dataDir,bool deferRestore=false)
    {
        path=Path.Combine(dataDir,"catalog-index.json");
        restore=deferRestore?Task.Run(ReadSaved):Task.FromResult(ReadSaved());
        if(!deferRestore){ApplySaved(restore.Result);restored=true;}
    }
    Entry[] ReadSaved()
    {
        try
        {
            if(new FileInfo(path).Length>12*1024*1024)return [];
            return (JsonSerializer.Deserialize<Entry[]>(CacheFiles.ReadAllText(path))??[]).Take(Limit).Where(row=>Valid(row.Item)).ToArray();
        }
        catch(IOException){return [];}catch(JsonException){return [];}catch(UnauthorizedAccessException){return [];}
    }
    void ApplySaved(Entry[] rows)
    {
        foreach(var row in rows)entries.TryAdd(Key(row.Item),row with{Item=MediaIdentity.Enrich(row.Item)});
    }
    // Publication happens on the caller's context; background disk reading never mutates UI-owned state.
    public async Task RestoreAsync(CancellationToken ct=default)
    {
        if(restored)return;
        var rows=await restore.WaitAsync(ct);
        await writer.WaitAsync(ct);
        try{if(!restored){ApplySaved(rows);restored=true;Revision++;}}
        finally{writer.Release();}
    }

    static string Key(MediaItem item)=>item.Section+"|"+item.PageUrl;
    string ReleasePath(MediaItem item)=>Path.Combine(Path.GetDirectoryName(path)!,"release-index",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MediaIdentity.StableKey(item))))+".json");
    static bool Valid(MediaItem item)=>item.Section is "Фильмы" or "Сериалы"&&
        !string.IsNullOrWhiteSpace(item.Title)&&Uri.TryCreate(item.PageUrl,UriKind.Absolute,out var url)&&
        url.Scheme=="https"&&(url.Host.Equals("zona.plus",StringComparison.OrdinalIgnoreCase)||url.Host.EndsWith(".zona.plus",StringComparison.OrdinalIgnoreCase)||url.Host.Equals("www.imdb.com",StringComparison.OrdinalIgnoreCase)&&Regex.IsMatch(url.AbsolutePath,@"^/title/tt\d{5,10}/?$"));
    static string Normalize(string text)=>Regex.Replace(text.Replace('ё','е').Replace('Ё','Е').ToLowerInvariant(),@"[^\p{L}\p{N}]+"," ").Trim();

    public IReadOnlyList<MediaItem> Search(string section,string query,int limit=80)
    {
        CacheFiles.Touch(path);var term=Normalize(query);if(term.Length==0)return Recent(section,limit);
        var tokens=term.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        return entries.Values.Where(x=>x.Item.Section==section)
            .Select(x=>(x.Item,x.SeenUtc,Score:MediaIdentity.Score(x.Item,term,tokens)))
            .Where(x=>x.Score>0).OrderByDescending(x=>x.Score).ThenByDescending(x=>x.SeenUtc)
            .Take(Math.Clamp(limit,1,200)).Select(x=>x.Item).ToArray();
    }

    public IReadOnlyList<MediaItem> Recent(string section,int limit=40)
    {
        CacheFiles.Touch(path);return entries.Values
        .Where(x=>x.Item.Section==section).OrderByDescending(x=>x.SeenUtc)
        .Take(Math.Clamp(limit,1,200)).Select(x=>x.Item).ToArray();
    }
    public MediaItem? FindByImdb(string section,string? imdbId)=>imdbId==null?null:entries.Values.Select(x=>x.Item).FirstOrDefault(x=>x.Section==section&&x.ImdbId==imdbId&&(x.TitleAliases.Length>0||!string.IsNullOrWhiteSpace(x.Description)));
    public async Task ClearMemoryAsync()
    {
        await RestoreAsync();
        await writer.WaitAsync();try{entries.Clear();Revision++;}finally{writer.Release();}
    }

    public IReadOnlyList<SourceEntry> CachedReleases(MediaItem item)=>CachedReleaseSnapshot(item)?.Items??[];
    public ReleaseCache? CachedReleaseSnapshot(MediaItem item)
    {
        if(!Valid(item))return null;
        try
        {
            var file=ReleasePath(item);
            if(!File.Exists(file))file=Path.Combine(Path.GetDirectoryName(path)!,"release-index",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Key(item))))+".json");
            if(new FileInfo(file).Length>3*1024*1024)return null;
            var saved=JsonSerializer.Deserialize<ReleaseCache>(CacheFiles.ReadAllText(file));
            return saved is {Items:not null}&&saved.SavedUtc>DateTime.UtcNow.AddDays(-7)&&saved.SavedUtc<=DateTime.UtcNow.AddMinutes(5)?saved:null;
        }
        catch(IOException){return null;}catch(JsonException){return null;}catch(UnauthorizedAccessException){return null;}
    }

    public async Task CacheReleasesAsync(MediaItem item,IReadOnlyList<SourceEntry> releases,IReadOnlyList<SourceCheck>? sources=null,CancellationToken ct=default)
    {
        if(!Valid(item)||releases.Count==0&&!ReleaseAvailability.ConfirmedEmpty(releases,sources))return;
        await writer.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            var file=ReleasePath(item);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await CacheFiles.WriteAllTextAsync(file,JsonSerializer.Serialize(new ReleaseCache(DateTime.UtcNow,releases.Take(300).ToArray(),sources?.ToArray()),new JsonSerializerOptions{IgnoreReadOnlyProperties=true}),ct);
            Revision++;
        }
        finally{writer.Release();}
    }

    public async Task AddAsync(IEnumerable<MediaItem> items,CancellationToken ct=default)
    {
        var incoming=items.Where(Valid).Select(MediaIdentity.Enrich).ToArray();if(incoming.Length==0)return;
        await RestoreAsync(ct);
        await writer.WaitAsync(ct);
        try
        {
            var seen=DateTime.UtcNow;
            foreach(var item in incoming)
            {
                var key=Key(item);
                if(entries.TryGetValue(key,out var prior))
                {
                    var old=prior.Item;
                    entries[key]=new(item with
                    {
                        OriginalTitle=item.OriginalTitle??old.OriginalTitle,
                        ImdbId=item.ImdbId??old.ImdbId,
                        TitleAliases=item.TitleAliases.Concat(old.TitleAliases).Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray(),
                        Description=item.Description??old.Description,
                        People=item.People.Length>0?item.People:old.People,
                        Awards=item.Awards.Length>0?item.Awards:old.Awards,
                        Collections=item.Collections.Length>0?item.Collections:old.Collections,
                        Kinopoisk=item.Kinopoisk=="—"?old.Kinopoisk:item.Kinopoisk,
                        Imdb=item.Imdb=="—"?old.Imdb:item.Imdb,
                        OnlineId=item.OnlineId??old.OnlineId,
                        ImageUrl=item.ImageUrl??old.ImageUrl
                    },seen);
                }
                else entries[key]=new(item,seen);
            }
            if(entries.Count>Limit)
                foreach(var key in entries.OrderBy(x=>x.Value.SeenUtc).Take(entries.Count-Limit).Select(x=>x.Key).ToArray())entries.Remove(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var snapshot=entries.Values.ToArray();
            var json=await Task.Run(()=>JsonSerializer.Serialize(snapshot),ct);
            await CacheFiles.WriteAllTextAsync(path,json,ct);
            Revision++;
        }
        finally{writer.Release();}
    }
}
