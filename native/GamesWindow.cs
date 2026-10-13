using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
namespace Kachalka;

public partial class MainWindow
{
    internal Func<GameBrowse,CancellationToken,Task<GameCatalogPage>>? gameCatalogProvider;
    internal Func<MediaItem,CancellationToken,Task<GameDetails>>? gameDetailProvider;
    internal Func<MediaItem,ReleaseSource[]>? gameSourcesProvider;
    // gameShelf: "" is the home page (banner and shelves); a sort (popular, specials, new, soon) opens as a paged list, optionally narrowed by gameGenre.
    string gameQuery="",gameKey="",gameError="",gameShelf="",gameGenre="";int gamePage=1;bool gameLoading,gameHasNext;
    GameBrowse GameRequest=>new(gameQuery,gameShelf.Length>0?gameShelf:GameBrowse.Popular,gameGenre,gamePage);
    MediaItem[] gameItems=[];CancellationTokenSource? gameCatalogRequest,gameDetailRequest;int? gameDetailId;
    readonly Dictionary<int,GameDetails> gameDetails=[];
    readonly Dictionary<int,SourceEntry[]> gameReleaseCache=[];
    TextBlock? gameTitle;Image? gameCover;
    // Capsules keep the 460:215 shape of Steam's own store header.
    void GamePosterSized(object sender,SizeChangedEventArgs args){if(sender is Border border){if(args.WidthChanged&&args.NewSize.Width>0){var height=args.NewSize.Width*215/460;if(double.IsNaN(border.Height)||Math.Abs(border.Height-height)>1)border.Height=height;}ClipPoster(border);}}
    SourceEntry[] gameReleases=[];SourceCheck[] gameSourceChecks=[];bool gameReleasesLoading;
    TextBlock? gameDescription;

