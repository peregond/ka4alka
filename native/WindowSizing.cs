using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace Kachalka;

public readonly record struct WindowDimensions(double Width,double Height,double MinWidth,double MinHeight,double MaxWidth,double MaxHeight);
public static class WindowSizing
{
    public static WindowDimensions Fit(double workWidth,double workHeight)
    {
        var maxWidth=Math.Max(1,workWidth);
        var maxHeight=Math.Max(1,workHeight);
        var width=Math.Max(1,Math.Min(1760,maxWidth-24));
        var height=Math.Max(1,Math.Min(950,maxHeight-24));
        return new(width,height,Math.Min(620,width),Math.Min(420,height),maxWidth,maxHeight);
    }
    public static WindowDimensions FitPixels(double width,double height,double scaleX,double scaleY)
    {
        if(!double.IsFinite(scaleX)||!double.IsFinite(scaleY)||scaleX<=0||scaleY<=0)throw new ArgumentOutOfRangeException(nameof(scaleX));
        return Fit(width/scaleX,height/scaleY);
    }
    // A poster cell is 144 wide plus a 16 gap, so rows hold as many whole cards as fit.
    public static int PosterColumns(double contentWidth)=>Math.Clamp((int)Math.Floor(contentWidth/160),1,10);
}

public partial class MainWindow
{
    const int WmDpiChanged=0x02E0,WmDisplayChange=0x007E;
    const uint MonitorDefaultNearest=2,SwpNoSize=0x0001,SwpNoZOrder=0x0004,SwpNoActivate=0x0010;
    IntPtr activeMonitor;
    double activeScaleX,activeScaleY;
    bool sizingQueued,compactWidth,compactHeight,tinyWidth,veryCompactHeight,compactSearch,layoutInitialized;
    ListBox? catalogList;
    IReadOnlyList<MediaItem> catalogDisplay=[];
    int catalogColumns;

    [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;public int Width=>Right-Left;public int Height=>Bottom-Top;}
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo {public uint Size;public NativeRect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr insertAfter,int x,int y,int width,int height,uint flags);

