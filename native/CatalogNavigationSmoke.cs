using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kachalka;

public partial class MainWindow
{
    internal Func<CinemaPerson,MediaItem?,MediaItem[],CancellationToken,Action<PersonProfile>?,Task<PersonProfile>>? personProfileProvider;
    public async Task CatalogNavigationSmokeTest(string output)
    {
        Directory.CreateDirectory(output);await Task.Delay(180);
        liveRequest?.Cancel();liveLoading=false;searchDelay.Stop();catalogRefreshTimer.Stop();
        var checks=new List<string>();
        void Check(bool ok,string message){if(!ok)throw new Exception(message);checks.Add(message);}
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        async Task Settle(){await Task.Delay(130);UpdateLayout();await Task.Delay(70);UpdateLayout();}
        ScrollViewer Scroll()=>FindVisual<ScrollViewer>(Body,viewer=>viewer.VerticalScrollBarVisibility!=ScrollBarVisibility.Disabled)??throw new Exception("Navigation fixture has no page scroll viewer.");
        Button Back(string name)=>FindVisual<Button>(PageHeader,button=>AutomationProperties.GetName(button)==name)??throw new Exception("Missing back action: "+name);
        Button Card(int id)=>FindVisual<Button>(Body,button=>button.Tag is MediaItem film&&film.Id==id)??throw new Exception("Missing film card: "+id);
        void Position(CatalogScrollPosition expected,string message)
        {
            var viewer=Scroll();
            if(expected.ItemId is not {} id){Check(Math.Abs(viewer.VerticalOffset-expected.Offset)<2,message);return;}
            var button=VisualElements<Button>(viewer).FirstOrDefault(button=>button.Tag is MediaItem item&&item.Id==id&&CatalogAnchorScope(button,viewer)==expected.Scope)??throw new Exception(message+": anchor disappeared.");
            var top=button.TransformToAncestor(viewer).Transform(new Point()).Y;
            var expectedTop=Math.Clamp(expected.Top,-Math.Max(0,button.ActualHeight-24),Math.Max(0,viewer.ViewportHeight-24));
            Check(Math.Abs(top-expectedTop)<2,message+" (title "+id+", delta "+Math.Round(top-expectedTop,2)+")");
        }
        void Shot(string name)
        {
            var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name+".png"));encoder.Save(stream);
        }
        var person=new CinemaPerson("Участник проверки навигации","Актёры","","https://kino-teatr.ua/ru/person/navigation-fixture-937001.phtml");
        var films=Enumerable.Range(1,80).Select(number=>new MediaItem(-937000-number,"Проверочная история "+number,
            number%2==0?"Сериалы":"Фильмы","драма",2025,"8.1","7.4","#526B69")
        {PageUrl=LiveCatalog.Base+(number%2==0?"/tvseries/":"/movies/")+"navigation-fixture-"+number,
            Description=string.Join(" ",Enumerable.Repeat("Описание для проверки сохранения места в карточке фильма.",45)),
            GenreKeys=["drama"],CountryKeys=["russia"],People=[person]}).ToArray();
        foreach(var film in films){requestedDetails.Add(film.Id);liveReleases[film.Id]=[];cardMetadata[film.Id]=Task.FromResult(film);}
        var biography=string.Join(" ",Enumerable.Repeat("Биография участника проверки навигации.",20));
        Directory.CreateDirectory(Path.GetDirectoryName(CinemaPeople.ProfileCachePath(person))!);
        await File.WriteAllTextAsync(CinemaPeople.ProfileCachePath(person),JsonSerializer.Serialize(new PersonProfile(person,biography,films,person.ProfileUrl)));
        var photoUrl="https://kino-teatr.ua/public/main/persons/navigation-fixture.jpg";
        var professional=new ProfessionalPerson(person,biography,photoUrl,person.ProfileUrl!,"Kino-Teatr.ua",[],[person.Name]);
        MediaItem?[] portraitOrigins=[null,..films];
        foreach(var cachedOrigin in portraitOrigins)await File.WriteAllTextAsync(ProfessionalCinemaPeople.CachePath(person,cachedOrigin),JsonSerializer.Serialize(professional));
        var photoDir=Path.Combine(Preferences.DataDir,"portraits");Directory.CreateDirectory(photoDir);
        var photo=new RenderTargetBitmap(20,30,96,96,PixelFormats.Pbgra32);var drawing=new DrawingVisual();using(var context=drawing.RenderOpen())context.DrawRectangle(Brushes.Teal,null,new Rect(0,0,20,30));photo.Render(drawing);
        var photoEncoder=new PngBitmapEncoder();photoEncoder.Frames.Add(BitmapFrame.Create(photo));using(var stream=File.Create(Path.Combine(photoDir,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(photoUrl)))+".img")))photoEncoder.Save(stream);

        designFixedViewport=true;MaxWidth=1800;MaxHeight=1200;MinWidth=620;MinHeight=420;Width=1440;Height=640;await Settle();
        section="Фильмы";lastCatalogSection=section;current=null;activePerson=null;returnPerson=null;savedReturn=null;
        submittedQuery="Проверочная история";Search.Text=submittedQuery;searchCategory="";livePage=2;favoritesOnly=false;
        ResetCatalogFilters();catalogGenre="drama";catalogCountry="russia";catalogYear=2025;catalogRating=7;catalogOrder="По рейтингу";
        prefs.CatalogQualityHeight=0;liveItems=films;liveKey=CurrentCatalogKey;liveError="";
        peopleSearchProvider=(_,_)=>Task.FromResult<IReadOnlyList<CinemaPerson>>([]);peopleSearchResolvedQuery=peopleSearchQuery=PersonSearchTerm(submittedQuery);
        Render();await Settle();Scroll().ScrollToVerticalOffset(600);await Settle();
        var catalogPosition=CaptureCatalogPosition()??throw new Exception("No catalog position.");
        Check(catalogPosition.Offset>0&&catalogPosition.ItemId!=null,"combined search's second filtered page has a real title anchor");
        var selected=VisualElements<Button>(Scroll()).Where(button=>button.Tag is MediaItem{Section:"Сериалы"}&&button.ActualHeight>0)
            .First(button=>{var bounds=button.TransformToAncestor(Scroll()).TransformBounds(new Rect(new Point(),button.RenderSize));return bounds.Bottom>0&&bounds.Top<Scroll().ViewportHeight;});
        var origin=(MediaItem)selected.Tag;Click(selected);await Settle();
        Check(current?.Id==origin.Id&&section=="Сериалы","a series opens from a combined search whose navigation section was Films");
        Scroll().ScrollToVerticalOffset(170);await Settle();var detailPosition=CaptureCatalogPosition()!;
        Check(detailPosition.Offset>0,"movie detail has a reading position before opening a participant");
        Click(FindVisual<Button>(Body,button=>button.Tag is CinemaPerson participant&&participant==person)!);
        var deadline=DateTime.UtcNow.AddSeconds(8);while((FindVisual<ItemsControl>(Body,control=>control.Name=="PersonFilmographyGrid")?.Items.Count??0)<2&&DateTime.UtcNow<deadline)await Task.Delay(40);
        await Settle();Check(activePerson==person&&personOrigin?.Id==origin.Id,"the participant page retains its origin series");
        Scroll().ScrollToVerticalOffset(1000);await Settle();var personPosition=CaptureCatalogPosition()!;
        Check(personPosition.Offset>0&&personPosition.ItemId!=null,"participant filmography has a real title anchor");
        Click(Card(personPosition.ItemId!.Value));await Settle();
        var delayedProfile=new TaskCompletionSource<PersonProfile>(TaskCreationOptions.RunContinuationsAsynchronously);
        personProfileProvider=(_,_,_,token,_)=>delayedProfile.Task.WaitAsync(token);
        CinemaBack();await Settle();
        Check(activePerson==person&&navigationPendingPosition?.ItemId==personPosition.ItemId&&
            (FindVisual<ItemsControl>(Body,control=>control.Name=="PersonFilmographyGrid")?.Items.Count??0)==0,
            "participant return keeps its title anchor while the actual asynchronous profile is deliberately withheld");
        delayedProfile.SetResult(new PersonProfile(person,biography,films,person.ProfileUrl));await Settle();personProfileProvider=null;
        Position(personPosition,"filmography film back restores the participant's same visible title");
        Click(Card(personPosition.ItemId!.Value));await Settle();
        var partialProfile=new PersonProfile(person,biography,films.Where(film=>film.Id!=personPosition.ItemId).Take(2).ToArray(),person.ProfileUrl);
        var settledPartial=new TaskCompletionSource<PersonProfile>(TaskCreationOptions.RunContinuationsAsynchronously);
        personProfileProvider=(_,_,_,token,progress)=>{progress?.Invoke(partialProfile);return settledPartial.Task.WaitAsync(token);};
        CinemaBack();await Settle();
        Check(personProfileLoading&&navigationPendingPosition?.ItemId==personPosition.ItemId,"a partial profile missing the previously visible title keeps the pending anchor");
        var partialExtent=Scroll().ExtentHeight;settledPartial.SetResult(partialProfile);await Settle();personProfileProvider=null;
        Check(!personProfileLoading&&navigationPendingPosition==null&&Math.Abs(Scroll().ExtentHeight-partialExtent)<1,
            "final unchanged partial filmography releases its missing-title fallback without requiring a new scroll or extent event");
        Click(Back("Назад к фильму"));await Settle();Position(detailPosition,"participant back restores the origin detail's reading position");
        CinemaBack();await Settle();
        Check(section=="Фильмы"&&current==null&&SearchActive&&searchCategory==""&&livePage==2&&catalogGenre=="drama"&&catalogCountry=="russia"&&catalogYear==2025&&catalogRating==7,
            "series back restores the original combined search, filters and page without changing its navigation section");
        Position(catalogPosition,"film and participant navigation restores the original catalog title");

        Click(SettingsButton);await Settle();Click(Back("Вернуться"));await Settle();Position(catalogPosition,"settings back retains the catalog's visible title");
        Click(DownloadsButton);await Settle();Click(Navigation.Children.OfType<Button>().First(button=>Equals(button.Tag,"Фильмы")));await Settle();
        Check(SearchActive&&livePage==2&&catalogGenre=="drama"&&catalogCountry=="russia","returning from Downloads restores query, page and filters");
        Position(catalogPosition,"returning from Downloads retains the catalog's visible title");

        var beforeResize=CaptureCatalogPosition()!;var wideColumns=catalogColumns;Width=820;await Settle();
        Check(catalogColumns<wideColumns,"the navigation fixture really reflows its poster columns at a smaller window width");
        Position(beforeResize,"resizing the catalog keeps its same title at the viewport edge");Shot("catalog-navigation-narrow");
        var narrowPosition=CaptureCatalogPosition()!;Click(Card(narrowPosition.ItemId!.Value));await Settle();Width=1440;await Settle();CinemaBack();await Settle();
        Position(narrowPosition,"returning from a film after resizing uses its catalog title instead of an obsolete pixel offset");
        var beforeRefresh=CaptureCatalogPosition()!;var previousItems=liveItems;
        liveLoading=true;liveItems=[];Render();await Settle();
        Check(livePage==2&&navigationPendingPosition?.ItemId==beforeRefresh.ItemId,"a temporarily empty asynchronous search retains its second page and pending title anchor");
        liveLoading=false;liveItems=previousItems;Render();await Settle();Position(beforeRefresh,"asynchronous search results restore the title after the interim empty list");
        var beforeInsertionOffset=Scroll().VerticalOffset;var inserted=Enumerable.Range(1,catalogColumns).Select(index=>films[0] with{Id=-939900-index,Title="Новая история после фонового обновления "+index}).ToArray();
        foreach(var additional in inserted)cardMetadata[additional.Id]=Task.FromResult(additional);
        liveItems=films.Take(40).Concat(inserted).Concat(films.Skip(40).Take(40-inserted.Length)).ToArray();Render();await Settle();
        Position(beforeRefresh,"a background catalog refresh inserting a title preserves the current title anchor");
        Check(Scroll().VerticalOffset>beforeInsertionOffset+100,"the refresh inserts a complete poster row and really moves the restored pixel offset");

        var savedCards=prefs.LiveFavorites;var savedIds=prefs.Favorites;
        try
        {
            prefs.LiveFavorites=films.ToList();prefs.Favorites=films.Select(film=>film.Id).ToHashSet();savedCategory="";savedPage=1;
            Click(SavedButton);await Settle();Scroll().ScrollToVerticalOffset(600);await Settle();var savedPosition=CaptureCatalogPosition()!;
            Check(savedPosition.Offset>0&&savedPosition.ItemId!=null,"the Saved fixture has a real title anchor");
            Click(Card(savedPosition.ItemId!.Value));await Settle();Width=820;await Settle();CinemaBack();await Settle();
            Check(section=="Сохранённое"&&savedPage==1,"Saved retains its existing collection return destination");
            Position(savedPosition,"Saved film back after resizing restores its title across a different column count");
        }
        finally{prefs.LiveFavorites=savedCards;prefs.Favorites=savedIds;}

        section="Фильмы";lastCatalogSection=section;current=null;activePerson=null;savedReturn=null;submittedQuery="Проверочная история";
        searchCategory="";livePage=1;ResetCatalogFilters();liveItems=films;Search.Text=submittedQuery;liveKey=CurrentCatalogKey;
        searchProvider=(kind,_,_)=>Task.FromResult<IReadOnlyList<MediaItem>>(films.Where(film=>film.Section==kind).ToArray());
        Render();await Settle();Scroll().ScrollToVerticalOffset(500);await Settle();Check(Scroll().VerticalOffset>0,"repeated search fixture is genuinely scrolled");
        Click(SearchSubmitButton);deadline=DateTime.UtcNow.AddSeconds(5);while(liveLoading&&DateTime.UtcNow<deadline)await Task.Delay(40);await Settle();
        Check(SearchActive&&livePage==1&&Scroll().VerticalOffset<1,"explicitly submitting the same query starts its first page at the top");

        var defaultCards=films.Where(film=>film.Section=="Фильмы").ToArray();
        catalogPages["Фильмы|sort-date|1"]=new(defaultCards,false,CatalogChoices.Genres,CatalogChoices.Countries);
        catalogPages["Фильмы||1"]=new(defaultCards,false,CatalogChoices.Genres,CatalogChoices.Countries);
        PrepareDiscoverySmokeRegions("Фильмы",defaultCards);
        var moviesNavigation=Navigation.Children.OfType<Button>().First(button=>Equals(button.Tag,"Фильмы"));
        Click(moviesNavigation);deadline=DateTime.UtcNow.AddSeconds(5);while(liveLoading&&DateTime.UtcNow<deadline)await Task.Delay(40);await Settle();
        Check(DiscoveryCatalog&&catalogDisplay.Count==40,"default route fixture contains its real discovery shelves and paged catalog");
        FindVisual<TextBlock>(Body,text=>text.Name=="CatalogAllHeading")!.BringIntoView();await Settle();
        Scroll().ScrollToVerticalOffset(Scroll().VerticalOffset+100);await Settle();var discoveryPosition=CaptureCatalogPosition()!;
        Check(discoveryPosition.Offset>0&&discoveryPosition.ItemId!=null&&discoveryPosition.Scope=="catalog","default route is anchored to a title beneath its discovery shelves");
        liveLoading=true;liveItems=[];Render();await Settle();
        Check(Scroll().ScrollableHeight>0&&navigationPendingPosition?.ItemId==discoveryPosition.ItemId,
            "catalog reload retains a missing main-grid anchor even when old discovery shelves leave a scrollable page");
        liveLoading=false;liveItems=defaultCards;Render();await Settle();Position(discoveryPosition,"catalog results restore the main-grid title beneath retained discovery shelves");
        Click(moviesNavigation);deadline=DateTime.UtcNow.AddSeconds(5);while(liveLoading&&DateTime.UtcNow<deadline)await Task.Delay(40);await Settle();
        Check(!SearchActive&&CatalogSelection.IsDefault&&livePage==1&&Scroll().VerticalOffset<1,"repeating Films on the same default route explicitly resets its scroll to the top");
        Scroll().ScrollToVerticalOffset(500);await Settle();var defaultPosition=CaptureCatalogPosition()!;Click(Card(defaultPosition.ItemId!.Value));await Settle();
        Click(moviesNavigation);deadline=DateTime.UtcNow.AddSeconds(5);while(liveLoading&&DateTime.UtcNow<deadline)await Task.Delay(40);await Settle();
        Check(current==null&&Scroll().VerticalOffset<1,"explicit Films navigation from a detail resets the route instead of restoring its previous scroll");
        await File.WriteAllTextAsync(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Checks=checks,RealScrollViewers=true,
            CatalogTitleAnchor=true,ParticipantTitleAnchor=true,SearchFiltersPagePreserved=true,SettingsAndDownloadsReturn=true,
            ResizeAndAsyncRefreshPreserveTitle=true,SavedNavigationPreserved=true},new JsonSerializerOptions{WriteIndented=true}));
        Close();
    }
}
