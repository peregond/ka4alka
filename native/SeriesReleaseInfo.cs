using System.Text.RegularExpressions;

namespace Kachalka;

public readonly record struct SeriesReleaseInfo(int? Season,int? Episode,bool MultipleSeasons,bool MultipleEpisodes)
{
    public string SeasonLabel => MultipleSeasons?"Несколько сезонов":Season.HasValue?$"{Season} сезон":"Сезон не указан";
    public string EpisodeLabel => MultipleEpisodes?"Несколько серий":Episode.HasValue?$"{Episode} серия":Season.HasValue||MultipleSeasons?"Сезон целиком":"Серия не указана";

    public static SeriesReleaseInfo Parse(string title)
    {
        var season=Regex.Match(title,@"(?i)(?:\bS|\b[cс]езоны?\s*)(?<first>\d{1,2})\s*[-–]\s*S?(?<last>\d{1,2})\b");
        var multipleSeasons=season.Success||Regex.IsMatch(title,@"(?i)(все\s+сезоны|complete\s+series|all\s+seasons)");
        var seasonNumber=season.Success?int.Parse(season.Groups["first"].Value):Number(title,@"(?i)\bS(?<n>\d{1,2})(?:\s*E\d{1,3})?\b")
            ??Number(title,@"(?i)(?:\bсезон\s*(?<n>\d{1,2})\b|\b(?<n>\d{1,2})\s*(?:-й\s*)?сезон\b)");
        var episode=Number(title,@"(?i)\bS\d{1,2}\s*E(?<n>\d{1,3})\b")
            ??Number(title,@"(?i)(?:\bсерия\s*(?<n>\d{1,3})\b|\b(?<n>\d{1,3})\s*(?:-я\s*)?серия\b)")
            ??Number(title,@"(?i)\b(?:EP|Episode)\s*(?<n>\d{1,3})\b")
            ??Number(title,@"\s[-–]\s*(?<n>\d{2,4})(?=\s|[.\[]|$)");
        var multipleEpisodes=Regex.IsMatch(title,@"(?i)(?:\bE\d{1,3}|\bсерии?\s*\d{1,3})\s*[-–]\s*(?:E|серии?\s*)?\d{1,3}\b");
        return new(seasonNumber,episode,multipleSeasons,multipleEpisodes);
    }

    static int? Number(string value,string pattern)
    {
        var match=Regex.Match(value,pattern);
        return match.Success&&int.TryParse(match.Groups["n"].Value,out var number)?number:null;
    }
}
