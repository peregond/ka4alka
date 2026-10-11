using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Kachalka;

public partial class MainWindow
{
    [StructLayout(LayoutKind.Sequential)] struct ScreenPoint{public int X,Y;}
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref ScreenPoint point);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    const int HtLeft=10;

    // The caption-less frame, asked the way Windows asks it: WM_NCHITTEST at real screen points of this window.
    internal async Task<object> CheckWindowFrame(string stage,Func<Task> settle,bool maximize)
    {
        await settle();
        var handle=new WindowInteropHelper(this).Handle;
        int Hit(int x,int y)=>(int)SendMessage(handle,WmNcHitTest,IntPtr.Zero,(IntPtr)(((y&0xFFFF)<<16)|(x&0xFFFF)));
        int HitAt(FrameworkElement element,Point point){var screen=element.PointToScreen(point);return Hit((int)Math.Round(screen.X),(int)Math.Round(screen.Y));}
        void Check(bool condition,string message){if(!condition)throw new Exception(stage+": "+message);}
        if(!GetWindowRect(handle,out var window))throw new Exception(stage+": cannot read the window rectangle.");
        var client=new ScreenPoint();ClientToScreen(handle,ref client);
        Check(Math.Abs(client.Y-window.Top)<=1,$"the client area must start at the window's top edge, without a caption (window top {window.Top}, client top {client.Y}).");
        var sidebarTop=SidePanel.TransformToAncestor(this).Transform(new Point()).Y;
        Check(Math.Abs(sidebarTop)<.5,$"the sidebar must run to the top edge (starts at {sidebarTop:F1}).");
        Check(new[]{CaptionMinimize,CaptionMaximize,CaptionClose}.All(button=>button.IsVisible&&button.ActualWidth>=40&&button.ActualHeight>=CaptionBand-.5),"the window buttons must be visible in the top-right corner.");
        var closeRight=CaptionClose.TransformToAncestor(this).Transform(new Point(CaptionClose.ActualWidth,0));
        Check(Math.Abs(closeRight.X-ActualWidth)<1.5&&Math.Abs(closeRight.Y)<.5,$"Close must sit in the very top-right corner ({closeRight}).");
        var band=new Point((SidePanel.ActualWidth+ActualWidth-CaptionButtons.ActualWidth)/2,CaptionBand/2);
        Check(HitAt(this,band)==HtCaption,"the empty top band above the content must drag the window (HTCAPTION).");
        Check(HitAt(BrandLogo,new Point(BrandLogo.ActualWidth/2,Math.Min(4,BrandLogo.ActualHeight/2)))==HtCaption||BrandLogo.TransformToAncestor(this).Transform(new Point()).Y>=CaptionBand,"the sidebar's logo inside the top band must drag the window.");
        foreach(var button in new[]{CaptionMinimize,CaptionMaximize,CaptionClose})
            Check(HitAt(button,new Point(button.ActualWidth/2,button.ActualHeight-4))==HtClient,"window button "+button.Name+" must stay clickable (HTCLIENT).");
        Check(Search.TransformToAncestor(this).Transform(new Point()).Y>=CaptionBand-.5,"the header must start below the top band.");
        Check(HitAt(Search,new Point(Search.ActualWidth/2,Search.ActualHeight/2))==HtClient,"the search field must stay clickable.");
        Check(HitAt(this,new Point(ActualWidth/2,ActualHeight/2))==HtClient,"the page itself must stay client area.");
        var middle=(window.Left+window.Right)/2;
        Check(Hit(middle,window.Top+1)==HtTop,"the top edge must resize the window (HTTOP).");
        Check(Hit(window.Left+2,(window.Top+window.Bottom)/2)==HtLeft,"the left edge must keep Windows' resize border (HTLEFT).");
        object? maximized=null;
        if(maximize)
        {
            var restore=(Width,Height,MinWidth,MinHeight,MaxWidth,MaxHeight);
            var monitor=MonitorFromWindow(handle,MonitorDefaultNearest);var info=new MonitorInfo{Size=(uint)Marshal.SizeOf<MonitorInfo>()};
            if(!GetMonitorInfo(monitor,ref info))throw new Exception(stage+": cannot read the monitor work area.");
            // The limits the app itself sets for this monitor (FitToMonitor), so the check sees what people get.
            var (scaleX,scaleY)=VisualTreeHelperDpi();var fit=WindowSizing.FitPixels(info.Work.Width,info.Work.Height,scaleX,scaleY);
            MinWidth=fit.MinWidth;MinHeight=fit.MinHeight;MaxWidth=fit.MaxWidth;MaxHeight=fit.MaxHeight;
            WindowState=WindowState.Maximized;await settle();
            try
            {
                var origin=new ScreenPoint();ClientToScreen(handle,ref origin);
                var corner=new ScreenPoint{X=(int)Math.Round(ActualWidth*scaleX),Y=(int)Math.Round(ActualHeight*scaleY)};ClientToScreen(handle,ref corner);
                maximized=new{Work=$"{info.Work.Left},{info.Work.Top}..{info.Work.Right},{info.Work.Bottom}",Client=$"{origin.X},{origin.Y}..{corner.X},{corner.Y}"};
                Check(Math.Abs(origin.Y-info.Work.Top)<=1&&Math.Abs(origin.X-info.Work.Left)<=1,$"a maximized window's content must start at the work area's corner, not off screen ({JsonText(maximized)}).");
                Check(Math.Abs(corner.X-info.Work.Right)<=1&&Math.Abs(corner.Y-info.Work.Bottom)<=1,$"a maximized window's content must fill the work area exactly ({JsonText(maximized)}).");
                Check(Equals(CaptionMaximize.Content,"")&&System.Windows.Automation.AutomationProperties.GetName(CaptionMaximize)=="Восстановить окно","the maximize button must offer Restore while maximized.");
                var top=Hit((info.Work.Left+info.Work.Right)/2,info.Work.Top);
                Check(top==HtCaption,$"a maximized window's top edge must drag rather than resize (hit {top}).");
            }
            finally
            {
                WindowState=WindowState.Normal;
                (Width,Height,MinWidth,MinHeight,MaxWidth,MaxHeight)=restore;await settle();
            }
            Check(Equals(CaptionMaximize.Content,""),"the maximize button must offer Maximize again after restoring.");
        }
        return new{Stage=stage,Window=$"{window.Left},{window.Top}..{window.Right},{window.Bottom}",ClientTop=client.Y,ResizeBorder=resizeBorder,Maximized=maximized};
    }
    (double X,double Y) VisualTreeHelperDpi(){var dpi=System.Windows.Media.VisualTreeHelper.GetDpi(this);return(dpi.DpiScaleX,dpi.DpiScaleY);}
    static string JsonText(object? value)=>System.Text.Json.JsonSerializer.Serialize(value);
}
