using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.IO;
using System.Text.Json;
using HtmlAgilityPack;
namespace Kachalka;
public sealed class LiveCatalog(SourceClient client)
{
    record DetailSnapshot(string Kinopoisk,string Imdb,string Description,string? OriginalTitle=null,string Genre="",string Country="",string[]? GenreKeys=null,string[]? CountryKeys=null);
    public const string Base="https://w6.zona.plus";
    static HtmlDocument Html(byte[] b){var h=new HtmlDocument();h.LoadHtml(Encoding.UTF8.GetString(b));return h;}
    static string Text(HtmlNode? n)=>HtmlEntity.DeEntitize(n?.InnerText??"").Trim();
    static string Class(string value)=>"contains(concat(' ',normalize-space(@class),' '),' "+value+" ')";
    public static CatalogPage ParsePage(byte[] bytes,string section,int page)
    {
        var html=Html(bytes);
        CatalogChoice[] Choices(string prefix)=> (html.DocumentNode.SelectNodes("//option[starts-with(@value,'"+prefix+"-')]")??new HtmlNodeCollection(null))
            .Select(n=>new CatalogChoice(n.GetAttributeValue("value","")[(prefix.Length+1)..],Text(n))).DistinctBy(x=>x.Key).ToArray();
        var hasNext=(html.DocumentNode.SelectNodes("//a[@href] | //link[@href and @rel='next']")??new HtmlNodeCollection(null)).Any(n=>Regex.IsMatch(HtmlEntity.DeEntitize(n.GetAttributeValue("href","")),@"[?&]page="+(page+1)+@"(?:&|$)"));
        return new(Parse(bytes,section).DistinctBy(x=>x.Id).ToArray(),hasNext&&page<CatalogPaging.Limit,Choices("genre"),Choices("country"));
    }
    public async Task<CatalogPage> BrowsePage(string section,int page,CatalogSelection selection,CancellationToken ct,bool forceRefresh=false)
    {
        var path=section=="Сериалы"?"/tvseries":"/movies";
        var filter=selection.Filter;
        async Task<CatalogPage> SourcePage(int sourcePage)
        {
            var url=Base+path+(filter.Length>0?"/filter/"+filter:"")+"?page="+sourcePage;
            var cache=System.IO.Path.Combine(Preferences.DataDir,"catalog",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(url)))+".html");
            if(!forceRefresh&&System.IO.File.Exists(cache)&&DateTime.UtcNow-System.IO.File.GetLastWriteTimeUtc(cache)<TimeSpan.FromMinutes(15))
                return ParsePage(await System.IO.File.ReadAllBytesAsync(cache,ct),section,sourcePage);
            try
            {
                var bytes=await client.Read(new Uri(url),4*1024*1024,ct);var parsed=ParsePage(bytes,section,sourcePage);
                if(parsed.Items.Length==0&&parsed.Genres.Length==0)throw new System.IO.InvalidDataException("Источник вернул страницу без каталога.");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cache)!);await System.IO.File.WriteAllBytesAsync(cache,bytes,ct);return parsed;
            }
            catch(Exception) when(!ct.IsCancellationRequested&&System.IO.File.Exists(cache)){return ParsePage(await System.IO.File.ReadAllBytesAsync(cache,ct),section,sourcePage);}
        }
        var window=CatalogPaging.SourceWindow(page);
        var first=await SourcePage(window.Page);
        var rows=first.Items.Skip(window.Offset).ToList();
        var tail=first;
        if(rows.Count<CatalogPaging.Size&&first.HasNext){tail=await SourcePage(window.Page+1);rows.AddRange(tail.Items);}
        return new(rows.Take(CatalogPaging.Size).ToArray(),page<CatalogPaging.Limit&&(rows.Count>CatalogPaging.Size||tail.HasNext),first.Genres,first.Countries);
    }
    public static IReadOnlyList<MediaItem> Parse(byte[] bytes,string section)
    {
        var h=Html(bytes);var cards=h.DocumentNode.SelectNodes("//li["+Class("results-item-wrap")+"]");if(cards==null)return [];
        return cards.Select(n=>{
            var a=n.SelectSingleNode(".//a[@itemprop='url']");var title=Text(n.SelectSingleNode(".//*[@itemprop='name']"));var path=a?.GetAttributeValue("href","")??"";if(title.Length==0||!path.StartsWith(section=="Сериалы"?"/tvseries/":"/movies/",StringComparison.Ordinal))return null;
            var url=new Uri(new Uri(Base),path).AbsoluteUri;var hash=System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(url));var id=-(BitConverter.ToInt32(hash,0)&int.MaxValue);
            int.TryParse(Text(n.SelectSingleNode(".//*["+Class("results-item-year")+"]")),out int year);
            return new MediaItem(id,title,section,"",year,"—","—","#526B69"){PageUrl=url,ImageUrl=n.SelectSingleNode(".//meta[@itemprop='image']")?.GetAttributeValue("content","")};
        }).Where(x=>x!=null).Cast<MediaItem>().Take(CatalogPaging.SourceSize).ToArray();
    }
    public async Task<IReadOnlyList<MediaItem>> Browse(string section,string query,int page,CancellationToken ct)
    {
        var path=section=="Сериалы"?"/tvseries":"/movies";
        var url=string.IsNullOrWhiteSpace(query)?Base+path+"/filter/sort-date?page="+page:Base+"/search-form?query="+Uri.EscapeDataString(query);
        var cache=System.IO.Path.Combine(Preferences.DataDir,"catalog",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(url)))+".html");
        try{var bytes=await client.Read(new Uri(url),4*1024*1024,ct);var parsed=Parse(bytes,section);if(parsed.Count>0){System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cache)!);await System.IO.File.WriteAllBytesAsync(cache,bytes,ct);}return parsed;}
        catch(Exception) when(!ct.IsCancellationRequested&&System.IO.File.Exists(cache)){return Parse(await System.IO.File.ReadAllBytesAsync(cache,ct),section);}
    }
    public async Task<MediaItem> Detail(MediaItem item,CancellationToken ct)
    {
        var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(item.PageUrl!)));
        var cache=System.IO.Path.Combine(Preferences.DataDir,"details",key+".json");
        static string ValidScore(string value)=>Regex.IsMatch(value,@"^\d{1,2}([.,]\d)?$")&&double.TryParse(value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var score)&&score is >0 and <=10?value:"—";
        try
        {
            var h=Html(await client.Read(SourceClient.WebUri(item.PageUrl!),4*1024*1024,ct));
            string Score(string name)=>ValidScore(Text(h.DocumentNode.SelectSingleNode("//*["+Class(name)+"]")));
            var original=HtmlEntity.DeEntitize(h.DocumentNode.SelectSingleNode("//meta[@itemprop='alternativeHeadline']")?.GetAttributeValue("content","")??"").Trim();
            var genres=(h.DocumentNode.SelectNodes("//*[@itemprop='genre']")??new HtmlNodeCollection(null)).Select(Text).Distinct().ToArray();
            HtmlNode[] Links(string prefix)=>(h.DocumentNode.SelectNodes("//a[contains(@href,'/filter/"+prefix+"-')]")??new HtmlNodeCollection(null)).ToArray();
            string[] Keys(string prefix)=>Links(prefix).Select(n=>n.GetAttributeValue("href","").Split("/filter/"+prefix+"-")[1]).Distinct().ToArray();
            var result=item with{Kinopoisk=Score("entity-rating-kp"),Imdb=Score("entity-rating-imdb"),Description=Text(h.DocumentNode.SelectSingleNode("//*[@itemprop='description' and not(self::meta)]")),OriginalTitle=original.Length==0?null:original,Genre=string.Join(", ",genres),Country=string.Join(", ",Links("country").Select(Text).Distinct()),GenreKeys=Keys("genre"),CountryKeys=Keys("country")};
            try{System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cache)!);await System.IO.File.WriteAllTextAsync(cache,JsonSerializer.Serialize(new DetailSnapshot(result.Kinopoisk,result.Imdb,result.Description??"",result.OriginalTitle,result.Genre,result.Country,result.GenreKeys,result.CountryKeys)),ct);}catch(System.IO.IOException){}
            return result;
        }
        catch(Exception) when(!ct.IsCancellationRequested&&System.IO.File.Exists(cache))
        {
            var saved=JsonSerializer.Deserialize<DetailSnapshot>(await System.IO.File.ReadAllTextAsync(cache,ct))!;
            return item with{Kinopoisk=ValidScore(saved.Kinopoisk)=="—"?item.Kinopoisk:saved.Kinopoisk,Imdb=ValidScore(saved.Imdb)=="—"?item.Imdb:saved.Imdb,Description=saved.Description,OriginalTitle=saved.OriginalTitle??item.OriginalTitle,Genre=saved.Genre,Country=saved.Country,GenreKeys=saved.GenreKeys??[],CountryKeys=saved.CountryKeys??[]};
        }
    }
    public async Task<IReadOnlyList<SourceEntry>> Releases(string title,CancellationToken ct)
    {
        Exception? failure=null;
        foreach(var host in new[]{"rutor.info","rutor.is"})
        {
            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(7));
                var bytes=await client.Read(new Uri("https://"+host+"/search/0/0/000/0/"+Uri.EscapeDataString(title)),4*1024*1024,timeout.Token);
                var rows=ParseRutor(bytes);if(rows.Count>0)return rows;
                if(Html(bytes).DocumentNode.SelectSingleNode("//div[@id='index']")==null)throw new InvalidDataException("RuTor returned no search index.");
                return rows;
            }
            catch(Exception error)when(!ct.IsCancellationRequested){failure=error;}
        }
        ct.ThrowIfCancellationRequested();throw failure??new InvalidDataException("RuTor unavailable.");
    }
    public static bool Matches(MediaItem item,SourceEntry entry)
    {
        static string Normalize(string value)=>Regex.Replace(Regex.Replace(value.Replace('ё','е').Replace('Ё','Е').ToLowerInvariant(),@"[^\p{L}\p{N}]+"," ").Trim(),@"\s+"," ");
        var title=Normalize(item.Title);var original=Normalize(item.OriginalTitle??"");
        if(title.Length==0)return false;
        var candidate=Regex.Replace(entry.Title,@"^(?:\s*\[[^\]]{1,40}\]\s*)+","");
        var names=Regex.Split(candidate,@"\s+[/|]\s+").Select(Normalize).ToArray();
        if(item.Section=="Фильмы"&&item.Year>0)
        {
            foreach(var name in names)
            foreach(var expected in new[]{title,original}.Where(x=>x.Length>0))
            {
                if(!name.StartsWith(expected+" ",StringComparison.Ordinal))continue;
                var metadata=name[(expected.Length+1)..];var alias=expected==title?original:title;
                if(alias.Length>0&&metadata.StartsWith(alias+" ",StringComparison.Ordinal))metadata=metadata[(alias.Length+1)..];
                var year=Regex.Match(metadata,@"^(?:19|20)\d{2}\b");
                if(year.Success&&int.Parse(year.Value)!=item.Year)return false;
            }
        }
        bool MatchName(string name,string expected)
        {
            if(expected.Length==0||!name.StartsWith(expected,StringComparison.Ordinal)||name.Length>expected.Length&&name[expected.Length]!=' ')return false;
            var tail=name.Length==expected.Length?"":name[(expected.Length+1)..];
            if(tail.Length==0)return true;
            var other=expected==title?original:title;
            if(tail.Length>0&&char.IsDigit(tail[0]))
            {
                if(Regex.IsMatch(tail,@"^(?:720p|1080p|2160p|4k)\b"))return true;
                if(item.Section=="Сериалы")return Regex.IsMatch(tail,@"^\d{1,2}\s+(?:сезон|season)\b")||Regex.IsMatch(tail,@"^\d{3,4}\b");
                return Regex.IsMatch(tail,@"^\d{4}\b");
            }
            if(other.Length>0&&(tail==other||tail.StartsWith(other+" ",StringComparison.Ordinal)))return true;
            return Regex.IsMatch(tail,@"^(?:s\d{1,2}(?:e\d{1,3})?|e\d{1,3}|season|сезон|серия|серии|episode|ep|complete|полный|все|web|hdtv|dvdrip|hdrip|bdrip|bluray|remux|720p|1080p|2160p|4k|x264|x265|h264|h265|hevc|avc|mkv|mp4|avi|repack)\b");
        }
        if(!names.Any(name=>MatchName(name,title)||original.Length>0&&MatchName(name,original)))return false;
        return item.Section=="Сериалы"||entry.Source!="RuTor"||item.Year<=0||Regex.IsMatch(entry.Title,@"(?<!\d)"+item.Year+@"(?!\d)");
    }
    public static IReadOnlyList<SourceEntry> ParseRutor(byte[] bytes)
    {
        var h=Html(bytes);var rows=h.DocumentNode.SelectNodes("//div[@id='index']//tr");if(rows==null)return [];
        return rows.Select(r=>{var a=r.SelectSingleNode(".//a[starts-with(@href,'/torrent/')]");var magnet=r.SelectSingleNode(".//a[starts-with(@href,'magnet:')]")?.GetAttributeValue("href","");if(a==null||magnet==null)return null;
            var url="https://rutor.info"+a.GetAttributeValue("href","");var seed=Text(r.SelectSingleNode(".//span["+Class("green")+"]"));int.TryParse(seed,out int count);
            var cells=r.SelectNodes("./td");var sizeText=cells is {Count:>=2}?Text(cells[^2]):"";var match=Regex.Match(sizeText,@"(?i)(\d+(?:[.,]\d+)?)\s*(GB|MB|ГБ|МБ)");long? size=null;if(match.Success&&double.TryParse(match.Groups[1].Value.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var number))size=(long)(number*(match.Groups[2].Value.Equals("GB",StringComparison.OrdinalIgnoreCase)||match.Groups[2].Value.Equals("ГБ",StringComparison.OrdinalIgnoreCase)?1073741824d:1048576d));
            return new SourceEntry(url,Text(a),"RuTor",url,HtmlEntity.DeEntitize(magnet),null,size,count);
        }).Where(x=>x!=null).Cast<SourceEntry>().Take(50).ToArray();
    }
}
