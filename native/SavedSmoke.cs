using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Kachalka;
public partial class MainWindow
{
    void CheckSidebarFooter(Action<bool,string> check,string stage)
    {
        var controls=new[]{SavedButton,DownloadsButton,SettingsButton,SidebarUpdateButton}.Where(button=>button.IsVisible).ToArray();
        var bounds=controls.Select(button=>button.TransformToAncestor(SidebarContent).TransformBounds(new Rect(new Point(),button.RenderSize))).ToArray();
        check(bounds.Length>=3&&bounds.All(box=>box.Width>0&&box.Height>0&&box.Left>=-.5&&box.Right<=SidebarContent.ActualWidth+.5&&box.Top>=-.5&&box.Bottom<=SidebarContent.ActualHeight+.5),"sidebar actions fit the visible panel ("+stage+")");
        check(bounds.Zip(bounds.Skip(1),(previous,next)=>previous.Bottom<=next.Top+.5).All(ok=>ok),"Saved, Downloads, Settings and Update have separate ordered rows ("+stage+")");
        var library=SidebarLibrary.TransformToAncestor(SidebarContent).TransformBounds(new Rect(new Point(),SidebarLibrary.RenderSize));
        var footerTop=SidebarFooter.TransformToAncestor(SidebarContent).Transform(new Point()).Y;
        check(library.Bottom<=footerTop+.5&&SidebarLibrary.ClipToBounds,"library scrolling stays above the sidebar footer ("+stage+")");
        foreach(var button in Navigation.Children.OfType<Button>())
        {
            var box=button.TransformToAncestor(SidebarLibrary).TransformBounds(new Rect(new Point(),button.RenderSize));
            check(box.Top>=-.5&&box.Bottom<=SidebarLibrary.ActualHeight+.5,"catalog navigation remains fully visible beside the footer: "+button.Tag+" ("+stage+")");
        }
    }
    async Task SavedSmoke(Action<bool,string> check)
    {
        var previousFavorites=prefs.Favorites;var previousCards=prefs.LiveFavorites;
        var previousSection=section;var previousCurrent=current;var previousQuery=submittedQuery;var previousSearch=Search.Text;var previousSearchCategory=searchCategory;
        var previousSavedCategory=savedCategory;var previousSavedPage=savedPage;var previousProvider=searchProvider;var previousPeopleProvider=peopleSearchProvider;var previousHeight=Height;
        var person=new CinemaPerson("Участник сохранённого фильма","Актёры","","https://kino-teatr.ua/ru/person/saved-navigation-smoke-930001.phtml");
        var films=Enumerable.Range(1,45).Select(id=>new MediaItem(-71000-id,"Сохранённый фильм "+id,"Фильмы","драма",2025,"8.0","7.5","#526B69")
            {PageUrl=LiveCatalog.Base+"/movies/saved-navigation-smoke-"+id,Description="Описание сохранённого фильма.",People=id<=5?[person]:[]}).ToArray();
        var series=Enumerable.Range(1,5).Select(id=>new MediaItem(-72000-id,"Сохранённый сериал "+id,"Сериалы","драма",2025,"8.0","7.5","#526B69")
            {PageUrl=LiveCatalog.Base+"/tvseries/saved-navigation-smoke-"+id,Description="Описание сохранённого сериала."}).ToArray();
        string State()=>$"section={section}, current={current?.Id}, person={activePerson?.Name}, category={savedCategory}, page={savedPage}, cards={catalogDisplay.Count}, scroll={savedScroll?.VerticalOffset}, restore={navigationRestore?.Status}, pendingOffset={navigationPendingPosition?.Offset}";
        Button Control(DependencyObject root,Func<Button,bool> predicate,string label)=>FindVisual<Button>(root,predicate)??throw new Exception("Missing Saved control: "+label+"; "+State());
        Button Filter(string name)=>Control(FilterControls,button=>AutomationProperties.GetName(button)=="Сохранённое: "+name,"filter "+name);
        ScrollViewer Scroll()=>savedScroll??throw new Exception("Saved scroll viewer is missing; "+State());
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        async Task Settle()
        {
            UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            await Task.Delay(80);UpdateLayout();
            await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();
        }
        try
        {
            prefs.Favorites=[];prefs.LiveFavorites=[];SavedButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
            check(section=="Сохранённое"&&catalogDisplay.Count==0&&FindVisual<TextBlock>(Body,text=>text.Text=="Сохрани историю на потом")!=null,"sidebar Saved opens its own empty gallery with an explanation");
            prefs.LiveFavorites=films.Concat(series).ToList();prefs.Favorites=prefs.LiveFavorites.Select(item=>item.Id).ToHashSet();
            foreach(var item in prefs.LiveFavorites){cardMetadata[item.Id]=Task.FromResult(item);requestedDetails.Add(item.Id);liveReleases[item.Id]=[];}
            savedCategory="";savedPage=1;Render();await Settle();
            check(catalogDisplay.Count==40&&catalogDisplay.Any(item=>item.Section=="Фильмы")&&catalogDisplay.Any(item=>item.Section=="Сериалы"),"unified Saved shows films and series together on a paged gallery");
            check(new[]{"Все","Фильмы","Сериалы"}.All(name=>Filter(name).IsVisible)&&FiltersPanel.IsVisible,"Saved exposes all, film and series filters below the persistent search");
            Click(Filter("Сериалы"));await Settle();check(catalogDisplay.Count==5&&catalogDisplay.All(item=>item.Section=="Сериалы"),"Saved series filter retains saved series from the same collection");
            Click(Filter("Фильмы"));await Settle();
            Click(Control(Body,button=>AutomationProperties.GetName(button)=="Сохранённое: страница 2","page 2"));await Settle();
            check(savedPage==2&&catalogDisplay.Count==5&&catalogDisplay.All(item=>item.Section=="Фильмы"),"Saved film filter preserves its own second page");
            Height=Math.Max(MinHeight,420);await Settle();
            var selected=catalogDisplay[0];Scroll().ScrollToVerticalOffset(80);await Settle();var previousOffset=Scroll().VerticalOffset;
            check(previousOffset>0,"Saved navigation fixture has a real scroll position to restore");
            var biography="Биография участника для проверки возврата в Сохранённое.";
            Directory.CreateDirectory(Path.GetDirectoryName(CinemaPeople.ProfileCachePath(person))!);
            await File.WriteAllTextAsync(CinemaPeople.ProfileCachePath(person),JsonSerializer.Serialize(new PersonProfile(person,biography,films.Take(2).ToArray(),person.ProfileUrl)));
            var professional=new ProfessionalPerson(person,biography,"https://kino-teatr.ua/public/main/persons/person-search-smoke.jpg",person.ProfileUrl!,"Kino-Teatr.ua",[],[person.Name]);
            foreach(var origin in new MediaItem?[]{null,selected,films[0]}.DistinctBy(item=>item?.Id))await File.WriteAllTextAsync(ProfessionalCinemaPeople.CachePath(person,origin),JsonSerializer.Serialize(professional));
            Click(Control(Body,button=>button.Tag is MediaItem item&&item.Id==selected.Id,"saved film "+selected.Id));await Settle();
            check(current?.Id==selected.Id&&SavedBackLabel()=="Сохранённое"&&savedReturn is{Category:"Фильмы",Page:2},"opening a saved film records its collection, filter and page as the back destination");
            Click(Control(HeaderArea,button=>AutomationProperties.GetName(button)=="Сохранённое","back to Saved"));await Settle();
            check(section=="Сохранённое"&&current==null&&savedCategory=="Фильмы"&&savedPage==2&&Math.Abs(Scroll().VerticalOffset-previousOffset)<2,"film back restores the Saved filter, page and scroll position");
            Click(Control(Body,button=>button.Tag is MediaItem item&&item.Id==selected.Id,"saved film "+selected.Id));await Settle();
            Click(Control(Body,button=>button.Tag is CinemaPerson actor&&actor==person,"saved film participant"));await Settle();
            check(activePerson==person&&personOrigin?.Id==selected.Id&&savedReturn!=null,"a person opened from a saved film retains the Saved navigation destination");
            Click(SettingsButton);await Settle();
            check(section=="Настройки"&&activePerson==null&&current==null,"settings opens from the saved film's person page");
            Click(Control(HeaderArea,button=>AutomationProperties.GetName(button)=="Вернуться","settings back to participant"));await Settle();
            check(activePerson==person&&personOrigin?.Id==selected.Id&&current?.Id==selected.Id&&savedReturn is{Category:"Фильмы",Page:2},"settings back restores the same person, origin film and Saved ancestor");
            var deadline=DateTime.UtcNow.AddSeconds(3);while(FindVisual<Button>(Body,button=>button.Tag is MediaItem film&&film.Id==films[0].Id)==null&&DateTime.UtcNow<deadline)await Settle();
            Click(Control(Body,button=>button.Tag is MediaItem film&&film.Id==films[0].Id,"participant filmography fixture"));await Settle();
            check(returnPerson?.Person==person&&savedReturn!=null,"person filmography opens another film without losing its Saved ancestor");
            Click(SettingsButton);await Settle();
            Click(Control(HeaderArea,button=>AutomationProperties.GetName(button)=="Вернуться","settings back to filmography film"));await Settle();
            check(current?.Id==films[0].Id&&activePerson==null&&returnPerson?.Person==person&&returnPerson?.Origin?.Id==selected.Id&&savedReturn is{Category:"Фильмы",Page:2},"settings back preserves the filmography film's person return destination and Saved ancestor");
            CinemaBack();await Settle();check(activePerson==person&&personOrigin?.Id==selected.Id,"back from filmography restores the saved film's person page first");
            Click(Control(HeaderArea,button=>AutomationProperties.GetName(button)=="Назад к фильму","participant back to film"));await Settle();
            check(current?.Id==selected.Id&&activePerson==null,"person back restores its saved origin film");
            Click(Control(Body,button=>button.Name=="DetailFavorite","film favorite action"));await Settle();
            check(!prefs.Favorites.Contains(selected.Id)&&!prefs.LiveFavorites.Any(item=>item.Id==selected.Id)&&!Preferences.Load().LiveFavorites.Any(item=>item.Id==selected.Id),"removing a saved film synchronizes its ID, card and persisted collection");
            CinemaBack();await Settle();
            check(section=="Сохранённое"&&savedPage==2&&catalogDisplay.Count==4&&!catalogDisplay.Any(item=>item.Id==selected.Id),"returning after removal refreshes the same Saved page without its deleted card");
            int searchCalls=0;searchProvider=(kind,query,token)=>{searchCalls++;return Task.FromResult<IReadOnlyList<MediaItem>>(kind=="Фильмы"?films:series);};peopleSearchProvider=(query,token)=>Task.FromResult<IReadOnlyList<CinemaPerson>>([]);
            Search.Text="Поиск из сохранённого";FocusCatalogSearch();await Settle();
            check(section=="Сохранённое"&&Search.IsKeyboardFocusWithin&&searchCalls==0,"the search shortcut focuses Saved search without leaving the collection or sending a request");
            Click(ClearSearchButton);await Settle();check(section=="Сохранённое"&&savedPage==2&&catalogDisplay.Count==4&&Search.Text=="","clearing search preserves the Saved filter and page");
            Search.Text="Поиск из сохранённого";Click(SearchSubmitButton);deadline=DateTime.UtcNow.AddSeconds(3);while((liveLoading||PeopleSearchLoading)&&DateTime.UtcNow<deadline)await Task.Delay(30);await Settle();
            check(section is "Фильмы" or "Сериалы"&&current==null&&SearchActive&&searchCategory==""&&searchCalls==2,"submitting from Saved opens the existing unified film and series search");
        }
        finally
        {
            prefs.Favorites=previousFavorites;prefs.LiveFavorites=previousCards;prefs.Save();searchProvider=previousProvider;peopleSearchProvider=previousPeopleProvider;
            savedCategory=previousSavedCategory;savedPage=previousSavedPage;savedReturn=null;current=previousCurrent;activePerson=null;personSearchReturn=null;returnPerson=null;section=previousSection;
            submittedQuery=previousQuery;searchCategory=previousSearchCategory;Search.Text=previousSearch;Height=previousHeight;Render();
        }
    }
}
