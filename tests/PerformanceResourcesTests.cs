using Kachalka;

public static class PerformanceResourcesTests
{
    public static async Task Run()
    {
        void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
        Check(PerformanceBudget.IsConstrained(2,16L<<30)&&PerformanceBudget.IsConstrained(8,4L<<30)&&!PerformanceBudget.IsConstrained(8,0)&&!PerformanceBudget.IsConstrained(4,8L<<30),"low-resource budgets cover two-core and 4 GB machines without treating unknown memory as zero");
        Check(PerformanceBudget.DefaultLiteMode(true,true)&&!PerformanceBudget.DefaultLiteMode(true,false),"automatic low-resource defaults preserve both explicit manual Lite choices");
        var cache=new WeightedLruCache<string,byte[]>(10,value=>value.Length);
        cache.Put("a",new byte[4]);cache.Put("b",new byte[4]);cache.TryGetValue("a",out _);cache.Put("c",new byte[4]);
        Check(cache.Bytes==8&&cache.ContainsKey("a")&&!cache.ContainsKey("b"),"image budget evicts the least recently used entry by bytes");
        cache.Put("large",new byte[11]);cache.Put("a",new byte[2]);cache.Resize(4);
        Check(cache.Bytes<=4&&!cache.ContainsKey("large")&&cache.ContainsKey("a"),"oversized images are not retained and switching to Lite shrinks the budget");
        cache.Clear();Check(cache.Count==0&&cache.Bytes==0,"clearing decoded images releases the entire accounted cache");
        var shared=new SharedAsyncWork<int>();var calls=0;CancellationToken producer=default;
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hold=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int> Load(CancellationToken token){Interlocked.Increment(ref calls);producer=token;entered.TrySetResult();return await hold.Task.WaitAsync(token);}
        using var first=new CancellationTokenSource();using var second=new CancellationTokenSource();
        var a=shared.Run("poster",Load,first.Token);await entered.Task;
        var b=shared.Run("poster",Load,second.Token);first.Cancel();
        try{await a;throw new Exception("Unloaded subscriber received an image");}catch(OperationCanceledException){}
        Check(calls==1&&!producer.IsCancellationRequested&&!b.IsCompleted,"duplicate covers share work and unloading one keeps the visible subscriber alive");
        hold.SetResult(7);Check(await b==7,"the remaining subscriber receives the shared image");
        entered=new(TaskCreationOptions.RunContinuationsAsynchronously);hold=new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var last=new CancellationTokenSource();var abandoned=shared.Run("away",Load,last.Token);await entered.Task;last.Cancel();
        try{await abandoned;throw new Exception("Abandoned request survived cancellation");}catch(OperationCanceledException){}
        Check(producer.IsCancellationRequested,"the final unloaded subscriber cancels network and decoding work");
        var recovered=await shared.Run("away",_=>Task.FromResult(9),CancellationToken.None);
        Check(recovered==9,"returning to a canceled image starts fresh work without a poisoned shared task");
    }
}
