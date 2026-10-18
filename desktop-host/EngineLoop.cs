using System.Collections.Concurrent;

sealed class EngineLoop:SynchronizationContext,IDisposable
{
    readonly BlockingCollection<Action> work=new();
    int closed;
    public override void Post(SendOrPostCallback callback,object? state)
    {
        if(Volatile.Read(ref closed)!=0)return;
        try{work.TryAdd(()=>callback(state));}
        catch(InvalidOperationException)when(Volatile.Read(ref closed)!=0)
        {
            // A cancelled shared HTTP operation can finish after its owner
            // has exited. The queue may close between the check and TryAdd;
            // that late continuation must not crash the helper on a pool thread.
        }
    }
    public void Run(Func<Task> entry)
    {
        var previous=Current;SetSynchronizationContext(this);
        try
        {
            var task=entry();
            _=task.ContinueWith(_=>
            {
                Volatile.Write(ref closed,1);work.CompleteAdding();
            },TaskScheduler.Default);
            foreach(var next in work.GetConsumingEnumerable())next();
            task.GetAwaiter().GetResult();
        }
        finally{SetSynchronizationContext(previous);}
    }
    public void Dispose(){Volatile.Write(ref closed,1);work.Dispose();}
}
