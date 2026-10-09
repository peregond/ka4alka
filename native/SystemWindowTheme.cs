using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Kachalka;

// Keep the system title bar and window controls. Windows versions without
// immersive dark captions continue to use their normal native appearance.
static class SystemWindowTheme
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);

    internal static void Apply(Window window,bool light)
    {
        var handle=new WindowInteropHelper(window).Handle;
        if(handle==nint.Zero)return;
        var dark=light?0:1;
        try
        {
            if(DwmSetWindowAttribute(handle,20,ref dark,sizeof(int))!=0)
                DwmSetWindowAttribute(handle,19,ref dark,sizeof(int));
        }
        catch(DllNotFoundException){}
        catch(EntryPointNotFoundException){}
    }
}