    // ---------- home page: banner and shelves ----------
    readonly Dictionary<string,MediaItem[]> gameShelfItems=[];
    readonly HashSet<string> gameShelfLoading=[],gameShelfFailed=[];
    readonly Dictionary<string,DiscoveryShelfView> gameShelfViews=[];
    CancellationTokenSource? gameHomeRequest,gameBannerRequest;
    DateTime gameShelfLoadedUtc;
    Panel? gameFilterRow;
    ScrollViewer? gameGenreScroll;Button? gameGenreBack,gameGenreForward;
    bool GameHome=>section=="Игры"&&current==null&&activePerson==null&&gameQuery.Length==0&&gameShelf.Length==0&&gameGenre.Length==0&&gamePage==1;
    // Games from the download queue, newest first, as the same cards the catalogue shows.
    internal Func<IEnumerable<DownloadItem>>? gameLibrarySource;
    MediaItem[] GameLibrary()=>(gameLibrarySource?.Invoke()??downloads.Items).Where(x=>x.MediaSection=="Игры").OrderByDescending(x=>x.AddedUtc).Select(DownloadMetadata.Card).OfType<MediaItem>().DistinctBy(x=>x.Id).Take(40).ToArray();
    MediaItem[] GameShelfItems(string key)=>gameShelfItems.TryGetValue(key,out var items)?items.Where(PersonallyVisible).ToArray():[];
    void AddGameDetails(GameDetails detail)
    {
        if(!gameDetails.ContainsKey(detail.Item.Id)&&gameDetails.Count>=60)gameDetails.Remove(gameDetails.Keys.First());
        gameDetails[detail.Item.Id]=detail;
    }
    void ShowGames()
    {
        var reset=section=="Игры"&&current==null;
        liveRequest?.Cancel();searchDelay.Stop();CancelPeopleSearch(true);current=null;activePerson=null;returnPerson=null;personSearchReturn=null;savedReturn=null;
        section="Игры";submittedQuery="";favoritesOnly=false;
        if(reset){gameQuery="";gamePage=1;gameKey="";gameShelf="";gameGenre="";ResetCatalogNavigationPosition();}
        Search.Text=gameQuery;Render();
    }
    void SubmitGameSearch()
    {
        gameQuery=Search.Text.Trim();gamePage=1;gameKey="";current=null;activePerson=null;submittedQuery="";ResetCatalogNavigationPosition();Render();
    }
    // A shelf's "Ещё" and the chips open one sort as a paged list, a genre pill narrows it; "" returns to the home page.
    void OpenGameList(string shelf,string genre="")
    {
        if(!SearchActive&&Search.Text.Length>0){Search.Text="";searchDelay.Stop();}
        gameQuery="";gameGenre=genre;gameShelf=shelf.Length==0&&genre.Length>0?GameBrowse.Popular:shelf;gamePage=1;gameKey="";current=null;activePerson=null;ResetCatalogNavigationPosition();Render();
    }
    void RefreshGames()
    {
        gameShelfItems.Clear();gameShelfFailed.Clear();gameShelfLoadedUtc=default;gameKey="";gameError="";Render();
    }
    void CancelGameWork()
    {
        var away=section!="Игры"||current!=null;
        if(away||!GameHome){gameHomeRequest?.Cancel();gameHomeRequest=null;gameShelfLoading.Clear();gameBannerRequest?.Cancel();gameBannerRequest=null;}
        if(away){gameCatalogRequest?.Cancel();gameCatalogRequest=null;gameLoading=false;gameKey="";}
        if(current?.Section!="Игры"||current.Id!=gameDetailId){gameDetailRequest?.Cancel();gameDetailRequest=null;gameDetailId=null;}
        if(current?.Section!="Игры"){gameGalleryRequest?.Cancel();gameGalleryRequest=null;}
    }
    void RenderGames()
    {
        if(GameHome)RenderGameHome();else RenderGameList();
    }
    // Under the search: the sorts as chips (home, popular, specials, new, soon) and a rail of genres to narrow them.
    void BuildGameFilters()
    {
        var stack=new StackPanel{Name="GameFilters"};
        var row=new DockPanel();stack.Children.Add(row);
        var refresh=ActionButton("","IconRefresh",RefreshGames,"PillButton");refresh.Name="GameRefresh";refresh.ToolTip=AppLanguage.T("Обновить каталог");AutomationProperties.SetName(refresh,AppLanguage.T("Обновить каталог"));
        refresh.Padding=new(0);refresh.Width=compactHeight?36:40;refresh.MinHeight=compactHeight?36:40;refresh.VerticalAlignment=VerticalAlignment.Top;refresh.Margin=new(8,0,0,8);DockPanel.SetDock(refresh,Dock.Right);row.Children.Add(refresh);
        var reset=ActionButton(ActualWidth<640?"":"Сбросить всё","IconReset",()=>{gameShelf="";gameGenre="";gamePage=1;gameKey="";ResetCatalogNavigationPosition();Render();},"PillButton");reset.Name="GameResetFilters";reset.ToolTip=AppLanguage.T("Сбросить подборку и жанр. Поиск сохраняется.");AutomationProperties.SetName(reset,AppLanguage.T("Сбросить выбранные фильтры"));if(ActualWidth<640){reset.Padding=new(0);reset.Width=compactHeight?36:40;}reset.MinHeight=compactHeight?36:40;reset.VerticalAlignment=VerticalAlignment.Top;reset.Margin=new(8,0,0,8);DockPanel.SetDock(reset,Dock.Right);row.Children.Add(reset);
        var chips=new WrapPanel();row.Children.Add(chips);
        void Chip(string label,string shelf)
        {
            var active=gameQuery.Length==0&&(shelf.Length==0?gameShelf.Length==0&&gameGenre.Length==0:gameShelf==shelf);
            var button=Button("",()=>OpenGameList(shelf,shelf.Length==0?"":gameGenre));button.Style=(Style)FindResource("PillButton");button.Content=ChipContent(label,false);StyleChip(button,active);
            button.HorizontalContentAlignment=HorizontalAlignment.Center;button.MinHeight=compactHeight?36:40;button.FontSize=14;button.Padding=new(16,0,16,0);button.Margin=new(0,0,8,8);
            button.Name="GameChip"+(shelf.Length==0?"All":shelf);AutomationProperties.SetName(button,AppLanguage.T(label));chips.Children.Add(button);
        }
        Chip("Все","");foreach(var sort in GameBrowse.Sorts)Chip(GameBrowse.SortName(sort),sort);
        stack.Children.Add(BuildGenreRail());
        FilterControls.Children.Add(stack);gameFilterRow=stack;
    }
    // Genres scroll sideways like the film filters do; the arrows appear only when the rail does not fit.
    FrameworkElement BuildGenreRail()
    {
        var rail=new DockPanel{Name="GameGenres"};
        gameGenreBack=ActionButton("","IconBack",()=>ScrollGameGenres(-1),"QuietButton");gameGenreBack.Width=30;gameGenreBack.Padding=new(6);gameGenreBack.Margin=new(0,0,4,8);gameGenreBack.ToolTip=AppLanguage.T("Предыдущие жанры");gameGenreBack.Visibility=Visibility.Collapsed;AutomationProperties.SetName(gameGenreBack,AppLanguage.T("Жанры: назад"));DockPanel.SetDock(gameGenreBack,Dock.Left);rail.Children.Add(gameGenreBack);
        gameGenreForward=ActionButton("","IconChevron",()=>ScrollGameGenres(1),"QuietButton");gameGenreForward.Width=30;gameGenreForward.Padding=new(6);gameGenreForward.Margin=new(4,0,0,8);gameGenreForward.ToolTip=AppLanguage.T("Следующие жанры");gameGenreForward.Visibility=Visibility.Collapsed;AutomationProperties.SetName(gameGenreForward,AppLanguage.T("Жанры: вперёд"));DockPanel.SetDock(gameGenreForward,Dock.Right);rail.Children.Add(gameGenreForward);
        var pills=new StackPanel{Orientation=Orientation.Horizontal};
        foreach(var genre in GameGenres.All)
        {
            var key=genre.Key;var active=gameQuery.Length==0&&gameGenre==key;
            var pill=Button("",()=>OpenGameList(gameShelf,gameGenre==key?"":key));pill.Style=(Style)FindResource("PillButton");pill.Content=ChipContent(genre.Chip,false);StyleChip(pill,active);
            pill.HorizontalContentAlignment=HorizontalAlignment.Center;pill.MinHeight=compactHeight?34:38;pill.FontSize=13;pill.Padding=new(14,0,14,0);pill.Margin=new(0,0,8,8);
            pill.Name="GameGenre"+genre.Key;AutomationProperties.SetName(pill,AppLanguage.T("Жанр: "+genre.Name));pills.Children.Add(pill);
        }
        var scroll=gameGenreScroll=new ScrollViewer{Content=pills,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Hidden,CanContentScroll=false};
        scroll.ScrollChanged+=(_,_)=>UpdateGameGenreOverflow();scroll.SizeChanged+=(_,_)=>UpdateGameGenreOverflow();
        scroll.PreviewMouseWheel+=(_,args)=>{if(scroll.ScrollableWidth<=0)return;scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset-args.Delta/120d*96);args.Handled=true;};
        rail.Children.Add(scroll);
        // The active genre is brought into view the first time the rail is laid out.
        scroll.Loaded+=(_,_)=>{if(GameGenres.ByKey(gameGenre) is not {} selected)return;var target=pills.Children.OfType<Button>().FirstOrDefault(x=>x.Name=="GameGenre"+selected.Key);if(target==null)return;scroll.UpdateLayout();target.BringIntoView();UpdateGameGenreOverflow();};
        return rail;
    }
    void ScrollGameGenres(int direction){if(gameGenreScroll is {} scroll)scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset+direction*Math.Max(160,scroll.ViewportWidth*.65));}
    void UpdateGameGenreOverflow()
    {
        if(gameGenreScroll==null||gameGenreBack==null||gameGenreForward==null)return;
        var overflow=gameGenreScroll.ViewportWidth>0&&gameGenreScroll.ScrollableWidth>1;var visibility=overflow?Visibility.Visible:Visibility.Collapsed;
        gameGenreBack.Visibility=visibility;gameGenreForward.Visibility=visibility;
        gameGenreBack.IsEnabled=gameGenreScroll.HorizontalOffset>1;gameGenreForward.IsEnabled=gameGenreScroll.HorizontalOffset+1<gameGenreScroll.ScrollableWidth;
    }
    // The first shelves load together, the rest one after another as they finish, so the page fills from the top without a burst of requests.
    void EnsureGameHome()
    {
        if(gameShelfLoading.Count==0&&gameShelfItems.Count>0&&DateTime.UtcNow-gameShelfLoadedUtc>TimeSpan.FromMinutes(15)){gameShelfItems.Clear();gameShelfFailed.Clear();}
        foreach(var spec in GameShelves.Home)
        {
            if(gameShelfLoading.Count>=3)break;
            if(!gameShelfItems.ContainsKey(spec.Key)&&!gameShelfFailed.Contains(spec.Key)&&gameShelfLoading.Add(spec.Key))_=LoadGameShelf(spec);
        }
    }
    async Task LoadGameShelf(GameShelfSpec spec)
    {
        var request=gameHomeRequest??=new CancellationTokenSource();var token=request.Token;
        await Task.Yield();
        try
        {
            var browse=new GameBrowse("",spec.Sort,spec.Genre,1);
            var result=await (gameCatalogProvider?.Invoke(browse,token)??new GameCatalog(sourceClient).Browse(browse,token));
            if(token.IsCancellationRequested)return;
            if(gameShelfItems.Count==0)gameShelfLoadedUtc=DateTime.UtcNow;
            gameShelfItems[spec.Key]=result.Items;
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error){if(!token.IsCancellationRequested){gameShelfFailed.Add(spec.Key);ErrorLog.Write(error);}}
        finally
        {
            if(!token.IsCancellationRequested){gameShelfLoading.Remove(spec.Key);if(!closed)GameShelfLoaded(spec.Key);}
        }
    }
    void GameShelfLoaded(string key)
    {
        if(!GameHome)return;
        // The banner is made from the popular shelf, so that page is built again once it arrives.
        if(key=="popular"){Render();return;}
        if(gameShelfViews.TryGetValue(key,out var shelf))shelf.Set(GameShelfItems(key),gameShelfLoading.Contains(key));
        EnsureGameHome();
        UpdateDiscoveryLayout();ApplyPosterDownloads();RefreshLoadingIndicator();
    }
    void RenderGameHome()
    {
        EnsureGameHome();
        var content=new StackPanel{Margin=new(6,6,6,0)};
        var popular=GameShelfItems("popular");
        if(popular.Length>0)
        {
            var banners=popular.Take(5).ToArray();
            discoveryHero=new Grid{Margin=new(0,0,0,28)};discoveryHero.Children.Add(BuildFeatureCarousel(banners));content.Children.Add(discoveryHero);
            StartGameBannerDetails(banners);
        }
        else if(gameShelfLoading.Contains("popular"))content.Children.Add(BannerSkeleton());
        BuildGameFilters();content.Children.Add(TakeFiltersPanel());
        // The games the player has already queued come first, so what they are playing is one click away.
        var library=GameLibrary();
        if(library.Length>0)
        {
            var mine=AddShelf(content,"Library","Ваши игры",()=>ShowDownloads(this,new RoutedEventArgs()),"GameRow",1);gameShelfViews["library"]=mine;mine.Set(library,false);
        }
        foreach(var spec in GameShelves.Home)
        {
            var open=spec;var view=AddShelf(content,char.ToUpperInvariant(spec.Key[0])+spec.Key[1..],spec.Title,()=>OpenGameList(open.Sort,open.Genre),"GameRow",spec.Rows);gameShelfViews[spec.Key]=view;
            view.Set(GameShelfItems(spec.Key),gameShelfLoading.Contains(spec.Key)||!gameShelfItems.ContainsKey(spec.Key)&&!gameShelfFailed.Contains(spec.Key));
        }
        if(gameShelfLoading.Count==0&&GameShelves.Home.All(x=>gameShelfFailed.Contains(x.Key)))
        {
            var empty=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=370,Margin=new(24)};
            var label=Text("Каталог Steam пока недоступен",23);label.FontWeight=FontWeights.SemiBold;label.TextAlignment=TextAlignment.Center;empty.Children.Add(label);
            var hint=Text("Проверь соединение и обнови каталог.",13,true);hint.TextAlignment=TextAlignment.Center;empty.Children.Add(hint);
            var retry=ActionButton("Повторить загрузку","IconRefresh",RefreshGames);retry.HorizontalAlignment=HorizontalAlignment.Center;empty.Children.Add(retry);content.Children.Add(empty);
        }
        Body.Children.Add(new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=content,Margin=new(-6,-6,-6,0),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        UpdateCatalogColumns(true);UpdateFilterRail();
    }
    // Banner slides get their genre, studio and score from Steam's store details; they are cached for a week.
    void StartGameBannerDetails(MediaItem[] slides)
    {
        gameBannerRequest?.Cancel();var request=gameBannerRequest=new CancellationTokenSource();
        _=LoadGameBannerDetails(slides,request.Token);
    }
    async Task LoadGameBannerDetails(MediaItem[] slides,CancellationToken token)
    {
        await Task.Yield();
        foreach(var slide in slides)
        {
            if(token.IsCancellationRequested||closed)return;
            if(gameDetails.ContainsKey(slide.Id))continue;
            try
            {
                var detail=await (gameDetailProvider?.Invoke(slide,token)??new GameCatalog(sourceClient).Detail(slide,token));
                if(token.IsCancellationRequested||closed)return;
                AddGameDetails(detail);
                if(carousel is {Bound: {} bound} view&&bound.Id==slide.Id){RebuildFeatureMeta(view,bound);UpdateFeaturePeople(view,bound);}
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){return;}
            catch(Exception error){ErrorLog.Write(error);}
        }
    }

    // ---------- list: a shelf or search results, page by page ----------
    void RenderGameList()
    {
        var key=gameQuery+"|"+gamePage+"|"+gameShelf+"|"+gameGenre;var request=GameRequest;
        if(gameKey!=key){gameKey=key;_=LoadGamePage(key);}
        var title=request.Title;
        var headingRow=new DockPanel{Margin=new(5,0,8,5)};
        var count=Text(gameLoading?"Загружаем…":"Страница "+gamePage,11,true);count.VerticalAlignment=VerticalAlignment.Center;count.Margin=new(14,0,0,0);DockPanel.SetDock(count,Dock.Right);headingRow.Children.Add(count);
        var heading=Text(title,compactHeight?20:22);heading.Name="GameListHeading";heading.FontWeight=FontWeights.Bold;heading.Margin=new(0);headingRow.Children.Add(heading);PageHeader.Children.Add(headingRow);
        var subtitle=Text(gameError.Length>0?gameError:request.Subtitle,13,true);subtitle.Tag="CatalogSubtitle";subtitle.Margin=new(5,3,0,12);subtitle.Visibility=compactHeight?Visibility.Collapsed:Visibility.Visible;PageHeader.Children.Add(subtitle);
        if(gameQuery.Length==0)BuildGameFilters();else gameFilterRow=null;
        var visible=gameItems.Where(item=>gameQuery.Length>0||PersonallyVisible(item)).ToArray();ShowCatalog(visible);
        var list=catalogList!;list.Opacity=gameLoading&&visible.Length>0?.65:1;
        Body.Children.Remove(list);ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Disabled);ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);WheelScroll.SetIsEnabled(list,false);
        // As on the catalogue, the scroll viewer extends 6 px past the gutter so hover and focus rings are not clipped.
        var content=new StackPanel{Margin=new(6,6,6,0)};content.Children.Add(list);
        if(visible.Length==0&&gameLoading)content.Children.Add(PosterSkeleton(2,"GameSkeleton"));
        else if(visible.Length==0)
        {
            var empty=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=370,Margin=new(24)};
            var label=Text(gameError.Length>0?"Каталог пока недоступен":"Игры не найдены",23);label.FontWeight=FontWeights.SemiBold;label.TextAlignment=TextAlignment.Center;empty.Children.Add(label);
            var hint=Text(gameError.Length>0?"Нажми «Обновить» и попробуй ещё раз.":"Измени запрос или проверь скрытые названия в настройках.",13,true);hint.TextAlignment=TextAlignment.Center;empty.Children.Add(hint);
            if(gameError.Length>0){var retry=ActionButton("Повторить загрузку","IconRefresh",RefreshGames);retry.HorizontalAlignment=HorizontalAlignment.Center;empty.Children.Add(retry);}
            content.Children.Add(empty);
        }
        var footer=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center,Margin=new(5,18,8,18)};
        void Page(string label,int page,bool enabled)
        {
            var button=Button(label,()=>{gamePage=page;gameKey="";ResetCatalogNavigationPosition();Render();});button.Style=(Style)FindResource("PillButton");button.IsEnabled=enabled&&!gameLoading;button.MinWidth=36;button.Padding=new(10,7,10,7);button.Margin=new(2);
            AutomationProperties.SetName(button,AppLanguage.T(label));footer.Children.Add(button);
        }
        Page("Назад",gamePage-1,gamePage>1);
        var number=Text(gamePage.ToString(),13);number.FontWeight=FontWeights.SemiBold;number.VerticalAlignment=VerticalAlignment.Center;number.Margin=new(12,0,12,0);footer.Children.Add(number);
        Page("Далее",gamePage+1,gameHasNext);
        content.Children.Add(footer);
        Body.Children.Add(new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=content,Margin=new(-6,-6,-6,0),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        UpdateFilterRail();
    }
    async Task LoadGamePage(string key)
    {
        gameCatalogRequest?.Cancel();var request=gameCatalogRequest=new CancellationTokenSource();var token=request.Token;
        var browse=GameRequest;gameLoading=true;gameError="";gameHasNext=false;gameItems=[];await Task.Yield();
        try{var result=await (gameCatalogProvider?.Invoke(browse,token)??new GameCatalog(sourceClient).Browse(browse,token));if(gameKey==key&&!token.IsCancellationRequested){gameItems=result.Items;gameHasNext=result.HasNext;}}
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error){if(gameKey==key&&!token.IsCancellationRequested){gameError="Каталог временно недоступен. Нажми «Обновить».";ErrorLog.Write(error);}}
        finally{if(ReferenceEquals(gameCatalogRequest,request)){gameCatalogRequest=null;gameLoading=false;if(!closed&&section=="Игры"&&current==null&&gameKey==key)Render();}request.Dispose();}
    }
    void GameBack(){if(ReturnToSaved())return;if(DownloadBackLabel()!=null){CinemaBack();return;}current=null;section="Игры";Search.Text=gameQuery;Render();}
}
