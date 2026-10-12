namespace Kachalka;

// Conservative budgets, independent of network speed. Unknown memory does not
// classify a machine as low-memory; an explicit Lite choice always wins.
public static class PerformanceBudget
{
    public static bool IsConstrained(int processors,long memoryBytes)=>processors<=2||memoryBytes>0&&memoryBytes<=4L*1024*1024*1024;
    public static bool Constrained {get;}=IsConstrained(Environment.ProcessorCount,GC.GetGCMemoryInfo().TotalAvailableMemoryBytes);
    public static bool DefaultLiteMode(bool configured,bool existing)=>configured?existing:Constrained;
    public static int ImageWorkers=>Constrained?1:2;
    public static int FilmographyWorkers=>Constrained?2:4;
    public static long ImageMemoryBytes=>Constrained?12L*1024*1024:32L*1024*1024;
}

// UI-owned decoded image cache: evict least recently used entries by bytes,
// rather than treating a 1280 px backdrop as the same size as a small poster.
public sealed class WeightedLruCache<TKey,TValue>(long budget,Func<TValue,long> weight) where TKey:notnull
{
    readonly Dictionary<TKey,LinkedListNode<(TKey Key,TValue Value,long Bytes)>> entries=[];
    readonly LinkedList<(TKey Key,TValue Value,long Bytes)> order=[];
    public long Bytes {get;private set;}
    public int Count=>entries.Count;
    public void Resize(long bytes){budget=bytes;while(Bytes>budget&&order.First is {} oldest)Remove(oldest.Value.Key);}
    public bool ContainsKey(TKey key)=>entries.ContainsKey(key);
    public void Clear(){entries.Clear();order.Clear();Bytes=0;}
    public bool Remove(TKey key){if(!entries.Remove(key,out var node))return false;order.Remove(node);Bytes-=node.Value.Bytes;return true;}
    public bool TryGetValue(TKey key,out TValue value)
    {
        if(!entries.TryGetValue(key,out var node)){value=default!;return false;}
        order.Remove(node);order.AddLast(node);value=node.Value.Value;return true;
    }
    public void Put(TKey key,TValue value)
    {
        if(entries.Remove(key,out var previous)){order.Remove(previous);Bytes-=previous.Value.Bytes;}
        var bytes=Math.Max(1,weight(value));if(bytes>budget)return;
        while(Bytes>budget-bytes&&order.First is {} oldest){order.RemoveFirst();entries.Remove(oldest.Value.Key);Bytes-=oldest.Value.Bytes;}
        var node=order.AddLast((key,value,bytes));entries.Add(key,node);Bytes+=bytes;
    }
}

// Each visible subscriber can leave independently. Only the last one cancels
// the shared request, so unloading one duplicate poster does not abort another.
public sealed class SharedAsyncWork<T>
{
    sealed class Work
    {
        public readonly CancellationTokenSource Stop=new();
        public Task<T> Task=null!;
        public int Subscribers;
    }
    readonly Dictionary<string,Work> active=[];
    readonly object gate=new();
    public async Task<T> Run(string key,Func<CancellationToken,Task<T>> load,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();Work work;
        lock(gate)
        {
            if(!active.TryGetValue(key,out work!)||work.Stop.IsCancellationRequested)
            {
                work=new();active[key]=work;var shared=work;
                work.Task=Task.Run(()=>load(shared.Stop.Token));
                _=work.Task.ContinueWith(task=>{_=task.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            }
            work.Subscribers++;
        }
        try{return await work.Task.WaitAsync(ct).ConfigureAwait(false);}
        finally
        {
            lock(gate)
            {
                if(--work.Subscribers==0)
                {
                    if(active.TryGetValue(key,out var current)&&ReferenceEquals(current,work))active.Remove(key);
                    if(!work.Task.IsCompleted)work.Stop.Cancel();
                    // The producer may still register cancellation callbacks.
                    _=work.Task.ContinueWith(_=>work.Stop.Dispose(),CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
                }
            }
        }
    }
}
