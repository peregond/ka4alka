using Kachalka;

static class CatalogBatchTests
{
    public static async Task Run()
    {
        void Check(bool condition,string name){if(!condition)throw new Exception(name);Console.WriteLine("PASS: "+name);}
        MediaItem Item(int id)=>new(id,"Фильм "+id,"Фильмы","",2026,"—","—","#526B69");
        var calls=new List<int>();
        Task<IReadOnlyList<MediaItem>> Fetch(int page,CancellationToken ct){calls.Add(page);return Task.FromResult<IReadOnlyList<MediaItem>>(Enumerable.Range((page-1)*40+1,40).Select(Item).ToArray());}
        var expanded=await CatalogBatches.ReadPage(_=>Task.FromResult<IReadOnlyList<MediaItem>>([Item(1)]),_=>Fetch(1,CancellationToken.None),true,CancellationToken.None);
        Check(expanded.Count==40,"partly indexed catalog page is completed from direct source");calls.Clear();
        var partial=await CatalogBatches.ReadPage(_=>Task.FromResult<IReadOnlyList<MediaItem>>([Item(1)]),_=>Task.FromException<IReadOnlyList<MediaItem>>(new IOException("offline")),true,CancellationToken.None);
        Check(partial.Count==1,"direct source failure retains usable partial index page");
        var search=await CatalogBatches.ReadPage(_=>Task.FromResult<IReadOnlyList<MediaItem>>([Item(1)]),_=>throw new Exception("Search must not fetch another page"),false,CancellationToken.None);
        Check(search.Count==1,"short search result does not trigger catalog page completion");
        var first=await CatalogBatches.Load(CatalogBatch.Empty,Fetch);
        Check(first.Items.Length==100&&first.Pending.Length==20&&first.NextPage==4&&calls.SequenceEqual([1,2,3]),"initial catalog contains 100 cards and retains unused page remainder");
        var cached=Enumerable.Range(1,100).Select(Item).ToArray();
        var incomplete=new CatalogBatch(cached.Take(40).ToArray(),[],2,true,true,100);
        Check(CatalogBatches.Visible(incomplete,cached,false,false).Length==100,"partial failed refresh retains previously visible cached selection");
        Check(CatalogBatches.Visible(incomplete with{Failed=false},cached,false,false).Length==40,"successful shorter catalog is not padded with obsolete cache entries");
        var next=await CatalogBatches.Load(first,Fetch);
        Check(next.Items.Length==200&&next.Items.Select(x=>x.Id).SequenceEqual(Enumerable.Range(1,200))&&calls.SequenceEqual([1,2,3,4,5]),"load more appends 100 without skipping or refetching the buffered 20 cards");
        Check(first.Items.Length==100&&first.Pending.Length==20,"next catalog batch does not mutate previous state");
        var broken=await CatalogBatches.Load(first,(page,ct)=>Task.FromException<IReadOnlyList<MediaItem>>(new IOException("offline")));
        Check(broken.Failed&&broken.Items.Length==120&&broken.NextPage==4&&broken.HasMore,"failed next page retains visible and buffered cards with a retry cursor");
        var retried=await CatalogBatches.Load(broken,Fetch);
        Check(retried.Items.Length==200&&retried.Items.Select(x=>x.Id).Distinct().Count()==retried.Items.Length&&retried.Items.Take(120).Select(x=>x.Id).SequenceEqual(Enumerable.Range(1,120)),"catalog retry completes interrupted batch, retains existing order and avoids duplicate cards");
        var duplicate=await CatalogBatches.Load(CatalogBatch.Empty,(page,ct)=>Task.FromResult<IReadOnlyList<MediaItem>>(Enumerable.Range(1,40).Select(Item).ToArray()));
        Check(duplicate.Items.Length==40&&!duplicate.HasMore,"source repeating same page cannot loop or duplicate catalog");
        var ended=await CatalogBatches.Load(first,(page,ct)=>Task.FromResult<IReadOnlyList<MediaItem>>([]));
        Check(ended.Items.Length==120&&!ended.HasMore,"catalog exhaustion keeps all remaining cards and hides load more");
        var capped=await CatalogBatches.Load(new([],Enumerable.Range(1,20).Select(Item).ToArray(),41),Fetch);
        Check(capped.Items.Length==20&&!capped.HasMore,"end of source page limit drains buffer without offering impossible requests");
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        try{await CatalogBatches.Load(first,Fetch,ct:canceled.Token);throw new Exception("Canceled catalog batch ran");}catch(OperationCanceledException){Check(first.Items.Length==100,"cancellation leaves previous catalog intact");}
        Check(WheelScroll.Distance(120,3,500)==48&&WheelScroll.Distance(30,3,500)==12,"wheel uses small pixel steps and preserves precision deltas");
        Check(WheelScroll.Distance(120,0,500)==0&&WheelScroll.Distance(120,-1,500)==425,"wheel respects disabled scrolling and system page preference");
        Check(WheelScroll.Destination(200,248,-48,1000,true)==152,"wheel reversal discards pending motion in old direction");
        Check(WheelScroll.Destination(200,248,48,1000,true)==296&&WheelScroll.Destination(990,990,48,1000,false)==1000,"rapid wheel input accumulates within scroll bounds");
    }
}
