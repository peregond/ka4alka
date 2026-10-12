using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
namespace Kachalka;

// Cover addresses of the catalog. Zona serves film covers only from its own image hosts and in one fixed
// path shape; the site's /api/poster proxy accepts exactly that shape, so anything else is not a cover.
public static class ZonaPoster
{
    static readonly Regex Host=new(@"^img[1-4]\.zonapic\.com$",RegexOptions.CultureInvariant);
    static readonly Regex FilmPath=new(@"^/images/film_\d+/\d+/\d+\.jpe?g$",RegexOptions.CultureInvariant);

    // The canonical https address of a catalog cover, or null for anything else. A protocol-relative address
    // ("//img3.zonapic.com/...") is completed; credentials, ports, queries and fragments are refused.
    public static string? Normalize(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))return null;
        value=HtmlEntity.DeEntitize(value).Trim();
        if(value.StartsWith("//",StringComparison.Ordinal))value="https:"+value;
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps||uri.UserInfo.Length>0||!uri.IsDefaultPort||uri.Query.Length>0||uri.Fragment.Length>0)return null;
        return Host.IsMatch(uri.Host)&&FilmPath.IsMatch(uri.AbsolutePath)?uri.AbsoluteUri:null;
    }

    // The cover a catalog page names for itself: Open Graph and the old image_src link in the head first, then
    // the schema.org image outside of the listing cards that recommendations are drawn as.
    public static string? FromPage(byte[] html)
    {
        var document=new HtmlDocument();document.LoadHtml(Encoding.UTF8.GetString(html));
        foreach(var (path,attribute) in new[]
        {
            ("//head/meta[@property='og:image']","content"),("//head/meta[@name='og:image']","content"),("//head/link[@rel='image_src']","href"),
            ("//meta[@itemprop='image' and not(ancestor::li[contains(concat(' ',normalize-space(@class),' '),' results-item-wrap ')])]","content")
        })
            foreach(var node in document.DocumentNode.SelectNodes(path)??new HtmlNodeCollection(null))
                if(Normalize(node.GetAttributeValue(attribute,""))is {} url)return url;
        return null;
    }

    // The cover inside the catalog's JSON object for a film: the usual field names first, then any text
    // field that is a cover address.
    public static string? FromJson(JsonElement movie)
    {
        if(movie.ValueKind!=JsonValueKind.Object)return null;
        foreach(var name in new[]{"cover","poster","image"})
            if(movie.TryGetProperty(name,out var field)&&field.ValueKind==JsonValueKind.String&&Normalize(field.GetString())is {} named)return named;
        foreach(var property in movie.EnumerateObject())
            if(property.Value.ValueKind==JsonValueKind.String&&Normalize(property.Value.GetString())is {} any)return any;
        return null;
    }

    // The same cover through the site's proxy, for networks where the image host itself does not answer.
    public static string? ViaSite(string? value)=>Normalize(value)is {} url?"api/poster?src="+Uri.EscapeDataString(url):null;
}
