using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kachalka;

// Verified video and PC game categories from RuTracker and The Pirate Bay.
public static class KnabenSource
{
    const int MaxResponseBytes=2_000_000;
    static readonly HttpClient Http=CreateClient();
    static readonly SemaphoreSlim RequestGate=new(1,1);
    static DateTime nextRequest=DateTime.MinValue;
    static readonly Regex HashPattern=new(@"^[a-f0-9]{40}$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);
    static readonly Regex MagnetHashPattern=new(@"(?i)(?:^|[?&])xt=urn:btih:([a-f0-9]{40})(?:&|$)",RegexOptions.CultureInvariant|RegexOptions.Compiled);
    static readonly Regex VideoCategoryPattern=new(@"(?:movie|tv|anime|video|film|кино|сериал)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled);

    static HttpClient CreateClient()
    {
        var client=new HttpClient{Timeout=TimeSpan.FromSeconds(25)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Kachalka/0.11");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public static async Task<IReadOnlyList<SourceEntry>> Search(MediaItem item,CancellationToken ct)
    {
        var queries=(item.Section=="Игры"?GameReleaseSources.Queries(item):new[]{item.OriginalTitle}.Concat(MediaIdentity.Queries(item)))
            .Where(query=>!string.IsNullOrWhiteSpace(query))
            .Select(query=>query!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
        return await ReleaseAliasSearch.Run(queries,(query,token)=>SearchQuery(query,item,token),ct);
    }

    // v2 supports cached GET requests and tracker filtering; unrelated trackers no longer consume the first page.
    public static Uri QueryUri(string title,bool games=false)
    {
        var query=games?"\""+title.Replace("\"","").Trim()+"\"":title.Trim();
        return new("https://api.knaben.org/v2/search?q="+Uri.EscapeDataString(query)+"&sf=title&t=rutracker,thepiratebay&o=seeders&d=desc&s=150");
    }

    static async Task<IReadOnlyList<SourceEntry>> SearchQuery(string query,MediaItem item,CancellationToken ct)
    {
        // Knaben currently limits public API requests to one every two seconds.
        await RequestGate.WaitAsync(ct);
        try
        {
            var delay=nextRequest-DateTime.UtcNow;
            if(delay>TimeSpan.Zero)await Task.Delay(delay,ct);
            nextRequest=DateTime.UtcNow.AddMilliseconds(2100);
        using var request=new HttpRequestMessage(HttpMethod.Get,QueryUri(query,item.Section=="Игры"));
        using var response=await Http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        response.EnsureSuccessStatusCode();
        if(response.Content.Headers.ContentLength>MaxResponseBytes)throw new InvalidDataException("Ответ индекса превышает допустимый размер.");
        await using var input=await response.Content.ReadAsStreamAsync(ct);
        using var output=new MemoryStream();
        var buffer=new byte[16384];int count;
        while((count=await input.ReadAsync(buffer,ct))!=0)
        {
            if(output.Length+count>MaxResponseBytes)throw new InvalidDataException("Ответ индекса превышает допустимый размер.");
            output.Write(buffer,0,count);
        }
        return Parse(output.ToArray(),item);
        }
        finally{RequestGate.Release();}
    }

    public static IReadOnlyList<SourceEntry> Parse(byte[] data,MediaItem item)
    {
        using var json=JsonDocument.Parse(data);
        if(!json.RootElement.TryGetProperty("hits",out var hits)||hits.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Индекс не вернул список раздач.");
        var entries=new List<SourceEntry>();
        foreach(var row in hits.EnumerateArray().Take(150))
        {
            if(row.ValueKind!=JsonValueKind.Object)continue;
            var tracker=String(row,"tracker")?.Trim();
            var source=string.Equals(tracker,"RuTracker",StringComparison.OrdinalIgnoreCase)||string.Equals(tracker,"RuTracker.org",StringComparison.OrdinalIgnoreCase)?"RuTracker":
                string.Equals(tracker,"The Pirate Bay",StringComparison.OrdinalIgnoreCase)?"The Pirate Bay":null;
            if(source==null)continue;
            var title=String(row,"title")?.Trim();
            if(string.IsNullOrWhiteSpace(title)||title.Length>500||!(item.Section=="Игры"?Regex.IsMatch(String(row,"category")??"",@"(?i)(?:games?.*(?:pc|windows)|pc.*games?|windows.*games?|игры.*(?:pc|пк))"):VideoCategoryPattern.IsMatch(String(row,"category")??"")))continue;
            var hash=String(row,"hash");
            if(hash==null||!HashPattern.IsMatch(hash))
            {
                var magnet=String(row,"magnetUrl")??"";
                var match=magnet.StartsWith("magnet:?",StringComparison.OrdinalIgnoreCase)?MagnetHashPattern.Match(magnet):Match.Empty;
                hash=match.Success?match.Groups[1].Value:null;
            }
            if(hash==null)continue;
            hash=hash.ToUpperInvariant();
            var magnetUrl="magnet:?xt=urn:btih:"+hash+"&dn="+Uri.EscapeDataString(title)+"&tr="+Uri.EscapeDataString("udp://tracker.opentrackr.org:1337/announce");
            var page=SecurePage(String(row,"details"),source);
            var size=Long(row,"bytes");
            var seeds=Count(row,"seeders");
            var entry=new SourceEntry(source+":"+hash,title,source,page,magnetUrl,null,size,seeds){Via="Knaben",Leechers=Count(row,"leechers")};
            if(item.Section=="Игры"?!GameReleaseSources.Matches(item,entry):!LiveCatalog.Matches(item,entry))continue;
            if(item.Section=="Фильмы"&&item.Year>0&&!Regex.IsMatch(title,@"(?<!\d)"+item.Year+@"(?!\d)"))continue;
            entries.Add(entry);
        }
        return ReleaseSearch.Distinct(entries);
    }

    static string? String(JsonElement row,string name)=>row.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
    static long? Long(JsonElement row,string name)=>row.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt64(out var number)&&number>0?number:null;
    static int? Count(JsonElement row,string name)=>row.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out var number)&&number>=0?number:null;

    static string SecurePage(string? value,string source)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!string.IsNullOrEmpty(uri.UserInfo))return "";
        // The index can return its own TPB proxy page. Preserve a usable canonical
        // source page without trusting arbitrary proxy paths or query parameters.
        if(source=="The Pirate Bay"&&uri.Host=="knaben.xyz"&&uri.IsDefaultPort&&uri.AbsolutePath=="/thepiratebay/description.php")
        {
            var id=Regex.Match(uri.Query,@"^\?id=(\d{1,15})$");
            return id.Success?"https://thepiratebay.org/description.php?id="+id.Groups[1].Value:"";
        }
        return ((uri.Host is "rutracker.org" or "www.rutracker.org" or "knaben.org" or "knaben.eu")||source=="The Pirate Bay"&&(uri.Host is "thepiratebay.org" or "www.thepiratebay.org"))?uri.AbsoluteUri:"";
    }
}
