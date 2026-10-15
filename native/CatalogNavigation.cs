using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Kachalka;

public partial class MainWindow
{
    // Offsets alone identify a different row after the poster grid changes its
    // column count. Keep the leading visible title and its position as well.
    sealed record CatalogScrollPosition(double Offset,int? ItemId,string Scope,double Top);
    sealed record CatalogBrowseContext(string Section,string Query,string Category,int Page,bool Favorites,
        string Genre,string Country,int? Year,int Rating,string Order,string Collection,string Region);
    readonly Dictionary<string,CatalogScrollPosition> catalogPositions=[];
    CatalogBrowseContext? catalogBrowseContext;
    ScrollViewer? navigationScroll;
    string navigationRoute="";
    CatalogScrollPosition? navigationPendingPosition;
    DispatcherOperation? navigationRestore;
    bool navigationApplying,navigationResetRequested;
    int navigationRestoreAttempts;
    double? navigationExpectedOffset;
    DateTime navigationUserScrollUntil;
    DispatcherTimer? navigationCaptureTimer;
    void CatalogUserScroll()
    {
        navigationUserScrollUntil=DateTime.UtcNow.AddMilliseconds(300);
        navigationRestore?.Abort();navigationRestore=null;navigationPendingPosition=null;navigationExpectedOffset=null;
        QueueCatalogPositionCapture();
    }
    void CatalogUserWheel(object sender,MouseWheelEventArgs args){if(args.Delta!=0&&navigationScroll?.ScrollableHeight>0)CatalogUserScroll();}
    void CatalogUserTouch(object? sender,TouchEventArgs args)=>CatalogUserScroll();
    void CatalogUserKey(object sender,KeyEventArgs args)
    {if(args.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)CatalogUserScroll();}
    void CatalogUserMouse(object sender,MouseButtonEventArgs args)
    {
        for(var node=args.OriginalSource as DependencyObject;node is Visual&&node!=navigationScroll;node=VisualTreeHelper.GetParent(node))
            if(node is ScrollBar){CatalogUserScroll();break;}
    }
    void QueueCatalogPositionCapture()
    {
        if(navigationCaptureTimer==null)
        {
            navigationCaptureTimer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(150)};
            navigationCaptureTimer.Tick+=(_,_)=>{navigationCaptureTimer.Stop();RememberCatalogPosition();};
        }
        navigationCaptureTimer.Stop();navigationCaptureTimer.Start();
    }


    void ResetCatalogNavigationPosition()=>navigationResetRequested=true;
    void CancelCatalogPositionRestoreForUserJump()
    {
        // An explicit jump takes precedence over a previous render's restore.
        // Discard its stored position as well, so an intervening extent event
        // cannot schedule the same obsolete anchor again.
        navigationRestore?.Abort();navigationRestore=null;navigationPendingPosition=null;navigationExpectedOffset=null;navigationCaptureTimer?.Stop();
        catalogPositions.Remove(navigationRoute);
    }

    string CatalogNavigationRoute()
    {
        if(activePerson is {} person)
            return "person|"+PeopleSearchIdentity(person)+"|origin:"+personOrigin?.Id;
        if(current is {} item)return "detail|"+item.Section+"|"+item.Id;
        if(section=="Сохранённое")return "saved|"+savedCategory+"|"+savedPage;
        if(section is "Фильмы" or "Сериалы")
            return "catalog|"+section+"|"+CurrentCatalogKey+"|page:"+livePage+"|category:"+searchCategory+
                "|selection:"+CatalogSelection.Filter+"|collection:"+catalogCollection+"|region:"+catalogRegion+
                "|poor:"+prefs.HidePoorQuality+"|favorites:"+favoritesOnly;
        if(section=="Игры")return "games|"+gameQuery+"|"+gamePage+"|"+gameShelf;
        return "page|"+section;
    }

    void BeginCatalogNavigationRender()
    {
        navigationCaptureTimer?.Stop();
        RememberCatalogPosition();
        navigationExpectedOffset=null;
        navigationRestore?.Abort();navigationRestore=null;
        if(navigationScroll!=null){navigationScroll.ScrollChanged-=CatalogNavigationScrolled;navigationScroll.Loaded-=CatalogNavigationLoaded;navigationScroll.RemoveHandler(Mouse.PreviewMouseWheelEvent,new MouseWheelEventHandler(CatalogUserWheel));navigationScroll.PreviewTouchDown-=CatalogUserTouch;navigationScroll.PreviewKeyDown-=CatalogUserKey;navigationScroll.PreviewMouseDown-=CatalogUserMouse;}
        navigationScroll=null;navigationPendingPosition=null;
    }

    void EndCatalogNavigationRender()
    {
        var previous=navigationRoute;var next=CatalogNavigationRoute();navigationRoute=next;
        // A deliberate change of page/filter starts at the top. Returning from
        // a film, person, settings or downloads restores that route's position.
        if(navigationResetRequested||previous.StartsWith("catalog|",StringComparison.Ordinal)&&next.StartsWith("catalog|",StringComparison.Ordinal)&&previous!=next||
            previous.StartsWith("saved|",StringComparison.Ordinal)&&next.StartsWith("saved|",StringComparison.Ordinal)&&previous!=next)
            catalogPositions.Remove(next);
        navigationResetRequested=false;
        if(current==null&&activePerson==null&&section is "Фильмы" or "Сериалы")
            catalogBrowseContext=new(section,submittedQuery,searchCategory,livePage,favoritesOnly,catalogGenre,catalogCountry,
                catalogYear,catalogRating,catalogOrder,catalogCollection,catalogRegion);
        navigationScroll=FindVisual<ScrollViewer>(Body,scroll=>scroll.VerticalScrollBarVisibility!=ScrollBarVisibility.Disabled);
        if(navigationScroll==null)return;
        navigationScroll.ScrollChanged+=CatalogNavigationScrolled;
        navigationScroll.Loaded+=CatalogNavigationLoaded;
        navigationScroll.AddHandler(Mouse.PreviewMouseWheelEvent,new MouseWheelEventHandler(CatalogUserWheel),true);navigationScroll.PreviewTouchDown+=CatalogUserTouch;navigationScroll.PreviewKeyDown+=CatalogUserKey;navigationScroll.PreviewMouseDown+=CatalogUserMouse;
        navigationRestoreAttempts=0;
        if(catalogPositions.TryGetValue(next,out var saved))navigationPendingPosition=saved;
        ScheduleCatalogPositionRestore();
    }

    bool TryRestoreCatalogContext(string name)
    {
        if(section is not ("Загрузки" or "Настройки")||catalogBrowseContext is not {} saved||saved.Section!=name)return false;
        RestoreCatalogBrowseContext(saved);return true;
    }

    void RestoreCatalogBrowseContext(CatalogBrowseContext saved)
    {
        var name=saved.Section;
        section=name;lastCatalogSection=name;current=null;activePerson=null;returnPerson=null;personSearchReturn=null;savedReturn=null;
        submittedQuery=saved.Query;searchCategory=saved.Category;livePage=saved.Page;favoritesOnly=saved.Favorites;
        catalogGenre=saved.Genre;catalogCountry=saved.Country;catalogYear=saved.Year;catalogRating=saved.Rating;
        catalogOrder=saved.Order;catalogCollection=saved.Collection;catalogRegion=saved.Region;Search.Text=saved.Query;
    }

    CatalogScrollPosition? CaptureCatalogReflow()=>navigationPendingPosition??CaptureCatalogPosition();
    void RestoreCatalogReflow(CatalogScrollPosition? saved)
    {
        if(saved==null||navigationScroll==null||WheelScroll.IsMoving(navigationScroll)||DateTime.UtcNow<navigationUserScrollUntil)return;
        navigationRestoreAttempts=0;navigationPendingPosition=saved;ScheduleCatalogPositionRestore();
    }
    void CatalogNavigationLoaded(object sender,RoutedEventArgs args)=>ScheduleCatalogPositionRestore();

    void CatalogNavigationScrolled(object sender,ScrollChangedEventArgs args)
    {
        if(navigationApplying||!ReferenceEquals(sender,navigationScroll)||!ReferenceEquals(args.OriginalSource,navigationScroll))return;
        if(WheelScroll.IsMoving(navigationScroll!)||DateTime.UtcNow<navigationUserScrollUntil){QueueCatalogPositionCapture();return;}
        if(navigationPendingPosition!=null)
        {
            if(args.VerticalChange!=0&&args.ExtentHeightChange==0&&args.ViewportHeightChange==0&&navigationExpectedOffset is {} expected&&Math.Abs(navigationScroll!.VerticalOffset-expected)>1.1){CatalogUserScroll();return;}
            ScheduleCatalogPositionRestore();return;
        }
        // Regional/quality results can insert a shelf above the current row.
        // Use the snapshot from before layout changed instead of its new Y.
        if(args.ExtentHeightChange!=0&&args.VerticalChange==0&&
            catalogPositions.TryGetValue(navigationRoute,out var previous)&&previous.Offset>0&&previous.ItemId!=null)
        {navigationRestoreAttempts=0;navigationPendingPosition=previous;ScheduleCatalogPositionRestore();return;}
        QueueCatalogPositionCapture();
    }

    void RememberCatalogPosition()
    {
        if(navigationPendingPosition!=null||navigationApplying||navigationRoute.Length==0||CaptureCatalogPosition() is not {} saved)return;
        if(!catalogPositions.ContainsKey(navigationRoute)&&catalogPositions.Count>=48)catalogPositions.Remove(catalogPositions.Keys.First());
        catalogPositions[navigationRoute]=saved;
    }

    CatalogScrollPosition? CaptureCatalogPosition()
    {
        if(navigationScroll is not {IsLoaded:true} scroll||scroll.ActualHeight<=0)return null;
        var offset=scroll.VerticalOffset;
        if(offset<.5)return new(0,null,"",0);
        var visible=VisualElements<Button>(scroll).Where(button=>button.Tag is MediaItem&&button.IsVisible&&button.ActualHeight>0)
            .Select(button=>(Button:button,Bounds:button.TransformToAncestor(scroll).TransformBounds(new Rect(new Point(),button.RenderSize))))
            .Where(entry=>entry.Bounds.Bottom>1&&entry.Bounds.Top<scroll.ViewportHeight-1)
            .OrderBy(entry=>entry.Bounds.Top).ThenBy(entry=>entry.Bounds.Left).FirstOrDefault();
        if(visible.Button?.Tag is MediaItem item)return new(offset,item.Id,CatalogAnchorScope(visible.Button,scroll),visible.Bounds.Top);
        return new(offset,null,"",0);
    }

    string CatalogAnchorScope(DependencyObject element,ScrollViewer scroll)
    {
        for(var parent=VisualTreeHelper.GetParent(element);parent!=null&&!ReferenceEquals(parent,scroll);parent=VisualTreeHelper.GetParent(parent))
        {
            if(ReferenceEquals(parent,catalogList))return "catalog";
            if(parent is FrameworkElement named&&named.Name.Length>0&&
                (named.Name.StartsWith("Person",StringComparison.Ordinal)||named.Name.StartsWith("Discovery",StringComparison.Ordinal)))return named.Name;
        }
        return "";
    }

    void ScheduleCatalogPositionRestore()
    {
        if(navigationScroll==null||navigationRestore is {Status:DispatcherOperationStatus.Pending})return;
        var scroll=navigationScroll;var route=navigationRoute;
        navigationRestore=Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>
        {
            navigationRestore=null;
            if(closed||!ReferenceEquals(navigationScroll,scroll)||navigationRoute!=route||!scroll.IsLoaded)return;
            if(navigationPendingPosition is not {} saved){RememberCatalogPosition();return;}
            if(!TryCatalogRestoreTarget(scroll,route,saved,out var target))return;
            navigationApplying=true;
            try{navigationExpectedOffset=target;scroll.ScrollToVerticalOffset(target);}
            finally{navigationApplying=false;}
            // WPF applies the scroll command on a following layout. Keep the
            // title anchor until that command and the new extent have settled;
            // an early zero extent must not replace it with the page's top.
            navigationRestore=Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>
            {
                navigationRestore=null;
                if(closed||!ReferenceEquals(navigationScroll,scroll)||navigationRoute!=route||!ReferenceEquals(navigationPendingPosition,saved))return;
                if(!TryCatalogRestoreTarget(scroll,route,saved,out var settledTarget))return;
                if(Math.Abs(scroll.VerticalOffset-settledTarget)>1.1&&++navigationRestoreAttempts<6){ScheduleCatalogPositionRestore();return;}
                navigationPendingPosition=null;navigationExpectedOffset=null;RememberCatalogPosition();
            }));
        }));
    }

    bool TryCatalogRestoreTarget(ScrollViewer scroll,string route,CatalogScrollPosition saved,out double target)
    {
        target=saved.Offset;
        if(!scroll.IsLoaded||!scroll.IsMeasureValid||!scroll.IsArrangeValid||scroll.ViewportHeight<=0)return false;
        // Keep a previous page's anchor while an asynchronous catalog fetch
        // temporarily renders an empty list. Its result will render again.
        if(saved.Offset>0&&scroll.ScrollableHeight<.5&&liveLoading&&route.StartsWith("catalog|",StringComparison.Ordinal))return false;
        if(saved.ItemId is {} id)
        {
            var anchor=VisualElements<Button>(scroll).FirstOrDefault(button=>button.Tag is MediaItem item&&item.Id==id&&
                button.IsVisible&&button.ActualHeight>0&&CatalogAnchorScope(button,scroll)==saved.Scope);
            if(anchor==null&&liveLoading&&route.StartsWith("catalog|",StringComparison.Ordinal))return false;
            if(anchor==null&&route.StartsWith("person|",StringComparison.Ordinal)&&personProfileLoading)return false;
            if(anchor!=null)
            {
                var top=anchor.TransformToAncestor(scroll).Transform(new Point()).Y;
                var visibleTop=Math.Clamp(saved.Top,-Math.Max(0,anchor.ActualHeight-24),Math.Max(0,scroll.ViewportHeight-24));
                target=scroll.VerticalOffset+top-visibleTop;
            }
        }
        target=Math.Clamp(target,0,scroll.ScrollableHeight);return true;
    }
}
