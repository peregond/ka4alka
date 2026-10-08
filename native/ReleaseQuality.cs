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
    public static bool IsScreen(SourceEntry entry)=>Screen.IsMatch(entry.Title);
    public static int CatalogHeight(int height)=>height is 720 or 1080 or 2160?height:0;
    public static bool Downloadable(SourceEntry entry)
    {
        if(string.IsNullOrWhiteSpace(entry.TorrentUrl)&&entry.Source=="Internet Archive"&&
           Uri.TryCreate(entry.PageUrl,UriKind.Absolute,out var archive)&&archive.Scheme=="https"&&archive.Host=="archive.org"&&archive.IsDefaultPort&&string.IsNullOrEmpty(archive.UserInfo)&&
           archive.AbsolutePath.StartsWith("/details/",StringComparison.Ordinal)&&Uri.UnescapeDataString(archive.AbsolutePath["/details/".Length..].TrimEnd('/')) is {Length:>0} identifier&&!identifier.Contains('/'))return true;
        if(!Uri.TryCreate(entry.TorrentUrl,UriKind.Absolute,out var url))return false;
        if(url.Scheme=="magnet")return Regex.IsMatch(url.Query,@"(?i)(?:[?&])xt=urn(?:%3a|:)(?:btih|btmh)(?:%3a|:)(?:[a-f0-9]{40}|[a-z2-7]{32}|1220[a-f0-9]{64})(?:&|$)");
        return url.Scheme is "http" or "https"&&!string.IsNullOrEmpty(url.Host)&&string.IsNullOrEmpty(url.UserInfo);
    }
    public static bool Matches(SourceEntry entry,int height)=>CatalogHeight(height)!=0&&Downloadable(entry)&&!IsScreen(entry)&&Height(entry)==height;
    public static bool HasResolution(IEnumerable<SourceEntry> entries,int height)=>entries.Any(x=>Matches(x,height));
    public static string Label(SourceEntry entry)=>IsScreen(entry)?"Экранка":Height(entry) is int height?height>=2160?"4K":height>=1080?"Full HD":height>=720?"HD Ready":"SD":StandardDefinition.IsMatch(entry.Title)?"SD":"";
    public static bool Poor(SourceEntry entry,int minimum=720)=>IsScreen(entry)||
        (Height(entry) is int height?height<(minimum==1080?1080:720):StandardDefinition.IsMatch(entry.Title));

    public static bool OnlyPoor(IEnumerable<SourceEntry> entries,int minimum=720)
    {
        var available=entries.Where(Downloadable).ToArray();
        return available.Length>0&&available.All(x=>Poor(x,minimum));
    }
}
