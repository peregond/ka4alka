using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Text.Json;
using HtmlAgilityPack;
namespace Kachalka;
public sealed class LiveCatalog(SourceClient client)
{
    record DetailSnapshot(string Kinopoisk,string Imdb,string Description,string? OriginalTitle=null,CinemaPerson[]? People=null,CinemaCollection[]? Collections=null);
    public const string Base="https://w6.zona.plus";
    static HtmlDocument Html(byte[] b){var h=new HtmlDocument();h.LoadHtml(Encoding.UTF8.GetString(b));return h;}
    static string Text(HtmlNode? n)=>HtmlEntity.DeEntitize(n?.InnerText??"").Trim();
    static string Class(string value)=>"contains(concat(' ',normalize-space(@class),' '),' "+value+" ')";
    public static IReadOnlyList<MediaItem> Parse(byte[] bytes,string section)
    {
        var h=Html(bytes);var cards=h.DocumentNode.SelectNodes("//li["+Class("results-item-wrap")+"]");if(cards==null)return [];
        return cards.Select(n=>{
            var a=n.SelectSingleNode(".//a[@itemprop='url']");var title=Text(n.SelectSingleNode(".//*[@itemprop='name']"));var path=a?.GetAttributeValue("href","")??"";if(title.Length==0||!path.StartsWith(section=="Сериалы"?"/tvseries/":"/movies/",StringComparison.Ordinal))return null;
            var url=new Uri(new Uri(Base),path).AbsoluteUri;var hash=System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(url));var id=-(BitConverter.ToInt32(hash,0)&int.MaxValue);
            int.TryParse(Text(n.SelectSingleNode(".//*["+Class("results-item-year")+"]")),out int year);
            return new MediaItem(id,title,section,"",year,"—","—","#526B69"){PageUrl=url,ImageUrl=n.SelectSingleNode(".//meta[@itemprop='image']")?.GetAttributeValue("content","")};
        }).Where(x=>x!=null).Cast<MediaItem>().Take(40).ToArray();
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
        try
        {
            var bytes=await client.Read(SourceClient.WebUri(item.PageUrl!),4*1024*1024,ct);
            var h=Html(bytes);
            string Score(string name){var v=Text(h.DocumentNode.SelectSingleNode("//*["+Class(name)+"]"));return Regex.IsMatch(v,@"^\d{1,2}([.,]\d)?$")?v:"—";}
            var original=HtmlEntity.DeEntitize(h.DocumentNode.SelectSingleNode("//meta[@itemprop='alternativeHeadline']")?.GetAttributeValue("content","")??"").Trim();
            var result=item with{Kinopoisk=Score("entity-rating-kp"),Imdb=Score("entity-rating-imdb"),Description=Text(h.DocumentNode.SelectSingleNode("//*[@itemprop='description' and not(self::meta)]")),OriginalTitle=original.Length==0?null:original,People=CinemaMetadata.People(bytes),Collections=CinemaMetadata.Collections(bytes)};
            try{System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cache)!);await System.IO.File.WriteAllTextAsync(cache,JsonSerializer.Serialize(new DetailSnapshot(result.Kinopoisk,result.Imdb,result.Description??"",result.OriginalTitle,result.People,result.Collections)),ct);}catch(System.IO.IOException){}
            return result;
        }
        catch(Exception) when(!ct.IsCancellationRequested&&System.IO.File.Exists(cache))
        {
            var saved=JsonSerializer.Deserialize<DetailSnapshot>(await System.IO.File.ReadAllTextAsync(cache,ct))!;
            return item with{Kinopoisk=saved.Kinopoisk,Imdb=saved.Imdb,Description=saved.Description,OriginalTitle=saved.OriginalTitle,People=saved.People??item.People,Collections=saved.Collections??item.Collections};
        }
    }
    public async Task<PersonProfile> Person(CinemaPerson person,CancellationToken ct)
    {
        var url=CinemaMetadata.CatalogUrl(person.PageUrl,"/persons/","/person/","/people/")??throw new ArgumentException("Неверная карточка человека.");
        var key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        var cache=System.IO.Path.Combine(Preferences.DataDir,"people",key+".html");
        try
        {
            var bytes=await client.Read(new Uri(url),4*1024*1024,ct);
            var result=CinemaMetadata.Person(bytes,person);
            if(result.Description.Length==0&&result.Filmography.Length==0)throw new System.IO.InvalidDataException("Источник не вернул сведения о человеке.");
            try{System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cache)!);await System.IO.File.WriteAllBytesAsync(cache,bytes,ct);}catch(System.IO.IOException){}
            return result;
        }
        catch(Exception) when(!ct.IsCancellationRequested&&System.IO.File.Exists(cache)){return CinemaMetadata.Person(await System.IO.File.ReadAllBytesAsync(cache,ct),person);}
    }
    public async Task<IReadOnlyList<SourceEntry>> Releases(string title,CancellationToken ct)
    {
        var bytes=await client.Read(new Uri("https://rutor.info/search/0/0/000/0/"+Uri.EscapeDataString(title)),4*1024*1024,ct);return ParseRutor(bytes);
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
