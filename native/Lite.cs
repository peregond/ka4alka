using System.Windows;
using System.Windows.Media;
namespace Kachalka;

// Lite mode removes decorative backdrops (carousel and film banner use the flat Panel surface with the small poster)
// and lets WPF scale posters with the cheaper filter. It adds no timers, effects or animations.
public partial class MainWindow
{
    void ApplyLiteMode()
    {
        lightweightMotion=prefs.LiteMode;PerformanceBudget.LiteMode=prefs.LiteMode;
        coverCache.Resize(prefs.LiteMode?Math.Min(PerformanceBudget.ImageMemoryBytes,12L*1024*1024):PerformanceBudget.ImageMemoryBytes);
        portraitCache.Resize(prefs.LiteMode||PerformanceBudget.Constrained?4L*1024*1024:8L*1024*1024);
        RenderOptions.SetBitmapScalingMode(this,prefs.LiteMode?BitmapScalingMode.LowQuality:BitmapScalingMode.Unspecified);
    }
}
