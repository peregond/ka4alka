using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace Kachalka;

// The main window has no system caption: the sidebar runs to the top edge and the window buttons are drawn in
// the app. Windows keeps the side and bottom frame, so resizing, the shadow, snapping and the rounded corners of
// Windows 11 stay native; only the caption is folded into the client area. The top band still drags, maximizes on
// a double click and opens the system menu on a right click.
// WindowChrome is not used: after a programmatic resize of a window larger than the screen it left the layout
// and every lower-priority dispatcher operation waiting (a Windows CI probe compared both approaches).
public partial class MainWindow
{
    internal const double CaptionBand=32;
    const int WmNcCalcSize=0x0083,WmNcHitTest=0x0084,HtClient=1,HtCaption=2,HtTop=12,HtTopLeft=13,HtTopRight=14;
    const uint SwpNoMove=0x0002,SwpFrameChanged=0x0020;
    // The invisible resize border Windows keeps around a restored window, in physical pixels.
    int resizeBorder=8;

    void EnableCustomFrame(IntPtr handle)
    {
        HwndSource.FromHwnd(handle)?.AddHook(FrameMessage);
        // The hook now decides the client area; make Windows measure the frame again.
        SetWindowPos(handle,IntPtr.Zero,0,0,0,0,SwpNoSize|SwpNoMove|SwpNoZOrder|SwpNoActivate|SwpFrameChanged);
        StateChanged+=(_,_)=>UpdateCaptionButtons();
        UpdateCaptionButtons();
    }
    IntPtr FrameMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message==WmNcCalcSize&&wParam!=IntPtr.Zero)
        {
            // The first rectangle of NCCALCSIZE_PARAMS starts the structure: left, top, right, bottom.
            var left=Marshal.ReadInt32(lParam,0);var top=Marshal.ReadInt32(lParam,4);
            DefWindowProc(hwnd,message,wParam,lParam);
            var frame=Marshal.ReadInt32(lParam,0)-left;
            // Windows places a maximized window with its frame outside the monitor on every side, so the top keeps
            // the same inset as the sides; a restored window gives the caption's height back to the client area.
            if(IsZoomed(hwnd)){Marshal.WriteInt32(lParam,4,top+frame);LeaveRoomForHiddenTaskbar(hwnd,lParam);}
            else{Marshal.WriteInt32(lParam,4,top);if(frame>0)resizeBorder=frame;}
            handled=true;return IntPtr.Zero;
        }
        if(message==WmNcHitTest)
        {
            var hit=DefWindowProc(hwnd,message,wParam,lParam);
            handled=true;
            if(hit!=HtClient)return hit;
            var x=(short)(lParam.ToInt64()&0xFFFF);var y=(short)((lParam.ToInt64()>>16)&0xFFFF);
            if(!IsZoomed(hwnd)&&GetWindowRect(hwnd,out var window)&&y-window.Top<resizeBorder)
                return x-window.Left<resizeBorder*2?HtTopLeft:window.Right-x<resizeBorder*2?HtTopRight:HtTop;
            // While the window is created or closed it has no presentation source to map the point into.
            if(PresentationSource.FromVisual(this)==null)return HtClient;
            try
            {
                var point=PointFromScreen(new Point(x,y));
                return point.Y<CaptionBand&&!InteractiveAt(point)?HtCaption:HtClient;
            }
            catch(InvalidOperationException){return HtClient;}
        }
        return IntPtr.Zero;
    }
    // Buttons, fields and scroll bars inside the top band keep working; everything else there drags the window.
    bool InteractiveAt(Point point)
    {
        for(var node=InputHitTest(point) as DependencyObject;node!=null;node=node is Visual?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node))
            if(node is ButtonBase or TextBoxBase or Thumb or ScrollBar or Selector)return true;
        return false;
    }
    // A maximized window covers the monitor's work area. With an auto-hiding taskbar that area is the whole monitor, and
    // the taskbar only comes up when the mouse reaches a screen edge the window does not cover, so that edge keeps 2 px.
    static void LeaveRoomForHiddenTaskbar(IntPtr hwnd,IntPtr rectangle)
    {
        var info=new MonitorInfo{Size=(uint)Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(MonitorFromWindow(hwnd,MonitorDefaultNearest),ref info))return;
        foreach(var (edge,offset,inward) in new[]{(0u,0,2),(1u,4,2),(2u,8,-2),(3u,12,-2)})
        {
            var bar=new AppBarData{Size=(uint)Marshal.SizeOf<AppBarData>(),Edge=edge,Area=info.Monitor};
            if(SHAppBarMessage(AbmGetAutoHideBarEx,ref bar)!=IntPtr.Zero)Marshal.WriteInt32(rectangle,offset,Marshal.ReadInt32(rectangle,offset)+inward);
        }
    }
    const uint AbmGetAutoHideBarEx=0x0B;
    [StructLayout(LayoutKind.Sequential)] struct AppBarData{public uint Size;public IntPtr Window;public uint CallbackMessage;public uint Edge;public NativeRect Area;public IntPtr Param;}
    [DllImport("shell32.dll")] static extern IntPtr SHAppBarMessage(uint message,ref AppBarData data);
    // Windows 10 draws no border above a window without a caption; a one-pixel line keeps the top edge visible there.
    static readonly bool DrawsOwnTopEdge=Environment.OSVersion.Version.Build<22000;
    void UpdateCaptionButtons()
    {
        var maximized=WindowState==WindowState.Maximized;
        WindowTopEdge.Visibility=DrawsOwnTopEdge&&!maximized?Visibility.Visible:Visibility.Collapsed;
        CaptionMaximize.Content=maximized?"":"";CaptionMaximize.ToolTip=AppLanguage.T(maximized?"Восстановить":"Развернуть");
        AutomationProperties.SetName(CaptionMaximize,AppLanguage.T(maximized?"Восстановить окно":"Развернуть окно"));
    }
    void CaptionMinimizeClick(object sender,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
    void CaptionMaximizeClick(object sender,RoutedEventArgs e){if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);}
    void CaptionCloseClick(object sender,RoutedEventArgs e)=>SystemCommands.CloseWindow(this);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr window);
}
