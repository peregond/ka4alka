using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Kachalka;
public partial class MainWindow
{
    public async Task CatalogPagingSmokeTest(string output)
    {
        Directory.CreateDirectory(output);liveRequest?.Cancel();searchDelay.Stop();
        section="Фильмы";Search.Text="";searchDelay.Stop();current=null;favoritesOnly=false;ResetCatalogFilters();livePage=1;liveKey="";
        var rows=Enumerable.Range(1,120).Select(id=>new MediaItem(id,"Фильм "+id,"Фильмы",id%2==0?"комедия":"драма",2024,"8.2","8.0","#526B69")
            {GenreKeys=[id%2==0?"komediia":"drama"],CountryKeys=[id%3==0?"rossiia":"ssha"],Country=id%3==0?"Россия":"США"}).ToArray();
        foreach(var selection in new[]{new CatalogSelection(),new CatalogSelection(Genre:"komediia"),new CatalogSelection(Genre:"komediia",Country:"ssha"),new CatalogSelection(Genre:"komediia",Country:"ssha",Rating:8),new CatalogSelection(Collection:"popular"),new CatalogSelection(Collection:"rated")})
        {
            var filtered=rows.Where(selection.Matches).ToArray();
            for(var page=1;page<=Math.Max(1,(filtered.Length+39)/40);page++)
                catalogPages["Фильмы|"+selection.Filter+"|"+page]=new(filtered.Skip((page-1)*40).Take(40).ToArray(),page*40<filtered.Length,CatalogChoices.Genres,CatalogChoices.Countries);
        }
        foreach(var row in rows)cardMetadata[row.Id]=Task.FromResult(row);
        async Task Settle(){var end=DateTime.UtcNow.AddSeconds(15);while(liveLoading&&DateTime.UtcNow<end)await Task.Delay(30);await Task.Delay(100);UpdateLayout();if(liveLoading)throw new Exception("Catalog fixture timed out");}
        void Check(bool ok,string label){if(!ok)throw new Exception(label);File.AppendAllText(Path.Combine(output,"checks.txt"),"PASS: "+label+Environment.NewLine);}
        void CheckCards(string stage,int minimumColumns)
        {
            var cards=VisualElements<Button>(catalogList!).Where(x=>x.IsVisible&&x.Tag is MediaItem).ToArray();
            Check(cards.Length>0,stage+": catalog realizes visible poster cards");
            var bounds=cards.Select(x=>x.TransformToAncestor(Body).TransformBounds(new Rect(new Point(),x.RenderSize))).ToArray();
            var firstRow=bounds.Count(x=>Math.Abs(x.Top-bounds.Min(b=>b.Top))<1);
            Check(catalogColumns>=minimumColumns&&firstRow>=minimumColumns,$"{stage}: dense catalog has {firstRow} cards in a row ({catalogColumns} columns)");
            Check(bounds.All(x=>x.Width>0&&x.Left>=-.5&&x.Right<=Body.ActualWidth+1),stage+": poster cards stay within the content width");
            Check(VisualElements<UIElement>(catalogList!).All(x=>x.Effect==null),stage+": poster cards have no blur or shadow effects");
        }
        Button Page(int page)=>FindVisual<Button>(Body,b=>AutomationProperties.GetName(b)=="Страница "+page)??throw new Exception("Missing page "+page);
        void Choose(string name,string key)
        {
            var button=FindVisual<Button>(RootGrid,x=>AutomationProperties.GetName(x)==name)??throw new Exception("Missing filter "+name);
            var option=button.ContextMenu!.Items.OfType<MenuItem>().Single(x=>x.Tag?.ToString()==key);
            option.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        // Exercise desktop layouts even on the hosted runner's smaller virtual monitor.
        MaxWidth=1800;MaxHeight=1000;MinWidth=1280;MinHeight=300;
        Width=1280;Height=800;Render();await Settle();
        Check(liveItems.Count==40&&catalogDisplay.Count==40,"one catalog page displays 40 cards");
        CheckCards("desktop",6);
        CheckCatalogFilterLine(Check,"desktop");
        Check(inlineCatalogFilters!.Children.Contains(topSaved!),"catalog filters share one line with All and Saved below search");
        Check(!VisualElements<TextBlock>(PageHeader).Any(x=>x.Text=="Настроить подборку"),"no redundant selection heading");
        var genreMenuButton=FindVisual<Button>(RootGrid,x=>AutomationProperties.GetName(x)=="Жанр")!;
        genreMenuButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(100);
        Check(genreMenuButton.ContextMenu is {IsOpen:true,ActualHeight:>0},"genre pill opens a visible dropdown menu");genreMenuButton.ContextMenu!.IsOpen=false;
        Check(!VisualElements<Button>(Body).Any(b=>b.Content?.ToString()?.Contains("Показать ещё")==true),"catalog has no load-more button");
        prefs.LiveFavorites=[];topSaved!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();
        Check(favoritesOnly&&catalogDisplay.Count==0,"empty Saved section is shown without catalog rows");
        Check(VisualElements<System.Windows.Shapes.Path>(topSaved!).Any(x=>x.Fill is SolidColorBrush brush&&brush.Color==Color.FromRgb(224,79,98)),"active Saved heart is filled red");
        var emptyReset=FindVisual<Button>(Body,x=>AutomationProperties.GetName(x)=="Сбросить фильтры")??throw new Exception("Missing reset button in empty Saved");
        emptyReset.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();
        Check(!favoritesOnly&&CatalogSelection.IsDefault&&livePage==1&&catalogDisplay.Count==40,"empty Saved reset returns to All with its first page");
        Check(VisualElements<System.Windows.Shapes.Path>(topSaved!).All(x=>x.Fill==null),"Saved heart returns to outline in All");
        var scroll=FindVisual<ScrollViewer>(Body,_=>true)!;
        var first=liveItems.Select(x=>x.Id).ToArray();
        var poster=VisualElements<Button>(catalogList!).First(x=>x.Tag is MediaItem);
        var wheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=Mouse.PreviewMouseWheelEvent};poster.RaiseEvent(wheel);await Task.Delay(250);UpdateLayout();
        Check(wheel.Handled&&scroll.VerticalOffset>0,"wheel scrolls cards within paged catalog");
        scroll.ScrollToEnd();await Task.Delay(100);UpdateLayout();
        Check(Page(2).TransformToAncestor(scroll).Transform(new Point()).Y>=0&&Page(2).TransformToAncestor(scroll).Transform(new Point()).Y<scroll.ViewportHeight,"page navigation is reachable below the cards");
        Page(2).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();
        Check(livePage==2&&liveItems.Count==40&&!liveItems.Select(x=>x.Id).Intersect(first).Any(),"page 2 replaces page 1 without overlapping cards");
        Check(FindVisual<ScrollViewer>(Body,_=>true)!.VerticalOffset<1,"page change returns to top");
        Page(1).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();Check(liveItems.Select(x=>x.Id).SequenceEqual(first),"returning to page 1 restores its cards");
        Choose("Жанр","komediia");await Settle();Check(livePage==1&&liveItems.All(x=>x.GenreKeys.Contains("komediia")),"genre filter resets pagination and applies to source selection");
        Choose("Страна","ssha");await Settle();Check(liveItems.Count==40&&liveItems.All(x=>x.CountryKeys.Contains("ssha")),"country and genre filters combine");
        Choose("Рейтинг от","8");await Settle();Check(liveItems.Count==40&&catalogRating==8,"rating threshold combines with genre and country");
        ResetCatalogFilters();liveKey="";Render();await Settle();Choose("Подборка","popular");await Settle();Check(catalogCollection=="popular"&&CatalogSelection.Filter=="","popular collection uses public source order");
        Choose("Подборка","rated");await Settle();Check(CatalogSelection.Filter=="rating-8/sort-rating","high-rating collection uses source rating threshold");
        ResetCatalogFilters();prefs.LiveFavorites=[rows[0] with{Kinopoisk="6.0"},rows[1] with{Kinopoisk="9.0"},rows[2] with{Kinopoisk="8.0"}];
        topSaved!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();
        Choose("Порядок","По рейтингу");await Settle();Check(catalogDisplay.Select(x=>x.Id).SequenceEqual([2,3,1]),"saved cards sort by real rating");
        ResetCatalogFilters();topAll!.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();Check(Page(2).IsEnabled&&catalogHasNext,"returning from saved cards restores catalog pagination");
        var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,"catalog.png")))png.Save(file);
        MinWidth=1600;Width=1600;Height=950;await Task.Delay(150);UpdateLayout();
        CheckCatalogFilterLine(Check,"1600-wide");
        Check(inlineCatalogFilterScroll is {ScrollableWidth:0},"1600-pixel window fits the default filters in one visible line");
        MinWidth=1760;Width=1760;Height=950;await Task.Delay(150);UpdateLayout();
        CheckCatalogFilterLine(Check,"wide");
        Check(discoveryHero is {ActualHeight:>180},"wide catalog retains cinematic banners below the single filter line");
        var wideGenre=FindVisual<Button>(RootGrid,b=>AutomationProperties.GetName(b)=="Жанр")!;
        wideGenre.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(80);UpdateLayout();
        Check(wideGenre.ContextMenu is {IsOpen:true,ActualHeight:>0},"single-line genre dropdown remains interactive after resizing");wideGenre.ContextMenu!.IsOpen=false;
        MinWidth=360;Width=680;Height=500;await Task.Delay(150);UpdateLayout();CheckCatalogFilterLine(Check,"narrow");
        Height=360;await Task.Delay(150);UpdateLayout();CheckCatalogFilterLine(Check,"short");
        Check(Body.ActualHeight>25,"short window preserves space for catalog cards below filters");
        Width=510;await Task.Delay(150);UpdateLayout();CheckCatalogFilterLine(Check,"narrow-short");
        Check(inlineCatalogFilterScroll is {ScrollableWidth:>0,ScrollableHeight:0}&&Body.ActualHeight>25,"overflowing filter choices scroll horizontally without adding rows or hiding the catalog");
        Render();await Settle();
        Check(Equals(FindVisual<Button>(RootGrid,b=>AutomationProperties.GetName(b)=="Год выхода")?.Content,"Год ▾"),"compact filter captions survive a catalog refresh in a narrow window");
        inlineCatalogFilterScroll!.ScrollToRightEnd();await Task.Delay(80);UpdateLayout();
        var narrowQuality=FindVisual<Button>(RootGrid,b=>AutomationProperties.GetName(b)=="Качество каталога")??throw new Exception("Missing single quality menu in narrow catalog.");
        var qualityBounds=narrowQuality.TransformToAncestor(inlineCatalogFilterScroll).TransformBounds(new Rect(new Point(),narrowQuality.RenderSize));
        Check(qualityBounds.Right>0&&qualityBounds.Left<inlineCatalogFilterScroll.ViewportWidth,"horizontal filter scrolling reaches the quality menu");
        narrowQuality.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(80);UpdateLayout();
        Check(narrowQuality.ContextMenu is {IsOpen:true,ActualHeight:>0},"quality menu remains usable after horizontal scrolling");narrowQuality.ContextMenu!.IsOpen=false;
        CheckCards("narrow",1);
        MinWidth=1280;Width=1280;Height=800;await Task.Delay(100);UpdateLayout();
        current=rows[0] with{PageUrl=LiveCatalog.Base+"/movies/fixture-one"};requestedDetails.Add(current.Id);prefs.Favorites.Add(current.Id);
        SourceEntry Release(string id,string resolution,int seeds)=>new(id,"Фильм 1 (2024) "+resolution,"Fixture","https://example.test/"+id,"magnet:?xt=urn:btih:"+new string(id[0],40),null,1024,seeds);
        liveReleases[current.Id]=[Release("a","720p",100),Release("b","1080p",50),Release("c","2160p",1)];Render();UpdateLayout();
        var qualityBox=FindVisual<ComboBox>(Body,x=>AutomationProperties.GetName(x)=="Раздачи: Качество")??throw new Exception("Missing release quality selector in movie detail");
        Check(qualityBox.Items.Cast<string>().SequenceEqual(["Все","HD Ready","Full HD","4K"]),"release quality menu displays friendly resolution names");
        var qualitySort=FindVisual<ComboBox>(Body,x=>AutomationProperties.GetName(x)=="Сортировка раздач")!;
        qualitySort.SelectedItem="Выше качество";UpdateLayout();
        Check(VisualElements<Button>(Body).Select(x=>x.Tag).OfType<SourceEntry>().Select(x=>x.Quality).SequenceEqual(["4K","Full HD","HD Ready"]),"friendly quality names still sort by actual resolution");
        qualityBox.SelectedItem="Full HD";UpdateLayout();
        Check(VisualElements<Button>(Body).Select(x=>x.Tag).OfType<SourceEntry>().Select(x=>x.Quality).SequenceEqual(["Full HD"]),"Full HD filter keeps only matching releases");
        var savedFilm=FindVisual<Button>(RootGrid,x=>AutomationProperties.GetName(x)=="Сохранено")??FindVisual<Button>(Body,x=>AutomationProperties.GetName(x)=="Сохранено");
        Check(savedFilm!=null&&VisualElements<System.Windows.Shapes.Path>(savedFilm).Any(x=>x.Fill!=null),"saved movie detail heart is filled too");
        await QualitySmoke(Check);
        await SearchDownloadsSmoke(output,Check);
        await CheckModernQueueVirtualization(output,Check);
        Close();
    }

    void CheckCatalogFilterLine(Action<bool,string> check,string stage)
    {
        check(RootGrid.ColumnDefinitions.Count==2&&FiltersPanel.IsVisible&&ReferenceEquals(FiltersPanel.Parent,HeaderArea)&&Grid.GetRow(FiltersPanel)==1,"catalog filters occupy the header below search without a right column ("+stage+")");
        check(inlineCatalogFilters is StackPanel {Orientation:Orientation.Horizontal}&&ReferenceEquals(inlineCatalogFilterScroll?.Parent,catalogToolbar)&&ReferenceEquals(catalogToolbar?.Parent,FilterControls),"catalog filters remain one horizontal line ("+stage+")");
        var searchBounds=SearchBar.TransformToAncestor(RootGrid).TransformBounds(new Rect(new Point(),SearchBar.RenderSize));
        var filterBounds=FiltersPanel.TransformToAncestor(RootGrid).TransformBounds(new Rect(new Point(),FiltersPanel.RenderSize));
        check(filterBounds.Top>=searchBounds.Bottom-.5,"catalog filter line sits below the search input ("+stage+")");
        var buttons=VisualElements<Button>(inlineCatalogFilters!).Where(x=>x.IsVisible).ToArray();
        var centers=buttons.Select(x=>x.TransformToAncestor(inlineCatalogFilters!).Transform(new Point(0,x.ActualHeight/2)).Y).ToArray();
        check(buttons.Length>=8&&buttons.All(x=>x.ActualWidth>0&&x.ActualHeight>0)&&centers.Max()-centers.Min()<=2,"catalog buttons share one row with no wrapping ("+stage+")");
        check(inlineCatalogFilterScroll is {VerticalScrollBarVisibility:ScrollBarVisibility.Disabled,ScrollableHeight:0},"filter overflow does not create vertical scrolling ("+stage+")");
        check(buttons.Count(x=>AutomationProperties.GetName(x)=="Качество каталога")==1&&!buttons.Any(x=>AutomationProperties.GetName(x) is "Минимальное качество" or "Скрыть плохое качество"),"catalog has one quality dropdown instead of separate quality buttons ("+stage+")");
    }

    async Task CheckModernQueueVirtualization(string output,Action<bool,string> check)
    {
        check(downloads.Items.Count==0&&!downloads.EngineCreated,"queue performance fixture starts without active downloads or a torrent engine");
        var fixtures=Enumerable.Range(1,200).Select(id=>new DownloadItem
        {
            Name=$"Queue.Fixture.{id:000}.1080p.mkv",MediaTitle="Проверка очереди "+id,Paused=true,AddedUtc=DateTime.UtcNow.AddSeconds(-id),
            Stats="25% · на паузе",Progress=25
        }).ToArray();
        try
        {
            foreach(var item in fixtures)downloads.Items.Add(item);
            current=null;section="Загрузки";Width=1280;Height=800;Render();
            await Task.Delay(150);UpdateLayout();
            var queue=FindVisual<ListBox>(Body,x=>AutomationProperties.GetName(x)=="Очередь загрузок")??throw new Exception("Queue performance fixture is missing.");
            check(queue.Items.Count==200&&VirtualizingPanel.GetIsVirtualizing(queue)&&VirtualizingPanel.GetVirtualizationMode(queue)==VirtualizationMode.Recycling&&ScrollViewer.GetCanContentScroll(queue),"200-item download queue keeps native recycling virtualization enabled");
            var scroller=FindVisual<ScrollViewer>(queue,_=>true)??throw new Exception("Queue scrolling fixture is missing.");
            scroller.ScrollToBottom();await Task.Delay(150);UpdateLayout();
            var containers=VisualElements<ListBoxItem>(queue).Count();
            check(containers>0&&containers<40,$"200-item download queue realizes only {containers} native row containers");
            check(queue.ItemContainerGenerator.ContainerFromIndex(199) is ListBoxItem,"virtualized queue can scroll to its final download");
            check(VisualElements<UIElement>(queue).All(x=>x.Effect==null),"download rows have no blur or shadow effects");
            check(!downloads.EngineCreated&&fixtures.All(x=>x.Paused),"queue scrolling and styling do not create an engine or start paused downloads");
            File.WriteAllText(Path.Combine(output,"queue-virtualization.json"),JsonSerializer.Serialize(new{Items=200,RealizedContainers=containers,Recycling=true,LastDownloadReachable=true,Effects=false,EngineCreated=downloads.EngineCreated}));
        }
        finally
        {
            foreach(var item in fixtures)downloads.Items.Remove(item);
            current=null;section="Загрузки";Render();
        }
    }
}
