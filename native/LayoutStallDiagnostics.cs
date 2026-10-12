using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Kachalka;

// Smoke-only evidence for a window that stops laying out: what the layout manager, the dispatcher and the
// Win32 window say about themselves, and which messages the thread keeps receiving.
sealed class LayoutStallDiagnostics
{
    readonly ConcurrentDictionary<string,int> threadMessages=new(),windowMessages=new();
    readonly Window window;
    // Taken on the UI thread: the report itself runs on a watchdog thread.
    readonly IntPtr WindowHandle;
    public LayoutStallDiagnostics(Window window)
    {
        this.window=window;WindowHandle=new WindowInteropHelper(window).Handle;
        ComponentDispatcher.ThreadFilterMessage+=(ref MSG message,ref bool _)=>threadMessages.AddOrUpdate(Name(message.message)+(message.hwnd==WindowHandle?"@window":"@other"),1,(_,count)=>count+1);
        HwndSource.FromHwnd(WindowHandle)?.AddHook((IntPtr _,int message,IntPtr _,IntPtr _,ref bool _)=>{windowMessages.AddOrUpdate(Name(message),1,(_,count)=>count+1);return IntPtr.Zero;});
    }

    // Called from a watchdog thread; reads the UI state through a short Send-priority call when the dispatcher still runs.
    public string Report()
    {
        var report=new Dictionary<string,object?>{["ThreadMessages"]=threadMessages.OrderByDescending(x=>x.Value).Take(25).ToDictionary(x=>x.Key,x=>x.Value),["WindowMessages"]=windowMessages.OrderByDescending(x=>x.Value).Take(25).ToDictionary(x=>x.Key,x=>x.Value)};
        var handle=WindowHandle;
        if(GetWindowRect(handle,out var outer))report["WindowRect"]=$"{outer.Left},{outer.Top} {outer.Right-outer.Left}x{outer.Bottom-outer.Top}";
        if(GetClientRect(handle,out var inner))report["ClientRect"]=$"{inner.Right-inner.Left}x{inner.Bottom-inner.Top}";
        report["Style"]=$"0x{GetWindowLong(handle,-16):X8} ex 0x{GetWindowLong(handle,-20):X8}";
        try
        {
            window.Dispatcher.Invoke(()=>
            {
                report["Wpf"]=new{window.Width,window.Height,window.MinWidth,window.ActualWidth,window.ActualHeight,Render=window.RenderSize.ToString(),Desired=window.DesiredSize.ToString(),window.IsMeasureValid,window.IsArrangeValid,State=window.WindowState.ToString()};
                if(window.Content is FrameworkElement root)report["Root"]=new{root.ActualWidth,root.ActualHeight,root.IsMeasureValid,root.IsArrangeValid,Margin=root.Margin.ToString()};
                report["QueueStatus"]=$"0x{GetQueueStatus(0x04FF):X8}";
                report["LayoutManager"]=Fields(LayoutManager(window.Dispatcher));
                report["Dispatcher"]=Fields(window.Dispatcher);
                var queue=typeof(Dispatcher).GetField("_queue",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(window.Dispatcher);
                report["DispatcherMaxPriority"]=queue?.GetType().GetProperty("MaxPriority",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(queue)?.ToString();
                report["MediaContext"]=Fields(typeof(Visual).Assembly.GetType("System.Windows.Media.MediaContext")?.GetMethod("From",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public,[typeof(Dispatcher)])?.Invoke(null,[window.Dispatcher]));
            },DispatcherPriority.Send,CancellationToken.None,TimeSpan.FromSeconds(3));
        }
        catch(Exception error){report["UiReadError"]=error.GetType().Name+": "+error.Message;}
        return JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});
    }

    static object? LayoutManager(Dispatcher dispatcher)
    {
        var type=typeof(UIElement).Assembly.GetType("System.Windows.ContextLayoutManager");
        return type?.GetMethod("From",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public,[typeof(Dispatcher)])?.Invoke(null,[dispatcher]);
    }
    // Every bool, int and enum field, and the same for small nested queue objects, so the report names the stuck flag.
    static Dictionary<string,string> Fields(object? target,int depth=0)
    {
        var result=new Dictionary<string,string>();
        if(target==null)return result;
        foreach(var field in target.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic))
        {
            object? value;try{value=field.GetValue(target);}catch{continue;}
            if(value is bool or int or long or double||value is Enum)result[field.Name]=value.ToString()!;
            else if(depth==0&&value!=null&&field.FieldType.Name.Contains("Queue",StringComparison.Ordinal))
                foreach(var (name,inner) in Fields(value,1))result[field.Name+"."+name]=inner;
        }
        return result;
    }
    static string Name(int message)=>message switch
    {
        0x0005=>"WM_SIZE",0x000F=>"WM_PAINT",0x0014=>"WM_ERASEBKGND",0x0024=>"WM_GETMINMAXINFO",0x0046=>"WM_WINDOWPOSCHANGING",0x0047=>"WM_WINDOWPOSCHANGED",
        0x0083=>"WM_NCCALCSIZE",0x0084=>"WM_NCHITTEST",0x0085=>"WM_NCPAINT",0x0086=>"WM_NCACTIVATE",0x0020=>"WM_SETCURSOR",0x0200=>"WM_MOUSEMOVE",0x0113=>"WM_TIMER",
        0x0231=>"WM_ENTERSIZEMOVE",0x0232=>"WM_EXITSIZEMOVE",0x031E=>"WM_DWMCOMPOSITIONCHANGED",0x02E0=>"WM_DPICHANGED",0x0003=>"WM_MOVE",0x001C=>"WM_ACTIVATEAPP",
        _=>message>=0xC000?"registered":"0x"+message.ToString("X4")
    };
    [StructLayout(LayoutKind.Sequential)] struct Rect{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")] static extern uint GetQueueStatus(uint flags);
}
