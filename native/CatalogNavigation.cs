using System.Windows;
using System.Windows.Controls;
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

    void ResetCatalogNavigationPosition()=>navigationResetRequested=true;

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
        return "page|"+section;
    }

    void BeginCatalogNavigationRender()
    {
        RememberCatalogPosition();
        navigationRestore?.Abort();navigationRestore=null;
        if(navigationScroll!=null)navigationScroll.ScrollChanged-=CatalogNavigationScrolled;
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
        if(saved==null||navigationScroll==null)return;
        navigationPendingPosition=saved;ScheduleCatalogPositionRestore();
    }

    void CatalogNavigationScrolled(object sender,ScrollChangedEventArgs args)
    {
        if(navigationApplying||!ReferenceEquals(sender,navigationScroll)||!ReferenceEquals(args.OriginalSource,navigationScroll))return;
        if(navigationPendingPosition!=null){ScheduleCatalogPositionRestore();return;}
        // Regional/quality results can insert a shelf above the current row.
        // Use the snapshot from before layout changed instead of its new Y.
        if(args.ExtentHeightChange!=0&&args.VerticalChange==0&&
            catalogPositions.TryGetValue(navigationRoute,out var previous)&&previous.Offset>0&&previous.ItemId!=null)
        {navigationPendingPosition=previous;ScheduleCatalogPositionRestore();return;}
        RememberCatalogPosition();
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
            // Keep a previous page's anchor while an asynchronous catalog fetch
            // temporarily renders an empty list. Its result will render again.
            if(saved.Offset>0&&scroll.ScrollableHeight<.5&&liveLoading&&route.StartsWith("catalog|",StringComparison.Ordinal))return;
            var target=saved.Offset;
            if(saved.ItemId is {} id)
            {
                var anchor=VisualElements<Button>(scroll).FirstOrDefault(button=>button.Tag is MediaItem item&&item.Id==id&&
                    button.IsVisible&&button.ActualHeight>0&&CatalogAnchorScope(button,scroll)==saved.Scope);
                if(anchor==null&&liveLoading&&route.StartsWith("catalog|",StringComparison.Ordinal))return;
                if(anchor==null&&route.StartsWith("person|",StringComparison.Ordinal)&&personProfileLoading)return;
                if(anchor!=null)
                {
                    var top=anchor.TransformToAncestor(scroll).Transform(new Point()).Y;
                    var visibleTop=Math.Clamp(saved.Top,-Math.Max(0,anchor.ActualHeight-24),Math.Max(0,scroll.ViewportHeight-24));
                    target=scroll.VerticalOffset+top-visibleTop;
                }
            }
            navigationApplying=true;
            try{scroll.ScrollToVerticalOffset(Math.Clamp(target,0,scroll.ScrollableHeight));}
            finally{navigationApplying=false;navigationPendingPosition=null;}
            // ScrollToVerticalOffset is applied during the following layout;
            // recording immediately would replace the anchor with the old top.
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>
            {if(ReferenceEquals(navigationScroll,scroll)&&navigationRoute==route)RememberCatalogPosition();}));
        }));
    }
}
