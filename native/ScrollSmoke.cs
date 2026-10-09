using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Kachalka;

public partial class MainWindow
{
    async Task<object> CheckWheelScrolling()
    {
        var previous=WheelScroll.SmoothOverride;
        try
        {
            WheelScroll.SmoothOverride=false;var immediate=await CheckWheelScrollingMode();
            WheelScroll.SmoothOverride=true;var smooth=await CheckWheelScrollingMode();
            return new{Immediate=immediate,Smooth=smooth};
        }
        finally{WheelScroll.SmoothOverride=previous;}
    }
    async Task<object> CheckWheelScrollingMode()
    {
        async Task Settle(int delay=500){await Task.Delay(delay);UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();}
        void Check(bool value,string reason){if(!value)throw new Exception("Wheel scroll: "+reason);}
        MouseWheelEventArgs Wheel(UIElement target,int delta)
        {
            var e=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,delta){RoutedEvent=Mouse.PreviewMouseWheelEvent};target.RaiseEvent(e);return e;
        }
        var catalog=catalogList??throw new Exception("Wheel scroll: catalog missing.");
        var catalogViewer=FindVisual<ScrollViewer>(Body,_=>true)??throw new Exception("Wheel scroll: catalog viewer missing.");
        Check(VirtualizingPanel.GetScrollUnit(catalog)==ScrollUnit.Pixel&&ScrollViewer.GetCanContentScroll(catalog),"catalog lost pixel scrolling or virtualization");
        catalogViewer.ScrollToTop();await Settle();
        var poster=VisualElements<Button>(catalog).First(x=>x.Tag is MediaItem);
        var wheel=Wheel(poster,-120);await Settle();
        var step=WheelScroll.Distance(120,SystemParameters.WheelScrollLines,catalogViewer.ViewportHeight);
        Check(wheel.Handled&&Math.Abs(catalogViewer.VerticalOffset-Math.Min(step,catalogViewer.ScrollableHeight))<2,"one notch jumped an entire poster row");
        var catalogStep=catalogViewer.VerticalOffset;Check(!WheelScroll.IsMoving(catalogViewer),"catalog timer remains active at rest");

        // A large native list verifies that pixel scrolling still recycles containers.
        Body.Children.Clear();Body.RowDefinitions.Clear();
        var row=new FrameworkElementFactory(typeof(Border));row.SetValue(FrameworkElement.HeightProperty,180d);
        var list=new ListBox{ItemsSource=Enumerable.Range(1,1000),ItemTemplate=new DataTemplate{VisualTree=row}};Body.Children.Add(list);await Settle();
        var viewer=FindVisual<ScrollViewer>(list,_=>true)??throw new Exception("Wheel scroll: fixture viewer missing.");
        viewer.ScrollToVerticalOffset(200);await Settle();var start=viewer.VerticalOffset;
        var frames=new List<double>();
        ScrollChangedEventHandler record=(_,_)=>frames.Add(viewer.VerticalOffset);
        viewer.ScrollChanged+=record;
        Wheel(list,-30);await Settle();
        viewer.ScrollChanged-=record;
        if(WheelScroll.SmoothingEnabled)
            Check(frames.Distinct().Count()>=3&&frames.Any(offset=>offset>start+.05&&offset<start+step/4-.05),"smooth wheel did not produce intermediate native scroll frames");
        Check(Math.Abs(viewer.VerticalOffset-start-step/4)<2,"precision wheel delta was rounded or amplified");
        viewer.ScrollToVerticalOffset(200);await Settle();start=viewer.VerticalOffset;
        Wheel(list,-120);Wheel(list,-120);Wheel(list,-120);await Settle();
        Check(Math.Abs(viewer.VerticalOffset-Math.Min(start+step*3,viewer.ScrollableHeight))<2,"rapid wheel input lost distance");
        viewer.ScrollToVerticalOffset(200);await Settle();start=viewer.VerticalOffset;
        Wheel(list,-120);Wheel(list,120);await Settle();
        var expected=WheelScroll.SmoothingEnabled?Math.Max(0,start-step):start;
        Check(Math.Abs(viewer.VerticalOffset-expected)<2,"direction reversal retained pending motion");
        Wheel(list,-120);
        list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(this)!,Environment.TickCount,Key.Home){RoutedEvent=Keyboard.PreviewKeyDownEvent});
        viewer.ScrollToTop();await Settle();Check(viewer.VerticalOffset==0&&!WheelScroll.IsMoving(viewer),"keyboard navigation was overwritten by wheel motion");
        viewer.ScrollToBottom();await Settle();Wheel(list,-120);await Settle();
        Check(Math.Abs(viewer.VerticalOffset-viewer.ScrollableHeight)<2&&!WheelScroll.IsMoving(viewer),"bottom boundary overshot or kept ticking");
        var containers=VisualElements<ListBoxItem>(list).Count();Check(containers<50,"1000-item fixture created too many visual containers");
        Wheel(list,120);Body.Children.Clear();await Settle();Check(!WheelScroll.IsMoving(viewer),"unloaded list left its timer running");

        var content=new StackPanel();var combo=new ComboBox{ItemsSource=new[]{"Первый","Второй"},SelectedIndex=0};var editor=new TextBox{Text="Поле ввода"};
        var nested=new ScrollViewer{Content=new Border{Height=500},Height=100};content.Children.Add(combo);content.Children.Add(editor);content.Children.Add(nested);content.Children.Add(new Border{Height=1800});
        var page=new ScrollViewer{Content=content,Style=(Style)FindResource("PageScroll"),VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Body.Children.Add(page);await Settle();
        Check(!Wheel(combo,-120).Handled&&combo.SelectedIndex==0&&!WheelScroll.IsMoving(page),"drop-down input was intercepted by the page");
        Wheel(editor,-120);Wheel(nested,-120);await Settle();Check(page.VerticalOffset==0&&!WheelScroll.IsMoving(page),"nested input scrolled the outer page");
        Wheel(content,-120);await Settle();Check(Math.Abs(page.VerticalOffset-step)<2&&!WheelScroll.IsMoving(page),"detail/settings page has a different wheel step or an idle timer");
        Body.Children.Clear();Render();await Settle();
        return new{CatalogStep=Math.Round(catalogStep,2),SystemWheelLines=SystemParameters.WheelScrollLines,SmoothingEnabled=WheelScroll.SmoothingEnabled,PixelScrolling=true,PrecisionDelta=true,RapidInput=true,DirectionReversal=true,KeyboardInterrupt=true,NestedControlsPreserved=true,IdleTimerStopped=true,VirtualizedItems=1000,RealizedContainers=containers};
    }
}
