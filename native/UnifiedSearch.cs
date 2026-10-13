namespace Kachalka;

public record SearchResult(MediaItem[] Items,bool Offline);
public static class UnifiedSearch
{
    public static async Task<SearchResult> FindAsync(string query,Func<string,CancellationToken,Task<IReadOnlyList<MediaItem>>> remote,Func<string,IReadOnlyList<MediaItem>> saved,CancellationToken cancellation,
        Func<CancellationToken,Task<IReadOnlyList<MediaItem>>>? supplement=null,Action<SearchResult>? updated=null,string preferredLanguage="ru")
    {
        cancellation.ThrowIfCancellationRequested();
        var term=query.Trim();
        int Rank(MediaItem item)=>MediaIdentity.Names(item).Any(name=>name.Equals(term,StringComparison.CurrentCultureIgnoreCase))?0:MediaIdentity.Names(item).Any(name=>name.StartsWith(term,StringComparison.CurrentCultureIgnoreCase))?1:2;
        // Read UI-owned local stores once. Every subsequent merge/rank runs on
        // a worker; publication returns to the caller's synchronization context.
        var local=new[]{"Фильмы","Сериалы"}.SelectMany(kind=>saved(kind).Where(x=>x.Section==kind)).DistinctBy(x=>(x.Section,x.Id)).ToArray();
        SearchResult Build(IEnumerable<MediaItem> rows,bool offline)=>new(MediaIdentity.Merge(rows.DistinctBy(x=>(x.Section,x.Id))).OrderBy(Rank).ThenBy(x=>SearchLanguage.TitleRank(x,preferredLanguage)).ThenBy(x=>x.Title,StringComparer.CurrentCultureIgnoreCase).ToArray(),offline);
        if(local.Length>0)updated?.Invoke(Build(local,false));
        async Task<SearchResult> Category(string category)
        {
            try{return new((await remote(category,cancellation).WaitAsync(cancellation)).Where(x=>x.Section==category).Take(80).ToArray(),false);}
            catch(Exception) when(!cancellation.IsCancellationRequested){return new([],true);}
        }
        async Task<SearchResult> Extra()
        {
            if(supplement==null)return new([],false);
            try{return new((await supplement(cancellation).WaitAsync(cancellation)).ToArray(),false);}
            catch(Exception) when(!cancellation.IsCancellationRequested){return new([],false);}
        }
        var pending=new List<Task<SearchResult>>{Category("Фильмы"),Category("Сериалы"),Extra()};
        var rows=new List<MediaItem>();var offline=false;var result=Build(local,false);
        while(pending.Count>0)
        {
            var completed=await Task.WhenAny(pending).WaitAsync(cancellation);pending.Remove(completed);
            var batch=await completed;cancellation.ThrowIfCancellationRequested();
            rows.AddRange(batch.Items);offline|=batch.Offline;
            // Empty replies don't cause a needless full catalog rebuild.
            if(batch.Items.Length>0||batch.Offline)
            {
                var snapshot=rows.Concat(local).ToArray();result=await Task.Run(()=>Build(snapshot,offline),cancellation);
                cancellation.ThrowIfCancellationRequested();updated?.Invoke(result);
            }
        }
        return result;
    }
    public static MediaItem[] Filter(IEnumerable<MediaItem> items,string category)=>items.Where(x=>category==""||x.Section==category).ToArray();
}
