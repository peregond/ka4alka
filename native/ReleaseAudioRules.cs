using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kachalka;

// Data only: bounded literal aliases and tracker codes, never downloaded code or regexes.
public sealed class ReleaseAudioRules
{
    public const int MaxBytes=64*1024;
    public sealed record Label(string Name,Regex Words,string[] Codes);
    public int Revision {get;}
    public int DubbingPriority {get;}
    readonly Label[] studios,types;
    readonly Regex codeFields;
    ReleaseAudioRules(int revision,int priority,Label[] studios,Label[] types,string[] otherCodes)
    {
        Revision=revision;DubbingPriority=priority;this.studios=studios;this.types=types;
        var codes=string.Join("|",types.SelectMany(x=>x.Codes).Concat(otherCodes).Distinct(StringComparer.OrdinalIgnoreCase).Select(Regex.Escape));
        if(codes.Length==0)codes="(?!)";
        var list="(?:"+codes+")(?:\\s*[,/+]\\s*(?:"+codes+"))*";
        codeFields=new("\\|\\s*(?<codes>"+list+")\\s*(?=\\||$)|\\[\\s*(?<codes>"+list+")\\s*\\]",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    }
    public string[] Studios(string title)=>studios.Where(x=>x.Words.IsMatch(title)).Select(x=>x.Name).ToArray();
    public string[] Types(string title,bool russianSource)
    {
        var codes=russianSource?codeFields.Matches(title).Cast<Match>().SelectMany(match=>match.Groups["codes"].Value.Split([',','/','+'],StringSplitOptions.TrimEntries)).ToHashSet(StringComparer.OrdinalIgnoreCase):[];
        return types.Where(x=>x.Words.IsMatch(title)||x.Codes.Any(codes.Contains)).Select(x=>x.Name).ToArray();
    }
    public static ReleaseAudioRules Parse(byte[] bytes)
    {
        if(bytes.Length is 0 or >MaxBytes)throw new InvalidDataException("Invalid release rules size.");
        using var json=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=12});var root=json.RootElement;
        int Number(string key,int low,int high)
        {
            if(!root.TryGetProperty(key,out var v)||v.ValueKind!=JsonValueKind.Number||!v.TryGetInt32(out var n)||n<low||n>high)throw new InvalidDataException("Invalid release rules field: "+key);
            return n;
        }
        if(root.ValueKind!=JsonValueKind.Object||Number("schemaVersion",1,1)!=1)throw new InvalidDataException("Unknown release rules schema.");
        var revision=Number("revision",1,int.MaxValue);var priority=Number("dubbingPriority",0,10);
        static string Text(JsonElement item,int limit)=>item.ValueKind==JsonValueKind.String&&item.GetString() is {Length:>0} value&&value.Length<=limit&&!value.Any(char.IsControl)?value:throw new InvalidDataException("Invalid release rule label.");
        static string[] Values(JsonElement obj,string key,int count,int length)
        {
            if(!obj.TryGetProperty(key,out var list)||list.ValueKind!=JsonValueKind.Array||list.GetArrayLength()>count)throw new InvalidDataException("Invalid release rule list.");
            return list.EnumerateArray().Select(x=>Text(x,length)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        Label[] Labels(string key,int max)
        {
            if(!root.TryGetProperty(key,out var list)||list.ValueKind!=JsonValueKind.Array||list.GetArrayLength() is 0||list.GetArrayLength()>max)throw new InvalidDataException("Invalid release rule labels.");
            var labels=new List<Label>();var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var row in list.EnumerateArray())
            {
                if(row.ValueKind!=JsonValueKind.Object||!row.TryGetProperty("name",out var n))throw new InvalidDataException("Invalid release rule.");
                var name=Text(n,80);if(!names.Add(name))throw new InvalidDataException("Duplicate release rule.");
                if(key=="types"&&name is not ("Дубляж" or "Многоголосая" or "Двухголосая" or "Одноголосая" or "Оригинал"))throw new InvalidDataException("Unknown audio type.");
                var aliases=Values(row,"aliases",24,80);var prefixes=Values(row,"prefixes",8,40);var codes=Values(row,"codes",16,4);
                if(prefixes.Any(word=>!word.All(char.IsLetter))||codes.Any(word=>!word.All(char.IsLetterOrDigit)))throw new InvalidDataException("Invalid audio rule token.");
                var parts=aliases.Select(Regex.Escape).Concat(prefixes.Select(word=>Regex.Escape(word)+@"[\p{L}]*")).ToArray();
                var words=parts.Length==0?"(?!)":@"(?<![\p{L}\p{N}])(?:"+string.Join("|",parts)+@")(?![\p{L}\p{N}])";
                labels.Add(new(name,new Regex(words,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(30)),codes));
            }
            return labels.ToArray();
        }
        var other=Values(root,"otherCodes",32,4);if(other.Any(x=>!x.All(char.IsLetterOrDigit)))throw new InvalidDataException("Invalid tracker code.");
        return new(revision,priority,Labels("studios",100),Labels("types",5),other);
    }
}
