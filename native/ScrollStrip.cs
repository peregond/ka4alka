using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Kachalka;

// The season and episode strips of a series card. A long series (27 seasons, a hundred episodes) cannot
// sit on one line, and a plain row simply ran past the window edge. The segments now live in a sideways
// scroller: arrows and Shift+wheel move it, a list button shows every value at once, and the chosen segment
// is brought into view. Nothing animates and no timer runs. Whether the strip overflows follows from its
// extent and from the width the page gives it, so showing the controls never changes that decision.
public partial class MainWindow
{
    sealed record StripEntry(string Value,string Label,int Count,RadioButton Button);
    sealed class ScrollStrip
    {
        public const double CaptionWidth=58,FrameChrome=10,WheelStep=48;
        public Grid Line=null!;
        public StackPanel Items=null!;
        public ScrollViewer Scroller=null!;
        public Button Previous=null!,Next=null!,List=null!;
        public ToggleContextMenu Menu=null!;
        public readonly List<StripEntry> Entries=[];
        // The segment that must be shown as soon as the strip has a layout.
        public RadioButton? Pending;
        public bool Overflowing;
    }

    ScrollStrip CreateScrollStrip(string caption,string name)
    {
        var strip=new ScrollStrip();
        var line=new Grid{Margin=new(0,0,0,10)};
        line.ColumnDefinitions.Add(new(){Width=new GridLength(ScrollStrip.CaptionWidth)});line.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
        var label=new TextBlock{Text=caption,FontSize=12,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center};label.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");line.Children.Add(label);

        // The frame hugs its segments while they fit and takes the whole row once they do not.
        var frame=new Border{CornerRadius=new(12),BorderThickness=new(1),Padding=new(4),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};
        frame.SetResourceReference(Border.BackgroundProperty,"Panel");frame.SetResourceReference(Border.BorderBrushProperty,"EdgeSoft");
        AutomationProperties.SetName(frame,name);Grid.SetColumn(frame,1);line.Children.Add(frame);
        var inner=new Grid();
        inner.ColumnDefinitions.Add(new(){Width=GridLength.Auto});inner.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});inner.ColumnDefinitions.Add(new(){Width=GridLength.Auto});inner.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        frame.Child=inner;

        var items=new StackPanel{Orientation=Orientation.Horizontal};
        var scroller=new ScrollViewer{Content=items,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=false};
        Grid.SetColumn(scroller,1);inner.Children.Add(scroller);

        Button Control(string icon,string tip,int column,Thickness margin)
        {
            var button=new Button{Style=(Style)FindResource("IconButton"),Width=34,Height=34,Margin=margin,Visibility=Visibility.Collapsed,ToolTip=tip,VerticalAlignment=VerticalAlignment.Center};
            button.Content=IconLabel("",icon,16);AutomationProperties.SetName(button,tip+": "+name);Grid.SetColumn(button,column);inner.Children.Add(button);return button;
        }
        strip.Previous=Control("IconChevronLeft","Прокрутить влево",0,new(0,0,4,0));
        strip.Next=Control("IconChevron","Прокрутить вправо",2,new(4,0,0,0));
        strip.List=Control("IconChevronDown","Показать весь список",3,new(4,0,0,0));
        strip.Line=line;strip.Items=items;strip.Scroller=scroller;

        strip.Previous.Click+=(_,_)=>scroller.ScrollToHorizontalOffset(StripLayout.Page(scroller.HorizontalOffset,scroller.ViewportWidth,scroller.ScrollableWidth,-1));
        strip.Next.Click+=(_,_)=>scroller.ScrollToHorizontalOffset(StripLayout.Page(scroller.HorizontalOffset,scroller.ViewportWidth,scroller.ScrollableWidth,1));
        // A plain wheel keeps scrolling the page; Shift turns it sideways while the strip has something to scroll.
        scroller.PreviewMouseWheel+=(_,e)=>
        {
            if(!strip.Overflowing||e.Delta==0||!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))return;
            scroller.ScrollToHorizontalOffset(Math.Clamp(scroller.HorizontalOffset-Math.Sign(e.Delta)*ScrollStrip.WheelStep,0,scroller.ScrollableWidth));e.Handled=true;
        };
        scroller.ScrollChanged+=(_,_)=>UpdateStripOverflow(strip);
        line.SizeChanged+=(_,e)=>
        {
            if(!e.WidthChanged)return;
            strip.Pending=ChosenSegment(strip)??strip.Pending;UpdateStripOverflow(strip);
        };

        // The list is rebuilt just before it opens: the episode strip changes with the chosen season.
        strip.Menu=new ToggleContextMenu{PlacementTarget=strip.List,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom,MaxHeight=380};
        strip.List.Click+=(_,_)=>RebuildStripMenu(strip);
        AttachMenuToggle(strip.List,strip.Menu);
        strip.Menu.Opened+=(_,_)=>{foreach(var entry in strip.Menu.Items.OfType<MenuItem>())if(entry.IsChecked){entry.BringIntoView();break;}};
        return strip;
    }

    static RadioButton? ChosenSegment(ScrollStrip strip)=>strip.Entries.Select(x=>x.Button).FirstOrDefault(x=>x.IsChecked==true);

    void AddStripItem(ScrollStrip strip,RadioButton radio,string label,int count)
    {
        strip.Entries.Add(new(radio.Tag?.ToString()??label,label,count,radio));
        radio.Checked+=(_,_)=>{strip.Pending=radio;FlushStripReveal(strip);};
        strip.Items.Children.Add(radio);
        // The segment may have been checked before it joined the strip.
        if(radio.IsChecked==true)strip.Pending=radio;
    }

    static void ClearStrip(ScrollStrip strip)
    {
        strip.Items.Children.Clear();strip.Entries.Clear();strip.Pending=null;
        strip.Scroller.ScrollToHorizontalOffset(0);
    }

    void UpdateStripOverflow(ScrollStrip strip)
    {
        var scroller=strip.Scroller;
        var overflowing=StripLayout.Overflows(scroller.ExtentWidth,strip.Line.ActualWidth-ScrollStrip.CaptionWidth-ScrollStrip.FrameChrome);
        if(overflowing!=strip.Overflowing)
        {
            strip.Overflowing=overflowing;
            var shown=overflowing?Visibility.Visible:Visibility.Collapsed;
            strip.Previous.Visibility=strip.Next.Visibility=strip.List.Visibility=shown;
            // The controls take room from the viewport: the chosen segment may need another look.
            if(overflowing)strip.Pending=ChosenSegment(strip)??strip.Pending;
        }
        strip.Previous.IsEnabled=scroller.HorizontalOffset>.5;
        strip.Next.IsEnabled=scroller.HorizontalOffset<scroller.ScrollableWidth-.5;
        FlushStripReveal(strip);
    }

    // Brings the pending segment into view once it has a size; until then the request waits for the next layout.
    void FlushStripReveal(ScrollStrip strip)
    {
        if(strip.Pending is not {} radio)return;
        var scroller=strip.Scroller;
        if(!radio.IsLoaded||radio.ActualWidth<=0||scroller.ViewportWidth<=0||!ReferenceEquals(VisualTreeHelper.GetParent(radio),strip.Items))return;
        strip.Pending=null;
        var target=StripLayout.Reveal(radio.TranslatePoint(new Point(),strip.Items).X,radio.ActualWidth,scroller.HorizontalOffset,scroller.ViewportWidth,scroller.ScrollableWidth);
        if(Math.Abs(target-scroller.HorizontalOffset)>.5)scroller.ScrollToHorizontalOffset(target);
    }

    void RebuildStripMenu(ScrollStrip strip)
    {
        if(strip.Menu.IsOpen)return;
        strip.Menu.Items.Clear();
        foreach(var entry in strip.Entries)
        {
            var header=new Grid();header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            header.Children.Add(new TextBlock{Text=entry.Label,VerticalAlignment=VerticalAlignment.Center});
            var count=new TextBlock{Text=entry.Count.ToString(),FontFamily=(FontFamily)FindResource("MonoFont"),FontSize=12,Margin=new(18,0,0,0),VerticalAlignment=VerticalAlignment.Center};
            count.SetResourceReference(TextBlock.ForegroundProperty,"Subtle");Grid.SetColumn(count,1);header.Children.Add(count);
            var item=new MenuItem{Header=header,IsChecked=entry.Button.IsChecked==true,MinHeight=36,Tag=entry.Value};
            AutomationProperties.SetName(item,entry.Label+", раздач: "+entry.Count);
            var target=entry.Button;
            item.Click+=(_,_)=>{strip.Menu.IsOpen=false;target.IsChecked=true;};
            strip.Menu.Items.Add(item);
        }
    }
}
