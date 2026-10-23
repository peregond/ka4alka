using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kachalka;

// A series found only through IMDb search has no catalog page, so its card was empty. TVmaze is a free
// television database that needs no key and answers IMDb ids: it fills the description, genres, cast,
// year and (when the search gave none) the poster. Its texts are English; nothing here invents a rating,
// because TVmaze's score is not IMDb's or Kinopoisk's.
public static class TvMazeMetadata
{
    const int Limit=2*1024*1024;

    public static async Task<MediaItem> Load(SourceClient client,MediaItem item,CancellationToken ct)
    {
        if(item.Section!="Сериалы"||item.ImdbId==null||!Regex.IsMatch(item.ImdbId,@"^tt\d{5,10}$"))throw new InvalidDataException("У карточки нет идентификатора IMDb сериала.");
        var show=await client.Read(new Uri("https://api.tvmaze.com/lookup/shows?imdb="+item.ImdbId),Limit,ct);
        byte[]? cast=null;
        if(ShowId(show) is {} id)
        {
            // The credits are a bonus: a failure here still leaves the description.
            try{cast=await client.Read(new Uri("https://api.tvmaze.com/shows/"+id+"/cast"),Limit,ct);}
            catch(Exception) when(!ct.IsCancellationRequested){}
        }
        return Apply(item,show,cast);
    }

    static int? ShowId(byte[] show)
    {
        try
        {
            using var json=JsonDocument.Parse(show);
            return json.RootElement.ValueKind==JsonValueKind.Object&&json.RootElement.TryGetProperty("id",out var id)&&id.ValueKind==JsonValueKind.Number&&id.TryGetInt32(out var value)&&value>0?value:null;
        }
        catch(JsonException){return null;}
    }

    // Only a show whose own IMDb id equals the card's is accepted; a title lookalike never lends its text.
    public static MediaItem Apply(MediaItem item,byte[] show,byte[]? cast=null)
    {
        using var json=JsonDocument.Parse(show);
        var root=json.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("externals",out var externals)||externals.ValueKind!=JsonValueKind.Object||
            Text(externals,"imdb") is not {} imdb||!imdb.Equals(item.ImdbId,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("TVmaze вернул другой сериал.");
        var description=Description(Text(root,"summary"));
        var genres=root.TryGetProperty("genres",out var list)&&list.ValueKind==JsonValueKind.Array
            ?list.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()!).Where(x=>x.Length>0).Select(TitleMetadata.Genre).Distinct().Take(4).ToArray():Array.Empty<string>();
        // A broadcaster names its country; web services (streaming) carry a null network and a webChannel instead.
        var country=Country(root,"network");
        if(country.Length==0)country=Country(root,"webChannel");
        var year=item.Year;
        if(year<=0&&Text(root,"premiered") is {Length:>=4} premiered&&int.TryParse(premiered[..4],out var first)&&first is >=1880 and <=2100)year=first;
        string? poster=item.ImageUrl;
        if(string.IsNullOrWhiteSpace(poster)&&root.TryGetProperty("image",out var image)&&image.ValueKind==JsonValueKind.Object)poster=Picture(Text(image,"original")??Text(image,"medium"));
        var people=cast==null?item.People:Cast(cast,item.People);
        return item with
        {
            Description=description??item.Description,
            Genre=genres.Length>0&&string.IsNullOrWhiteSpace(item.Genre)?string.Join(", ",genres):item.Genre,
            Country=string.IsNullOrWhiteSpace(item.Country)?country:item.Country,
            Year=year,ImageUrl=poster,People=people
        };
    }

    static string? Text(JsonElement row,string name)=>row.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;

    // TVmaze summaries are small HTML fragments: <p><b>Title</b> text…</p>.
    static string? Description(string? html)
    {
        if(string.IsNullOrWhiteSpace(html))return null;
        var text=Regex.Replace(html,@"<br\s*/?>|</p>\s*<p>","\n",RegexOptions.IgnoreCase);
        text=WebUtility.HtmlDecode(Regex.Replace(text,@"<[^>]{1,200}>",""));
        text=Regex.Replace(text,@"[ \t]+"," ");text=Regex.Replace(text,@"\s*\n\s*","\n").Trim();
        return text.Length==0?null:text;
    }

    static string Country(JsonElement root,string channel)
    {
        if(!root.TryGetProperty(channel,out var network)||network.ValueKind!=JsonValueKind.Object||
            !network.TryGetProperty("country",out var country)||country.ValueKind!=JsonValueKind.Object)return "";
        return Text(country,"code") switch{"US"=>"США","GB"=>"Великобритания","CA"=>"Канада","AU"=>"Австралия","JP"=>"Япония","KR"=>"Южная Корея","DE"=>"Германия","FR"=>"Франция","RU"=>"Россия",_=>""};
    }

    static string? Picture(string? url)=>Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host=="static.tvmaze.com"&&uri.UserInfo.Length==0?uri.AbsoluteUri:null;

    static CinemaPerson[] Cast(byte[] bytes,CinemaPerson[] known)
    {
        try
        {
            using var json=JsonDocument.Parse(bytes);
            if(json.RootElement.ValueKind!=JsonValueKind.Array)return known;
            var credits=json.RootElement.EnumerateArray().Where(row=>row.ValueKind==JsonValueKind.Object&&row.TryGetProperty("person",out var p)&&p.ValueKind==JsonValueKind.Object)
                .Select(row=>new CastCredit(Text(row.GetProperty("person"),"name")?.Trim()??"",Character:row.TryGetProperty("character",out var c)&&c.ValueKind==JsonValueKind.Object?Text(c,"name")?.Trim():null))
                .Where(row=>row.Name.Length is >0 and <=120).GroupBy(row=>row.Name,StringComparer.OrdinalIgnoreCase).Take(12)
                .Select(group=>
                {
                    var character=string.Join(" / ",group.Select(row=>row.Character).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct().Take(4));
                    return group.First() with{Character=character[..Math.Min(200,character.Length)]};
                }).ToArray();
            var people=known.Any(x=>x.Role=="Актёры")?known:known.Concat(credits.Select(x=>new CinemaPerson(x.Name,"Актёры",""))).ToArray();
            return CharacterCredits.Apply(people,credits);
        }
        catch(JsonException){return known;}
    }
}
