namespace Kachalka;

public static class ReleaseAvailability
{
    public static bool Downloadable(SourceEntry entry)=>!string.IsNullOrWhiteSpace(entry.TorrentUrl)||entry.Source=="Internet Archive";
    public static bool ConfirmedEmpty(IEnumerable<SourceEntry> entries,IReadOnlyList<SourceCheck>? checks)=>
        !entries.Any(Downloadable)&&checks is {Count:>0}&&checks.All(x=>x.State==SourceState.Empty);
    public static bool Recommended(ReleaseCache? cache,DateTime now)=>cache!=null&&cache.SavedUtc>now.AddDays(-7)&&cache.SavedUtc<=now.AddMinutes(5)&&cache.Items.Any(Downloadable);
    public static bool Hidden(ReleaseCache? cache,DateTime now)=>cache!=null&&cache.SavedUtc>now.AddDays(-1)&&cache.SavedUtc<=now.AddMinutes(5)&&ConfirmedEmpty(cache.Items,cache.Sources);
}

