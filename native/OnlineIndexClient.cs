using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kachalka;

// Reads the shared catalog without bringing a browser engine into the desktop app.
// A short cooldown keeps an unavailable or private Site from slowing every card.
public sealed class OnlineIndexClient(SourceClient client,Uri? baseUri=null)
{
    public static readonly Uri PublishedSite=new("https://kachalka-index-2026.peregon.chatgpt.site/");
    readonly Uri site=baseUri??PublishedSite;
    long retryAfterTicks;

    public void RetryNow()=>Interlocked.Exchange(ref retryAfterTicks,0);

    async Task<JsonDocument> Get(string path,CancellationToken ct)
    {
        if(DateTime.UtcNow.Ticks<Interlocked.Read(ref retryAfterTicks))throw new HttpRequestException("Онлайн-индекс временно недоступен.");
        try
        {
            var bytes=await client.Read(new Uri(site,path),4*1024*1024,ct);
            return JsonDocument.Parse(bytes);
        }
        catch(HttpRequestException error) when(error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            Interlocked.Exchange(ref retryAfterTicks,DateTime.UtcNow.AddMinutes(15).Ticks);
            throw;
        }
        catch(HttpRequestException)
        {
            Interlocked.Exchange(ref retryAfterTicks,DateTime.UtcNow.AddSeconds(30).Ticks);
            throw;
        }
        catch(Exception error) when(error is JsonException or IOException || error is OperationCanceledException && !ct.IsCancellationRequested)
        {
            Interlocked.Exchange(ref retryAfterTicks,DateTime.UtcNow.AddSeconds(30).Ticks);
            throw;
        }
    }

