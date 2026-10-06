using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Kachalka;

public static class WheelScroll
{
    public static readonly DependencyProperty IsEnabledProperty=DependencyProperty.RegisterAttached(
        "IsEnabled",typeof(bool),typeof(WheelScroll),new PropertyMetadata(false,Changed));
    static readonly DependencyProperty MotionProperty=DependencyProperty.RegisterAttached(
        "Motion",typeof(Motion),typeof(WheelScroll));
    public static void SetIsEnabled(DependencyObject element,bool value)=>element.SetValue(IsEnabledProperty,value);
    public static bool GetIsEnabled(DependencyObject element)=>(bool)element.GetValue(IsEnabledProperty);
    internal static bool IsMoving(ScrollViewer viewer)=>(viewer.GetValue(MotionProperty) as Motion)?.Running==true;

    // Coordinates are WPF device-independent pixels, so the step scales with Windows DPI.
    public static double Distance(int delta,int lines,double viewport)=>delta/120d*(lines<0?Math.Max(0,viewport)*.85:Math.Clamp(lines,0,6)*16d);
    public static double Destination(double current,double target,double distance,double maximum,bool moving)
    {
        // Reversing the wheel discards pending travel in the old direction.
        var origin=moving&&Math.Sign(target-current)==Math.Sign(distance)?target:current;
        return Math.Clamp(origin+distance,0,Math.Max(0,maximum));
    }

    static void Changed(DependencyObject element,DependencyPropertyChangedEventArgs change)
    {
        if(element is not FrameworkElement host)return;
        if((bool)change.NewValue){host.PreviewMouseWheel+=Wheel;host.PreviewMouseDown+=Interrupt;host.PreviewKeyDown+=Key;}
        else{host.PreviewMouseWheel-=Wheel;host.PreviewMouseDown-=Interrupt;host.PreviewKeyDown-=Key;Stop(host);}
    }
    static DependencyObject? Parent(DependencyObject node)=>node is Visual or System.Windows.Media.Media3D.Visual3D
        ?VisualTreeHelper.GetParent(node):node is FrameworkContentElement text?text.Parent:LogicalTreeHelper.GetParent(node);
    static ScrollViewer? Viewer(DependencyObject node)
    {
        if(node is ScrollViewer viewer)return viewer;
        if(node is Visual)
            for(var index=0;index<VisualTreeHelper.GetChildrenCount(node);index++)
                if(Viewer(VisualTreeHelper.GetChild(node,index)) is {} found)return found;
        return null;
    }
    static void Wheel(object sender,MouseWheelEventArgs e)
    {
        if(e.Handled||e.Delta==0||Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))return;
        var host=(FrameworkElement)sender;var viewer=Viewer(host);
        if(viewer==null||viewer.ScrollableHeight<=0)return;
        // An editor, drop-down, scrollbar or nested viewer keeps its own input handling.
        for(var node=e.OriginalSource as DependencyObject;node!=null;node=Parent(node))
        {
            if(node is TextBoxBase or ComboBox or ScrollBar)return;
            if(node is ScrollViewer inner&&inner!=viewer)return;
            if(node==host)break;
        }
        if(viewer.GetValue(MotionProperty) is not Motion motion){motion=new(viewer);viewer.SetValue(MotionProperty,motion);}
        motion.Move(-Distance(e.Delta,SystemParameters.WheelScrollLines,viewer.ViewportHeight));e.Handled=true;
    }
    static void Stop(FrameworkElement host){if(Viewer(host)?.GetValue(MotionProperty) is Motion motion)motion.Stop();}
    static void Interrupt(object sender,MouseButtonEventArgs e)=>Stop((FrameworkElement)sender);
    static void Key(object sender,KeyEventArgs e)
    {
        if(e.Key is System.Windows.Input.Key.Up or System.Windows.Input.Key.Down or System.Windows.Input.Key.PageUp or System.Windows.Input.Key.PageDown or System.Windows.Input.Key.Home or System.Windows.Input.Key.End)
            Stop((FrameworkElement)sender);
    }
    sealed class Motion
    {
        readonly ScrollViewer viewer;
        readonly DispatcherTimer timer;
        double start,target;
        long began;
        public bool Running=>timer.IsEnabled;
        public Motion(ScrollViewer viewer)
        {
            this.viewer=viewer;
            timer=new(DispatcherPriority.Background,viewer.Dispatcher){Interval=TimeSpan.FromMilliseconds(16)};
            timer.Tick+=Tick;viewer.Unloaded+=(_,_)=>Stop();
        }
        public void Move(double distance)
        {
            var next=Destination(viewer.VerticalOffset,target,distance,viewer.ScrollableHeight,Running);
            start=viewer.VerticalOffset;target=next;began=Stopwatch.GetTimestamp();
            if(!SystemParameters.ClientAreaAnimation){Stop();viewer.ScrollToVerticalOffset(target);return;}
            if(Math.Abs(target-start)<.1){Stop();return;}
            timer.Start();
        }
        void Tick(object? sender,EventArgs e)
        {
            if(!viewer.IsLoaded||!viewer.IsVisible){Stop();return;}
            var progress=Math.Min(1,Stopwatch.GetElapsedTime(began).TotalMilliseconds/120);
            target=Math.Clamp(target,0,viewer.ScrollableHeight);
            var eased=1-Math.Pow(1-progress,3);
            viewer.ScrollToVerticalOffset(start+(target-start)*eased);
            if(progress>=1)Stop();
        }
        public void Stop()=>timer.Stop();
    }
}
