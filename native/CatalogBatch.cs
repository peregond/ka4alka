namespace Kachalka;

public record CatalogBatch(MediaItem[] Items,MediaItem[] Pending,int NextPage=1,bool HasMore=true,bool Failed=false,int Goal=0)
{
    public static CatalogBatch Empty=>new([],[]);
}

public static class CatalogBatches
{
    public const int Size=100;
    public static MediaItem[] Visible(CatalogBatch batch,IEnumerable<MediaItem> cached,bool append,bool checking)
    {
        if(append)return batch.Items;
        if(batch.Items.Length==0)return cached.Take(Size).ToArray();
        return checking||batch.Failed?batch.Items.Concat(cached).DistinctBy(x=>x.Id).Take(Size).ToArray():batch.Items;
    }
    public static async Task<IReadOnlyList<MediaItem>> ReadPage(Func<CancellationToken,Task<IReadOnlyList<MediaItem>>> primary,
        Func<CancellationToken,Task<IReadOnlyList<MediaItem>>> fallback,bool collection,CancellationToken ct)
    {
        IReadOnlyList<MediaItem> rows;
        try{rows=await primary(ct);}catch(Exception) when(!ct.IsCancellationRequested){rows=[];}
        // A partly populated index page is not evidence that the source catalog ends here.
        if(rows.Count==0||collection&&rows.Count<40)
        {
            try{var direct=await fallback(ct);if(direct.Count>rows.Count)rows=direct;}
            catch(Exception) when(rows.Count>0&&!ct.IsCancellationRequested){}
        }
        ct.ThrowIfCancellationRequested();return rows;
    }
    public static async Task<CatalogBatch> Load(CatalogBatch previous,Func<int,CancellationToken,Task<IReadOnlyList<MediaItem>>> fetch,
        IProgress<CatalogBatch>? progress=null,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        var items=previous.Items.ToList();var seen=items.Select(x=>x.Id).ToHashSet();
        var pending=previous.Pending.Where(x=>seen.Add(x.Id)).ToList();
        var target=previous.Failed&&previous.Goal>items.Count?previous.Goal:items.Count+Size;var page=previous.NextPage;var more=previous.HasMore&&page<=40;
        CatalogBatch Snapshot(bool failed=false)=>new(items.ToArray(),pending.ToArray(),page,more||pending.Count>0,failed,target);
        void Drain(){var count=Math.Min(target-items.Count,pending.Count);items.AddRange(pending.Take(count));pending.RemoveRange(0,count);}
        Drain();
        while(items.Count<target&&more&&page<=40)
        {
            IReadOnlyList<MediaItem> rows;
            try{rows=await fetch(page,ct);}
            catch(Exception) when(!ct.IsCancellationRequested){var failed=Snapshot(true);progress?.Report(failed);return failed;}
            ct.ThrowIfCancellationRequested();
            var unique=rows.Where(x=>seen.Add(x.Id)).ToArray();
            if(unique.Length==0){more=false;break;}
            pending.AddRange(unique);page++;more=page<=40;Drain();progress?.Report(Snapshot());
        }
        var result=Snapshot();progress?.Report(result);return result;
    }
}
