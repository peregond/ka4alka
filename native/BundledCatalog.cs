using System.Text.Json;
namespace Kachalka;

// A real, versioned source snapshot keeps browsing usable when both remote services are unavailable.
public static class BundledCatalog
{
    static readonly Lazy<MediaItem[]> snapshot=new(()=>
    {
        using var stream=typeof(BundledCatalog).Assembly.GetManifestResourceStream("Kachalka.catalog-seed.json")??throw new InvalidOperationException("Missing catalog snapshot");
        using var json=JsonDocument.Parse(stream);
        return json.RootElement.EnumerateArray().Select(row=>OnlineIndexClient.Media(row,row.GetProperty("section").GetString()=="movies"?"Фильмы":"Сериалы")).OfType<MediaItem>().DistinctBy(x=>x.Id).ToArray();
    });
    public static IReadOnlyList<MediaItem> All=>snapshot.Value;
    public static int Count(string section)=>snapshot.Value.Count(x=>x.Section==section);
    public static CatalogPage Page(string section,int page)
    {
        var rows=snapshot.Value.Where(x=>x.Section==section).ToArray();
        var offset=(Math.Clamp(page,1,CatalogPaging.Limit)-1)*CatalogPaging.Size;
        return new(rows.Skip(offset).Take(CatalogPaging.Size).ToArray(),offset+CatalogPaging.Size<rows.Length,[],[]);
    }
    public static IReadOnlyList<MediaItem> Search(string section,string query)=>snapshot.Value.Where(x=>x.Section==section&&x.Title.Contains(query,StringComparison.CurrentCultureIgnoreCase)).Take(80).ToArray();
    public static IReadOnlyList<MediaItem> FindWork(string section,string title,int year,string? originalTitle=null)
    {
        var work=new CinemaPeople.Work(title,year,originalTitle);
        return snapshot.Value.Where(x=>x.Section==section&&CinemaPeople.MatchesWork(x,work)).Take(10).ToArray();
    }
}