    void EnableAdaptiveLayout()
    {
        SourceInitialized+=(_,_)=>{var handle=new WindowInteropHelper(this).Handle;HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);SystemWindowTheme.Apply(this,prefs.Light);FitToMonitor(true);};
        LocationChanged+=(_,_)=>QueueMonitorFit();
        StateChanged+=(_,_)=>QueueMonitorFit();
        SizeChanged+=(_,_)=>ApplyCompactLayout();
        Body.SizeChanged+=(_,_)=>{UpdateCatalogColumns();UpdateDetailLayout();UpdateFilterRail();UpdateDiscoveryLayout();};
    }
    IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message is WmDpiChanged or WmDisplayChange)QueueMonitorFit();
        return IntPtr.Zero;
    }
    void QueueMonitorFit()
    {
        if(designFixedViewport||sizingQueued||closed)return;
        sizingQueued=true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>{sizingQueued=false;FitToMonitor(false);}));
    }
    void FitToMonitor(bool first)
    {
        // Isolated design captures choose their own viewport after the initial fit.
        if(designFixedViewport&&!first)return;
        var handle=new WindowInteropHelper(this).Handle;
        if(handle==IntPtr.Zero)return;
        var monitor=MonitorFromWindow(handle,MonitorDefaultNearest);
        var info=new MonitorInfo{Size=(uint)Marshal.SizeOf<MonitorInfo>()};
        if(monitor==IntPtr.Zero||!GetMonitorInfo(monitor,ref info))return;
        var dpi=VisualTreeHelper.GetDpi(this);
        var scaleX=dpi.DpiScaleX;var scaleY=dpi.DpiScaleY;
        if(scaleX<=0||scaleY<=0)return;
        var changed=first||monitor!=activeMonitor||Math.Abs(scaleX-activeScaleX)>0.01||Math.Abs(scaleY-activeScaleY)>0.01;
        if(!changed&&Width<=MaxWidth&&Height<=MaxHeight)return;
        activeMonitor=monitor;activeScaleX=scaleX;activeScaleY=scaleY;
        var fit=WindowSizing.FitPixels(info.Work.Width,info.Work.Height,scaleX,scaleY);
        MaxWidth=fit.MaxWidth;MaxHeight=fit.MaxHeight;
        MinWidth=fit.MinWidth;MinHeight=fit.MinHeight;
        if(WindowState==WindowState.Normal){Width=first?fit.Width:Math.Min(Width,fit.Width);Height=first?fit.Height:Math.Min(Height,fit.Height);}
        ApplyCompactLayout();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>
        {
            var currentInfo=new MonitorInfo{Size=(uint)Marshal.SizeOf<MonitorInfo>()};
            var currentMonitor=MonitorFromWindow(handle,MonitorDefaultNearest);
            if(GetMonitorInfo(currentMonitor,ref currentInfo))PlaceWithinWorkArea(handle,currentInfo.Work,first);
        }));
    }
    static void PlaceWithinWorkArea(IntPtr handle,NativeRect work,bool center)
    {
        if(!GetWindowRect(handle,out var window))return;
        var x=center?work.Left+(work.Width-window.Width)/2:Math.Clamp(window.Left,work.Left,Math.Max(work.Left,work.Right-window.Width));
        var y=center?work.Top+(work.Height-window.Height)/2:Math.Clamp(window.Top,work.Top,Math.Max(work.Top,work.Bottom-window.Height));
        SetWindowPos(handle,IntPtr.Zero,x,y,0,0,SwpNoSize|SwpNoZOrder|SwpNoActivate);
    }
    void ApplyCompactLayout()
    {
        if(!ready)return;
        var narrow=ActualWidth>0&&ActualWidth<950;
        var tiny=ActualWidth>0&&ActualWidth<780;
        var shortView=ActualHeight>0&&ActualHeight<560;
        var veryShort=ActualHeight>0&&ActualHeight<360;
        var narrowSearch=ActualWidth>0&&ActualWidth<560;
        // Windows scale changes the size of the window in device independent units, so every
        // threshold is expressed in those units: 200 % on a 1080p laptop leaves roughly 960 x 520.
        var gutter=narrow?(tiny?16d:24d):ActualWidth<1180?24d:32d;
        FitDownloadsToolbar();
        ThemeButton.Visibility=ActualWidth>0&&ActualWidth<480?Visibility.Collapsed:Visibility.Visible;
        if(activeCatalogFilterScroll!=null)activeCatalogFilterScroll.MaxHeight=shortView?68:double.PositiveInfinity;
        if(layoutInitialized&&narrow==compactWidth&&tiny==tinyWidth&&shortView==compactHeight&&veryShort==veryCompactHeight&&narrowSearch==compactSearch&&gutter==contentGutter){UpdateFilterRail();UpdateDownloadsWidget();return;}
        layoutInitialized=true;compactWidth=narrow;tinyWidth=tiny;compactHeight=shortView;veryCompactHeight=veryShort;compactSearch=narrowSearch;contentGutter=gutter;
        SidebarColumn.Width=new GridLength(narrow?64:236);
        SidePanel.Padding=narrow?new Thickness(8,veryShort?8:16,8,10):new Thickness(14,veryShort?8:shortView?14:20,14,12);
        Brand.Margin=narrow?new Thickness(0,0,0,shortView?10:18):new Thickness(8,0,0,shortView?12:24);
        Brand.HorizontalAlignment=narrow?HorizontalAlignment.Center:HorizontalAlignment.Stretch;
        Brand.Visibility=veryShort?Visibility.Collapsed:Visibility.Visible;
        BrandLogo.Width=narrow?32:36;BrandLogo.Height=narrow?32:36;BrandLogo.Margin=new Thickness(0,0,narrow?0:12,0);
        BrandText.Visibility=narrow?Visibility.Collapsed:Visibility.Visible;
        LibraryLabel.Visibility=narrow||shortView?Visibility.Collapsed:Visibility.Visible;
        foreach(var nav in Navigation.Children.OfType<Button>()){var name=nav.Tag?.ToString()??"";nav.Content=NavContent(name,name=="Фильмы"?"IconFilm":"IconTv",narrow);}
        SavedButton.Content=NavContent("Сохранённое","IconHeart",narrow,1);DownloadsButton.Content=NavContent("Загрузки","IconDownload",narrow,2);SettingsButton.Content=NavContent("Настройки","IconSettings",narrow);
        foreach(var nav in Navigation.Children.OfType<Button>().Concat(new[]{SavedButton,DownloadsButton,SettingsButton}))
        {
            nav.HorizontalContentAlignment=narrow?HorizontalAlignment.Center:HorizontalAlignment.Stretch;
            nav.Padding=new Thickness(narrow?6:12,0,narrow?6:12,0);
            nav.MinHeight=veryShort?30:shortView?40:44;nav.Margin=new(0,0,0,veryShort?2:4);
        }
        SidebarUpdateButton.MinHeight=veryShort?30:40;
        var control=tiny?40d:shortView?44d:48d;
        AddTorrentButton.Content=IconLabel(tiny?"":narrow?"Добавить":"Добавить торрент","IconMagnet",18);
        AddTorrentButton.Padding=new Thickness(tiny?0:narrow?14:18,0,tiny?0:narrow?14:18,0);AddTorrentButton.MinHeight=control;AddTorrentButton.Height=control;AddTorrentButton.MinWidth=tiny?control:0;AddTorrentButton.Margin=new Thickness(0,0,tiny?8:12,0);
        BackButton.Width=control;BackButton.Height=control;BackButton.Margin=new Thickness(0,0,tiny?8:12,0);ThemeButton.Width=control;ThemeButton.Height=control;
        HeaderArea.Margin=new Thickness(gutter,veryShort?6:shortView?12:20,gutter,veryShort?6:shortView?10:20);
        CenterRegion.Margin=new Thickness(gutter,0,gutter,0);Status.Margin=new Thickness(gutter,6,gutter,8);
        SearchBar.Margin=new Thickness(0,0,tiny?8:12,0);SearchBar.Height=control;Search.MinHeight=control;
        Search.Padding=new Thickness(narrowSearch?12:48,0,narrowSearch?72:110,0);
        SearchMagnifier.Visibility=narrowSearch?Visibility.Collapsed:Visibility.Visible;
        SearchPlaceholder.Text=narrowSearch?"Поиск":"Найти фильм, сериал или человека";SearchPlaceholder.Margin=new Thickness(narrowSearch?12:48,0,narrowSearch?72:110,0);
        foreach(var subtitle in PageHeader.Children.OfType<TextBlock>().Where(x=>Equals(x.Tag,"CatalogSubtitle")))subtitle.Visibility=shortView?Visibility.Collapsed:Visibility.Visible;
        RenderNavSelection();
        UpdateSavedCount();UpdateDownloadsWidget();
        UpdateFilterRail();
        UpdateCatalogColumns();RefreshSidebarUpdate();
    }
    void UpdateFilterRail()
    {
        var visible=inlineCatalogFilterScroll!=null&&catalogToolbar!=null&&current==null&&activePerson==null&&section is "Фильмы" or "Сериалы" or "Сохранённое";
        FiltersPanel.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
        if(!visible||inlineCatalogFilters==null)return;
        var width=FiltersPanel.ActualWidth>0?FiltersPanel.ActualWidth:Math.Max(1,ActualWidth-(compactWidth?64:236)-2*contentGutter);
        var compact=width<1100;var shortChip=compactHeight;
        if(catalogFiltersCompact!=compact)
        {
            catalogFiltersCompact=compact;
            foreach(var (button,caption) in catalogFilterCaptions)
                SetChipText(button,compact?caption.Compact:caption.Full);
        }
        foreach(var button in inlineCatalogFilters.Children.OfType<Button>().Concat(catalogToolbar?.Children.OfType<StackPanel>().SelectMany(x=>x.Children.OfType<Button>())??[]))
        {
            if(ReferenceEquals(button,catalogRefreshButton))continue;
            button.HorizontalContentAlignment=HorizontalAlignment.Center;button.MinHeight=shortChip?36:40;button.FontSize=compact?13:14;
            button.Padding=new(compact?12:16,0,compact?12:16,0);button.Margin=new(0,0,8,8);
        }
        if(catalogRefreshButton!=null){catalogRefreshButton.Padding=new(0);catalogRefreshButton.Width=shortChip?36:40;catalogRefreshButton.MinHeight=shortChip?36:40;catalogRefreshButton.Margin=new(8,0,0,8);}
        UpdateCatalogFilterOverflow();UpdateDiscoveryLayout();
    }
    void UpdateCatalogFilterOverflow()
    {
        if(inlineCatalogFilterScroll==null||inlineCatalogFilters==null||catalogToolbar==null||catalogFilterBack==null||catalogFilterForward==null)return;
        var overflow=inlineCatalogFilterScroll.ViewportWidth>0&&inlineCatalogFilterScroll.ScrollableWidth>1;
        var visibility=overflow?Visibility.Visible:Visibility.Collapsed;
        catalogFilterBack.Visibility=visibility;catalogFilterForward.Visibility=visibility;
        catalogFilterBack.IsEnabled=inlineCatalogFilterScroll.HorizontalOffset>1;
        catalogFilterForward.IsEnabled=inlineCatalogFilterScroll.HorizontalOffset+1<inlineCatalogFilterScroll.ScrollableWidth;
    }
    void ScrollCatalogFilters(int direction)
    {
        if(inlineCatalogFilterScroll is not {} scroll)return;
        scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset+direction*Math.Max(160,scroll.ViewportWidth*.65));
    }
    void ShowCatalog(IReadOnlyList<MediaItem> items)
    {
        catalogDisplay=items;
        catalogList=new ListBox{ItemTemplate=(DataTemplate)FindResource("MediaRow")};
        Body.Children.Add(catalogList);
        UpdateCatalogColumns(true);
    }
    void UpdateCatalogColumns(bool force=false)
    {
        if(catalogList==null)return;
        var width=Body.ActualWidth>0?Body.ActualWidth:Math.Max(1,Width-(compactWidth?64:236)-2*contentGutter);
        var columns=WindowSizing.PosterColumns(width);
        if(!force&&columns==catalogColumns)return;
        var position=CaptureCatalogReflow();
        catalogColumns=columns;
        catalogList.ItemsSource=catalogDisplay.Chunk(columns).Select(x=>new CatalogRow(x,columns)).ToArray();ApplyPosterDownloads(catalogDisplay);
        UpdateDiscoveryLayout();
        RestoreCatalogReflow(position);
    }
}
