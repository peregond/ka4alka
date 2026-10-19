static class EngineLoopTests
{
    public static void Run()
    {
        var previous=SynchronizationContext.Current;
        using var loop=new EngineLoop();SynchronizationContext? captured=null;
        var owner=Environment.CurrentManagedThreadId;var resumed=false;var lateCallbacks=0;
        loop.Run(async()=>
        {
            captured=SynchronizationContext.Current;
            await Task.Yield();await Task.Delay(1);
            resumed=Environment.CurrentManagedThreadId==owner&&SynchronizationContext.Current==captured;
        });
        Check(resumed&&captured==loop,"engine continuations stay on the owner thread before shutdown");
        Check(SynchronizationContext.Current==previous,"engine shutdown restores the caller synchronization context");
        captured!.Post(_=>Interlocked.Increment(ref lateCallbacks),null);
        Check(lateCallbacks==0,"late captured-context callbacks are ignored after the owner exits");
        loop.Dispose();
        Parallel.For(0,256,_=>captured.Post(_=>Interlocked.Increment(ref lateCallbacks),null));
        Check(lateCallbacks==0,"concurrent HTTP completions after loop disposal cannot crash shutdown");
        using var failing=new EngineLoop();
        try{failing.Run(()=>Task.FromException(new IOException("owner failure")));throw new Exception("Owner failure was swallowed");}
        catch(IOException error)when(error.Message=="owner failure"){Console.WriteLine("PASS: owner failures still propagate before the loop closes");}
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