    static string? String(JsonElement row,string key)=>row.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
    static int Integer(JsonElement row,string key)=>row.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out var number)?number:0;
    static long? Long(JsonElement row,string key)=>row.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt64(out var number)&&number>0?number:null;
    static int? Count(JsonElement row,string key)=>row.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out var number)&&number>=0?number:null;
    static string Score(string? value)=>value!=null&&Regex.IsMatch(value,@"^\d{1,2}([.,]\d)?$")?value:"—";
    static string? Https(string? value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&string.IsNullOrEmpty(uri.UserInfo)?uri.AbsoluteUri:null;
    static string? SectionKey(string section)=>section switch{"Фильмы"=>"movies","Сериалы"=>"series",_=>null};
    static string? PathPart(string section)=>section switch{"Фильмы"=>"/movies/","Сериалы"=>"/tvseries/",_=>null};
    static bool Slug(string value)=>value.Length is >0 and <=120&&value.All(c=>char.IsLetterOrDigit(c)||c=='-');

    public static string? IdFor(MediaItem item)
    {
        var section=SectionKey(item.Section);var path=PathPart(item.Section);
        if(section==null||path==null)return null;
        if(item.OnlineId?.StartsWith(section+":",StringComparison.Ordinal)==true&&Slug(item.OnlineId[(section.Length+1)..]))return item.OnlineId;
        if(!Uri.TryCreate(item.PageUrl,UriKind.Absolute,out var page)||page.Scheme!="https"||page.Host!="w6.zona.plus"||!page.AbsolutePath.StartsWith(path,StringComparison.Ordinal))return null;
        var slug=Uri.UnescapeDataString(page.AbsolutePath[path.Length..].TrimEnd('/'));
        return Slug(slug)?section+":"+slug:null;
    }

    internal static MediaItem? Media(JsonElement row,string section)
    {
        if(String(row,"section")!=SectionKey(section))return null;
        var title=String(row,"title")?.Trim();var onlineId=String(row,"id");var pageText=String(row,"pageUrl");var path=PathPart(section);
        if(string.IsNullOrWhiteSpace(title)||onlineId==null||path==null||!Uri.TryCreate(pageText,UriKind.Absolute,out var page)||page.Scheme!="https"||page.Host!="w6.zona.plus"||!page.AbsolutePath.StartsWith(path,StringComparison.Ordinal))return null;
        var slug=Uri.UnescapeDataString(page.AbsolutePath[path.Length..].TrimEnd('/'));
        if(!Slug(slug)||onlineId!=SectionKey(section)+":"+slug)return null;
        var url=page.AbsoluteUri;
        var hash=SHA256.HashData(Encoding.UTF8.GetBytes(url));var id=-(BitConverter.ToInt32(hash,0)&int.MaxValue);
        return new MediaItem(id,title,section,"",Integer(row,"year"),Score(String(row,"kinopoisk")),Score(String(row,"imdb")),"#526B69")
        {
            OnlineId=onlineId,PageUrl=url,ImageUrl=Https(String(row,"poster")),
            OriginalTitle=String(row,"originalTitle"),Description=String(row,"description")
        };
    }

    public async Task<IReadOnlyList<MediaItem>> Browse(string section,string query,int page,CancellationToken ct)
    {
        var key=SectionKey(section)??throw new ArgumentOutOfRangeException(nameof(section));
        var path="api/catalog?section="+key+"&q="+Uri.EscapeDataString(query.Trim())+"&page="+Math.Clamp(page,1,CatalogPaging.Limit);
        using var json=await Get(path,ct);
        if(!json.RootElement.TryGetProperty("items",out var items)||items.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Онлайн-индекс вернул неверный каталог.");
        return items.EnumerateArray().Take(80).Where(x=>x.ValueKind==JsonValueKind.Object).Select(x=>Media(x,section)).OfType<MediaItem>().DistinctBy(x=>x.Id).ToArray();
    }

    public async Task<CatalogPage> BrowsePage(string section,int page,CancellationToken ct)
    {
        var key=SectionKey(section)??throw new ArgumentOutOfRangeException(nameof(section));
        using var json=await Get("api/catalog?section="+key+"&page="+Math.Clamp(page,1,CatalogPaging.Limit),ct);
        if(!json.RootElement.TryGetProperty("items",out var rows)||rows.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Онлайн-индекс вернул неверный каталог.");
        var items=rows.EnumerateArray().Take(CatalogPaging.Size).Where(x=>x.ValueKind==JsonValueKind.Object).Select(x=>Media(x,section)).OfType<MediaItem>().DistinctBy(x=>x.Id).ToArray();
        var hasNext=json.RootElement.TryGetProperty("hasMore",out var next)&&next.ValueKind is JsonValueKind.True or JsonValueKind.False?next.GetBoolean():items.Length==CatalogPaging.Size;
        return new(items,hasNext&&page<CatalogPaging.Limit,[],[]);
    }

    public async Task<MediaItem> Detail(MediaItem item,CancellationToken ct)
    {
        var id=IdFor(item)??throw new InvalidDataException("Карточка не связана с онлайн-индексом.");
        using var json=await Get("api/media?id="+Uri.EscapeDataString(id),ct);
        if(!json.RootElement.TryGetProperty("item",out var row)||row.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Онлайн-индекс вернул неверную карточку.");
        var fresh=Media(row,item.Section);
        if(fresh==null||fresh.Id!=item.Id)throw new InvalidDataException("Онлайн-индекс вернул другую карточку.");
        return item with
        {
            OnlineId=fresh.OnlineId,Title=fresh.Title,Year=fresh.Year>0?fresh.Year:item.Year,
            ImageUrl=fresh.ImageUrl??item.ImageUrl,OriginalTitle=fresh.OriginalTitle??item.OriginalTitle,
            Description=fresh.Description??item.Description,
            Kinopoisk=fresh.Kinopoisk=="—"?item.Kinopoisk:fresh.Kinopoisk,
            Imdb=fresh.Imdb=="—"?item.Imdb:fresh.Imdb
        };
    }

    static string? Torrent(string? value)
    {
        if(value==null)return null;
        if(value.StartsWith("magnet:?",StringComparison.OrdinalIgnoreCase)&&Regex.IsMatch(value,@"(?i)(?:^|[?&])xt=urn:btih:[a-f0-9]{40}(?:&|$)"))return value;
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo))return null;
        if(uri.AbsolutePath.EndsWith(".torrent",StringComparison.OrdinalIgnoreCase))return uri.AbsoluteUri;
        if(uri.Host=="nnmclub.to"&&uri.AbsolutePath=="/forum/download.php"&&uri.Fragment.Length==0&&Regex.IsMatch(uri.Query,@"^\?id=\d{1,12}$"))return uri.AbsoluteUri;
        if(uri.Host=="megapeer.vip"&&uri.Query.Length==0&&uri.Fragment.Length==0&&Regex.IsMatch(uri.AbsolutePath,@"^/download/\d{1,12}$"))return uri.AbsoluteUri;
        return uri.Host is "knaben.org" or "knaben.eu"&&uri.AbsolutePath.StartsWith("/live/dl/",StringComparison.Ordinal)?uri.AbsoluteUri:null;
    }

    static SourceEntry? Release(JsonElement row,string expectedId)
    {
        if(String(row,"mediaId")!=expectedId)return null;
        var title=String(row,"title")?.Trim();var source=String(row,"source")?.Trim();var id=String(row,"id")?.Trim();
        if(string.IsNullOrWhiteSpace(title)||string.IsNullOrWhiteSpace(source)||string.IsNullOrWhiteSpace(id))return null;
        var torrent=Torrent(String(row,"torrentUrl"));
        var page=Https(String(row,"pageUrl"))??"";
        if(torrent==null)
        {
            if(String(row,"torrentUrl")!=null||source!="Internet Archive"||!Uri.TryCreate(page,UriKind.Absolute,out var archive)||archive.Host!="archive.org"||!archive.AbsolutePath.StartsWith("/details/",StringComparison.Ordinal))return null;
            id=Uri.UnescapeDataString(archive.AbsolutePath["/details/".Length..].TrimEnd('/'));
            if(id.Length==0||id.Contains('/'))return null;
        }
        return new SourceEntry(id,title,source,page,torrent,null,Long(row,"size"),Count(row,"seeds")){Via=String(row,"via")};
    }

    public async Task<IReadOnlyList<SourceEntry>> Releases(MediaItem item,CancellationToken ct)
    {
        var id=IdFor(item)??throw new InvalidDataException("Карточка не связана с онлайн-индексом.");
        using var json=await Get("api/releases?id="+Uri.EscapeDataString(id),ct);
        if(!json.RootElement.TryGetProperty("items",out var items)||items.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Онлайн-индекс вернул неверные раздачи.");
        return items.EnumerateArray().Take(300).Where(x=>x.ValueKind==JsonValueKind.Object).Select(x=>Release(x,id)).OfType<SourceEntry>().ToArray();
    }
}
