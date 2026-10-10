using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kachalka;

// Reads the shared catalog without bringing a browser engine into the desktop app.
// The same site runs on chatgpt.site and on Railway. Addresses in Russia and
// Belarus, which chatgpt.site refuses, start with Railway; everyone else starts
// with chatgpt.site. The other copy answers when the first does not, and a short
// cooldown keeps an unavailable or private copy from slowing every card.
public sealed class OnlineIndexClient(SourceClient client,Uri? baseUri=null,IndexRelays? relays=null,Func<CancellationToken,Task<string?>>? country=null)
{
    public static readonly Uri ChatGptSite=new("https://ka4alka-online-new.peregon.chatgpt.site/");
    public static readonly Uri RailwaySite=new("https://web-production-d7aa7.up.railway.app/");
    public static readonly IReadOnlyList<Uri> PublishedSites=[ChatGptSite,RailwaySite];
    public static Uri PublishedSite=>ChatGptSite;
    public static bool IsPublishedSite(Uri uri)=>PublishedSites.Any(site=>site.Host==uri.Host);
    readonly Uri[] sites=baseUri!=null?[baseUri]:[..PublishedSites];
    readonly long[] retryAfterTicks=new long[baseUri!=null?1:PublishedSites.Count];
    readonly Func<CancellationToken,Task<string?>> countryOf=country??(ct=>SiteCountry.Get(client,ct));
    bool CoolingDown(int index)=>DateTime.UtcNow.Ticks<Interlocked.Read(ref retryAfterTicks[index]);
    void Cool(int index,TimeSpan span)=>Interlocked.Exchange(ref retryAfterTicks[index],DateTime.UtcNow.Add(span).Ticks);
    async Task<int[]> Available(CancellationToken ct)
    {
        var order=Enumerable.Range(0,sites.Length);
        if(baseUri==null&&SiteCountry.Blocked(await countryOf(ct)))order=order.OrderByDescending(index=>sites[index]==RailwaySite);
        return order.Where(index=>!CoolingDown(index)).ToArray();
    }

    public void RetryNow(){for(var index=0;index<retryAfterTicks.Length;index++)Interlocked.Exchange(ref retryAfterTicks[index],0);}

    // Reads one path from the first copy of the site that answers. A refusal
    // (401/403, the country block) rests that copy for 15 minutes, any other
    // failure but 404 for 30 seconds, or 5 minutes once another copy has answered.
    public async Task<byte[]> ReadSite(string path,int limit,CancellationToken ct,Action<byte[]>? validate=null)
    {
        var order=await Available(ct);
        if(order.Length==0)throw new HttpRequestException("Онлайн-индекс временно недоступен.");
        var failed=new List<int>();Exception? failure=null;
        foreach(var index in order)
        {
            try
            {
                var bytes=await client.Read(new Uri(sites[index],path),limit,ct);
                validate?.Invoke(bytes);
                Answered(failed);
                return bytes;
            }
            catch(Exception error) when(!ct.IsCancellationRequested&&error is HttpRequestException or JsonException or IOException or InvalidDataException or OperationCanceledException)
            {
                if(Rest(index,error))failed.Add(index);
                failure=error;
            }
        }
        throw failure!;
    }

    // 404 is an answer about this path (a missing backdrop, an older copy without the route), not an outage.
    bool Rest(int index,Exception error)
    {
        if(error is HttpRequestException{StatusCode:HttpStatusCode.NotFound})return false;
        Cool(index,error is HttpRequestException{StatusCode:HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden}?TimeSpan.FromMinutes(15):TimeSpan.FromSeconds(30));
        return true;
    }
    // Once another copy or relay has answered, the copies that failed rest longer.
    void Answered(List<int> failed)
    {
        foreach(var index in failed)if(Interlocked.Read(ref retryAfterTicks[index])<DateTime.UtcNow.AddMinutes(5).Ticks)Cool(index,TimeSpan.FromMinutes(5));
    }

    async Task<JsonDocument> Get(string path,CancellationToken ct)
    {
        JsonDocument? json=null;
        await ReadSite(path,4*1024*1024,ct,bytes=>json=JsonDocument.Parse(bytes));
        return json!;
    }

