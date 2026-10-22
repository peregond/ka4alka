using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Kachalka;

public record FranchisePart(string ImdbId,string Title,string OriginalTitle,int Year,string Description,string? Poster,string? EnglishTitle=null,string? EnglishDescription=null,string? EnglishPoster=null);
public record MovieFranchise(int Id,string Name,FranchisePart[] Items,string? EnglishName=null);
public static class MovieFranchiseCatalog
{
    static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true};
    static readonly Lazy<MovieFranchise[]> bundled=new(()=>
    {
        using var stream=typeof(MovieFranchiseCatalog).Assembly.GetManifestResourceStream("Kachalka.MovieFranchises.json");
        return stream==null?[]:JsonSerializer.Deserialize<MovieFranchise[]>(stream,Json)??[];
    });
    static readonly ConcurrentDictionary<string,(DateTime Expires,MovieFranchise? Value)> memory=new();
    static readonly SharedAsyncWork<MovieFranchise?> work=new();
    static readonly Lazy<MediaItem[]> russianSearch=new(()=>bundled.Value.SelectMany(row=>row.Items).DistinctBy(part=>part.ImdbId).Select(part=>Card(part,"ru")).ToArray());
    static readonly Lazy<MediaItem[]> englishSearch=new(()=>bundled.Value.SelectMany(row=>row.Items).DistinctBy(part=>part.ImdbId).Select(part=>Card(part,"en")).ToArray());
    static MediaItem Card(FranchisePart part,string language)
    {
        var card=ImdbSuggestions.Card(part.ImdbId,part.Title,part.OriginalTitle,"Фильмы",part.Year,EnglishMedia.PosterUrl(part.Poster)) with
        {
            Description=part.Description,TitleAliases=new[]{part.Title,part.OriginalTitle,part.EnglishTitle??""}.Where(name=>!string.IsNullOrWhiteSpace(name)).DistinctBy(MediaIdentity.Normalize).ToArray()
        };
        if(language=="en")card.SetEnglish(new("en",part.EnglishTitle??part.Title,part.EnglishDescription??part.Description,EnglishMedia.PosterUrl(part.EnglishTitle!=null?part.EnglishPoster:part.Poster),null,[],[],[],[]));
        return card;
    }
    // Franchise membership already identifies these movies. Search the same
    // bilingual cards locally instead of depending on IMDb's Latin-only search.
    public static MediaItem[] Search(string query,string language)
    {
        if(string.IsNullOrWhiteSpace(query))return [];
        var now=DateTime.UtcNow;
        var cached=memory.Where(entry=>entry.Value.Expires>now&&entry.Value.Value!=null)
            .OrderByDescending(entry=>entry.Key.StartsWith(language+"|",StringComparison.Ordinal))
            .SelectMany(entry=>entry.Value.Value!.Items).Select(part=>Card(part,language));
        var rows=(language=="en"?englishSearch.Value:russianSearch.Value).Concat(cached);
        return CatalogSearch.Find(MediaIdentity.Merge(rows),query,80,language);
    }
    static bool Same(FranchisePart part,MediaItem item)=>item.ImdbId!=null?part.ImdbId==item.ImdbId:
        item.Year>0&&item.Year==part.Year&&MediaIdentity.Names(item).Select(MediaIdentity.Normalize).Intersect(new[]{part.Title,part.OriginalTitle,part.EnglishTitle??""}.Select(MediaIdentity.Normalize)).Any();
    public static MovieFranchise? Bundled(MediaItem item)=>item.Section=="Фильмы"?bundled.Value.FirstOrDefault(row=>row.Items.Any(part=>Same(part,item))):null;
    public static string? Identity(MediaItem item)=>item.Section!="Фильмы"?null:OnlineIndexClient.IdFor(item)??(Regex.IsMatch(item.ImdbId??"",@"^tt\d{5,10}$")?item.ImdbId:null);
    public static bool CanLookup(MediaItem item)=>Identity(item)!=null||Bundled(item)!=null;
    public static MediaItem[] OtherParts(MovieFranchise? row,MediaItem current,string language)
    {
        if(row==null)return [];
        return row.Items.Where(part=>!Same(part,current)).DistinctBy(part=>part.ImdbId).OrderBy(part=>part.Year==0?9999:part.Year).Select(part=>Card(part,language)).ToArray();
    }
    public static MovieFranchise? Parse(byte[] bytes,string identity)
    {
        using var json=JsonDocument.Parse(bytes);var root=json.RootElement;
        if(root.GetProperty("id").GetString()!=identity)throw new InvalidDataException("Franchise belongs to another movie.");
        var value=root.GetProperty("item");if(value.ValueKind==JsonValueKind.Null)return null;
        var row=value.Deserialize<MovieFranchise>(Json)??throw new InvalidDataException("Invalid franchise.");
        if(row.Id<=0||string.IsNullOrWhiteSpace(row.Name)||row.Name.Length>300||row.Items==null||row.Items.Length>40)throw new InvalidDataException("Invalid franchise.");
        if(row.Items.Any(part=>!Regex.IsMatch(part.ImdbId??"",@"^tt\d{5,10}$")||string.IsNullOrWhiteSpace(part.Title)||part.Title.Length>300||(part.Year!=0&&part.Year is <1880 or >2100)||part.Description==null||part.Description.Length>4000))throw new InvalidDataException("Invalid franchise part.");
        return row;
    }
    public static async Task<MovieFranchise?> Load(MediaItem item,OnlineIndexClient online,string language,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(Bundled(item) is {} ready)return ready;
        var id=Identity(item);if(id==null)return null;
        var key=language+"|"+id;
        if(memory.TryGetValue(key,out var saved)&&saved.Expires>DateTime.UtcNow)return saved.Value;
        return await work.Run(key,async token=>
        {
            var query=id.StartsWith("movies:",StringComparison.Ordinal)?"id="+Uri.EscapeDataString(id):"imdb="+id;
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(22));
            var bytes=await online.ReadSite("api/franchise?"+query+"&language="+AppLanguage.Normalize(language),512*1024,deadline.Token,restSites:false);
            var result=Parse(bytes,id);
            if(memory.Count>=64)memory.Clear();memory[key]=(DateTime.UtcNow.Add(result==null?TimeSpan.FromHours(6):TimeSpan.FromDays(7)),result);
            return result;
        },ct);
    }
}
