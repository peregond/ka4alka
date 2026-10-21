using Kachalka;

// One owner for mutable queue and preference state, as in WPF's dispatcher.
// Native IPC uses inherited pipes. A separate local browser server starts only on explicit user request.
using var loop=new EngineLoop();
loop.Run(async()=>
{
    await using var engine=new DesktopEngine();
    await engine.Run();
});
