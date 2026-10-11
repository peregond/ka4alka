namespace Kachalka;

public record SearchResult(MediaItem[] Items,bool Offline);
public static class UnifiedSearch
{
    // supplement: titles from outside the catalog (IMDb suggestions); only those the catalog lacks are added.
    public static async Task<SearchResult> FindAsync(string query,Func<string,CancellationToken,Task<IReadOnlyList<MediaItem>>> remote,Func<string,IReadOnlyList<MediaItem>> saved,CancellationToken cancellation,
        Func<CancellationToken,Task<IReadOnlyList<MediaItem>>>? supplement=null,Action<SearchResult>? updated=null)
    {
        cancellation.ThrowIfCancellationRequested();
        var term=query.Trim();
        int Rank(MediaItem item)=>item.Title.Equals(term,StringComparison.CurrentCultureIgnoreCase)?0:item.Title.StartsWith(term,StringComparison.CurrentCultureIgnoreCase)?1:2;
        var local=new[]{"Фильмы","Сериалы"}.SelectMany(kind=>saved(kind).Where(x=>x.Section==kind)).DistinctBy(x=>(x.Section,x.Id)).OrderBy(Rank).ThenBy(x=>x.Title,StringComparer.CurrentCultureIgnoreCase).ToArray();
        if(local.Length>0)updated?.Invoke(new(local,false));
        async Task<IReadOnlyList<MediaItem>> Extra()
        {
            if(supplement==null)return [];
            try{return await supplement(cancellation);}
            catch(Exception) when(!cancellation.IsCancellationRequested){return [];}
        }
        var extra=Extra();
        async Task<SearchResult> Category(string category)
        {
            IReadOnlyList<MediaItem> items=[];bool offline=false;
            try{items=await remote(category,cancellation);}
            catch(Exception) when(!cancellation.IsCancellationRequested){offline=true;}
            cancellation.ThrowIfCancellationRequested();
            return new(items.Concat(saved(category)).Where(x=>x.Section==category).DistinctBy(x=>(x.Section,x.Id)).Take(80).ToArray(),offline);
        }
        var batches=await Task.WhenAll(Category("Фильмы"),Category("Сериалы"));var found=await extra;cancellation.ThrowIfCancellationRequested();
        var catalog=batches.SelectMany(x=>x.Items).ToArray();
        return new(catalog.Concat(ImdbSuggestions.Missing(catalog,found)).DistinctBy(x=>(x.Section,x.Id)).OrderBy(Rank).ThenBy(x=>x.Title,StringComparer.CurrentCultureIgnoreCase).ToArray(),batches.Any(x=>x.Offline));
    }
    public static MediaItem[] Filter(IEnumerable<MediaItem> items,string category)=>items.Where(x=>category==""||x.Section==category).ToArray();
}

