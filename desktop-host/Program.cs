using System.Collections.Concurrent;
using System.Text.Json;
using Kachalka;

// One owner for mutable queue and preference state, as in WPF's dispatcher.
// IPC is over inherited pipes only: there is no localhost listener or web renderer.
using var loop=new EngineLoop();
loop.Run(async()=>
{
    await using var engine=new DesktopEngine();
    await engine.Run();
});
sealed class EngineLoop:SynchronizationContext,IDisposable
{
    readonly BlockingCollection<Action> work=new();
    public override void Post(SendOrPostCallback callback,object? state)=>work.Add(()=>callback(state));
    public void Run(Func<Task> entry)
    {
        SetSynchronizationContext(this);var task=entry();
        _=task.ContinueWith(_=>work.CompleteAdding(),TaskScheduler.Default);
        foreach(var next in work.GetConsumingEnumerable())next();
        task.GetAwaiter().GetResult();
    }
    public void Dispose()=>work.Dispose();
}
