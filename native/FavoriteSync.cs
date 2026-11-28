namespace Kachalka;

/// <summary>One favourite as the devices exchange it: the shared-catalog id ("movies:slug"), when it last changed (Unix ms) and whether it was removed.</summary>
public sealed record FavoriteMark(string Id,long At,bool Removed);

/// <summary>
/// Last-writer-wins merge of the favourites of two devices. Each device keeps a mark per title; a removal is a mark too, so it
/// travels to the other device instead of the title coming back. Marks are derived from the favourites list when a sync happens
/// (a title that appeared since the last sync was added now, one that vanished was removed now), so no editing code has to know about sync.
/// </summary>
public static class FavoriteSync
{
    public const int MaxMarks=5000;
    static bool ValidId(string? id)=>id!=null&&id.Length is >0 and <=140&&(id.StartsWith("movies:",StringComparison.Ordinal)||id.StartsWith("series:",StringComparison.Ordinal))&&id.All(c=>char.IsAsciiLetterOrDigit(c)||c is ':' or '-' or '_');

    /// <summary>Brings the marks in line with the favourites as they are now.</summary>
    public static void Reconcile(Dictionary<string,FavoriteMark> marks,IReadOnlyCollection<string> currentIds,long now)
    {
        var current=currentIds.Where(ValidId).ToHashSet(StringComparer.Ordinal);
        foreach(var id in current)
            if(!marks.TryGetValue(id,out var mark)||mark.Removed)marks[id]=new(id,now,false);
        foreach(var mark in marks.Values.Where(x=>!x.Removed&&!current.Contains(x.Id)).ToArray())marks[mark.Id]=new(mark.Id,now,true);
        // Old removals are dropped when the table gets large: a device that never synced for that long would re-add them, which is harmless.
        if(marks.Count>MaxMarks)foreach(var old in marks.Values.Where(x=>x.Removed).OrderBy(x=>x.At).Take(marks.Count-MaxMarks).ToArray())marks.Remove(old.Id);
    }

    /// <summary>Applies what the other device sent. Returns the titles that must now be added to and removed from the favourites list.</summary>
    public static (List<string> Add,List<string> Remove) Merge(Dictionary<string,FavoriteMark> marks,IEnumerable<FavoriteMark> incoming)
    {
        var add=new List<string>();var remove=new List<string>();
        foreach(var mark in incoming.Where(x=>ValidId(x.Id)&&x.At>0).Take(MaxMarks))
        {
            if(marks.TryGetValue(mark.Id,out var known)&&mark.At<=known.At)continue;
            marks[mark.Id]=mark;
            (mark.Removed?remove:add).Add(mark.Id);
        }
        return (add,remove);
    }
}
