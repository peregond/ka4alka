using Kachalka;

// One owner for mutable queue and preference state, as in WPF's dispatcher.
// IPC is over inherited pipes only: there is no localhost listener or web renderer.
using var loop=new EngineLoop();
loop.Run(async()=>
{
    await using var engine=new DesktopEngine();
    await engine.Run();
});
