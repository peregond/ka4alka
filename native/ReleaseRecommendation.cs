namespace Kachalka;

// Picks the one release worth offering behind the primary "Download" button.
// It never invents data: a release qualifies only on facts that are already in the row.
public static class ReleaseRecommendation
{
    public const int MinimumSeeds=20;
    public sealed record Choice(SourceEntry Entry,string Reason);

    public static Choice? Pick(IReadOnlyList<SourceEntry> releases,int minimumHeight=720)
    {
        var candidates=releases.Where(x=>ReleaseQuality.Downloadable(x)&&!ReleaseQuality.Poor(x,minimumHeight)&&x.Seeds>=MinimumSeeds&&x.Quality is "Full HD" or "4K").ToArray();
        if(candidates.Length==0)return null;
        // Same default order as the table: Russian audio first, then the most seeded.
        var best=RussianAudio.Order(candidates).First();
        return new(best,Reason(best,candidates));
    }
    static string Reason(SourceEntry best,SourceEntry[] candidates)
    {
        var parts=new List<string>();
        if(RussianAudio.Rank(best)==3)parts.Add("русская озвучка");
        if(best.Seeds==candidates.Max(x=>x.Seeds))parts.Add("больше всего сидов");
        else if(best.Seeds>=100)parts.Add("много сидов");
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
    public static string Caption(Health health)=>health switch{Health.Fast=>"Быстро",Health.Medium=>"Средне",Health.Slow=>"Медленно",Health.None=>"Нет сидов",_=>"Неизвестно"};
}
