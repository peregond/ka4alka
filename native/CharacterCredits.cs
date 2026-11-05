using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kachalka;

public record CastCredit(string Name,string? OriginalName=null,string? Character=null);

// A character belongs to this work, never to the person's profession or global identity.
public static class CharacterCredits
{
    static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true};
    static string Key(string? name)=>Regex.Replace((name??"").Normalize(NormalizationForm.FormD).ToLowerInvariant(),@"[\p{M}\W_]+","");
    public static CastCredit[] Read(JsonElement row)=>row.ValueKind==JsonValueKind.Object&&row.TryGetProperty("cast",out var cast)&&cast.ValueKind==JsonValueKind.Array
        ?cast.EnumerateArray().Take(12).Where(x=>x.ValueKind==JsonValueKind.Object).Select(x=>x.Deserialize<CastCredit>(Json)).OfType<CastCredit>()
            .Where(x=>x.Name is {Length:>0 and <=120}&&(x.OriginalName==null||x.OriginalName.Length<=120)&&(x.Character==null||x.Character.Length<=200)).ToArray():[];
    public static CinemaPerson[] Apply(IEnumerable<CinemaPerson> people,IEnumerable<CastCredit> cast)
    {
        var credits=cast.ToArray();
        return people.Select(person=>
        {
            if(person.Role!="Актёры")return person;
            var names=new[]{Key(person.Name),Key(person.OriginalName)}.Where(x=>x.Length>0).ToHashSet(StringComparer.Ordinal);
            var matches=credits.Where(x=>names.Contains(Key(x.Name))||names.Contains(Key(x.OriginalName))).ToArray();
            // An ambiguous name cannot borrow a character from a different actor; never match by tile position.
            return matches.Length==1&&!string.IsNullOrWhiteSpace(matches[0].Character)?person with{Character=matches[0].Character!.Trim()}:person;
        }).ToArray();
    }
    public static async Task<MediaItem> Load(OnlineIndexClient online,MediaItem item,string language,CancellationToken ct,bool force=false)
    {
        if(!item.Cinema||item.People.Length==0||language=="en")return item;
        var id=OnlineIndexClient.IdFor(item)??item.ImdbId;if(id==null)return item;
        var section=item.Section=="Сериалы"?"series":"movies";
        var file=Path.Combine(Preferences.DataDir,"people","cast-v1-"+section+"-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(id)))+".json");
        CastCredit[] Parse(byte[] bytes)
        {
            using var json=JsonDocument.Parse(bytes);var root=json.RootElement;
            if(root.GetProperty("id").GetString()!=id||root.GetProperty("section").GetString()!=section)throw new InvalidDataException("Cast belongs to another title.");
            return Read(root.GetProperty("item"));
        }
        try
        {
            byte[]? bytes=null;
            if(!force&&File.Exists(file)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(file)<TimeSpan.FromDays(7))
                try{bytes=await CacheFiles.ReadAllBytesAsync(file,ct);_ = Parse(bytes);}
                catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException){bytes=null;}
            if(bytes==null)
            {
                var query=OnlineIndexClient.IdFor(item)!=null?"id="+Uri.EscapeDataString(id):"imdb="+Uri.EscapeDataString(id)+"&section="+section;
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(8));
                bytes=await online.ReadSite("api/localized-media?language=ru&"+query,128*1024,deadline.Token,b=>Parse(b),restSites:false);
                if(Parse(bytes).Length>0)
                    try{Directory.CreateDirectory(Path.GetDirectoryName(file)!);await CacheFiles.WriteAllBytesAsync(file,bytes,ct);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
            return item with{People=Apply(item.People,Parse(bytes))};
        }
        catch(Exception)when(!ct.IsCancellationRequested){return item;}
    }
}
