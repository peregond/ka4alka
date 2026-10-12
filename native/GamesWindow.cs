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
    string gameQuery="",gameKey="",gameError="";int gamePage=1;bool gameNewest,gameLoading,gameHasNext;
    MediaItem[] gameItems=[];CancellationTokenSource? gameCatalogRequest,gameDetailRequest;int? gameDetailId;
    readonly Dictionary<int,GameDetails> gameDetails=[];
    readonly Dictionary<int,SourceEntry[]> gameReleaseCache=[];
    TextBlock? gameTitle;Image? gameCover;
    void GamePosterSized(object sender,SizeChangedEventArgs args){if(sender is Border border){if(args.WidthChanged&&args.NewSize.Width>0)border.Height=args.NewSize.Width*9/16;ClipPoster(border);}}
    SourceEntry[] gameReleases=[];SourceCheck[] gameSourceChecks=[];bool gameReleasesLoading;
    TextBlock? gameDescription,gameRequirements,gameReleaseStatus;StackPanel? gameReleaseRows;
    void UpdateGameDetailLayout(){if(gameCover!=null&&current?.Section=="Игры"){gameCover.Width=Math.Min(460,Math.Max(160,Body.ActualWidth-20));gameCover.Height=gameCover.Width*9/16;}}
    void ShowGames()
    {
        var reset=section=="Игры"&&current==null;
        liveRequest?.Cancel();searchDelay.Stop();CancelPeopleSearch(true);current=null;activePerson=null;returnPerson=null;personSearchReturn=null;savedReturn=null;
        section="Игры";submittedQuery="";favoritesOnly=false;
        if(reset){gameQuery="";gamePage=1;gameKey="";ResetCatalogNavigationPosition();}
        Search.Text=gameQuery;Render();
    }
    void SubmitGameSearch()
    {
        gameQuery=Search.Text.Trim();gamePage=1;gameKey="";current=null;activePerson=null;submittedQuery="";ResetCatalogNavigationPosition();Render();
    }
    void CancelGameWork()
    {
        if(section!="Игры"||current!=null){gameCatalogRequest?.Cancel();gameCatalogRequest=null;gameLoading=false;gameKey="";}
        if(current?.Section!="Игры"||current.Id!=gameDetailId){gameDetailRequest?.Cancel();gameDetailRequest=null;gameDetailId=null;}
    }
    void RenderGames()
    {
        var key=gameQuery+"|"+gamePage+"|"+gameNewest;
        if(gameKey!=key){gameKey=key;_=LoadGamePage(key);}
        var heading=Text(gameQuery.Length>0?"Поиск игр":"Игры для Windows",28);heading.FontWeight=FontWeights.SemiBold;PageHeader.Children.Add(heading);
        PageHeader.Children.Add(Text(gameLoading?"Загружаем игры…":gameError.Length>0?gameError:"Каталог Steam · раздачи ищутся при открытии игры",12,true));
        var controls=new WrapPanel();
        foreach(var (label,newest) in new[]{("Популярные",false),("Новинки",true)})
        {
            var button=Button(label,()=>{gameNewest=newest;gamePage=1;gameKey="";Render();});button.Style=(Style)FindResource("PillButton");button.SetResourceReference(Control.BackgroundProperty,gameNewest==newest?"Selected":"Panel");controls.Children.Add(button);
        }
        controls.Children.Add(ActionButton("Обновить","IconRefresh",()=>{gameKey="";Render();},"QuietButton"));PageHeader.Children.Add(controls);
        var visible=gameItems.Where(item=>gameQuery.Length>0||!HiddenCatalog.TitleHidden(item,prefs.HiddenTitles)).ToArray();ShowCatalog(visible);catalogList!.ItemTemplate=(DataTemplate)FindResource("GameRow");
        var pages=new WrapPanel();
        if(gamePage>1)pages.Children.Add(Button("← Назад",()=>{gamePage--;gameKey="";ResetCatalogNavigationPosition();Render();}));
        pages.Children.Add(Text("Страница "+gamePage,13,true));
        if(gameHasNext)pages.Children.Add(Button("Далее →",()=>{gamePage++;gameKey="";ResetCatalogNavigationPosition();Render();}));PageHeader.Children.Add(pages);
        if(!gameLoading&&visible.Length==0)PageHeader.Children.Add(Text(gameError.Length>0?"Попробуй повторить загрузку.":"Игры не найдены. Измени запрос или проверь скрытые названия в настройках.",13,true));
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
    void RenderGameDetail(MediaItem game)
    {
        SetBack(SavedBackLabel()??DownloadBackLabel()??"Игры",GameBack);
        var content=new StackPanel{Margin=new(4)};Body.Children.Add(new ScrollViewer{Style=(Style)FindResource("PageScroll"),Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var title=gameTitle=Text(game.Title,28);title.FontWeight=FontWeights.SemiBold;content.Children.Add(title);
        var poster=gameCover=new Image{DataContext=game,Height=180,Stretch=Stretch.UniformToFill,HorizontalAlignment=HorizontalAlignment.Left,MaxWidth=460,Margin=new(0,12,0,12)};poster.Width=Math.Min(460,Math.Max(160,Body.ActualWidth-20));poster.Height=poster.Width*9/16;poster.Loaded+=SourceCover;poster.DataContextChanged+=SourceCoverChanged;content.Children.Add(poster);
        var actions=new WrapPanel();actions.Children.Add(ActionButton(IsSaved(game)?"Сохранено":"Сохранить",IsSaved(game)?"IconHeartFilled":"IconHeart",()=>{ToggleSaved(current??game);Render();},"PillButton"));
        actions.Children.Add(Button("Открыть в Steam",()=>System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(game.PageUrl!){UseShellExecute=true})));content.Children.Add(actions);
        gameDescription=Text(gameDetails.TryGetValue(game.Id,out var detail)?detail.Item.Description??"Описание пока недоступно.":"Загружаем описание…",14);gameDescription.Margin=new(0,14,0,8);content.Children.Add(gameDescription);
        gameRequirements=Text(detail==null?"":GameRequirements(detail),12,true);content.Children.Add(gameRequirements);
        var releaseHeading=Text("Варианты загрузки",22);releaseHeading.Margin=new(0,22,0,10);content.Children.Add(releaseHeading);
        gameReleaseStatus=Text("",12,true);content.Children.Add(gameReleaseStatus);gameReleaseRows=new StackPanel();content.Children.Add(gameReleaseRows);
        if(gameDetailId!=game.Id){gameDetailRequest?.Cancel();gameDetailId=game.Id;gameReleases=gameReleaseCache.GetValueOrDefault(game.Id)??[];gameSourceChecks=[];gameReleasesLoading=true;gameDetailRequest=new();_=LoadGameDetail(game,gameDetailRequest);}
        RenderGameReleases();
    }
    static string GameRequirements(GameDetails detail)=>string.Join("\n",new[]{detail.Item.CardMeta,detail.Developers.Length>0?"Разработчик: "+detail.Developers:"",detail.Publishers.Length>0?"Издатель: "+detail.Publishers:"",detail.Requirements.Length>0?"Минимальные требования:\n"+detail.Requirements:"Системные требования пока не указаны."}.Where(x=>x.Length>0));
    void GameBack(){if(ReturnToSaved())return;if(DownloadBackLabel()!=null){CinemaBack();return;}current=null;section="Игры";Search.Text=gameQuery;Render();}
    async Task LoadGameDetail(MediaItem game,CancellationTokenSource request)
    {
        var token=request.Token;bool Active()=>!closed&&!token.IsCancellationRequested&&current?.Id==game.Id&&current.Section=="Игры";
        async Task Metadata()
        {
            try
            {
                var detail=gameDetails.GetValueOrDefault(game.Id)??await (gameDetailProvider?.Invoke(game,token)??new GameCatalog(sourceClient).Detail(game,token));
                if(!Active())return;if(gameDetails.Count>=60)gameDetails.Remove(gameDetails.Keys.First());gameDetails[game.Id]=detail;current=detail.Item;
                if(gameTitle!=null)gameTitle.Text=detail.Item.Title;if(gameCover!=null)gameCover.DataContext=detail.Item;
                if(gameDescription!=null)gameDescription.Text=detail.Item.Description??"Описание пока недоступно.";if(gameRequirements!=null)gameRequirements.Text=GameRequirements(detail);
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){}
            catch(Exception error){if(Active()&&gameDescription!=null)gameDescription.Text="Описание временно недоступно. Поиск раздач продолжается.";ErrorLog.Write(error);}
        }
        async Task Releases()
        {
            try
            {
                var progress=new Progress<ReleaseSearchUpdate>(result=>{if(!Active())return;gameReleases=ReleaseSearch.WithSaved(result.Items,gameReleases);gameSourceChecks=result.Sources;RenderGameReleases();});
                var result=await ReleaseSearch.RunAsync((gameSourcesProvider?.Invoke(game)??GameReleaseSources.Create(sourceClient,game,sourceConfigs)),progress,token,TimeSpan.FromSeconds(18));
                if(!Active())return;gameReleases=ReleaseSearch.WithSaved(result.Items,gameReleases);gameSourceChecks=result.Sources;
                if(gameReleases.Length>0){if(gameReleaseCache.Count>=60)gameReleaseCache.Remove(gameReleaseCache.Keys.First());gameReleaseCache[game.Id]=gameReleases;}
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){}
            catch(Exception error){ErrorLog.Write(error);}
            finally{if(Active()){gameReleasesLoading=false;RenderGameReleases();}}
        }
        try{await Task.WhenAll(Metadata(),Releases());}finally{if(ReferenceEquals(gameDetailRequest,request))gameDetailRequest=null;request.Dispose();}
    }
    void RenderGameReleases()
    {
        if(gameReleaseRows==null||current?.Section!="Игры")return;gameReleaseRows.Children.Clear();
        if(gameReleaseStatus!=null)gameReleaseStatus.Text=gameReleasesLoading?"Ищем раздачи… · найдено "+gameReleases.Length:gameReleases.Length>0?"Найдено: "+gameReleases.Length:gameSourceChecks.Any(x=>x.State is SourceState.TimedOut or SourceState.Unavailable)?"Часть источников недоступна. Попробуй открыть игру позже.":"Подходящих раздач не найдено.";
        foreach(var entry in gameReleases.OrderByDescending(x=>x.Seeds??-1).Take(100))
        {
            var row=new DockPanel{Margin=new(0,8,0,8)};var download=new Button{Content="Скачать",Tag=entry,Margin=new(12,0,0,0),VerticalAlignment=VerticalAlignment.Center};download.Click+=SourceDownload;DockPanel.SetDock(download,Dock.Right);row.Children.Add(download);
            var info=new StackPanel();info.Children.Add(Text(entry.Title,14));info.Children.Add(Text(entry.Source+" · Windows"+(entry.Size is {} size?" · "+CacheSize(size):"")+(entry.Seeds is {} seeds?" · отдают "+seeds:""),12,true));row.Children.Add(info);
            gameReleaseRows.Children.Add(new Border{Child=row,BorderBrush=(Brush)FindResource("Edge"),BorderThickness=new(0,0,0,1)});
        }
    }
}
