using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Kachalka;

public record SeriesIdentity(string Title,string ImdbId,int Year);

public sealed partial class SourceClient
{
    static string NormalizeTitle(string value)=>Regex.Replace(value.ToLowerInvariant(),@"[^\p{L}\p{N}]+"," ").Trim();

    public static SeriesIdentity? ParseTvMaze(byte[] data,string title,int year)
    {
        using var json=JsonDocument.Parse(data);
        var wanted=NormalizeTitle(title);
        var matches=json.RootElement.EnumerateArray().Select(x=>x.GetProperty("show"))
            .Where(show=>show.TryGetProperty("name",out var name)&&NormalizeTitle(name.GetString()??"")==wanted)
            .Select(show=>
            {
                var premiered=show.TryGetProperty("premiered",out var date)&&date.ValueKind==JsonValueKind.String?date.GetString():null;
                int.TryParse(premiered?.Split('-')[0],out var firstYear);
                var imdb=show.TryGetProperty("externals",out var external)&&external.ValueKind==JsonValueKind.Object&&external.TryGetProperty("imdb",out var id)&&id.ValueKind==JsonValueKind.String?id.GetString():null;
                return new SeriesIdentity(show.GetProperty("name").GetString()!,imdb??"",firstYear);
            })
            .Where(x=>Regex.IsMatch(x.ImdbId,@"^tt\d{6,12}$"))
            .OrderBy(x=>year>0?Math.Abs(x.Year-year):0).ToArray();
        return matches.Length==0||year>0&&Math.Abs(matches[0].Year-year)>2?null:matches[0];
    }

    public async Task<SeriesIdentity?> FindSeriesIdentity(string title,int year,CancellationToken ct)
    {
        var url=new Uri("https://api.tvmaze.com/search/shows?q="+Uri.EscapeDataString(title));
        return ParseTvMaze(await Read(url,1024*1024,ct),title,year);
    }

    public static IReadOnlyList<SourceEntry> ParseEztv(byte[] data,string expectedTitle)
    {
        using var json=JsonDocument.Parse(data);
        if(!json.RootElement.TryGetProperty("torrents",out var torrents)||torrents.ValueKind!=JsonValueKind.Array)return [];
        var expected=NormalizeTitle(expectedTitle);
        return torrents.EnumerateArray().Select(x=>
        {
            var title=x.TryGetProperty("title",out var name)&&name.ValueKind==JsonValueKind.String?name.GetString()??"":"";
            var normalized=NormalizeTitle(title);
            if(!normalized.StartsWith(expected+" ",StringComparison.Ordinal)&&normalized!=expected)return null;
            var tail=normalized.Length==expected.Length?"":normalized[(expected.Length+1)..];
            if(tail.Length>0&&!Regex.IsMatch(tail,@"^(?:s\d{1,2}(?:e\d{1,3})?|e\d{1,3}|season|episode|complete|\d{4})\b"))return null;
            var magnet=x.TryGetProperty("magnet_url",out var m)&&m.ValueKind==JsonValueKind.String?m.GetString():null;
            if(magnet?.StartsWith("magnet:?xt=urn:btih:",StringComparison.OrdinalIgnoreCase)!=true)return null;
            long? size=x.TryGetProperty("size_bytes",out var s)&&long.TryParse(s.ToString(),out var n)?n:null;
            int? seeds=x.TryGetProperty("seeds",out var seed)&&int.TryParse(seed.ToString(),out var count)?count:null;
            var id=x.TryGetProperty("hash",out var hash)&&hash.ValueKind==JsonValueKind.String?hash.GetString():magnet;
            return new SourceEntry(id??magnet,title,"EZTV","",magnet,null,size,seeds);
        }).Where(x=>x!=null).Cast<SourceEntry>().ToArray();
    }

    public async Task<IReadOnlyList<SourceEntry>> SearchEztv(SeriesIdentity series,CancellationToken ct)
    {
        var id=series.ImdbId[2..];
        var first=await Read(new Uri($"https://eztvx.to/api/get-torrents?imdb_id={id}&limit=100&page=1"),4*1024*1024,ct);
        var results=ParseEztv(first,series.Title).ToList();
        using var json=JsonDocument.Parse(first);
        if(json.RootElement.TryGetProperty("torrents_count",out var count)&&int.TryParse(count.ToString(),out var total)&&total>100)
        {
            try
            {
                var second=await Read(new Uri($"https://eztvx.to/api/get-torrents?imdb_id={id}&limit=100&page=2"),4*1024*1024,ct);
                results.AddRange(ParseEztv(second,series.Title));
            }
            catch when(!ct.IsCancellationRequested){ /* The first page is still useful. */ }
        }
        return results;
    }

    public static IReadOnlyList<SourceEntry> ParseNyaa(byte[] data)
    {
        using var stream=new MemoryStream(data);
        using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=2*1024*1024});
        var xml=XDocument.Load(reader);
        if(xml.Root?.Name.LocalName!="rss")throw new InvalidDataException("Nyaa вернул не RSS.");
        return xml.Descendants("item").Take(75).Select(item=>
        {
            string? Field(string name)=>item.Elements().FirstOrDefault(x=>x.Name.LocalName==name)?.Value;
            var link=Field("link");var page=Field("guid");var title=Field("title");
            if(string.IsNullOrWhiteSpace(title)||!Uri.TryCreate(link,UriKind.Absolute,out var torrent)||torrent.Scheme!="https"||torrent.Host!="nyaa.si"||!torrent.AbsolutePath.EndsWith(".torrent",StringComparison.OrdinalIgnoreCase))return null;
            if(!Uri.TryCreate(page,UriKind.Absolute,out var view)||view.Scheme!="https"||view.Host!="nyaa.si")page="";
            var sizeText=Field("size")??"";var match=Regex.Match(sizeText,@"(?i)(\d+(?:\.\d+)?)\s*(KiB|MiB|GiB|TiB)");
            long? size=match.Success&&double.TryParse(match.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)?(long)(value*Math.Pow(1024,match.Groups[2].Value.ToLowerInvariant() switch{"kib"=>1,"mib"=>2,"gib"=>3,_=>4})):null;
            int? seeds=int.TryParse(Field("seeders"),out var count)?count:null;
            return new SourceEntry(Field("infoHash")??page??link!,title,"Nyaa",page??"",link,null,size,seeds);
        }).Where(x=>x!=null).Cast<SourceEntry>().ToArray();
    }

    public async Task<IReadOnlyList<SourceEntry>> SearchNyaa(string title,CancellationToken ct)
    {
        var url=new Uri("https://nyaa.si/?page=rss&c=1_0&f=0&q="+Uri.EscapeDataString(title));
        return ParseNyaa(await Read(url,2*1024*1024,ct));
    }
}
