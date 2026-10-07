using System.Text.RegularExpressions;
namespace Kachalka;

public static class ReleaseQuality
{
    static readonly Regex Screen=new(@"(?i)(?<![\p{L}\p{N}])(?:hd[ ._-]?)?(?:cam(?:rip)?|ts|tc|telesync|telecine|screener|dvd[ ._-]?scr|scr)(?![\p{L}\p{N}])(?!\s*[\[(]?\s*(?:19|20)\d{2}(?:\D|$))|экранк|съемк[аи]|съёмк[аи]",RegexOptions.CultureInvariant|RegexOptions.Compiled);
    static readonly Regex Pixels=new(@"(?i)(?<!\d)(\d{3,4})[pi]\b",RegexOptions.CultureInvariant|RegexOptions.Compiled);
    static readonly Regex Dimensions=new(@"(?i)(?<!\d)\d{3,4}[xх×](\d{3,4})(?!\d)",RegexOptions.CultureInvariant|RegexOptions.Compiled);
    static readonly Regex StandardDefinition=new(@"(?i)\b(?:dvd(?:rip|5|9)?|vcd|svcd|tv[ ._-]?rip|sat[ ._-]?rip)\b",RegexOptions.CultureInvariant|RegexOptions.Compiled);

    public static int? Height(SourceEntry entry)
    {
        if(entry.Quality=="4K")return 2160;
        if(entry.Quality=="Full HD")return 1080;
        if(entry.Quality=="HD Ready")return 720;
        var match=Pixels.Match(entry.Title);
        if(!match.Success)match=Dimensions.Match(entry.Title);
        return match.Success&&int.TryParse(match.Groups[1].Value,out var height)&&height is >=100 and <=8640?height:null;
    }
    public static bool Poor(SourceEntry entry,int minimum=720)=>Screen.IsMatch(entry.Title)||
        (Height(entry) is int height?height<(minimum==1080?1080:720):StandardDefinition.IsMatch(entry.Title));

    public static bool OnlyPoor(IEnumerable<SourceEntry> entries,int minimum=720)
    {
        var available=entries.Where(x=>x.TorrentUrl!=null||x.Source=="Internet Archive").ToArray();
        return available.Length>0&&available.All(x=>Poor(x,minimum));
    }
}
