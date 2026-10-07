using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
namespace Kachalka;

public record CinemaPerson(string Name,string Role,string PageUrl);
public record CinemaAward(string Name,int Year,string Category,bool Winner,string? PersonUrl=null)
{
    public string Label=>$"{Name} · {Year} · {Category} · {(Winner?"Победитель":"Номинация")}";
}
public record CinemaCollection(string Name,string Kind,string PageUrl);
public record PersonProfile(CinemaPerson Person,string Description,MediaItem[] Filmography);

public static class CinemaMetadata
{
    static string Text(HtmlNode? node)=>Regex.Replace(HtmlEntity.DeEntitize(node?.InnerText??""),@"\s+"," ").Trim();
    static HtmlDocument Html(byte[] bytes){var doc=new HtmlDocument();doc.LoadHtml(Encoding.UTF8.GetString(bytes));return doc;}
    public static string? CatalogUrl(string? value,params string[] prefixes)
    {
        if(string.IsNullOrWhiteSpace(value)||!Uri.TryCreate(new Uri(LiveCatalog.Base),HtmlEntity.DeEntitize(value),out var uri)||uri.Scheme!="https"||uri.Host!="w6.zona.plus"||uri.Port!=443||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0)return null;
        return prefixes.Any(prefix=>Regex.IsMatch(uri.AbsolutePath,"^"+Regex.Escape(prefix)+@"[\p{L}\p{N}-]+/?$"))?uri.AbsoluteUri:null;
    }
    public static CinemaPerson[] People(byte[] bytes)
    {
        var doc=Html(bytes);var result=new List<CinemaPerson>();
        foreach(var anchor in doc.DocumentNode.SelectNodes("//a[@href]")??Enumerable.Empty<HtmlNode>())
        {
            var url=CatalogUrl(anchor.GetAttributeValue("href",""),"/persons/","/person/","/people/");
            if(url==null)continue;
            var name=Text(anchor.SelectSingleNode(".//*[@itemprop='name']")??anchor);if(name.Length==0)continue;
            string? role=null;
            foreach(var node in anchor.AncestorsAndSelf().Take(5))
            {
                var prop=node.GetAttributeValue("itemprop","");
                role=prop switch{"actor"=>"Актёры","director"=>"Режиссёры","cinematographer"=>"Операторы",_=>null};
                if(role==null)
                {
                    var label=Text(node.SelectSingleNode("./dt|./th|./*[contains(@class,'label') or contains(@class,'title')]"));
                    role=label.ToLowerInvariant() switch{var s when s.StartsWith("в ролях")||s.StartsWith("актер")||s.StartsWith("актёр")=>"Актёры",var s when s.StartsWith("режисс")=>"Режиссёры",var s when s.StartsWith("оператор")=>"Операторы",_=>null};
                }
                if(role!=null)break;
            }
            if(role!=null)result.Add(new(name,role,url));
        }
        return result.DistinctBy(x=>(x.PageUrl,x.Role)).Take(80).ToArray();
    }
    public static CinemaCollection[] Collections(byte[] bytes)
    {
        var doc=Html(bytes);var result=new List<CinemaCollection>();
        foreach(var node in doc.DocumentNode.SelectNodes("//*[@itemprop='isPartOf']")??Enumerable.Empty<HtmlNode>())
        foreach(var anchor in node.SelectNodes(".//a[@href]|self::a[@href]")??Enumerable.Empty<HtmlNode>())
        {
            var url=CatalogUrl(anchor.GetAttributeValue("href",""),"/collections/","/franchise/");var name=Text(anchor);
            if(url!=null&&name.Length>0)result.Add(new(name,url.Contains("/franchise/",StringComparison.Ordinal)?"Франшиза":"Подборка",url));
        }
        return result.DistinctBy(x=>x.PageUrl).Take(30).ToArray();
    }
    public static PersonProfile Person(byte[] bytes,CinemaPerson person)
    {
        var doc=Html(bytes);
        var bio=Text(doc.DocumentNode.SelectSingleNode("//*[@itemprop='description' and not(self::meta)]"));
        if(bio.Length==0)bio=HtmlEntity.DeEntitize(doc.DocumentNode.SelectSingleNode("//meta[@name='description']")?.GetAttributeValue("content","")??"").Trim();
        var items=LiveCatalog.Parse(bytes,"Фильмы").Concat(LiveCatalog.Parse(bytes,"Сериалы")).DistinctBy(x=>x.Id).ToArray();
        // Ratings here belong to each film card, never to the person or adjacent films.
        foreach(var card in doc.DocumentNode.SelectNodes("//li[contains(@class,'results-item-wrap')]")??Enumerable.Empty<HtmlNode>())
        {
            var url=CatalogUrl(card.SelectSingleNode(".//a[@itemprop='url']")?.GetAttributeValue("href",""),"/movies/","/tvseries/");
            var rating=Text(card.SelectSingleNode(".//*[contains(@class,'results-item-rating')]"));
            var index=Array.FindIndex(items,x=>x.PageUrl==url);
            if(index>=0&&Regex.IsMatch(rating,@"^\d{1,2}([.,]\d)?$"))items[index]=items[index] with{Kinopoisk=rating};
        }
        return new(person,bio,items);
    }
    public static double Rating(MediaItem item)=>double.TryParse(item.Kinopoisk.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&value is >0 and <=10?value:0;
    public static MediaItem[] Top(IEnumerable<MediaItem> items)=>items.Where(x=>Rating(x)>0).OrderByDescending(Rating).ThenByDescending(x=>x.Year).Take(10).ToArray();
}