    static string? String(JsonElement row,string key)=>row.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
    static int Integer(JsonElement row,string key)=>row.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out var number)?number:0;
    static long? Long(JsonElement row,string key)=>row.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt64(out var number)&&number>0?number:null;
    static int? Count(JsonElement row,string key)=>row.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out var number)&&number>=0?number:null;
    static string Score(string? value)=>value!=null&&Regex.IsMatch(value,@"^\d{1,2}([.,]\d)?$")&&double.TryParse(value.Replace(',','.'),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var score)&&score is >0 and <=10?value:"—";
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

    static T[] MetadataArray<T>(JsonElement row,string name)
    {
        if(!row.TryGetProperty(name,out var value)||value.ValueKind!=JsonValueKind.Array)return [];
        try{return JsonSerializer.Deserialize<T[]>(value.GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})?.Where(x=>x!=null).Take(100).ToArray()??[];}
        catch(JsonException){return [];}
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
            OriginalTitle=String(row,"originalTitle"),ImdbId=String(row,"imdbId"),Description=String(row,"description"),
            Country=String(row,"country")??"",CountryKeys=CatalogRegions.CountryKeys(MetadataArray<string>(row,"countryKeys")),
            CountryKeysComplete=!row.TryGetProperty("countryKeysComplete",out var completeCountries)||completeCountries.ValueKind!=JsonValueKind.False,
            People=MetadataArray<CinemaPerson>(row,"people").Where(x=>!string.IsNullOrWhiteSpace(x.Name)&&!string.IsNullOrWhiteSpace(x.Role)&&(x.PageUrl==""||CinemaMetadata.CatalogUrl(x.PageUrl,"/persons/","/person/","/people/")!=null)).ToArray(),
            Awards=MetadataArray<CinemaAward>(row,"awards").Where(x=>!string.IsNullOrWhiteSpace(x.Name)&&!string.IsNullOrWhiteSpace(x.Category)&&x.Year>0).ToArray(),
            Collections=MetadataArray<CinemaCollection>(row,"collections").Where(x=>!string.IsNullOrWhiteSpace(x.Name)&&CinemaMetadata.CatalogUrl(x.PageUrl,"/collections/","/franchise/")!=null).ToArray()
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
            ImdbId=fresh.ImdbId??item.ImdbId,
            Description=fresh.Description??item.Description,
            Country=fresh.Country.Length>0?fresh.Country:item.Country,CountryKeys=fresh.CountryKeys.Length>0?fresh.CountryKeys:item.CountryKeys,
            CountryKeysComplete=fresh.CountryKeys.Length>0?fresh.CountryKeysComplete:item.CountryKeysComplete,
            People=fresh.People.Length>0?fresh.People:item.People,Awards=fresh.Awards.Length>0?fresh.Awards:item.Awards,Collections=fresh.Collections.Length>0?fresh.Collections:item.Collections,
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
        if(uri.Host=="bigfangroup.org"&&uri.AbsolutePath=="/download.php"&&uri.Fragment.Length==0&&Regex.IsMatch(uri.Query,@"^\?id=\d{1,12}$"))return uri.AbsoluteUri;
        return uri.Host is "knaben.org" or "knaben.eu"&&uri.AbsolutePath.StartsWith("/live/dl/",StringComparison.Ordinal)?uri.AbsoluteUri:null;
    }

    // Trackers that some networks block (RuTor, NNM-Club, MegaPeer, BigFanGroup
    // and Nyaa are in the Russian registry) stay reachable through the site or a
    // standalone relay, which fetch only these exact download addresses.
    public static bool CanRelay(Uri torrent)
    {
        if(torrent.Scheme!="https"||!string.IsNullOrEmpty(torrent.UserInfo)||!torrent.IsDefaultPort||torrent.Fragment.Length>0)return false;
        return torrent.Host switch
        {
            "nnmclub.to"=>torrent.AbsolutePath=="/forum/download.php"&&Regex.IsMatch(torrent.Query,@"^\?id=\d{1,12}$"),
            "megapeer.vip"=>torrent.Query.Length==0&&Regex.IsMatch(torrent.AbsolutePath,@"^/download/\d{1,12}$"),
            "bigfangroup.org"=>torrent.AbsolutePath=="/download.php"&&Regex.IsMatch(torrent.Query,@"^\?id=\d{1,12}$"),
            "nyaa.si"=>torrent.Query.Length==0&&Regex.IsMatch(torrent.AbsolutePath,@"^/download/\d{1,12}\.torrent$"),
            _=>false
        };
    }
    public static Uri RelayUri(Uri torrent,Uri? site=null)=>CanRelay(torrent)
        ?new(site??PublishedSite,"api/torrent?url="+Uri.EscapeDataString(torrent.AbsoluteUri))
        :throw new ArgumentException("Этот torrent-файл нельзя получить через онлайн-индекс.",nameof(torrent));
    // Runs alongside the direct request, so a slow or unavailable site never
    // delays a reachable tracker. Each copy of the site is tried first unless it
    // has just refused, then each standalone relay.
    public async Task<byte[]> RelayTorrent(Uri torrent,CancellationToken ct)
    {
        var hosts=new List<(Uri Host,int Site)>();
        hosts.AddRange((await Available(ct)).Select(index=>(sites[index],index)));
        if(relays!=null)hosts.AddRange((await relays.ListAsync(ct)).Select(relay=>(relay,-1)));
        if(hosts.Count==0)hosts.Add((sites[0],0));
        var failed=new List<int>();Exception? failure=null;
        foreach(var (host,index) in hosts)
        {
            try
            {
                var bytes=await client.Read(RelayUri(torrent,host),SourceClient.TorrentLimit,ct);
                // A block page with status 200 or a damaged file moves on to the next copy or relay.
                try{MonoTorrent.Torrent.Load(bytes);}
                catch(Exception error) when(error is not OperationCanceledException){throw new InvalidDataException("Онлайн-индекс вернул не torrent-файл.",error);}
                Answered(failed);
                return bytes;
            }
            catch(Exception error) when(!ct.IsCancellationRequested&&error is HttpRequestException or IOException or InvalidDataException or OperationCanceledException)
            {
                failure=error;
                if(index>=0&&Rest(index,error))failed.Add(index);
            }
        }
        throw failure!;
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
        return new SourceEntry(id,title,source,page,torrent,null,Long(row,"size"),Count(row,"seeds")){Via=String(row,"via"),Leechers=Count(row,"leechers")};
    }

    // Catalog-wide quality checks read the site only. An opened card may also ask
    // a standalone relay when the site does not answer (allowRelay).
    public async Task<IReadOnlyList<SourceEntry>> Releases(MediaItem item,CancellationToken ct,bool allowRelay=false)
    {
        var id=IdFor(item)??throw new InvalidDataException("Карточка не связана с онлайн-индексом.");
        try
        {
            using var json=await Get("api/releases?id="+Uri.EscapeDataString(id),ct);
            return ReleaseRows(json,id);
        }
        catch(Exception error) when(allowRelay&&relays!=null&&!ct.IsCancellationRequested&&error is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException)
        {
            var query="api/search?id="+Uri.EscapeDataString(id)+"&title="+Uri.EscapeDataString(item.Title)+"&original="+Uri.EscapeDataString(item.OriginalTitle??"")+"&year="+Math.Clamp(item.Year,0,2100);
            foreach(var relay in await relays.ListAsync(ct))
            {
                try
                {
                    using var json=JsonDocument.Parse(await client.Read(new Uri(relay,query),4*1024*1024,ct));
                    return ReleaseRows(json,id);
                }
                catch(Exception relayError) when(!ct.IsCancellationRequested&&relayError is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException)
                {
                    DiagnosticLog.Write("index-relay-failed",new{Relay=relay.Host,Error=relayError.GetType().Name,Status=(relayError as HttpRequestException)?.StatusCode?.ToString()});
                }
            }
            throw;
        }
    }

    static IReadOnlyList<SourceEntry> ReleaseRows(JsonDocument json,string id)
    {
        if(!json.RootElement.TryGetProperty("items",out var items)||items.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Онлайн-индекс вернул неверные раздачи.");
        // Catalog quality/availability read this API directly too. Record this
        // response receipt before any caller merges it with older saved rows.
        // The index's upstream tracker refresh time is deliberately not inferred.
        var receivedUtc=DateTime.UtcNow;
        return items.EnumerateArray().Take(300).Where(x=>x.ValueKind==JsonValueKind.Object).Select(x=>Release(x,id)).OfType<SourceEntry>()
            .Select(row=>row with{DataReceivedUtc=receivedUtc,DataProvider="Онлайн-индекс"}).ToArray();
    }
}
