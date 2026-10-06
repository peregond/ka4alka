using System.Text.Json;
namespace Kachalka;
public record Broadcast(string Name,string Stream,string? Image,string? Website,string Source,string Quality);
public sealed class BroadcastCatalog(SourceClient http)
{
    public async Task<IReadOnlyList<Broadcast>> Radio(CancellationToken ct)
    {
        using var json=JsonDocument.Parse(await http.Read(new Uri("https://de1.api.radio-browser.info/json/stations/bycountrycodeexact/RU?limit=80&order=votes&reverse=true&hidebroken=true"),512*1024,ct));
        return json.RootElement.EnumerateArray().Select(x=>{
            var name=Get(x,"name");var stream=Get(x,"url_resolved");
            return string.IsNullOrWhiteSpace(name)||!ValidStream(stream)?null:new Broadcast(name!,stream!,Image(x,"favicon"),Get(x,"homepage"),"Radio Browser",Get(x,"codec")??"");
        }).Where(x=>x!=null).Cast<Broadcast>().Take(60).ToArray();
    }
    public async Task<IReadOnlyList<Broadcast>> Television(bool sports,CancellationToken ct)
    {
        var channelsTask=http.Read(new Uri("https://iptv-org.github.io/api/channels.json"),10*1024*1024,ct);
        var streamsTask=http.Read(new Uri("https://iptv-org.github.io/api/streams.json"),6*1024*1024,ct);
        using var channelJson=JsonDocument.Parse(await channelsTask);using var streamsJson=JsonDocument.Parse(await streamsTask);
        var choices=channelJson.RootElement.EnumerateArray().Where(x=>!Bool(x,"is_nsfw")&&string.IsNullOrEmpty(Get(x,"closed"))&&(sports?Categories(x).Contains("sports"):Get(x,"country")=="RU"))
            .Select(x=>(Id:Get(x,"id"),Name:Get(x,"name"),Website:Get(x,"website")))
            .Where(x=>!string.IsNullOrEmpty(x.Id)).ToDictionary(x=>x.Id!,x=>x);
        var result=new List<Broadcast>();var seen=new HashSet<string>();
        foreach(var stream in streamsJson.RootElement.EnumerateArray()){
            var id=Get(stream,"channel");var url=Get(stream,"url");if(id==null||!choices.TryGetValue(id,out var channel)||!ValidStream(url)||!seen.Add(id))continue;
            if(!string.IsNullOrEmpty(Get(stream,"user_agent"))||!string.IsNullOrEmpty(Get(stream,"referrer")))continue;
            if(stream.TryGetProperty("labels",out var labels)&&labels.ValueKind==JsonValueKind.Array&&labels.EnumerateArray().Any(x=>x.GetString()=="Geo-blocked"))continue;
            result.Add(new(channel.Name??id,url!,null,channel.Website,"IPTV-org",Get(stream,"quality")??""));if(result.Count>=100)break;
        }
        return result;
    }
    static string? Get(JsonElement x,string name)=>x.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
    static bool Bool(JsonElement x,string name)=>x.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.True;
    static IEnumerable<string> Categories(JsonElement x)=>x.TryGetProperty("categories",out var v)&&v.ValueKind==JsonValueKind.Array?v.EnumerateArray().Select(y=>y.GetString()??""):[];
    static string? Image(JsonElement x,string name){var v=Get(x,name);return Uri.TryCreate(v,UriKind.Absolute,out var u)&&u.Scheme=="https"?v:null;}
    static bool ValidStream(string? url)=>Uri.TryCreate(url,UriKind.Absolute,out var u)&&u.Scheme is "http" or "https";
}
