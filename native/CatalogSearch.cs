using System.Text;
using System.Text.RegularExpressions;

namespace Kachalka;

// Search tolerance never participates in MediaIdentity: a match is not a merge decision.
public sealed class CatalogSearchQuery
{
    sealed record YearTerm(int Index,int Year,string Phrase);
    sealed record Variant(string Term,string[] Words,int Penalty,YearTerm[] Years);
    readonly Variant[] variants;
    readonly string? imdb;
    public bool Empty {get;}
    public CatalogSearchQuery(string query)
    {
        var text=query.Trim();if(text.Length>200)text=text[..200];
        var id=Regex.Match(text,@"^(?:https://(?:www\.)?imdb\.com/title/)?(tt\d{5,10})/?$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        if(id.Success)imdb=id.Groups[1].Value.ToLowerInvariant();
        var primary=MediaIdentity.Normalize(text);Empty=primary.Length==0;
        var mapped=MediaIdentity.Normalize(Keyboard(text));
        variants=(mapped.Length>0&&mapped!=primary?new[]{(primary,0),(mapped,450)}:new[]{(primary,0)})
            .Select(x=>
            {
                var words=x.Item1.Split(' ',StringSplitOptions.RemoveEmptyEntries).Distinct().Take(12).ToArray();
                var years=new List<YearTerm>();
                if(words.Length>1)for(var i=0;i<words.Length;i++)
                    if(words[i].Length==4&&int.TryParse(words[i],out var year)&&year is >=1900 and <=2100)
                        years.Add(new(i,year,string.Join(" ",words.Where((_,index)=>index!=i))));
                return new Variant(x.Item1,words,x.Item2,years.ToArray());
            }).ToArray();
    }
    public int Score(MediaItem item,bool typo=false)
    {
        if(imdb!=null)return string.Equals(item.ImdbId,imdb,StringComparison.OrdinalIgnoreCase)?2000:0;
        var best=0;
        var names=MediaIdentity.NormalizedNames(item);var words=MediaIdentity.WordNames(item);
        foreach(var variant in variants)
        {
            if(variant.Words.Length==0)continue;
            for(var i=0;i<words.Length;i++)
            {
                var yearIndex=-1;var phrase=variant.Term;var wrongYear=false;
                foreach(var year in variant.Years)
                {
                    var inTitle=false;
                    foreach(var word in words[i])if(word==variant.Words[year.Index]){inTitle=true;break;}
                    if(inTitle)continue;
                    wrongYear=item.Year!=year.Year;yearIndex=year.Index;phrase=year.Phrase;break;
                }
                if(wrongYear)continue;
                var count=variant.Words.Length-(yearIndex>=0?1:0);var edits=0;var matches=true;
                for(var j=0;j<variant.Words.Length;j++)
                {
                    if(j==yearIndex)continue;
                    var term=variant.Words[j];var found=false;var numeric=char.IsDigit(term[0]);
                    foreach(var word in words[i])if(numeric?word==term:word.StartsWith(term,StringComparison.Ordinal)){found=true;break;}
                    if(found)continue;
                    if(typo&&edits==0&&count<=4&&term.Length>=5)
                    {
                        foreach(var word in words[i])if(OneEdit(term,word)){found=true;break;}
                        if(found){edits++;continue;}
                    }
                    matches=false;break;
                }
                if(!matches)continue;
                var score=edits>0?100:names[i]==phrase?1000:names[i].StartsWith(phrase,StringComparison.Ordinal)?800:600;
                if(score>variant.Penalty)best=Math.Max(best,score-variant.Penalty);
            }
        }
        return best;
    }
    // Common accidental keyboard-layout input, not arbitrary title translation.
    static string Keyboard(string text)
    {
        const string latin="`qwertyuiop[]asdfghjkl;'zxcvbnm,.";
        const string russian="ёйцукенгшщзхъфывапролджэячсмитьбю";
        var toRussian=text.Any(c=>c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')&&!text.Any(c=>c is >= '\u0400' and <= '\u04ff');
        var from=toRussian?latin:russian;var to=toRussian?russian:latin;
        var result=new StringBuilder(text.Length);
        foreach(var c in text.ToLowerInvariant()){var position=from.IndexOf(c);result.Append(position>=0?to[position]:c);}
        return result.ToString();
    }
    static bool OneEdit(string a,string b)
    {
        if(b.Length<5||a[0]!=b[0]||Math.Abs(a.Length-b.Length)>1)return false;
        var i=0;while(i<Math.Min(a.Length,b.Length)&&a[i]==b[i])i++;
        if(i==Math.Min(a.Length,b.Length))return Math.Abs(a.Length-b.Length)==1;
        if(a.Length==b.Length)
        {
            if(a.AsSpan(i+1).SequenceEqual(b.AsSpan(i+1)))return true;
            return i+1<a.Length&&a[i]==b[i+1]&&a[i+1]==b[i]&&a.AsSpan(i+2).SequenceEqual(b.AsSpan(i+2));
        }
        return a.Length>b.Length?a.AsSpan(i+1).SequenceEqual(b.AsSpan(i)):a.AsSpan(i).SequenceEqual(b.AsSpan(i+1));
    }
}

public static class CatalogSearch
{
    public static MediaItem[] Find(IEnumerable<MediaItem> items,string query,int limit=80,string language="ru")
    {
        var parsed=new CatalogSearchQuery(query);var rows=items as MediaItem[]??items.ToArray();limit=Math.Clamp(limit,1,200);
        if(parsed.Empty)return rows.Take(limit).ToArray();
        var ranked=new List<(MediaItem Item,int Score)>();
        foreach(var item in rows){var score=parsed.Score(item);if(score>0)ranked.Add((item,score));}
        // Typos are a bounded fallback; they never crowd out valid exact/prefix/alias results.
        if(ranked.Count==0)foreach(var item in rows){var score=parsed.Score(item,true);if(score>0)ranked.Add((item,score));}
        return ranked.OrderByDescending(x=>x.Score).ThenBy(x=>SearchLanguage.TitleRank(x.Item,language)).ThenBy(x=>x.Item.Title,StringComparer.CurrentCultureIgnoreCase).Take(limit).Select(x=>x.Item).ToArray();
    }
}
