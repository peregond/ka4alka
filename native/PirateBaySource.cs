using System.Text.Json;
using System.Text.RegularExpressions;
namespace Kachalka;

// Public API: real magnets only, with the film/TV category checked on every row.
public sealed class PirateBaySource(SourceClient client)
{
    public static Uri QueryUri(string title,bool series)=>new("https://apibay.org/q.php?q="+Uri.EscapeDataString(title.Trim())+"&cat="+(series?"205,208":"201,202,207"));
    public async Task<IReadOnlyList<SourceEntry>> Search(MediaItem item,CancellationToken ct)
    {
        var groups=await Task.WhenAll(new[]{item.Title,item.OriginalTitle}.Where(x=>!string.IsNullOrWhiteSpace(x)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Select(async title=>
        {
            try{return (Items:Parse(await client.Read(QueryUri(title,item.Section=="Сериалы"),2_000_000,ct),item),Error:(Exception?)null);}
            catch(Exception error) when(!ct.IsCancellationRequested){return (Items:(IReadOnlyList<SourceEntry>)[],Error:error);}
        }));
        if(groups.All(x=>x.Error!=null))throw groups.First().Error!;
        return groups.SelectMany(x=>x.Items).DistinctBy(x=>x.Id).OrderByDescending(x=>x.Seeds??-1).Take(100).ToArray();
    }
    public static IReadOnlyList<SourceEntry> Parse(byte[] bytes,MediaItem item)
    {
        using var json=JsonDocument.Parse(bytes);
        if(json.RootElement.ValueKind!=JsonValueKind.Array)throw new FormatException("The Pirate Bay вернул неверный ответ.");
        var result=new List<SourceEntry>();
        foreach(var row in json.RootElement.EnumerateArray().Take(100))
        {
            if(row.ValueKind!=JsonValueKind.Object)continue;
            string Value(string key)=>row.TryGetProperty(key,out var value)&&value.ValueKind is JsonValueKind.String or JsonValueKind.Number?value.ToString():"";
            var id=Value("id");var hash=Value("info_hash");var title=Value("name");var category=Value("category");
            if(!long.TryParse(id,out var number)||number<=0||!Regex.IsMatch(hash,@"\A[a-fA-F0-9]{40}\z")||hash.All(x=>x=='0')||title.Length is 0 or >500)continue;
            if(item.Section=="Сериалы"?category is not ("205" or "208"):category is not ("201" or "202" or "207"))continue;
            long? size=long.TryParse(Value("size"),out var amount)&&amount>0?amount:null;
            int? seeds=int.TryParse(Value("seeders"),out var count)&&count>=0?count:null;
            var entry=new SourceEntry("The Pirate Bay:"+id,title,"The Pirate Bay","https://thepiratebay.org/description.php?id="+id,
                "magnet:?xt=urn:btih:"+hash.ToUpperInvariant()+"&dn="+Uri.EscapeDataString(title)+"&tr="+Uri.EscapeDataString("udp://tracker.opentrackr.org:1337/announce"),null,size,seeds){Leechers=int.TryParse(Value("leechers"),out var peers)&&peers>=0?peers:null};
            if(!LiveCatalog.Matches(item,entry))continue;
            if(item.Section=="Фильмы"&&item.Year>0&&!Regex.IsMatch(title,@"(?<!\d)"+item.Year+@"(?!\d)"))continue;
            result.Add(entry);
        }
        return result.DistinctBy(x=>x.Id).ToArray();
    }
}
