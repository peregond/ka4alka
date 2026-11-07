namespace Kachalka;

// The rules of the banner's automatic change of slides, the same as in the macOS version: every eight seconds,
// only while the banner is on screen, the window is active and nobody points at it, focuses it or has paused it.
public static class HeroRotation
{
    public static readonly TimeSpan Interval=TimeSpan.FromSeconds(8);

    public static bool CanAdvance(int count,bool visible,bool active,bool interacting,bool paused,bool reduced)=>
        count>1&&visible&&active&&!interacting&&!paused&&!reduced;

    // Resolves against identity rather than a position, so a refresh cannot select another title by accident.
    // A selection that is gone falls back to the first slide.
    public static string? Next(IReadOnlyList<string> ids,string? selected,int delta)
    {
        if(ids.Count==0)return null;
        var current=Math.Max(0,ids.ToList().IndexOf(selected??""));
        return ids[((current+delta)%ids.Count+ids.Count)%ids.Count];
    }
}
