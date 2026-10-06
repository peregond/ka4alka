namespace Kachalka;

public record SearchResult(MediaItem[] Items,bool Offline);
public static class UnifiedSearch
{
    public static async Task<SearchResult> FindAsync(string query,Func<string,CancellationToken,Task<IReadOnlyList<MediaItem>>> remote,Func<string,IReadOnlyList<MediaItem>> saved,CancellationToken cancellation)
    {
        async Task<SearchResult> Category(string category)
        {
            IReadOnlyList<MediaItem> items=[];bool offline=false;
            try{items=await remote(category,cancellation);}
            catch(Exception) when(!cancellation.IsCancellationRequested){offline=true;}
            cancellation.ThrowIfCancellationRequested();
            return new(items.Concat(saved(category)).Where(x=>x.Section==category).DistinctBy(x=>(x.Section,x.Id)).Take(80).ToArray(),offline);
        }
        var batches=await Task.WhenAll(Category("Фильмы"),Category("Сериалы"));cancellation.ThrowIfCancellationRequested();
        var term=query.Trim();
        int Rank(MediaItem item)=>item.Title.Equals(term,StringComparison.CurrentCultureIgnoreCase)?0:item.Title.StartsWith(term,StringComparison.CurrentCultureIgnoreCase)?1:2;
        return new(batches.SelectMany(x=>x.Items).DistinctBy(x=>(x.Section,x.Id)).OrderBy(Rank).ThenBy(x=>x.Title,StringComparer.CurrentCultureIgnoreCase).ToArray(),batches.Any(x=>x.Offline));
    }
    public static MediaItem[] Filter(IEnumerable<MediaItem> items,string category)=>items.Where(x=>category==""||x.Section==category).ToArray();
}
