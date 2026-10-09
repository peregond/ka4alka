using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Kachalka;

// One observer per window, rather than one scroll/layout listener per poster.
// Loaded containers may still live far below a non-virtualized shelf. Only the
// visible area and one neighbouring poster row are allowed into the IO queue.
internal sealed class CoverViewport(Window owner):IDisposable
{
    internal readonly record struct Position(bool Visible,bool Near,double Distance);
    sealed class Entry(Image image,Action<Position> changed)
    {
        public readonly Image Image=image;
        public readonly Action<Position> Changed=changed;
        public FrameworkElement? Anchor;
        public readonly List<ScrollContentPresenter> Clips=[];
        public readonly List<ScrollViewer> Scrollers=[];
    }
    readonly Dictionary<Image,Entry> entries=[];
    readonly Dictionary<ScrollViewer,int> scrollers=[];
    DispatcherOperation? scheduled;
    bool listening,disposed;
    internal int SweepCount {get;private set;}
    internal int TrackedCount=>entries.Count;

    internal void Track(Image image,Action<Position> changed)
    {
        if(disposed||!image.IsLoaded)return;
        if(entries.ContainsKey(image)){Schedule();return;}
        var entry=new Entry(image,changed);entries.Add(image,entry);
        image.Unloaded+=Unloaded;image.SizeChanged+=Resized;
        entry.Anchor=Anchor(image);
        if(entry.Anchor!=null&&entry.Anchor!=image)entry.Anchor.SizeChanged+=Resized;
        for(DependencyObject? parent=VisualTreeHelper.GetParent(image);parent!=null;parent=VisualTreeHelper.GetParent(parent))
        {
            if(parent is ScrollContentPresenter presenter)entry.Clips.Add(presenter);
            if(parent is ScrollViewer scroll)
            {
                entry.Scrollers.Add(scroll);
                if(scrollers.TryGetValue(scroll,out var count))scrollers[scroll]=count+1;
                else{scrollers.Add(scroll,1);scroll.ScrollChanged+=Scrolled;scroll.SizeChanged+=Resized;}
            }
        }
        if(!listening){listening=true;owner.SizeChanged+=Resized;owner.StateChanged+=StateChanged;owner.IsVisibleChanged+=VisibilityChanged;owner.LayoutUpdated+=LayoutChanged;owner.Closed+=Closed;}
        Schedule();
    }
    internal void Schedule()
    {
        if(!disposed&&scheduled==null)scheduled=owner.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(Sweep));
    }
    internal Position Measure(Image image)=>entries.TryGetValue(image,out var entry)?Measure(entry):default;
    static FrameworkElement? Anchor(Image image)
    {
        // An Image with no Source can arrange to 0 × 0 even inside a sized
        // poster. Measuring that empty bitmap would prevent the very request
        // which gives it dimensions. Use the visible poster container until
        // the bitmap exists, and always do so for hidden banner brush images.
        if(image.Tag?.ToString()!="FeaturePoster"&&image.ActualWidth>0&&image.ActualHeight>0)return image;
        for(DependencyObject? parent=VisualTreeHelper.GetParent(image);parent!=null;parent=VisualTreeHelper.GetParent(parent))
            if(parent is FrameworkElement element&&element.ActualWidth>0&&element.ActualHeight>0)return element;
        return null;
    }
    Position Measure(Entry entry)
    {
        if(disposed||!entry.Image.IsLoaded||!entry.Image.IsVisible||!owner.IsVisible||owner.WindowState==WindowState.Minimized)return default;
        var anchor=entry.Anchor;
        // Revisit a fallback after layout/decoding: a closer poster container
        // may have acquired its size, or the Image may now have its own bounds.
        if(anchor==null||anchor!=entry.Image||anchor.ActualWidth<=0||anchor.ActualHeight<=0)
        {
            var updated=Anchor(entry.Image);
            if(updated!=anchor){if(anchor!=null&&anchor!=entry.Image)anchor.SizeChanged-=Resized;entry.Anchor=anchor=updated;if(anchor!=null&&anchor!=entry.Image)anchor.SizeChanged+=Resized;}
        }
        if(anchor==null||!anchor.IsVisible||anchor.ActualWidth<=0||anchor.ActualHeight<=0)return default;
        var visible=true;var near=true;double distance=0;
        var margin=Math.Min(360,anchor.ActualHeight+16);
        try
        {
            void Clip(Visual visual,double width,double height)
            {
                if(width<=0||height<=0){visible=false;near=false;return;}
                var bounds=anchor.TransformToAncestor(visual).TransformBounds(new Rect(new Point(),anchor.RenderSize));
                visible&=bounds.IntersectsWith(new Rect(0,0,width,height));
                near&=bounds.IntersectsWith(new Rect(0,-margin,width,height+2*margin));
                distance=Math.Max(distance,Math.Max(0,Math.Max(-bounds.Bottom,bounds.Top-height)));
            }
            foreach(var clip in entry.Clips)Clip(clip,clip.ActualWidth,clip.ActualHeight);
            Clip(owner,owner.ActualWidth,owner.ActualHeight);
            return new(visible,near,distance);
        }
        catch(InvalidOperationException){return default;}
    }
    void Sweep()
    {
        scheduled=null;if(disposed)return;SweepCount++;
        // Let currently visible posters take both slots before prefetched rows.
        var positions=entries.Values.Select(entry=>(Entry:entry,Position:Measure(entry))).ToArray();
        foreach(var entry in positions.Where(x=>!x.Position.Near))entry.Entry.Changed(entry.Position);
        foreach(var entry in positions.Where(x=>x.Position.Near).OrderBy(x=>!x.Position.Visible).ThenBy(x=>x.Position.Distance))entry.Entry.Changed(entry.Position);
    }
    void Unloaded(object sender,RoutedEventArgs args)
    {
        var image=(Image)sender;if(!entries.Remove(image,out var entry))return;
        Detach(entry);entry.Changed(default);
    }
    void Detach(Entry entry)
    {
        entry.Image.Unloaded-=Unloaded;entry.Image.SizeChanged-=Resized;
        if(entry.Anchor!=null&&entry.Anchor!=entry.Image)entry.Anchor.SizeChanged-=Resized;
        foreach(var scroll in entry.Scrollers)
        {
            var count=scrollers[scroll]-1;
            if(count>0)scrollers[scroll]=count;
            else{scrollers.Remove(scroll);scroll.ScrollChanged-=Scrolled;scroll.SizeChanged-=Resized;}
        }
    }
    void Resized(object sender,SizeChangedEventArgs args)=>Schedule();
    void Scrolled(object sender,ScrollChangedEventArgs args)=>Schedule();
    void StateChanged(object? sender,EventArgs args)=>Schedule();
    void LayoutChanged(object? sender,EventArgs args){if(entries.Count>0)Schedule();}
    void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs args)=>Schedule();
    void Closed(object? sender,EventArgs args)=>Dispose();
    public void Dispose()
    {
        if(disposed)return;disposed=true;scheduled?.Abort();scheduled=null;
        foreach(var entry in entries.Values.ToArray()){Detach(entry);entry.Changed(default);}entries.Clear();
        if(listening){owner.SizeChanged-=Resized;owner.StateChanged-=StateChanged;owner.IsVisibleChanged-=VisibilityChanged;owner.LayoutUpdated-=LayoutChanged;owner.Closed-=Closed;listening=false;}
    }
}
