using System.Text.Json;
namespace Kachalka;

// Explicit editorial membership: no ranking guesswork or per-title requests.
public sealed record FeaturedGame(int Id,string Title);
// Layout: "shelf" is the ordinary row of posters, "lead" one large story with a short list beside it, "ranking" numbered posters.
public sealed record FeaturedCollection(string Id,string Title,string TitleEn,string Description,string DescriptionEn,string[]? MediaIds=null,FeaturedGame[]? Games=null,string Layout="shelf")
{
    public string Label=>AppLanguage.Current=="en"?TitleEn:Title;
    public string Summary=>AppLanguage.Current=="en"?DescriptionEn:Description;
    public bool IsGames=>Games is {Length:>0};
    // The small caps rubric above the heading.
    public string Kicker=>AppLanguage.T(Id.StartsWith("seasonal-",StringComparison.Ordinal)?"Сезон":Id.StartsWith("genre-",StringComparison.Ordinal)?"Жанр":Id==FeaturedCatalog.FreshId?"Новое":Id==FeaturedCatalog.TopRatedId?"Рейтинг":IsGames?"Игры":"Подборка");
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
        // Rubrics drawn from the catalog itself, so the page has fresh material without any editorial work.
        if(Fresh(catalog) is {Length:>0} fresh)groups.Insert(Math.Min(1,groups.Count),new(new(FreshId,"Свежее в каталоге","Fresh in the catalog","Недавние релизы с раздачами","Recent releases"),fresh));
        if(TopRated(catalog) is {Length:>0} top)groups.Insert(Math.Min(3,groups.Count),new(new(TopRatedId,"Лучшее по оценкам","Top rated","Фильмы и сериалы с самыми высокими оценками зрителей","Titles with the highest audience ratings",Layout:"ranking"),top));
        // One shelf per big genre, from the best rated titles of the catalog.
        // A title stands on the shelf of the first genre that takes it, so the shelves do not repeat each other.
        var taken=new HashSet<int>();
        foreach(var (key,title,titleEn) in Genres)
            if(ByGenre(catalog,key,taken) is {Length:>=GenreMinimum} best)
            {
                foreach(var item in best)taken.Add(item.Id);
                groups.Add(new(new("genre-"+key,title,titleEn,"Лучшее по оценкам зрителей","Best rated by viewers"),best));
            }
        return seasonalGroups.Concat(groups).ToArray();
    }
    const int GenreMinimum=6;
    static readonly (string Key,string Title,string TitleEn)[] Genres=
    [
        ("fantastika","Фантастика","Sci-Fi"),("triller","Триллеры","Thrillers"),("komediia","Комедии","Comedies"),("uzhasy","Ужасы","Horror"),
        ("detektiv","Детективы","Mysteries"),("boevik","Боевики","Action"),("multfilm","Мультфильмы","Animation"),("kriminal","Криминал","Crime")
    ];
    // Any available rating counts here, the Kinopoisk one first; a genre shelf does not need both sources like the ranking.
    static double AnyRating(MediaItem item)
    {
        var kp=CatalogPaging.Rating(item);if(kp>=0)return kp;
        return double.TryParse(item.Imdb.Replace(',','.'),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)&&value is >=0 and <=10?value:-1;
    }
    static MediaItem[] ByGenre(IEnumerable<MediaItem> catalog,string key,ISet<int> taken)=>catalog.Where(Presentable).Where(item=>item.GenreKeys.Contains(key)&&AnyRating(item)>=6.5&&!taken.Contains(item.Id))
        .OrderByDescending(AnyRating).ThenByDescending(item=>item.Year).ThenBy(item=>item.Id).Take(40).ToArray();
    public const string FreshId="fresh",TopRatedId="top-rated";
    static double Score(MediaItem item)
    {
        var kp=CatalogPaging.Rating(item);
        var imdb=double.TryParse(item.Imdb.Replace(',','.'),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var value)&&value is >=0 and <=10?value:-1;
        return kp>=0&&imdb>=0?(kp+imdb)/2:-1;
    }
    static bool Presentable(MediaItem item)=>item.Cinema&&item.OnlineId!=null&&!string.IsNullOrWhiteSpace(item.ImageUrl)&&!string.IsNullOrWhiteSpace(item.Description);
    // The newest releases first; among equal years the better rated one comes first.
    static MediaItem[] Fresh(IEnumerable<MediaItem> catalog)=>catalog.Where(Presentable).OrderByDescending(item=>item.Year).ThenByDescending(Score).ThenBy(item=>item.Id).Take(40).ToArray();
    // Both sources must agree: a single enthusiastic rating of an obscure title does not make a ranking.
    static MediaItem[] TopRated(IEnumerable<MediaItem> catalog)=>catalog.Where(Presentable).Where(item=>Score(item)>=7.8&&item.Year>=1960).OrderByDescending(Score).ThenByDescending(item=>item.Year).ThenBy(item=>item.Id).Take(40).ToArray();
}
