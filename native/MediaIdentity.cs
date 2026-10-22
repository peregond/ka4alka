using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kachalka;

// Names are aliases of an exact work, never a fuzzy translation of an arbitrary title.
public static class MediaIdentity
{
    sealed record Known(string ImdbId,string Section,int Year,string OriginalTitle,string[] Aliases,string? RussianTitle=null);
    sealed record SearchNames(string[] Names,string[] Normalized,string[][] Words,string? RussianTitle);
    static readonly ConditionalWeakTable<MediaItem,SearchNames> names=new();
    static readonly Lazy<Known[]> known=new(()=>
    {
        using var stream=typeof(MediaIdentity).Assembly.GetManifestResourceStream("Kachalka.title-aliases.json");
        return stream==null?[]:JsonSerializer.Deserialize<Known[]>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??[];
    });
    public static string Normalize(string value)=>Regex.Replace(value.Replace('ё','е').Replace('Ё','Е').ToLowerInvariant(),@"[^\p{L}\p{N}]+"," ").Trim();
    static string Section(Known row)=>row.Section=="series"?"Сериалы":"Фильмы";
    static readonly Lazy<Dictionary<string,Known>> byId=new(()=>known.Value.ToDictionary(x=>Section(x)+"|"+x.ImdbId));
    static readonly Lazy<Dictionary<string,Known>> byName=new(()=>known.Value.SelectMany(row=>row.Aliases.Select(name=>(Key:Section(row)+"|"+row.Year+"|"+Normalize(name),Row:row))).GroupBy(x=>x.Key).Where(group=>group.Select(x=>x.Row.ImdbId).Distinct().Count()==1).ToDictionary(group=>group.Key,group=>group.First().Row));
    static Known? Find(MediaItem item)
    {
        if(!string.IsNullOrWhiteSpace(item.ImdbId))return byId.Value.GetValueOrDefault(item.Section+"|"+item.ImdbId.ToLowerInvariant());
        if(item.Year<=0)return null;
        foreach(var title in new[]{item.Title,item.OriginalTitle??""})if(byName.Value.TryGetValue(item.Section+"|"+item.Year+"|"+Normalize(title),out var row))return row;
        return null;
    }
    static string[] Clean(IEnumerable<string?> values)=>values.Where(x=>!string.IsNullOrWhiteSpace(x)&&x.Length<=200).Select(x=>x!.Trim()).DistinctBy(Normalize).Take(16).ToArray();
    public static MediaItem Enrich(MediaItem item)
    {
        var row=Find(item);
        var aliases=Clean(item.TitleAliases.Concat(row?.Aliases??[]));
        if(aliases.SequenceEqual(item.TitleAliases)&&(row==null||item.ImdbId!=null&&!string.IsNullOrWhiteSpace(item.OriginalTitle)))return item;
        return item with{ImdbId=item.ImdbId??row?.ImdbId,OriginalTitle=item.OriginalTitle??row?.OriginalTitle,TitleAliases=aliases};
    }
    static SearchNames Index(MediaItem item)=>names.GetValue(item,key=>
    {
        var row=Find(key);
        var values=Clean(new[]{key.Title,key.OriginalTitle}.Concat(key.TitleAliases).Concat(row?.Aliases??[]));
        var normalized=values.Select(Normalize).ToArray();
        return new(values,normalized,normalized.Select(x=>x.Split(' ',StringSplitOptions.RemoveEmptyEntries)).ToArray(),row?.RussianTitle);
    });
    public static string[] Names(MediaItem item)=>Index(item).Names;
    public static string RussianTitle(MediaItem item)=>Index(item).RussianTitle??item.Title;
    internal static string[] NormalizedNames(MediaItem item)=>Index(item).Normalized;
    internal static string[][] WordNames(MediaItem item)=>Index(item).Words;
    public static string OriginalQuery(string query)
    {
        var term=Normalize(query);
        var matches=known.Value.Where(row=>row.Aliases.Any(name=>Normalize(name)==term)).Select(row=>row.OriginalTitle).Distinct().Take(2).ToArray();
        return matches.Length==1?matches[0]:query;
    }
    public static string[] Queries(MediaItem item)=>(AppLanguage.Current=="en"?new[]{item.English?.Title,item.OriginalTitle}.Where(x=>!string.IsNullOrWhiteSpace(x)).Cast<string>().Concat(Names(item)).Distinct(StringComparer.OrdinalIgnoreCase):Names(item)).Take(4).ToArray();
    public static bool MatchesQuery(MediaItem item,string[] words)=>Index(item).Words.Any(title=>words.All(word=>title.Any(part=>part.StartsWith(word,StringComparison.Ordinal))||item.Year>0&&item.Year.ToString().StartsWith(word,StringComparison.Ordinal)));
    public static int Score(MediaItem item,string term,string[] words)
    {
        var index=Index(item);if(!MatchesQuery(item,words))return 0;
        for(var i=0;i<index.Normalized.Length;i++)if(index.Normalized[i]==term)return 100-i;
        return 30+words.Length;
    }
    public static string StableKey(MediaItem item)=>!string.IsNullOrWhiteSpace(item.ImdbId)?item.Section+"|"+item.ImdbId.ToLowerInvariant():item.Section+"|"+item.PageUrl;
    public static bool Same(MediaItem a,MediaItem b)
    {
        if(a.Section!=b.Section)return false;
        if(a.ImdbId!=null&&b.ImdbId!=null)return a.ImdbId.Equals(b.ImdbId,StringComparison.OrdinalIgnoreCase);
        return a.Year>0&&a.Year==b.Year&&Index(a).Normalized.Intersect(Index(b).Normalized).Any();
    }
    static MediaItem Join(MediaItem prior,MediaItem item)
    {
        var preferItem=ImdbSuggestions.IsImdbOnly(prior)&&!ImdbSuggestions.IsImdbOnly(item);
        var preferred=preferItem?item:prior;var other=preferItem?prior:item;
        return MediaMetadata.Merge(preferred,other) with{TitleAliases=Clean(Names(preferred).Concat(Names(other)))};
    }
    // Linear indexed grouping. Conflicting IMDb IDs and ambiguous aliases are kept separate.
    public static MediaItem[] Merge(IEnumerable<MediaItem> items)
    {
        var result=new List<MediaItem>();var keys=new Dictionary<string,HashSet<int>>();var removed=new HashSet<int>();
        IEnumerable<string> Keys(MediaItem item)
        {
            if(item.ImdbId!=null)yield return StableKey(item);
            if(item.Year>0)foreach(var name in Index(item).Normalized)yield return item.Section+"|"+item.Year+"|"+name;
            yield return item.Section+"|page|"+(item.PageUrl??item.Id.ToString());
        }
        foreach(var value in items)
        {
            var item=Enrich(value);var candidates=new HashSet<int>();
            foreach(var key in Keys(item))if(keys.TryGetValue(key,out var indices))foreach(var index in indices)if(!removed.Contains(index)&&(Same(result[index],item)||result[index].Section==item.Section&&result[index].Id==item.Id&&(result[index].ImdbId==null||item.ImdbId==null)))candidates.Add(index);
            int target;
            if(candidates.Count==1||candidates.Count>1&&item.ImdbId!=null&&candidates.All(index=>result[index].ImdbId==null||string.Equals(result[index].ImdbId,item.ImdbId,StringComparison.OrdinalIgnoreCase)))
            {
                target=candidates.Min();
                foreach(var index in candidates.Where(index=>index!=target)){result[target]=Join(result[target],result[index]);removed.Add(index);}
                result[target]=Join(result[target],item);
            }
            else{target=result.Count;result.Add(item);}
            foreach(var key in Keys(result[target])){if(!keys.TryGetValue(key,out var indices))keys[key]=indices=[];indices.Add(target);}
        }
        return result.Where((_,index)=>!removed.Contains(index)).ToArray();
    }
}
