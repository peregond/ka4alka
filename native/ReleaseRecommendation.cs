namespace Kachalka;

// Picks the one release worth offering behind the primary "Download" button.
// It never invents data: a release qualifies only on facts that are already in the row.
public static class ReleaseRecommendation
{
    public const int MinimumSeeds=20;
    public sealed record Choice(SourceEntry Entry,string Reason);

    // Every visible subset can use the same ranked candidates (Windows filters and the Mac engine contract).
    public static SourceEntry[] Ranked(IReadOnlyList<SourceEntry> releases,int minimumHeight=720,string preferredLanguage="ru")
    {
        var usable=releases.Where(x=>ReleaseQuality.Downloadable(x)&&!ReleaseQuality.Poor(x,minimumHeight)).ToArray();
        var dubs=preferredLanguage=="en"?[]:usable.Where(x=>x.Seeds>0&&ReleaseQuality.Height(x)>=720&&
            RussianAudio.Rank(x)==3&&RussianAudio.DubbedAudio(x)&&ReleaseAudio.DubbingPriority(x)>0).ToArray();
        var healthy=usable.Where(x=>x.Seeds>=MinimumSeeds&&x.Quality is "Full HD" or "4K");
        return SearchLanguage.AudioOrder(dubs.Concat(healthy).Distinct(),preferredLanguage).ToArray();
    }
    public static Choice? Pick(IReadOnlyList<SourceEntry> releases,int minimumHeight=720,string preferredLanguage="ru")
    {
        var candidates=Ranked(releases,minimumHeight,preferredLanguage);
        return candidates.Length==0?null:new(candidates[0],Reason(candidates[0],candidates,preferredLanguage));
    }
    static string Reason(SourceEntry best,SourceEntry[] candidates,string preferredLanguage)
    {
        var parts=new List<string>();
        if(preferredLanguage=="en"&&SearchLanguage.EnglishAudioRank(best)==3)parts.Add("английская озвучка");
        else if(RussianAudio.Rank(best)==3)parts.Add(RussianAudio.DubbedAudio(best)?"русский дубляж":"русская озвучка");
        if(best.Seeds<MinimumSeeds)parts.Add("мало отдающих");
        else if(best.Seeds==candidates.Max(x=>x.Seeds))parts.Add("больше всего отдающих");
        else if(best.Seeds>=100)parts.Add("много отдающих");
        var sizes=candidates.Where(x=>x.Size.HasValue).Select(x=>x.Size!.Value).OrderBy(x=>x).ToArray();
        if(best.Size.HasValue&&sizes.Length>1&&best.Size.Value<=sizes[sizes.Length/2])parts.Add("умеренный размер");
        if(parts.Count==0)parts.Add("проверенный источник");
        var text=string.Join(" при ",parts.Take(2));
        return char.ToUpperInvariant(text[0])+text[1..];
    }

    // Seed health shown in the release table. Thresholds follow the redesign specification.
    public enum Health{Unknown,None,Slow,Medium,Fast}
    public static Health HealthOf(int? seeds)=>seeds switch{null=>Health.Unknown,<=0=>Health.None,<10=>Health.Slow,<50=>Health.Medium,_=>Health.Fast};
    public static int Bars(Health health)=>health switch{Health.Fast=>3,Health.Medium=>2,Health.Slow=>1,_=>0};
    public static string Caption(Health health)=>health switch{Health.Fast=>"Быстро",Health.Medium=>"Средне",Health.Slow=>"Медленно",Health.None=>"Никто не отдаёт",_=>"Неизвестно"};
}
