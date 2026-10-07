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
    // The native smoke test exercises both paths even on a headless Windows runner.
    internal static bool? SmoothOverride;
    internal static bool SmoothingEnabled=>SmoothOverride??SystemParameters.ClientAreaAnimation;

    public static (double Position,double Velocity) Advance(double position,double velocity,double target,double seconds)
    {
        // An exact critically damped spring preserves velocity between wheel events.
        // Unlike restarting a short ease-out, the first frame starts gently.
        const double frequency=28;
        seconds=Math.Clamp(seconds,0,.05);
        var displacement=position-target;
        var travel=(velocity+frequency*displacement)*seconds;
        var decay=Math.Exp(-frequency*seconds);
        return(target+(displacement+travel)*decay,(velocity-frequency*travel)*decay);
    }

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
            if(node is ScrollViewer inner&&inner!=viewer&&inner.ScrollableHeight>0)return;
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
        DispatcherOperation? immediate;
        double position,target,velocity;
        long lastTick;
        TimeSpan lastFrame;
        bool rendering;
        public bool Running=>rendering||immediate?.Status==DispatcherOperationStatus.Pending;
        public Motion(ScrollViewer viewer)
        {
            this.viewer=viewer;
            viewer.Unloaded+=(_,_)=>Stop();
        }
        public void Move(double distance)
        {
            if(!SmoothingEnabled)
            {
                StopRendering();
                // ScrollViewer applies scroll commands during layout. Accumulate
                // input arriving before that layout instead of reading a stale offset.
                var pending=immediate?.Status==DispatcherOperationStatus.Pending;
                target=Math.Clamp((pending?target:viewer.VerticalOffset)+distance,0,viewer.ScrollableHeight);
                if(Math.Abs(target-viewer.VerticalOffset)<.1){Stop();return;}
                if(pending)return;
                immediate=viewer.Dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(()=>
                {
                    immediate=null;
                    if(viewer.IsLoaded&&viewer.IsVisible)viewer.ScrollToVerticalOffset(Math.Clamp(target,0,viewer.ScrollableHeight));
                }));
                return;
            }
            if(!rendering)position=viewer.VerticalOffset;
            var next=Destination(position,target,distance,viewer.ScrollableHeight,Running);
            if(Math.Sign(next-position)!=Math.Sign(velocity))velocity=0;
            immediate?.Abort();immediate=null;
            target=next;
            if(Math.Abs(target-position)<.1){Stop();return;}
            if(rendering)return;
            lastTick=Stopwatch.GetTimestamp();lastFrame=TimeSpan.MinValue;
            rendering=true;CompositionTarget.Rendering+=Tick;
        }
        void Tick(object? sender,EventArgs e)
        {
            if(!viewer.IsLoaded||!viewer.IsVisible){Stop();return;}
            if(e is RenderingEventArgs frame){if(frame.RenderingTime==lastFrame)return;lastFrame=frame.RenderingTime;}
            var seconds=Stopwatch.GetElapsedTime(lastTick).TotalSeconds;lastTick=Stopwatch.GetTimestamp();
            target=Math.Clamp(target,0,viewer.ScrollableHeight);
            // Keep the fractional position between frames; virtualizing panels may
            // round the displayed offset, which must not stall the spring near rest.
            var next=Advance(position,velocity,target,seconds);velocity=next.Velocity;
            position=Math.Clamp(next.Position,0,viewer.ScrollableHeight);
            viewer.ScrollToVerticalOffset(position);
            if(Math.Abs(target-next.Position)<.25&&Math.Abs(velocity)<4){viewer.ScrollToVerticalOffset(target);Stop();}
        }
        void StopRendering(){if(rendering){CompositionTarget.Rendering-=Tick;rendering=false;}velocity=0;}
        public void Stop(){StopRendering();immediate?.Abort();immediate=null;}
    }
}
