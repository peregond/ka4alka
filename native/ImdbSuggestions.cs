using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kachalka;

// The Russian catalog (Zona and the online index built from it) misses many foreign titles, and its
// search barely understands English names. IMDb's public title suggestions fill that gap for Latin
// queries: a found title becomes a minimal card (poster, year, type) whose releases are searched by
// its original name. No key or account is involved.
public static partial class ImdbSuggestions
{
    const string Host="https://www.imdb.com/title/";
    static readonly HttpClient http=CreateClient();
    static HttpClient CreateClient()
    {
        var client=new HttpClient{Timeout=TimeSpan.FromSeconds(8)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        return client;
    }

    public static bool IsImdbOnly(MediaItem item)=>item.PageUrl?.StartsWith(Host,StringComparison.Ordinal)==true;

    // Only Latin queries: IMDb answers Cyrillic ones with unrelated English titles.
    public static bool Applies(string query)=>Latin().IsMatch(query)&&!Cyrillic().IsMatch(query)&&Words(query).Length>0;

    public static async Task<IReadOnlyList<MediaItem>> SearchAsync(string query,CancellationToken ct)
    {
        if(!Applies(query))return [];
        var term=query.Trim().ToLowerInvariant();
        var url="https://v3.sg.media-imdb.com/suggestion/"+Uri.EscapeDataString(term[..1])+"/"+Uri.EscapeDataString(term)+".json";
        var json=await http.GetStringAsync(url,ct);
        return Parse(json,query);
    }

    public static MediaItem[] Parse(string json,string query)
    {
        using var document=JsonDocument.Parse(json);
        if(!document.RootElement.TryGetProperty("d",out var rows)||rows.ValueKind!=JsonValueKind.Array)return [];
        var words=Words(query);
        var items=new List<MediaItem>();
        foreach(var row in rows.EnumerateArray())
        {
            if(row.ValueKind!=JsonValueKind.Object)continue;
            var id=Text(row,"id");var title=Text(row,"l");var kind=Text(row,"q");
            if(id==null||!TitleId().IsMatch(id)||string.IsNullOrWhiteSpace(title))continue;
            var section=kind switch{"TV series" or "TV mini-series"=>"Сериалы","feature" or "TV movie"=>"Фильмы",_=>null};
            if(section==null)continue;
            // Every query word must start a word of the title, so "solar" never returns "Polar Opposites".
            var titleWords=Words(title);
            if(!words.All(word=>titleWords.Any(part=>part.StartsWith(word,StringComparison.Ordinal))))continue;
            var year=row.TryGetProperty("y",out var y)&&y.ValueKind==JsonValueKind.Number&&y.TryGetInt32(out var value)?value:0;
            string? image=null;
            if(row.TryGetProperty("i",out var picture)&&picture.ValueKind==JsonValueKind.Object)image=Poster(Text(picture,"imageUrl"));
            items.Add(Card(id,title.Trim(),title.Trim(),section,year,image));
        }
        return items.DistinctBy(x=>x.Id).ToArray();
    }

    // The minimal card of a title known only by its IMDb id: its details come from the site's /api/title and its
    // releases are searched by the original name, exactly as for a title found through the suggestions.
    public static MediaItem Card(string imdbId,string title,string? originalTitle,string section,int year,string? image)
    {
        var hash=SHA256.HashData(Encoding.UTF8.GetBytes("imdb:"+imdbId));
        return new MediaItem(-(BitConverter.ToInt32(hash,0)&int.MaxValue),title,section,"",year,"—","—","#526B69")
        {
            PageUrl=Host+imdbId+"/",ImdbId=imdbId,OriginalTitle=originalTitle,ImageUrl=image
        };
    }

    // IMDb serves resized posters from the same address with a size directive; a 400 px wide copy is plenty for a card.
    static string? Poster(string? url)
    {
        if(url==null||!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https")return null;
        var marker=url.LastIndexOf("._V1_",StringComparison.Ordinal);
        return marker>0&&url.EndsWith(".jpg",StringComparison.OrdinalIgnoreCase)?url[..marker]+"._V1_QL75_UX400_.jpg":url;
    }

    static string[] Words(string value)=>NonWord().Split(value.ToLowerInvariant()).Where(x=>x.Length>0).ToArray();
    static string? Text(JsonElement row,string name)=>row.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
    [GeneratedRegex("[a-zA-Z]")] private static partial Regex Latin();
    [GeneratedRegex("[\\u0400-\\u04FF]")] private static partial Regex Cyrillic();
    [GeneratedRegex("^tt\\d{5,10}$")] private static partial Regex TitleId();
    [GeneratedRegex("[^\\p{L}\\p{Nd}]+")] private static partial Regex NonWord();
}
