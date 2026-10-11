using System.Text.Json;
using System.Text.RegularExpressions;

namespace Kachalka;

public sealed record SeasonalCollection(string Id,string Title,string[] MediaIds,DateTime? StartsAtUtc=null,DateTime? EndsAtUtc=null)
{
    public bool Active(DateTime now)=>(StartsAtUtc==null||now>=StartsAtUtc)&&(EndsAtUtc==null||now<EndsAtUtc);
}

public static class SeasonalCatalog
{
    public static SeasonalCollection[] Parse(JsonElement root)
    {
        if(!root.TryGetProperty("collections",out var array)||array.ValueKind!=JsonValueKind.Array)return [];
        var result=new List<SeasonalCollection>();var ids=new HashSet<string>();
        foreach(var row in array.EnumerateArray().Take(100))
        {
            if(row.ValueKind!=JsonValueKind.Object||!row.TryGetProperty("id",out var key)||key.ValueKind!=JsonValueKind.String||!row.TryGetProperty("title",out var label)||label.ValueKind!=JsonValueKind.String)continue;
            var id=key.GetString()??"";var title=label.GetString()??"";
            if(id.Length is 0 or >80||!Regex.IsMatch(id,"^[a-z0-9]+(?:-[a-z0-9]+)*$")||string.IsNullOrWhiteSpace(title)||title.Length>200||!ids.Add(id))continue;
            if(!row.TryGetProperty("mediaIds",out var members)||members.ValueKind!=JsonValueKind.Array||members.GetArrayLength()>1000)continue;
            bool Date(string name,out DateTime? date)
            {
                date=null;if(!row.TryGetProperty(name,out var value)||value.ValueKind==JsonValueKind.Null)return true;
                if(value.ValueKind!=JsonValueKind.String)return false;
                var text=value.GetString();
                if(text==null||!(text.EndsWith('Z')||text.EndsWith("+00:00",StringComparison.Ordinal))||!value.TryGetDateTimeOffset(out var parsed)||parsed.Offset!=TimeSpan.Zero)return false;
                date=parsed.UtcDateTime;return true;
            }
            if(!Date("startsAtUtc",out var start)||!Date("endsAtUtc",out var end)||start>=end)continue;
            var values=members.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString()!).Where(x=>Regex.IsMatch(x,@"^(movies|series):[\p{L}\p{N}-]{1,120}$")).Distinct().ToArray();
            if(values.Length>0)result.Add(new(id,title.Trim(),values,start,end));
        }
        return result.ToArray();
    }
    public static CatalogPage Page(SeasonalCollection collection,IEnumerable<MediaItem> catalog,string section,int page,CatalogSelection selection)
    {
        var byId=catalog.Where(x=>x.Section==section&&x.OnlineId!=null&&selection.Matches(x)).GroupBy(x=>x.OnlineId!).ToDictionary(x=>x.Key,x=>x.First());
        var rows=collection.MediaIds.Where(byId.ContainsKey).Select(id=>byId[id]).ToArray();
        var offset=(Math.Clamp(page,1,CatalogPaging.Limit)-1)*CatalogPaging.Size;
        return new(rows.Skip(offset).Take(CatalogPaging.Size).ToArray(),offset+CatalogPaging.Size<rows.Length,[],[]);
    }
}
