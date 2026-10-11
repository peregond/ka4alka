namespace Kachalka;

// TEMPORARY: switches for one Windows CI experiment (.github/workflows/frame-experiment.yml) that decides what made the
// WindowChrome window's layout loop. Removed together with that workflow.
static class FrameExperiment
{
    static string? Value(string name)=>Environment.GetEnvironmentVariable("KACHALKA_EXPERIMENT_"+name);
    public static bool Chrome=>Value("FRAME")=="chrome0";
    public static bool NoBand=>Value("BAND")=="0";
    public static bool PlainWidth=>Value("WIDTH")=="plain";
    public static bool BarVisible=>Value("BAR")=="visible";
    public static bool Sweep=>Value("SWEEP")=="1";
}
