using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace Kachalka;

// The soft light that runs over loading placeholders from left to right.
// - One gradient brush per lane (a column of posters, a row of lines) shared by every shape in it, so a shelf of
//   twenty cards costs a handful of animations, and each lane starts a moment after the previous one: the light
//   rolls across the shelf instead of blinking on all cards at once.
// - The animation moves a brush transform on WPF's render thread (no timer, no per-frame code), at 30 frames a
//   second (20 in Lite mode), and runs only while its placeholder is on screen. A hidden or removed placeholder stops it.
// - With motion off (Windows animations off, smoke tests) nothing is created: the shapes keep the
//   flat theme colour through a resource reference, exactly as before.
sealed class ShimmerWave(bool animated,int framesPerSecond=30)
{
    static readonly TimeSpan Sweep=TimeSpan.FromMilliseconds(1100),Cycle=TimeSpan.FromMilliseconds(1900);
    sealed class Lane(LinearGradientBrush brush,TranslateTransform slide,int index,int framesPerSecond)
    {
        public readonly LinearGradientBrush Brush=brush;
        readonly TranslateTransform slide=slide;
        public void Start()
        {
            // The light crosses the shape in 1.1 s, then rests until the cycle ends; later lanes begin a little later.
            var frames=new DoubleAnimationUsingKeyFrames{Duration=Cycle,RepeatBehavior=RepeatBehavior.Forever,BeginTime=TimeSpan.FromMilliseconds(70*Math.Min(index,10))};
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(-1,KeyTime.FromTimeSpan(TimeSpan.Zero)));
            frames.KeyFrames.Add(new EasingDoubleKeyFrame(1,KeyTime.FromTimeSpan(Sweep),new SineEase{EasingMode=EasingMode.EaseInOut}));
            Timeline.SetDesiredFrameRate(frames,framesPerSecond);
            slide.BeginAnimation(TranslateTransform.XProperty,frames);
        }
        public void Stop(){slide.BeginAnimation(TranslateTransform.XProperty,null);slide.X=-1;}
    }
    readonly Dictionary<(string Resource,int Index),Lane> lanes=[];
    bool running;
    internal bool IsRunning=>running;
    internal int LaneCount=>lanes.Count;
    public void Stop(){foreach(var overlay in shown.Keys.ToArray())Hide(overlay);Set(false);}
    // Stops this wave and hands back the posters that were still shimmering, so a new wave (after a theme or Lite mode change) can carry on with them.
    public (Border Overlay,int Lane)[] Detach(){var carried=shown.Select(pair=>(pair.Key,pair.Value)).ToArray();Stop();return carried;}

    // Colours the shape with the lane's brush, or with the plain theme colour when nothing animates.
    public void Paint(Border target,int lane,string resource="Raised")
    {
        if(animated&&Application.Current.Resources[resource] is SolidColorBrush {Color: var color})target.Background=For(resource,lane,color).Brush;
        else target.SetResourceReference(Border.BackgroundProperty,resource);
    }
    Lane For(string resource,int index,Color color)
    {
        if(lanes.TryGetValue((resource,index),out var known))return known;
        var light=Lift(color);var slide=new TranslateTransform(-1,0);
        // The gradient is flat outside the light, so while the transform rests at either end the shape shows its plain colour.
        var brush=new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(1,0),RelativeTransform=slide};
        brush.GradientStops.Add(new(color,0));brush.GradientStops.Add(new(color,.2));brush.GradientStops.Add(new(light,.5));brush.GradientStops.Add(new(color,.8));brush.GradientStops.Add(new(color,1));
        var lane=new Lane(brush,slide,index,framesPerSecond);lanes.Add((resource,index),lane);
        if(running)lane.Start();
        return lane;
    }
    // For single cards: a poster whose cover is still on its way shows the shimmer until the cover arrives. The wave
    // runs only while at least one such poster is shown, and a poster that leaves the page drops out by itself.
    readonly Dictionary<Border,int> shown=[];
    public void Show(Border overlay,int lane)
    {
        if(!animated||shown.ContainsKey(overlay))return;
        Paint(overlay,lane);overlay.Visibility=Visibility.Visible;
        shown[overlay]=lane;overlay.Unloaded+=Gone;
        Set(true);
    }
    public void Hide(Border overlay)
    {
        if(overlay.Visibility!=Visibility.Collapsed)overlay.Visibility=Visibility.Collapsed;
        if(shown.Remove(overlay)){overlay.Unloaded-=Gone;if(shown.Count==0)Set(false);}
    }
    void Gone(object sender,RoutedEventArgs args){if(sender is Border overlay)Hide(overlay);}
    // Runs the wave while the placeholder is shown and stops it when it is hidden, replaced or the page is left.
    public T Run<T>(T host) where T:FrameworkElement
    {
        if(!animated)return host;
        void Sync(){Set(host.IsLoaded&&host.IsVisible);}
        host.Loaded+=(_,_)=>Sync();host.IsVisibleChanged+=(_,_)=>Sync();host.Unloaded+=(_,_)=>Set(false);
        return host;
    }
    // While the page is being scrolled the light rests, so the render thread works for the scroll alone.
    bool wanted,paused;
    public void Pause(bool value){if(paused==value)return;paused=value;Apply();}
    void Set(bool on){wanted=on;Apply();}
    void Apply()
    {
        var on=wanted&&!paused;
        if(running==on)return;running=on;
        foreach(var lane in lanes.Values){if(on)lane.Start();else lane.Stop();}
    }
    // Dark surfaces get a slightly lighter band, bright ones a slightly darker one; a translucent chip over a backdrop turns more opaque.
    internal static Color Lift(Color color)
    {
        if(color.A<200)return Color.FromArgb((byte)Math.Min(255,color.A*2+10),color.R,color.G,color.B);
        var luminance=(color.R*299+color.G*587+color.B*114)/1000;
        return luminance<128?Mix(color,Colors.White,.09):luminance>=246?Mix(color,Colors.Black,.045):Mix(color,Colors.White,.6);
    }
    static Color Mix(Color from,Color to,double amount)=>Color.FromArgb(from.A,(byte)(from.R+(to.R-from.R)*amount),(byte)(from.G+(to.G-from.G)*amount),(byte)(from.B+(to.B-from.B)*amount));
}
