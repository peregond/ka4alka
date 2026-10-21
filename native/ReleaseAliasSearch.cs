namespace Kachalka;

// A slow alias must not erase successful replies when its provider's overall budget ends.
public static class ReleaseAliasSearch
{
    public static async Task<IReadOnlyList<SourceEntry>> Run(IEnumerable<string> names,
        Func<string,CancellationToken,Task<IReadOnlyList<SourceEntry>>> search,CancellationToken ct,
        TimeSpan? aliasTimeout=null)
    {
        ct.ThrowIfCancellationRequested();
        var queries=names.Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>x.Trim()).DistinctBy(MediaIdentity.Normalize).Take(4).ToArray();
        if(queries.Length==0)return [];
        var batches=new IReadOnlyList<SourceEntry>[queries.Length];var errors=new Exception?[queries.Length];var cursor=-1;
        async Task Worker()
        {
            int index;
            while((index=Interlocked.Increment(ref cursor))<queries.Length)
            {
                ct.ThrowIfCancellationRequested();
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(aliasTimeout??TimeSpan.FromSeconds(queries.Length<=2?15:7));
                try{batches[index]=await search(queries[index],deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);}
                catch(Exception error)when(!ct.IsCancellationRequested){batches[index]=[];errors[index]=error;}
            }
        }
        await Task.WhenAll(Enumerable.Range(0,Math.Min(2,queries.Length)).Select(_=>Worker()));
        ct.ThrowIfCancellationRequested();
        if(errors.All(x=>x!=null))System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]!).Throw();
        return ReleaseSearch.Distinct(batches.SelectMany(x=>x)).OrderByDescending(x=>x.Seeds??-1).Take(300).ToArray();
    }
}
