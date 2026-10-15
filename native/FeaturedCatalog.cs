using System.Text.Json;
namespace Kachalka;

// Explicit editorial membership: no ranking guesswork or per-title requests.
public sealed record FeaturedGame(int Id,string Title);
public sealed record FeaturedCollection(string Id,string Title,string TitleEn,string Description,string DescriptionEn,string[]? MediaIds=null,FeaturedGame[]? Games=null)
{
    public string Label=>AppLanguage.Current=="en"?TitleEn:Title;
    public string Summary=>AppLanguage.Current=="en"?DescriptionEn:Description;
    public bool IsGames=>Games is {Length:>0};
}
public sealed record FeaturedGroup(FeaturedCollection Collection,MediaItem[] Items);
public static class FeaturedCatalog
{
    static readonly Lazy<FeaturedCollection[]> collections=new(()=>
    {
        using var stream=typeof(FeaturedCatalog).Assembly.GetManifestResourceStream("Kachalka.FeaturedCollections.json")??throw new InvalidOperationException("Featured collections are missing.");
        return JsonSerializer.Deserialize<FeaturedCollection[]>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??[];
    });
    public static IReadOnlyList<FeaturedCollection> Collections=>collections.Value;
    public static FeaturedGroup[] Resolve(IEnumerable<MediaItem> catalog,IEnumerable<SeasonalCollection>? seasonal=null)
    {
        var lookup=catalog.Where(item=>item.OnlineId!=null).GroupBy(item=>item.OnlineId!,StringComparer.Ordinal).ToDictionary(group=>group.Key,group=>group.First(),StringComparer.Ordinal);
        MediaItem[] Members(IEnumerable<string> ids)=>ids.Distinct(StringComparer.Ordinal).Where(lookup.ContainsKey).Select(id=>lookup[id]).Take(40).ToArray();
        var groups=Collections.Select(collection=>new FeaturedGroup(collection,collection.IsGames
            ?collection.Games!.Take(40).Select(game=>GameCatalog.Item(game.Id,game.Title,"https://cdn.akamai.steamstatic.com/steam/apps/"+game.Id+"/header.jpg")).ToArray()
            :Members(collection.MediaIds??[]))).ToList();
        var seasonalGroups=new List<FeaturedGroup>();
        foreach(var collection in (seasonal??[]).Take(10))
        {
            var items=Members(collection.MediaIds);if(items.Length==0)continue;
            seasonalGroups.Add(new(new("seasonal-"+collection.Id,collection.Title,AppLanguage.T(collection.Title),"","",collection.MediaIds),items));
        }
        return seasonalGroups.Concat(groups).ToArray();
    }
}
