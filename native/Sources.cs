using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
namespace Kachalka;

public record SourceConfig(string Name,string Endpoint,string ApiKey="");
public record SourceEntry(string Id,string Title,string Source,string PageUrl,string? TorrentUrl,string? ImageUrl,long? Size=null,int? Seeds=null)
{
    public string? Via {get;init;}
    public int? Leechers {get;init;}
    public SeriesReleaseInfo Series => SeriesReleaseInfo.Parse(Title);
    public string Quality => System.Text.RegularExpressions.Regex.IsMatch(Title,@"(?i)(2160[pi]?\b|\b4[ .-]?K\b|\bUHD\b)")?"4K":System.Text.RegularExpressions.Regex.IsMatch(Title,@"(?i)(1080[pi]?\b|\bFull[ .-]?HD\b)")?"Full HD":System.Text.RegularExpressions.Regex.IsMatch(Title,@"(?i)(720p?\b|HD[ .-]?Ready\b)")?"HD Ready":"Не указано";
    public string Type => System.Text.RegularExpressions.Regex.IsMatch(Title,@"(?i)(Blu.?Ray|BDRip|BDRemux)")?"BluRay":System.Text.RegularExpressions.Regex.IsMatch(Title,@"(?i)(WEB.?DL|WEBRip)")?"WEB-DL":"Не указано";
    public string Voice => System.Text.RegularExpressions.Regex.IsMatch(Title,@"(?i)(дубляж|дублирован|DUB)")?"Дубляж":System.Text.RegularExpressions.Regex.IsMatch(Title,@"(?i)(многоголос|MVO)")?"Многоголосая":System.Text.RegularExpressions.Regex.IsMatch(Title,@"(?i)(оригинал|Original|ENG)")?"Оригинал":"Не указано";
    public string Subs => System.Text.RegularExpressions.Regex.IsMatch(Title,@"(?i)(субтитр|subs|subbed)")?"Есть":"Не указано";
    public string Codec => System.Text.RegularExpressions.Regex.IsMatch(Title,@"(?i)(H[. ]?265|x265|HEVC)")?"H.265":System.Text.RegularExpressions.Regex.IsMatch(Title,@"(?i)(H[. ]?264|x264|AVC)")?"H.264":"Не указано";
    public string Hdr => System.Text.RegularExpressions.Regex.IsMatch(Title,@"(?i)(Dolby.?Vision|\bDV\b)")?"Dolby Vision":System.Text.RegularExpressions.Regex.IsMatch(Title,@"(?i)(HDR10|\bHDR\b)")?"HDR":"Не указано";
}
public sealed partial class SourceClient : IDisposable
{
    readonly HttpClient http;
    public SourceClient(HttpMessageHandler? handler=null){http=handler==null?new HttpClient():new HttpClient(handler);http.Timeout=TimeSpan.FromSeconds(25);http.DefaultRequestHeaders.UserAgent.ParseAdd("Kachalka/0.11");}
    public static Uri WebUri(string value){if(!Uri.TryCreate(value,UriKind.Absolute,out var u)||u.Scheme is not ("http" or "https")||!string.IsNullOrEmpty(u.UserInfo))throw new FormatException("Нужен адрес HTTP или HTTPS без логина в URL.");return u;}
    public async Task<byte[]> Read(Uri uri,int max,CancellationToken ct)
    {
        using var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength>max)throw new InvalidDataException("Ответ источника превышает допустимый размер.");
        await using var input=await response.Content.ReadAsStreamAsync(ct);using var output=new MemoryStream();var buffer=new byte[16384];int count;
        while((count=await input.ReadAsync(buffer,ct))!=0){if(output.Length+count>max)throw new InvalidDataException("Ответ источника превышает допустимый размер.");output.Write(buffer,0,count);}return output.ToArray();
    }
    public static Uri ArchiveQuery(string query,string category,int page)
    {
        var type=category switch{"Музыка"=>"audio","Игры" or "Программы"=>"software",_=>"movies"};
        var words=query.Split(' ',StringSplitOptions.RemoveEmptyEntries).Select(w=>"\""+w.Replace("\\","\\\\").Replace("\"","\\\"")+"\"");
        var q=$"mediatype:{type} AND -noindex:true"+(query.Trim().Length==0?"":" AND ("+string.Join(" AND ",words)+")");
        return new Uri("https://archive.org/advancedsearch.php?q="+Uri.EscapeDataString(q)+"&fl[]=identifier&fl[]=title&rows=24&page="+Math.Max(1,page)+"&sort[]=downloads+desc&output=json");
    }
    public async Task<IReadOnlyList<SourceEntry>> SearchArchive(string query,string category,int page,CancellationToken ct)
    {
        using var json=JsonDocument.Parse(await Read(ArchiveQuery(query,category,page),2*1024*1024,ct));
        return json.RootElement.GetProperty("response").GetProperty("docs").EnumerateArray().Select(x=>{
            var id=x.GetProperty("identifier").GetString()!;var escaped=Uri.EscapeDataString(id);var title=x.TryGetProperty("title",out var t)&&t.ValueKind==JsonValueKind.String?t.GetString()!:id;
            return new SourceEntry(id,title,"Internet Archive","https://archive.org/details/"+escaped,null,"https://archive.org/services/img/"+escaped);
        }).ToArray();
    }
    public async Task<SourceEntry> ResolveArchive(SourceEntry entry,CancellationToken ct)
    {
        using var json=JsonDocument.Parse(await Read(new Uri("https://archive.org/metadata/"+Uri.EscapeDataString(entry.Id)),8*1024*1024,ct));
        if(!json.RootElement.TryGetProperty("files",out var files))throw new InvalidDataException("Источник не вернул список файлов.");
        foreach(var file in files.EnumerateArray()){var name=file.GetProperty("name").GetString()!;if(name.EndsWith(".torrent",StringComparison.OrdinalIgnoreCase))return entry with{TorrentUrl="https://archive.org/download/"+Uri.EscapeDataString(entry.Id)+"/"+Uri.EscapeDataString(name)};}
        throw new InvalidOperationException("У этой записи нет доступного torrent-файла. Выбери другую запись.");
    }
    public async Task<IReadOnlyList<SourceEntry>> SearchTorznab(SourceConfig source,string query,CancellationToken ct)
    {
        var endpoint=WebUri(source.Endpoint);
        var sep=string.IsNullOrEmpty(endpoint.Query)?"?":"&";
        var uri=new Uri(endpoint.AbsoluteUri+sep+"t=search&q="+Uri.EscapeDataString(query)+"&limit=50&apikey="+Uri.EscapeDataString(source.ApiKey));
        return ParseTorznab(await Read(uri,4*1024*1024,ct),source.Name);
    }
    public static IReadOnlyList<SourceEntry> ParseTorznab(byte[] data,string source)
    {
        using var stream=new MemoryStream(data);using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=4*1024*1024});var xml=XDocument.Load(reader);
        if(xml.Root?.Name.LocalName=="error")throw new InvalidOperationException("Индексатор отклонил запрос. Проверь API-ключ и адрес.");
        if(xml.Root?.Name.LocalName!="rss")throw new InvalidDataException("Источник вернул не Torznab RSS.");
        return xml.Descendants("item").Take(50).Select(x=>{
            string? Attr(string name)=>x.Elements().FirstOrDefault(a=>a.Name.LocalName=="attr"&&(string?)a.Attribute("name")==name)?.Attribute("value")?.Value;
            var title=(string?)x.Element("title")??"Без названия";
            var link=Attr("magneturl")??(string?)x.Element("enclosure")?.Attribute("url")??(string?)x.Element("link");
            if(!Uri.TryCreate(link,UriKind.Absolute,out var uri)||uri.Scheme is not ("magnet" or "http" or "https"))return null;
            long.TryParse(Attr("size")??(string?)x.Element("enclosure")?.Attribute("length"),out var size);int? seeds=int.TryParse(Attr("seeders"),out var seedCount)?seedCount:null;
            var page=(string?)x.Element("comments");if(!Uri.TryCreate(page,UriKind.Absolute,out var p)||p.Scheme is not ("http" or "https"))page="";
            return new SourceEntry((string?)x.Element("guid")??link!,title,source,page!,link,null,size==0?null:size,seeds);
        }).Where(x=>x!=null).Cast<SourceEntry>().ToArray();
    }
    public async Task<string> TorrentFile(SourceEntry entry,CancellationToken ct)
    {
        if(entry.TorrentUrl==null)entry=await ResolveArchive(entry,ct);
        if(entry.TorrentUrl!.StartsWith("magnet:",StringComparison.OrdinalIgnoreCase))return entry.TorrentUrl;
        var bytes=await Read(WebUri(entry.TorrentUrl),10*1024*1024,ct);
        // Validate before making the response available to the download queue.
        MonoTorrent.Torrent.Load(bytes);
        var folder=Path.Combine(Preferences.DataDir,"sources");Directory.CreateDirectory(folder);
        var path=Path.Combine(folder,Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))+".torrent");
        await File.WriteAllBytesAsync(path,bytes,ct);return path;
    }
    public void Dispose()=>http.Dispose();
}
