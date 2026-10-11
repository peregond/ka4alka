using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Kachalka;

// TEMPORARY experiment: KACHALKA_WINDOW_FRAME picks how the main window drops its caption, so one Windows CI
// run can compare them under the cinema smoke. Removed once the full-height window is settled.
public partial class MainWindow
{
    internal static string FrameVariant=>Environment.GetEnvironmentVariable("KACHALKA_WINDOW_FRAME")??"native";
    void ApplyFrameProbe()
    {
        switch(FrameVariant)
        {
            case "chrome0":case "chrome0-fit":
                WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=32,ResizeBorderThickness=new(6),GlassFrameThickness=new(0),CornerRadius=new(0),UseAeroCaptionButtons=false});break;
            case "chrome1":
                WindowChrome.SetWindowChrome(this,new WindowChrome{CaptionHeight=32,ResizeBorderThickness=new(6),GlassFrameThickness=new(0,0,0,1),CornerRadius=new(0),UseAeroCaptionButtons=false});break;
            case "manual":
                SourceInitialized+=(_,_)=>
                {
                    var handle=new WindowInteropHelper(this).Handle;HwndSource.FromHwnd(handle)?.AddHook(ManualFrame);
                    SetWindowPos(handle,IntPtr.Zero,0,0,0,0,0x0001|0x0002|0x0004|0x0010|0x0020);
                };
                break;
        }
    }
    const int WmNcCalcSize=0x0083,WmNcHitTest=0x0084,HtClient=1,HtCaption=2,HtTop=12,HtTopLeft=13,HtTopRight=14;
    // The caption goes, the side and bottom frame stay (Windows keeps the shadow, resize edges and rounded corners).
    IntPtr ManualFrame(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message==WmNcCalcSize&&wParam!=IntPtr.Zero)
        {
            var left=Marshal.ReadInt32(lParam,0);var top=Marshal.ReadInt32(lParam,4);
            DefWindowProc(hwnd,message,wParam,lParam);
            // A maximized window hangs its frame outside the monitor: keep the same inset on top as on the sides.
            var frame=Marshal.ReadInt32(lParam,0)-left;
            Marshal.WriteInt32(lParam,4,IsZoomed(hwnd)?top+frame:top);
            handled=true;return IntPtr.Zero;
        }
        if(message==WmNcHitTest)
        {
            var result=DefWindowProc(hwnd,message,wParam,lParam);
            if(result!=HtClient)return result;
            handled=true;
            var screen=new Point((short)(lParam.ToInt64()&0xFFFF),(short)((lParam.ToInt64()>>16)&0xFFFF));
            var point=PointFromScreen(screen);
            if(!IsZoomed(hwnd)&&point.Y<6)return point.X<16?HtTopLeft:point.X>ActualWidth-16?HtTopRight:HtTop;
            if(point.Y<32&&!InteractiveAt(point))return HtCaption;
            return HtClient;
        }
        return IntPtr.Zero;
    }
    bool InteractiveAt(Point point)
    {
        if(InputHitTest(point) is not DependencyObject hit)return false;
        for(DependencyObject? node=hit;node!=null;node=node is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
            if(node is ButtonBase or TextBox or Thumb or ScrollBar or ComboBox or Selector)return true;
        return false;
    }
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr window);
}
