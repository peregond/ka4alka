using System.Windows;
using System.Windows.Media;
namespace Kachalka;

// Lite mode removes decorative backdrops (carousel and film banner use the flat Panel surface with the small poster)
// and lets WPF scale posters with the cheaper filter. It adds no timers, effects or animations.
public partial class MainWindow
{
    void ApplyLiteMode()
    {
        RenderOptions.SetBitmapScalingMode(this,prefs.LiteMode?BitmapScalingMode.LowQuality:BitmapScalingMode.Unspecified);
    }
}
