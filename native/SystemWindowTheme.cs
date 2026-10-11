using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Kachalka;

// Keep the system title bar and window controls (snap layouts, keyboard and accessibility
// behaviour stay native). Windows versions without immersive dark captions continue to use
// their normal native appearance.
static class SystemWindowTheme
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
    [StructLayout(LayoutKind.Sequential)]
    struct ThemeOptions{public uint Flags;public uint Mask;}
    [DllImport("uxtheme.dll")]
    static extern int SetWindowThemeAttribute(nint window,int attribute,ref ThemeOptions options,uint size);
    const int ImmersiveDarkMode=20,ImmersiveDarkModeLegacy=19,CaptionColor=35;
    const int NonClient=1;const uint NoDrawCaption=1,NoDrawIcon=2;

    // merged: the main window's caption carries no second title or icon (the sidebar shows the brand)
    // and, where Windows allows it, takes the window's own background so the app reads as one surface.
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
            // Windows 11 colours the caption; older versions ignore the attribute and keep their own.
            if(window.TryFindResource("Bg") is SolidColorBrush {Color:var color})
            {
                var reference=color.R|color.G<<8|color.B<<16;
                DwmSetWindowAttribute(handle,CaptionColor,ref reference,sizeof(int));
            }
        }
        catch(DllNotFoundException){}
        catch(EntryPointNotFoundException){}
        if(!merged)return;
        try
        {
            // The taskbar and Alt+Tab still use the window title; only the caption stops drawing it.
            var options=new ThemeOptions{Flags=NoDrawCaption|NoDrawIcon,Mask=NoDrawCaption|NoDrawIcon};
            SetWindowThemeAttribute(handle,NonClient,ref options,(uint)Marshal.SizeOf<ThemeOptions>());
        }
        catch(DllNotFoundException){}
        catch(EntryPointNotFoundException){}
    }
}
