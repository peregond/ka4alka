using System.Text.RegularExpressions;
namespace Kachalka;

public static class RussianAudio
{
    static readonly Regex Russian=new(@"(?i)(?<![\p{L}\p{N}])(?:RU|RUS|RUSSIAN|русский|русская|русское)(?![\p{L}\p{N}])",RegexOptions.CultureInvariant);
    static readonly Regex Dubbed=new(@"(?i)(?<![\p{L}\p{N}])(?:DUB|MVO|DVO|AVO|дубляж|дублирован\w*|многоголос\w*|двухголос\w*|одноголос\w*|озвучка|перевод)(?![\p{L}\p{N}])",RegexOptions.CultureInvariant);
    static readonly Regex Original=new(@"(?i)(?<![\p{L}\p{N}])(?:ENG|ENGLISH|ITA|ITALIAN|FRENCH|FRA|GERMAN|GER|JAPANESE|JPN|оригинал|original)(?![\p{L}\p{N}])",RegexOptions.CultureInvariant);
    public static bool RussianSource(SourceEntry entry)=>entry.Source is "RuTor" or "RuTracker" or "NNM-Club" or "MegaPeer" or "BigFanGroup";
    public static int Rank(SourceEntry entry)
    {
        // Russian subtitles alone are not evidence of a Russian audio track.
        var audio=ReleaseAudio.AudioTitle(entry.Title);
        if(Russian.IsMatch(audio)||RussianSource(entry)&&(Dubbed.IsMatch(audio)||DubbedAudio(entry)))return 3;
        if(Original.IsMatch(audio))return 0;
        return RussianSource(entry)?2:1;
    }
    // An advertised dub wins over voice-over only within the same language
    // tier. A studio name alone is never evidence of dubbing or licensing.
    public static bool DubbedAudio(SourceEntry entry)=>ReleaseAudio.TypeNames(entry).Contains("Дубляж");
    public static IEnumerable<SourceEntry> Order(IEnumerable<SourceEntry> entries)=>entries
        .OrderBy(x=>x.Seeds==0).ThenByDescending(Rank).ThenByDescending(ReleaseAudio.DubbingPriority).ThenByDescending(x=>x.Seeds??-1);
}
