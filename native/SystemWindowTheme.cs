using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Kachalka;

// Dialogs keep the system title bar and window controls. Windows versions without
// immersive dark captions continue to use their normal native appearance.
static class SystemWindowTheme
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
    const int ImmersiveDarkMode=20,ImmersiveDarkModeLegacy=19,CornerPreference=33,RoundCorners=2;

    // merged: the main window draws its own caption over the whole window (WindowChrome); Windows 11
    // still rounds its corners like any other window. Older versions ignore the attribute.
    internal static void Apply(Window window,bool light,bool merged=false)
    {
        var handle=new WindowInteropHelper(window).Handle;
        if(handle==nint.Zero)return;
        var dark=light?0:1;
        try
        {
            if(DwmSetWindowAttribute(handle,ImmersiveDarkMode,ref dark,sizeof(int))!=0)
                DwmSetWindowAttribute(handle,ImmersiveDarkModeLegacy,ref dark,sizeof(int));
            if(!merged)return;
            var round=RoundCorners;
            DwmSetWindowAttribute(handle,CornerPreference,ref round,sizeof(int));
        }
        catch(DllNotFoundException){}
        catch(EntryPointNotFoundException){}
    }
}
