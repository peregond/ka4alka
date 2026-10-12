using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Kachalka;
public partial class MainWindow
{
    public async Task GamesSmokeTest(string output)
    {
        Directory.CreateDirectory(output);metadataLifetime.Cancel();liveRequest?.Cancel();catalogRefreshTimer.Stop();searchDelay.Stop();
        MaxWidth=1800;MaxHeight=1000;MinHeight=300;
        var checks=new List<string>();void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add(label);}
        async Task Settle(){await Task.Delay(100);UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);}
        async Task Size(double width,double height){MinWidth=Math.Min(width,MaxWidth-24);MinHeight=Math.Min(height,MaxHeight-24);Width=MinWidth;Height=MinHeight;await Settle();}
        void Shot(string name)
        {
            UpdateLayout();
            var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(ActualWidth)),Math.Max(1,(int)Math.Ceiling(ActualHeight)),96,96,PixelFormats.Pbgra32);bitmap.Render(this);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name+".png"));png.Save(stream);
        }
        // Covers and the hero picture are drawn here and placed in the decoded cache, so the page needs no network.
        BitmapImage Art(string title,string from,string to,int width,int height)
        {
            var visual=new DrawingVisual();
            using(var context=visual.RenderOpen())
            {
                context.DrawRectangle(new LinearGradientBrush((Color)ColorConverter.ConvertFromString(from),(Color)ColorConverter.ConvertFromString(to),20),null,new Rect(0,0,width,height));
                var text=new FormattedText(title,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),height/6d,Brushes.White,1);
                context.DrawText(text,new Point(width*.06,height*.58));
            }
            var target=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);target.Render(visual);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(target));
            using var stream=new MemoryStream();encoder.Save(stream);stream.Position=0;
            var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();return image;
        }
        var palette=new[]{("#B5452F","#2A1A3A"),("#2F7FB5","#101A3A"),("#6B9A3C","#16302A"),("#B58A2F","#3A2A10"),("#7B4FB5","#1E143A"),("#B52F6B","#3A1020"),("#2FB59A","#10303A"),("#8A8F99","#20242E")};
        var titles=new[]{(1245620,"Elden Ring"),(1091500,"Cyberpunk 2077"),(1086940,"Baldur's Gate 3"),(292030,"The Witcher 3: Wild Hunt"),(620,"Portal 2"),(1145350,"Hades II"),(413150,"Stardew Valley"),(1174180,"Red Dead Redemption 2")};
        var games=titles.Select((entry,index)=>
        {
            var url="https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/"+entry.Item1+"/capsule_231x87.jpg";
            var item=GameCatalog.Item(entry.Item1,entry.Item2,url);
            coverCache.Put(url+"|"+PosterDecodeWidth,Art(entry.Item2,palette[index].Item1,palette[index].Item2,460,215));
            coverCache.Put(GameCatalog.HeroImage(item)!,Art(entry.Item2,palette[index].Item1,palette[index].Item2,1280,413));
            return item;
        }).ToArray();
        var game=games[4];var spotlight=games[0];var fresh=games[5..];
        var popular=new TaskCompletionSource<GameCatalogPage>(TaskCreationOptions.RunContinuationsAsynchronously);var released=new TaskCompletionSource<GameCatalogPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var metadata=new TaskCompletionSource<GameDetails>(TaskCreationOptions.RunContinuationsAsynchronously);var releases=new TaskCompletionSource<IReadOnlyList<SourceEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queries=new List<string>();
        gameCatalogProvider=(query,page,isNew,ct)=>{queries.Add(query+"|"+page+"|"+isNew);return(isNew?released:popular).Task.WaitAsync(ct);};
        // Banner slides ask for their store details too; the opened game stays pending until the check below, the others answer at once.
        gameDetailProvider=(item,ct)=>item.Id==game.Id?metadata.Task.WaitAsync(ct):Task.FromResult(new GameDetails(item with{Year=2019+item.Id%6,Genre="Экшены, Ролевые игры"},"ОС: Windows 10","Fixture Studio","Fixture Publisher","1 999 руб.",80+(int)(item.Id%15)));
        gameSourcesProvider=_=>[new("Fixture PC",ct=>releases.Task.WaitAsync(ct))];
        await Size(1280,900);

        var nav=Navigation.Children.OfType<Button>().Single(button=>Equals(button.Tag,"Игры"));
        Check(nav.Content is Grid navGrid&&ReferenceEquals(navGrid.Children.OfType<System.Windows.Shapes.Path>().Single(x=>Equals(x.Tag,"NavIcon")).Data,FindResource("IconGame")),"the Games entry in the sidebar shows the gamepad, not the television");
        nav.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
        Check(section=="Игры"&&SearchBar.IsVisible&&GameHome&&gameShelfLoading.Count==2&&!IsEnabled.Equals(false),"Games navigation opens its own home page while both shelves are pending");
        Check(VisualElements<Border>(Body).Any(x=>x.Name=="FeatureSkeleton")&&VisualElements<FrameworkElement>(Body).Any(x=>x.Name=="DiscoveryPopularSkeleton"),"the pending home shows breathing placeholders for the banner and the shelves");
        var beat=0;var timer=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(15)};timer.Tick+=(_,_)=>beat++;timer.Start();await Task.Delay(150);timer.Stop();Check(beat>=3&&Search.IsEnabled,"pending game shelves keep the WPF input dispatcher responsive");

        popular.SetResult(new(games,false));await Settle();
        Check(carousel is {} banner&&banner.Slides[0].Id==spotlight.Id&&banner.Slides.Length==5&&banner.Slides[4].Id==game.Id&&banner.Eyebrow.Text=="В центре внимания"&&Equals(banner.About.Tag,spotlight),"the popular shelf becomes a five-slide banner with its first game in the spotlight");
        Check(discoveryShelves.Count==2&&discoveryShelves[DiscoveryShelfKind.Popular].Row.Content is CatalogRow popularRow&&popularRow.Items.Any(x=>x.Id==game.Id)&&discoveryShelves[DiscoveryShelfKind.Popular].Row.ContentTemplate==FindResource("GameRow"),"the popular shelf lists games on wide capsule cards");
        Check(discoveryShelves[DiscoveryShelfKind.New].Row.Visibility!=Visibility.Visible&&FindVisual<TextBlock>(Body,x=>x.Text=="Новые игры")!=null,"the new releases shelf waits for its own request under its own title");
        if(!prefs.LiteMode)
        {
            for(var attempt=0;attempt<40&&carousel!.Poster.Source==null;attempt++)await Task.Delay(100);
            Check(carousel!.Poster.Source!=null,"the banner draws Steam's wide hero picture of the spotlight game");
        }
        for(var attempt=0;attempt<40&&!(carousel is {Bound: {} first}&&gameDetails.ContainsKey(first.Id));attempt++)await Task.Delay(100);
        Check(VisualElements<TextBlock>(Body).Any(x=>x.Text.StartsWith("Metacritic"))&&VisualElements<TextBlock>(Body).Any(x=>x.Text.StartsWith("Разработчик"))&&VisualElements<Border>(Body).Any(x=>x.Child is TextBlock {Text:"1 999 руб."}),"the banner shows the Metacritic score, studio and price once the store details arrive");
        released.SetResult(new(fresh,false));await Settle();
        Check(discoveryShelves[DiscoveryShelfKind.New].Row.Content is CatalogRow newRow&&newRow.Items.Length==fresh.Length&&queries.Count(x=>x=="|1|False")==1&&queries.Count(x=>x=="|1|True")==1,"each shelf is requested once and fills in without rebuilding the page");
        Check(VisualElements<Border>(Body).Any(x=>x.Name=="GameCardArt"&&x.ActualWidth>0&&Math.Abs(x.ActualHeight-x.ActualWidth*215/460)<2),"capsules keep the 460 by 215 shape of Steam's store header");
        Shot("games-home-dark");
        foreach(var width in new[]{510d,1280d})
        {
            await Size(width,width<700?800:900);
            var cards=VisualElements<Button>(Body).Where(b=>b.Tag is MediaItem item&&item.Section=="Игры").ToArray();
            Check(cards.Length>0&&cards.All(b=>b.ActualWidth>0)&&catalogColumns==WindowSizing.GameColumns(Body.ActualWidth),"game cards remain visible and follow the capsule columns at window width "+width);
            if(width<700)Shot("games-home-narrow");
        }
        var oldTheme=prefs.Light;prefs.Light=true;ApplyTheme();Render();await Settle();Shot("games-home-light");prefs.Light=oldTheme;ApplyTheme();Render();await Settle();

        // A shelf's "Ещё" opens it as a list, the chips switch lists, and "Все" returns to the home page.
        OpenDiscoveryShelf(DiscoveryShelfKind.New);await Settle();
        Check(!GameHome&&gameShelf=="new"&&queries.Last()=="|1|True"&&FindVisual<TextBlock>(PageHeader,x=>x.Name=="GameListHeading")?.Text=="Новые игры"&&catalogList?.ItemTemplate==FindResource("GameRow"),"a shelf's \"Ещё\" opens that shelf as a list of game cards");
        Check(FiltersPanel.IsVisible&&VisualElements<Button>(FiltersPanel).Any(b=>ChipText(b)=="Новинки"),"the chips stay pinned under the search while a list is open");
        Shot("games-list-dark");
        OpenGameList("");await Settle();Check(GameHome&&carousel!=null,"the \"Все\" chip returns to the banner and shelves");

        var button=VisualElements<Button>(Body).First(card=>card.Tag is MediaItem item&&item.Id==game.Id&&!ReferenceEquals(card,carousel?.About));button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
        Check(current?.Section=="Игры"&&gameDescription?.Text.Contains("Загружаем")==true&&gameReleasesLoading,"a game opens its own page while descriptions and releases are still pending");
        Check(VisualElements<Border>(Body).Any(x=>x.Name=="GameHero")&&VisualElements<Border>(Body).Any(x=>x.Name=="GameAbout")&&FindVisual<StackPanel>(Body,x=>x.Name=="ReleaseSkeleton")!=null,"the page has a banner, a facts card and breathing placeholders where releases will be");
        Shot("game-detail-loading");
        metadata.SetResult(new(game with{Year=2011,Genre="Приключения, Головоломки",Description="Продолжение культовой головоломки от Valve: порталы, гели и тестовые камеры Aperture Science. Одиночная кампания и совместный режим на двоих — вместе с другом или с ИИ. Проверенное описание игры.",ImageUrl=game.ImageUrl},"Минимальные:\nОС: Windows 7 / Vista / XP\nПроцессор: 3.0 GHz P4, Dual Core 2.0 или лучше\nПамять: 2 GB RAM\nВидеокарта: DirectX 9 совместимая, 128 MB\nМесто на диске: 13 GB","Valve","Valve","399 руб.",95));await Settle();
        Check(gameDescription?.Text.StartsWith("Продолжение культовой")==true&&gameDescription.Visibility==Visibility.Visible&&VisualElements<TextBlock>(Body).Any(x=>x.Text=="2 GB RAM")&&gameReleasesLoading,"metadata and requirements appear without waiting for torrent sources");
        Check(VisualElements<TextBlock>(Body).Any(x=>x.Name=="GameYear"&&x.Text=="2011")&&VisualElements<TextBlock>(Body).Any(x=>x.Text.Contains("Metacritic"))&&VisualElements<Border>(Body).Any(x=>x.Child is TextBlock {Text:"399 руб."}),"the banner shows the year, the Metacritic score and the price from the store details");
        string Magnet(char digit)=>"magnet:?xt=urn:btih:"+new string(digit,40);
        releases.SetResult([
            new("pc1","Portal 2 (2011) PC RePack by xatab [RUS/ENG]","Fixture","",Magnet('a'),null,9_000_000_000,180){Leechers=14},
            new("pc2","Portal 2 [FitGirl Repack] v1.0.0 + All DLCs","Fixture","",Magnet('b'),null,6_200_000_000,260){Leechers=31},
            new("pc3","Portal 2 (2011) PC GOG [ENG]","Fixture 2","",Magnet('c'),null,12_400_000_000,6)]);
        await Settle();
        Check(gameReleases.Length==3&&!gameReleasesLoading&&VisualElements<Button>(Body).Any(b=>b.Name=="GameDownload"&&b.Tag is SourceEntry),"the best seeded release becomes the main download button of the page");
        Check(VisualElements<Border>(Body).Any(b=>b.Name=="GameBestRelease")&&VisualElements<Button>(Body).Any(b=>b.Name=="GameBestDownload"),"the recommended release is shown on top of the list with its own download button");
        Check(VisualElements<TextBlock>(Body).Any(x=>x.Text=="Репак")&&VisualElements<Border>(Body).Any(b=>b.Child is TextBlock {Text:"FitGirl"})&&VisualElements<Border>(Body).Any(b=>b.Child is TextBlock {Text:"Русский язык"}),"a release row names its kind, repack author and language from its title");
        Check(VisualElements<Button>(Body).Any(b=>AutomationProperties.GetName(b).StartsWith("Скачать раздачу:")&&b.Name!="GameDownload"),"the rows keep the shared download action");
        await Settle();Shot("game-detail-dark");
        await Size(700,900);Shot("game-detail-narrow");
        await Size(1280,900);
        var old=prefs.LiveFavorites.ToList();var oldIds=new HashSet<int>(prefs.Favorites);
        try{ToggleSaved(current!);Check(SavedItems().Any(item=>item.Section=="Игры"&&item.Id==game.Id),"saved collection includes a game alongside films and series");}
        finally{prefs.LiveFavorites=old;prefs.Favorites=oldIds;prefs.Save();}
        GameBack();await Settle();Check(GameHome,"going back from a game returns to the home page");
        Search.Text="Portal";SubmitSearch(SearchSubmitButton,new RoutedEventArgs());await Settle();Check(gameQuery=="Portal"&&queries.Last()=="Portal|1|False"&&section=="Игры"&&!GameHome&&FindVisual<TextBlock>(PageHeader,x=>x.Name=="GameListHeading")?.Text=="Результаты поиска","game search stays in Games and does not start a film/series search");
        ClearSearch(ClearSearchButton,new RoutedEventArgs());await Settle();Check(gameQuery==""&&Search.Text==""&&GameHome,"clearing game search restores the home page");
        Check(GameCatalog.Query("",1,false).Query.Contains("untags=113")&&GameCatalog.Query("",1,true).Query.Contains("untags=113")&&!GameCatalog.Query("Portal",1,false).Query.Contains("untags"),"browsing leaves out Free to Play games while a title search can still find any game");

        var blocked=new TaskCompletionSource<GameCatalogPage>(TaskCreationOptions.RunContinuationsAsynchronously);CancellationToken gameToken=default;
        gameCatalogProvider=(_,_,_,ct)=>{gameToken=ct;return blocked.Task.WaitAsync(ct);};RefreshGames();await Settle();
        Check(gameShelfLoading.Count>0,"refreshing the home page requests both shelves again");
        ShowCatalogSection("Фильмы");await Settle();Check(gameToken.IsCancellationRequested&&gameShelfLoading.Count==0,"leaving Games cancels its unfinished shelf requests");
        await File.WriteAllTextAsync(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Checks=checks,WindowsGames=true,GamepadIcon=true,HomeBanner=true,Shelves=true,GameCardLikeFilm=true,SeparateSearch=true,ResponsiveInput=true,DownloadAction=true,SavedGames=true}));Close();
    }
}
