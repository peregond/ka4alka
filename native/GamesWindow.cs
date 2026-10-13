using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
namespace Kachalka;

public partial class MainWindow
{
    internal Func<string,int,bool,CancellationToken,Task<GameCatalogPage>>? gameCatalogProvider;
    internal Func<MediaItem,CancellationToken,Task<GameDetails>>? gameDetailProvider;
    internal Func<MediaItem,ReleaseSource[]>? gameSourcesProvider;
    // gameShelf: "" is the home page (banner and shelves); "popular" and "new" open that shelf as a paged list.
    string gameQuery="",gameKey="",gameError="",gameShelf="";int gamePage=1;bool gameLoading,gameHasNext;
    bool gameNewest=>gameShelf=="new";
    MediaItem[] gameItems=[];CancellationTokenSource? gameCatalogRequest,gameDetailRequest;int? gameDetailId;
    readonly Dictionary<int,GameDetails> gameDetails=[];
    readonly Dictionary<int,SourceEntry[]> gameReleaseCache=[];
    TextBlock? gameTitle;Image? gameCover;
    // Capsules keep the 460:215 shape of Steam's own store header.
    void GamePosterSized(object sender,SizeChangedEventArgs args){if(sender is Border border){if(args.WidthChanged&&args.NewSize.Width>0){var height=args.NewSize.Width*215/460;if(double.IsNaN(border.Height)||Math.Abs(border.Height-height)>1)border.Height=height;}ClipPoster(border);}}
    SourceEntry[] gameReleases=[];SourceCheck[] gameSourceChecks=[];bool gameReleasesLoading;
    TextBlock? gameDescription;

