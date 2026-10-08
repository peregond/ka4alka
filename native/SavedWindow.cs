using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Kachalka;
public partial class MainWindow
{
    string savedCategory="";
    int savedPage=1;
    ScrollViewer? savedScroll;
    double? restoreSavedOffset;
    sealed record SavedReturn(string Category,int Page,double Offset);
    SavedReturn? savedReturn;

    bool IsSaved(MediaItem item)=>prefs.Favorites.Contains(item.Id)||prefs.LiveFavorites.Any(saved=>saved.Id==item.Id);
    void ToggleSaved(MediaItem item)
    {
        if(IsSaved(item)){prefs.Favorites.Remove(item.Id);prefs.LiveFavorites.RemoveAll(saved=>saved.Id==item.Id);}
        else{prefs.Favorites.Add(item.Id);prefs.LiveFavorites.RemoveAll(saved=>saved.Id==item.Id);prefs.LiveFavorites.Add(item);}
        prefs.Save();
    }
    MediaItem[] SavedItems()=>prefs.LiveFavorites.AsEnumerable().Reverse()
        .Concat(liveItems.Concat(catalogIndex.Recent("Фильмы",200)).Concat(catalogIndex.Recent("Сериалы",200)).Concat(Catalog.Items)
            .Where(item=>prefs.Favorites.Contains(item.Id)))
        .Where(item=>item.Section is "Фильмы" or "Сериалы").DistinctBy(item=>item.Id).ToArray();
    void ShowSaved(object sender,RoutedEventArgs e)
    {
        searchDelay.Stop();liveRequest?.Cancel();liveLoading=false;
        current=null;activePerson=null;returnPerson=null;personSearchReturn=null;savedReturn=null;favoritesOnly=false;
        section="Сохранённое";Search.Text="";Render();
    }
    string? SavedBackLabel()=>savedReturn!=null?"Сохранённое":null;
    bool ReturnToSaved()
    {
        if(savedReturn is not {} previous)return false;
        savedCategory=previous.Category;savedPage=previous.Page;restoreSavedOffset=previous.Offset;savedReturn=null;
        current=null;activePerson=null;personOrigin=null;returnPerson=null;favoritesOnly=false;section="Сохранённое";Search.Text="";Render();return true;
    }
    void GoSavedPage(int page)
    {
        savedPage=Math.Max(1,page);Render();
    }
    void RenderSaved()
    {
        var saved=SavedItems();
        var filtered=saved.Where(item=>savedCategory.Length==0||item.Section==savedCategory).ToArray();
        var pageCount=Math.Max(1,(filtered.Length+CatalogPaging.Size-1)/CatalogPaging.Size);
        savedPage=Math.Clamp(savedPage,1,pageCount);
        var heading=new DockPanel{Margin=new(5,0,8,12)};
        var count=Text(saved.Length+" · в коллекции",12,true);count.Margin=new(12,0,0,0);count.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(count,Dock.Right);heading.Children.Add(count);
        var title=Text("Сохранённое",compactHeight?22:28);title.FontWeight=FontWeights.SemiBold;title.Margin=new(0);heading.Children.Add(title);PageHeader.Children.Add(heading);
        var toolbar=new DockPanel();catalogToolbar=toolbar;
        var filters=inlineCatalogFilters=new StackPanel{Orientation=Orientation.Horizontal};
        foreach(var (key,label) in new[]{("","Все"),("Фильмы","Фильмы"),("Сериалы","Сериалы")})
        {
            var total=saved.Count(item=>key.Length==0||item.Section==key);
            var button=Button(label+" · "+total,()=>{savedCategory=key;savedPage=1;Render();});button.Style=(Style)FindResource("PillButton");
            button.SetResourceReference(Control.BackgroundProperty,savedCategory==key?"Selected":"Panel");
            AutomationProperties.SetName(button,"Сохранённое: "+label);filters.Children.Add(button);
        }
        inlineCatalogFilterScroll=new ScrollViewer{Content=filters,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,CanContentScroll=false};
        inlineCatalogFilterScroll.ScrollChanged+=(_,_)=>UpdateCatalogFilterOverflow();inlineCatalogFilterScroll.SizeChanged+=(_,_)=>UpdateFilterRail();
        inlineCatalogFilterScroll.PreviewMouseWheel+=(_,args)=>{if(inlineCatalogFilterScroll is not {} scroll||scroll.ScrollableWidth<=0)return;scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset-args.Delta/120d*96);args.Handled=true;};
        catalogFilterBack=ActionButton("","IconBack",()=>ScrollCatalogFilters(-1),"QuietButton");catalogFilterBack.Width=30;catalogFilterBack.Padding=new(6);catalogFilterBack.Margin=new(0,0,4,0);catalogFilterBack.ToolTip="Предыдущие фильтры";AutomationProperties.SetName(catalogFilterBack,"Фильтры: назад");DockPanel.SetDock(catalogFilterBack,Dock.Left);toolbar.Children.Add(catalogFilterBack);
        catalogFilterForward=ActionButton("","IconChevron",()=>ScrollCatalogFilters(1),"QuietButton");catalogFilterForward.Width=30;catalogFilterForward.Padding=new(6);catalogFilterForward.Margin=new(4,0,0,0);catalogFilterForward.ToolTip="Следующие фильтры";AutomationProperties.SetName(catalogFilterForward,"Фильтры: вперёд");DockPanel.SetDock(catalogFilterForward,Dock.Right);toolbar.Children.Add(catalogFilterForward);
        toolbar.Children.Add(inlineCatalogFilterScroll);FilterControls.Children.Add(toolbar);UpdateFilterRail();
        ShowCatalog(filtered.Skip((savedPage-1)*CatalogPaging.Size).Take(CatalogPaging.Size).ToArray());
        var list=catalogList!;Body.Children.Remove(list);ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Disabled);ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);WheelScroll.SetIsEnabled(list,false);
        var content=new StackPanel();content.Children.Add(list);
        if(filtered.Length==0)
        {
            var empty=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=380,Margin=new(20,28,20,20)};
            var icon=IconLabel("","IconHeart",34);icon.HorizontalAlignment=HorizontalAlignment.Center;icon.Margin=new(0,0,0,16);empty.Children.Add(icon);
            foreach(var shape in VisualElements<System.Windows.Shapes.Path>(icon)){System.Windows.Data.BindingOperations.ClearBinding(shape,System.Windows.Shapes.Shape.StrokeProperty);shape.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"Muted");}
            var label=Text(saved.Length==0?"Сохрани историю на потом":savedCategory=="Фильмы"?"Сохранённых фильмов пока нет":"Сохранённых сериалов пока нет",22);label.FontWeight=FontWeights.SemiBold;label.TextAlignment=TextAlignment.Center;empty.Children.Add(label);
            var hint=Text("Нажми «Сохранить» в карточке фильма или сериала — он появится здесь.",13,true);hint.TextAlignment=TextAlignment.Center;empty.Children.Add(hint);content.Children.Add(empty);
        }
        if(pageCount>1)
        {
            var pages=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center,Margin=new(0,16,8,18)};
            void Page(string label,int page,bool enabled)
            {
                var button=Button(label,()=>GoSavedPage(page));button.Style=(Style)FindResource("PillButton");button.IsEnabled=enabled;button.MinWidth=36;button.Margin=new(2);button.Padding=new(10,7,10,7);
                if(page==savedPage&&int.TryParse(label,out _))button.SetResourceReference(Control.BackgroundProperty,"Selected");
                AutomationProperties.SetName(button,int.TryParse(label,out _)?"Сохранённое: страница "+page:"Сохранённое: "+label);pages.Children.Add(button);
            }
            Page("Назад",savedPage-1,savedPage>1);foreach(var page in CatalogPaging.Numbers(savedPage,pageCount))Page(page.ToString(),page,true);Page("Далее",savedPage+1,savedPage<pageCount);content.Children.Add(pages);
        }
        var scroll=savedScroll=new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Body.Children.Add(scroll);
        if(restoreSavedOffset is {} offset)
        {
            restoreSavedOffset=null;scroll.Loaded+=(_,_)=>Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{if(ReferenceEquals(savedScroll,scroll))scroll.ScrollToVerticalOffset(offset);}));
        }
    }
}
