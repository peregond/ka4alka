using System.IO;
using System.Text.Json;
namespace Kachalka;

// A real, versioned source snapshot keeps browsing usable when both remote services are unavailable.
public static class BundledCatalog
{
    const int StarterMaxBytes=1024*1024;
    sealed record SearchRow(MediaItem Item,string[] Words);
    sealed record Snapshot(MediaItem[] Items,SearchRow[] Search);
    static string Normalize(string value)=>System.Text.RegularExpressions.Regex.Replace(value.Replace('ё','е').Replace('Ё','Е').ToLowerInvariant(),@"[^\p{L}\p{N}]+"," ").Trim();
    static Snapshot Index(MediaItem[] items)=>new(items,items.Select(item=>new SearchRow(item,Normalize(string.Join(" ",MediaIdentity.Names(item))+" "+item.Year).Split(' ',StringSplitOptions.RemoveEmptyEntries))).ToArray());
    static Snapshot? loaded;
    static readonly Lazy<Snapshot> starter=new(()=>
    {
        try{return Index(Read("Kachalka.catalog-starter.json",StarterMaxBytes,80));}
        catch(Exception error){ErrorLog.Write(error);return Index([]);}
    });
    static readonly Lazy<Task<MediaItem[]>> snapshot=new(()=>Task.Run(()=>
    {
        MediaItem[] result;
        try{result=Read("Kachalka.catalog-seed.json",SharedCatalog.MaxBytes,15000);}
        catch(Exception error){ErrorLog.Write(error);result=starter.Value.Items;}
        var indexed=Index(result);Volatile.Write(ref loaded,indexed);return result;
    }));
    static MediaItem[] Read(string resource,int maxBytes,int maxItems)
    {
        using var stream=typeof(BundledCatalog).Assembly.GetManifestResourceStream(resource)??throw new InvalidOperationException("Missing catalog snapshot");
        if(stream.Length>maxBytes)throw new InvalidDataException("Embedded catalog is too large.");
        using var json=JsonDocument.Parse(stream);
        if(json.RootElement.ValueKind!=JsonValueKind.Array||json.RootElement.GetArrayLength()>maxItems)throw new InvalidDataException("Embedded catalog has too many records.");
        return json.RootElement.EnumerateArray().Select(row=>OnlineIndexClient.Media(row,row.GetProperty("section").GetString()=="movies"?"Фильмы":"Сериалы")).OfType<MediaItem>().DistinctBy(x=>x.Id).ToArray();
    }
    // Synchronous UI readers use only the bounded starter until the shared worker is done.
    public static IReadOnlyList<MediaItem> All
    {
        get{_=snapshot.Value;return (Volatile.Read(ref loaded)??starter.Value).Items;}
    }
    public static Task EnsureLoadedAsync(CancellationToken ct=default)=>snapshot.Value.WaitAsync(ct);
    static readonly Lazy<SeasonalCollection[]> collections=new(()=>
    {
        using var stream=typeof(BundledCatalog).Assembly.GetManifestResourceStream("Kachalka.catalog-collections.json");
        if(stream==null)return [];
        using var array=JsonDocument.Parse(stream);
        using var root=JsonDocument.Parse("{\"collections\":"+array.RootElement.GetRawText()+"}");
        return SeasonalCatalog.Parse(root.RootElement);
    });
    public static IReadOnlyList<SeasonalCollection> Collections=>collections.Value;
    public static int Count(string section)=>All.Count(x=>x.Section==section);
    public static CatalogPage Page(string section,int page)
    {
        var rows=All.Where(x=>x.Section==section).ToArray();
        var offset=(Math.Clamp(page,1,CatalogPaging.Limit)-1)*CatalogPaging.Size;
        return new(rows.Skip(offset).Take(CatalogPaging.Size).ToArray(),offset+CatalogPaging.Size<rows.Length,[],[]);
    }
    public static IReadOnlyList<MediaItem> Search(string section,string query)
    {
        var words=Normalize(query).Split(' ',StringSplitOptions.RemoveEmptyEntries);
        _=snapshot.Value;
        var index=Volatile.Read(ref loaded)??starter.Value;
        return index.Search.Where(row=>row.Item.Section==section&&words.All(word=>row.Words.Any(part=>part.StartsWith(word,StringComparison.Ordinal)))).Take(80).Select(row=>row.Item).ToArray();
    }
    public static IReadOnlyList<MediaItem> FindWork(string section,string title,int year,string? originalTitle=null)
    {
        var work=new CinemaPeople.Work(title,year,originalTitle);
        return All.Where(x=>x.Section==section&&CinemaPeople.MatchesWork(x,work)).Take(10).ToArray();
    }
}