    // ---------- home page: banner and shelves ----------
    static readonly DiscoveryShelfKind[] GameShelves=[DiscoveryShelfKind.Popular,DiscoveryShelfKind.New];
    readonly Dictionary<DiscoveryShelfKind,MediaItem[]> gameShelfItems=[];
    readonly HashSet<DiscoveryShelfKind> gameShelfLoading=[],gameShelfFailed=[];
    CancellationTokenSource? gameHomeRequest,gameBannerRequest;
    DateTime gameShelfLoadedUtc;
    Panel? gameFilterRow;
    bool GameHome=>section=="Игры"&&current==null&&activePerson==null&&gameQuery.Length==0&&gameShelf.Length==0&&gamePage==1;
    MediaItem[] GameShelfItems(DiscoveryShelfKind kind)=>gameShelfItems.TryGetValue(kind,out var items)?items.Where(PersonallyVisible).ToArray():[];
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
        if(reset){gameQuery="";gamePage=1;gameKey="";gameShelf="";ResetCatalogNavigationPosition();}
        Search.Text=gameQuery;Render();
    }
    void SubmitGameSearch()
    {
        gameQuery=Search.Text.Trim();gamePage=1;gameKey="";current=null;activePerson=null;submittedQuery="";ResetCatalogNavigationPosition();Render();
    }
    // A shelf's "Ещё" and the chips open one shelf as a paged list; "" returns to the home page.
    void OpenGameList(string shelf)
    {
        if(!SearchActive&&Search.Text.Length>0){Search.Text="";searchDelay.Stop();}
        gameQuery="";gameShelf=shelf;gamePage=1;gameKey="";current=null;activePerson=null;ResetCatalogNavigationPosition();Render();
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
    }
    void RenderGames()
    {
        if(GameHome)RenderGameHome();else RenderGameList();
    }
    // The chips under the search: the home page, then the shelves as full lists.
    void BuildGameFilters()
    {
        var row=new DockPanel{Name="GameFilters"};
        var refresh=ActionButton("","IconRefresh",RefreshGames,"PillButton");refresh.Name="GameRefresh";refresh.ToolTip="Обновить каталог";AutomationProperties.SetName(refresh,"Обновить каталог");
        refresh.Padding=new(0);refresh.Width=compactHeight?36:40;refresh.MinHeight=compactHeight?36:40;refresh.Margin=new(8,0,0,8);DockPanel.SetDock(refresh,Dock.Right);row.Children.Add(refresh);
        var reset=ActionButton("Сбросить всё","IconReset",()=>{gameShelf="";gamePage=1;gameKey="";ResetCatalogNavigationPosition();Render();},"PillButton");reset.Name="GameResetFilters";reset.MinHeight=compactHeight?36:40;reset.Margin=new(8,0,0,8);DockPanel.SetDock(reset,Dock.Right);row.Children.Add(reset);
        var chips=new WrapPanel();row.Children.Add(chips);
        void Chip(string label,string shelf)
        {
            var button=Button("",()=>OpenGameList(shelf));button.Style=(Style)FindResource("PillButton");button.Content=ChipContent(label,false);StyleChip(button,gameQuery.Length==0&&gameShelf==shelf);
            button.HorizontalContentAlignment=HorizontalAlignment.Center;button.MinHeight=compactHeight?36:40;button.FontSize=14;button.Padding=new(16,0,16,0);button.Margin=new(0,0,8,8);
            AutomationProperties.SetName(button,label);chips.Children.Add(button);
        }
        Chip("Все","");Chip("Популярные","popular");Chip("Новинки","new");
        FilterControls.Children.Add(row);gameFilterRow=row;
    }
    void EnsureGameHome()
    {
        if(gameShelfLoading.Count==0&&gameShelfItems.Count>0&&DateTime.UtcNow-gameShelfLoadedUtc>TimeSpan.FromMinutes(15)){gameShelfItems.Clear();gameShelfFailed.Clear();}
        foreach(var kind in GameShelves)
            if(!gameShelfItems.ContainsKey(kind)&&!gameShelfFailed.Contains(kind)&&gameShelfLoading.Add(kind))_=LoadGameShelf(kind);
    }
    async Task LoadGameShelf(DiscoveryShelfKind kind)
    {
        var request=gameHomeRequest??=new CancellationTokenSource();var token=request.Token;var newest=kind==DiscoveryShelfKind.New;
        await Task.Yield();
        try
        {
            var result=await (gameCatalogProvider?.Invoke("",1,newest,token)??new GameCatalog(sourceClient).Browse("",1,newest,token));
            if(token.IsCancellationRequested)return;
            if(gameShelfItems.Count==0)gameShelfLoadedUtc=DateTime.UtcNow;
            gameShelfItems[kind]=result.Items;
        }
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error){if(!token.IsCancellationRequested){gameShelfFailed.Add(kind);ErrorLog.Write(error);}}
        finally
        {
            if(!token.IsCancellationRequested){gameShelfLoading.Remove(kind);if(!closed)GameShelfLoaded(kind);}
        }
    }
    void GameShelfLoaded(DiscoveryShelfKind kind)
    {
        if(!GameHome)return;
        // The banner is made from the popular shelf, so that page is built again once it arrives.
        if(kind==DiscoveryShelfKind.Popular){Render();return;}
        if(discoveryShelves.TryGetValue(kind,out var shelf))shelf.Set(GameShelfItems(kind),gameShelfLoading.Contains(kind));
        UpdateDiscoveryLayout();ApplyPosterDownloads();RefreshLoadingIndicator();
    }
    void RenderGameHome()
    {
        EnsureGameHome();
        var content=new StackPanel{Margin=new(6,6,6,0)};
        var popular=GameShelfItems(DiscoveryShelfKind.Popular);
        if(popular.Length>0)
        {
            var banners=popular.Take(5).ToArray();
            discoveryHero=new Grid{Margin=new(0,0,0,28)};discoveryHero.Children.Add(BuildFeatureCarousel(banners));content.Children.Add(discoveryHero);
            StartGameBannerDetails(banners);
        }
        else if(gameShelfLoading.Contains(DiscoveryShelfKind.Popular))content.Children.Add(BannerSkeleton());
        BuildGameFilters();content.Children.Add(TakeFiltersPanel());
        foreach(var kind in GameShelves)
        {
            var view=AddDiscoveryShelf(content,kind);discoveryShelves[kind]=view;
            view.Set(GameShelfItems(kind),gameShelfLoading.Contains(kind));
        }
        if(gameShelfLoading.Count==0&&GameShelves.All(gameShelfFailed.Contains))
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
        var key=gameQuery+"|"+gamePage+"|"+gameNewest;
        if(gameKey!=key){gameKey=key;_=LoadGamePage(key);}
        var title=gameQuery.Length>0?"Результаты поиска":gameNewest?"Новые игры":"Популярные игры";
        var headingRow=new DockPanel{Margin=new(5,0,8,5)};
        var count=Text(gameLoading?"Загружаем…":"Страница "+gamePage,11,true);count.VerticalAlignment=VerticalAlignment.Center;count.Margin=new(14,0,0,0);DockPanel.SetDock(count,Dock.Right);headingRow.Children.Add(count);
        var heading=Text(title,compactHeight?20:22);heading.Name="GameListHeading";heading.FontWeight=FontWeights.Bold;heading.Margin=new(0);headingRow.Children.Add(heading);PageHeader.Children.Add(headingRow);
        var subtitle=Text(gameError.Length>0?gameError:gameQuery.Length>0?"Результаты для «"+gameQuery+"».":gameNewest?"Свежие релизы Steam для Windows.":"Самые продаваемые игры Steam для Windows.",13,true);subtitle.Tag="CatalogSubtitle";subtitle.Margin=new(5,3,0,12);subtitle.Visibility=compactHeight?Visibility.Collapsed:Visibility.Visible;PageHeader.Children.Add(subtitle);
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
            AutomationProperties.SetName(button,label);footer.Children.Add(button);
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
        var query=gameQuery;var page=gamePage;var newest=gameNewest;gameLoading=true;gameError="";gameHasNext=false;gameItems=[];await Task.Yield();
        try{var result=await (gameCatalogProvider?.Invoke(query,page,newest,token)??new GameCatalog(sourceClient).Browse(query,page,newest,token));if(gameKey==key&&!token.IsCancellationRequested){gameItems=result.Items;gameHasNext=result.HasNext;}}
        catch(OperationCanceledException)when(token.IsCancellationRequested){}
        catch(Exception error){if(gameKey==key&&!token.IsCancellationRequested){gameError="Каталог временно недоступен. Нажми «Обновить».";ErrorLog.Write(error);}}
        finally{if(ReferenceEquals(gameCatalogRequest,request)){gameCatalogRequest=null;gameLoading=false;if(!closed&&section=="Игры"&&current==null&&gameKey==key)Render();}request.Dispose();}
    }
    void GameBack(){if(ReturnToSaved())return;if(DownloadBackLabel()!=null){CinemaBack();return;}current=null;section="Игры";Search.Text=gameQuery;Render();}
}
