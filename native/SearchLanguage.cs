using System.Text.RegularExpressions;
namespace Kachalka;

public static class SearchLanguage
{
    // A script preference ranks title representations; it does not claim to
    // identify the language of the film or translate names guessed from text.
    public static bool LatinTitle(string title)=>title.Any(c=>c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')&&!title.Any(c=>c is >= '\u0400' and <= '\u04ff');
    public static int TitleRank(MediaItem item,string language)=>language!="en"?0:LatinTitle(item.Title)?0:MediaIdentity.Names(item).Any(LatinTitle)?1:2;
    static readonly Regex English=new(@"(?i)(?<![\p{L}\p{N}])(?:EN|ENG|ENGLISH|английск[\p{L}]*)(?![\p{L}\p{N}])",RegexOptions.CultureInvariant);
    static readonly Regex Other=new(@"(?i)(?<![\p{L}\p{N}])(?:HINDI|HIN|FRENCH|FRA|FRE|GERMAN|GER|DEU|ITALIAN|ITA|SPANISH|SPA|ESP|JAPANESE|JPN|JAP|KOREAN|KOR|CHINESE|CHI|ZHO|TAMIL|TELUGU|MULTI)(?![\p{L}\p{N}])",RegexOptions.CultureInvariant);
    public static int EnglishAudioRank(SourceEntry entry)
    {
        var audio=ReleaseAudio.AudioTitle(entry.Title);
        if(English.IsMatch(audio))return 3;
        if(RussianAudio.Rank(entry)==3||Other.IsMatch(audio))return 0;
        return RussianAudio.RussianSource(entry)?1:2;
    }
    public static IEnumerable<SourceEntry> AudioOrder(IEnumerable<SourceEntry> entries,string language)=>language=="en"
        ?entries.OrderBy(x=>x.Seeds==0).ThenByDescending(EnglishAudioRank).ThenByDescending(x=>x.Seeds??-1)
        :RussianAudio.Order(entries);
}
