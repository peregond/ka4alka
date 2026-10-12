using System.Text.RegularExpressions;
namespace Kachalka;

// What a game release's own title says about it. Every value comes from the title text, the same way a film
// release's quality and audio do; nothing is looked up or guessed beyond that.
public static class GameReleaseInfo
{
    static readonly (string Pattern,string Name)[] Groups=
    [
        (@"(?i)fit\s?girl","FitGirl"),(@"(?i)(?<![\p{L}\p{N}])DODI(?![\p{L}\p{N}])","DODI"),(@"(?i)x[ae]tab|хатаб","xatab"),(@"(?i)el\s?amigos","ElAmigos"),
        (@"(?i)R\.?G\.?\s?Mechanics|механики","R.G. Механики"),(@"(?i)(?<![\p{L}\p{N}])Rune(?![\p{L}\p{N}])","Rune"),(@"(?i)chovka","Chovka"),
        (@"(?i)(?<![\p{L}\p{N}])KaOs(?![\p{L}\p{N}])","KaOs"),(@"(?i)wanterlude","Wanterlude"),(@"(?i)seyter","SEYTER"),(@"(?i)(?<![\p{L}\p{N}])GOG(?![\p{L}\p{N}])","GOG"),
        (@"(?i)(?<![\p{L}\p{N}])TENOKE(?![\p{L}\p{N}])","TENOKE"),(@"(?i)(?<![\p{L}\p{N}])CODEX(?![\p{L}\p{N}])","CODEX"),(@"(?i)(?<![\p{L}\p{N}])PLAZA(?![\p{L}\p{N}])","PLAZA"),
        (@"(?i)(?<![\p{L}\p{N}])SKIDROW(?![\p{L}\p{N}])","SKIDROW"),(@"(?i)(?<![\p{L}\p{N}])FLT(?![\p{L}\p{N}])","FLT"),(@"(?i)(?<![\p{L}\p{N}])RAZOR1911(?![\p{L}\p{N}])","RAZOR1911")
    ];
    static readonly Regex ByAuthor=new(@"(?i)(?:re-?pack|репак)\s*(?:by|от)\s*([\p{L}\p{N}_.\-]{2,24})",RegexOptions.CultureInvariant|RegexOptions.Compiled);
    static readonly Regex VersionText=new(@"(?i)(?<![\p{L}\p{N}])(?:v\.?\s?(\d[\w.\-]{0,20})|(?:build|билд)\s?(\d{3,8}))",RegexOptions.CultureInvariant|RegexOptions.Compiled);

    // "Репак", "Лицензия", "Portable" and so on: the first column of a release row.
    public static string Kind(SourceEntry entry)
    {
        var title=entry.Title;
        if(Regex.IsMatch(title,@"(?i)portable|портатив|портабл"))return "Portable";
        if(Regex.IsMatch(title,@"(?i)re-?pack|репак"))return "Репак";
        if(Regex.IsMatch(title,@"(?i)steam[ -]?rip"))return "Steam-Rip";
        if(Regex.IsMatch(title,@"(?i)лицензи|licen[cs]e|(?<![\p{L}\p{N}])GOG(?![\p{L}\p{N}])|drm[ -]?free"))return "Лицензия";
        if(Regex.IsMatch(title,@"(?i)(?<![\p{L}\p{N}])ISO(?![\p{L}\p{N}])|образ"))return "ISO";
        return "Игра";
    }
    public static string? Group(SourceEntry entry)
    {
        foreach(var (pattern,name) in Groups)if(Regex.IsMatch(entry.Title,pattern))return name;
        var author=ByAuthor.Match(entry.Title);
        return author.Success?author.Groups[1].Value:null;
    }
    public static string? Version(SourceEntry entry)
    {
        var match=VersionText.Match(entry.Title);
        if(!match.Success)return null;
        return match.Groups[1].Success?"v"+match.Groups[1].Value.TrimEnd('.','-'):"Build "+(match.Groups[2].Success?match.Groups[2].Value:"");
    }
    public static bool Russian(SourceEntry entry)=>Regex.IsMatch(entry.Title,@"(?i)(?<![\p{L}\p{N}])(?:rus|рус|русск\w*|ru)(?![\p{L}\p{N}])");
    public static bool WithDlc(SourceEntry entry)=>Regex.IsMatch(entry.Title,@"(?i)all\s+dlcs?|\+\s*\d*\s*dlcs?|со\s+всеми\s+dlc|все\s+dlc");
}

// The one release worth a primary "Download" button. Seeders decide, as the film rule does, but a game has no
// quality tiers: a Russian-language release wins among the well-seeded ones.
public static class GameReleaseRecommendation
{
    public const int MinimumSeeds=10;
    public static ReleaseRecommendation.Choice? Pick(IReadOnlyList<SourceEntry> releases)
    {
        var candidates=releases.Where(x=>ReleaseQuality.Downloadable(x)&&x.Seeds>=MinimumSeeds&&GameReleaseSources.WindowsRelease(x.Title)).ToArray();
        if(candidates.Length==0)return null;
        var most=candidates.Max(x=>x.Seeds)!.Value;
        // A release with half of the best seeder count is as fast in practice, so language may decide between them.
        var pool=candidates.Where(x=>x.Seeds>=Math.Max(MinimumSeeds,most/2)).ToArray();
        var best=pool.OrderByDescending(GameReleaseInfo.Russian).ThenByDescending(x=>x.Seeds).ThenBy(x=>x.Size??long.MaxValue).First();
        return new(best,Reason(best,most));
    }
    static string Reason(SourceEntry best,int most)
    {
        var parts=new List<string>();
        if(best.Seeds==most)parts.Add("больше всего отдающих");else if(best.Seeds>=100)parts.Add("много отдающих");
        if(GameReleaseInfo.Russian(best))parts.Add("есть русский язык");
        if(GameReleaseInfo.Group(best) is {} group)parts.Add(GameReleaseInfo.Kind(best)=="Репак"?"репак "+group:group);
        if(parts.Count==0)parts.Add("проверенный источник");
        var text=string.Join(" · ",parts.Take(3));
        return char.ToUpperInvariant(text[0])+text[1..];
    }
}
