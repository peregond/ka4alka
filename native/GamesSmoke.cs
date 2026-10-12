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
        // Each game carries what a Steam search row would: a price (some discounted), reviews of every tone, one not yet released.
        var stores=new GameStoreInfo[]
        {
            new("$59.99","",0,false,"24 фев. 2022 г.",false,93,1_200_000,"positive","Очень положительные"),
            new("$14.99","$59.99",75,false,"9 дек. 2020 г.",false,88,640_000,"positive","Очень положительные"),
            new("$59.99","",0,false,"3 авг. 2023 г.",false,96,540_000,"positive","Крайне положительные"),
            new("$9.99","$39.99",75,false,"18 мая. 2015 г.",false,97,720_000,"positive","Крайне положительные"),
            new("$9.99","",0,false,"18 апр. 2011 г.",false,98,390_000,"positive","Крайне положительные"),
            new("$29.99","",0,false,"28 окт. 2026 г.",true),
            new("$45.49","$69.99",35,false,"13 авг. 2026 г.",false,41,2_963,"mixed","Смешанные"),
            new("$69.99","",0,false,"6 окт. 2026 г.",false,37,1_132,"negative","В основном отрицательные")
        };
        var genres=new[]{"Экшены, Ролевые игры","Экшены, Ролевые игры","Ролевые игры","Ролевые игры, Приключенческие игры","Приключенческие игры","Экшены, Инди","Симуляторы, Инди","Экшены, Приключенческие игры"};
        var games=titles.Select((entry,index)=>
        {
            var url="https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/"+entry.Item1+"/capsule_231x87.jpg";
            var item=GameCatalog.Item(entry.Item1,entry.Item2,url) with{Store=stores[index],Genre=genres[index],Year=GameRelease.Parse(stores[index].Released,new DateTime(2026,10,12)).Year};
            coverCache.Put(url+"|"+PosterDecodeWidth,Art(entry.Item2,palette[index].Item1,palette[index].Item2,460,215));
            coverCache.Put(GameCatalog.HeroImage(item)!,Art(entry.Item2,palette[index].Item1,palette[index].Item2,1280,413));
            return item;
        }).ToArray();
        var game=games[4];var spotlight=games[0];
        // Screenshots are drawn here and placed in the decoded cache, so the gallery needs no network either.
        GameShot[] ShotList(int id,int index,int count)=>Enumerable.Range(0,count).Select(number=>
        {
            var thumb="https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/"+id+"/ss_"+number+".600x338.jpg";var full="https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/"+id+"/ss_"+number+".1920x1080.jpg";
            var tone=palette[(index+number)%palette.Length];
            coverCache.Put(thumb+"|320",Art("Скриншот "+(number+1),tone.Item1,tone.Item2,320,180));coverCache.Put(full+"|1280",Art("Скриншот "+(number+1),tone.Item1,tone.Item2,1280,720));
            return new GameShot(thumb,full);
        }).ToArray();
        // The first shelves are held back by hand so the pending page can be checked; every other request answers at once.
        var gates=new Dictionary<string,TaskCompletionSource<GameCatalogPage>>{["popular||1|"]=new(TaskCreationOptions.RunContinuationsAsynchronously),["specials||1|"]=new(TaskCreationOptions.RunContinuationsAsynchronously),["new||1|"]=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var metadata=new TaskCompletionSource<GameDetails>(TaskCreationOptions.RunContinuationsAsynchronously);var releases=new TaskCompletionSource<IReadOnlyList<SourceEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queries=new List<string>();var peak=0;
        GameCatalogPage Rotate(int offset)=>new(games.Skip(offset%games.Length).Concat(games).Take(8).ToArray(),false);
        gameCatalogProvider=(browse,ct)=>
        {
            var key=browse.Sort+"|"+browse.Genre+"|"+browse.Page+"|"+browse.Query;queries.Add(key);peak=Math.Max(peak,gameShelfLoading.Count);
            return gates.TryGetValue(key,out var gate)?gate.Task.WaitAsync(ct):Task.FromResult(Rotate(queries.Count));
        };
        // Banner slides ask for their store details too; the opened game stays pending until the check below, the others answer at once.
        gameDetailProvider=(item,ct)=>item.Id==game.Id?metadata.Task.WaitAsync(ct):Task.FromResult(new GameDetails(item with{Year=2019+item.Id%6,Genre="Экшены, Ролевые игры"},"ОС: Windows 10","Fixture Studio","Fixture Publisher","1 999 руб.",80+(int)(item.Id%15)));
        gameSourcesProvider=_=>[new("Fixture PC",ct=>releases.Task.WaitAsync(ct))];
        await Size(1280,900);

        var nav=Navigation.Children.OfType<Button>().Single(button=>Equals(button.Tag,"Игры"));
        Check(nav.Content is Grid navGrid&&ReferenceEquals(navGrid.Children.OfType<System.Windows.Shapes.Path>().Single(x=>Equals(x.Tag,"NavIcon")).Data,FindResource("IconGame")),"the Games entry in the sidebar shows the gamepad, not the television");
        nav.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
        Check(section=="Игры"&&SearchBar.IsVisible&&GameHome&&gameShelfLoading.Count==3&&gameShelfViews.Count==GameShelves.Home.Length,"Games navigation opens its own home page with every shelf in place while the first three are pending");
        Check(VisualElements<Border>(Body).Any(x=>x.Name=="FeatureSkeleton")&&VisualElements<FrameworkElement>(Body).Any(x=>x.Name=="DiscoveryPopularSkeleton")&&VisualElements<FrameworkElement>(Body).Any(x=>x.Name=="DiscoveryActionSkeleton"||x.Name=="DiscoveryActionSection"),"the pending home shows breathing placeholders for the banner and the shelves");
        var beat=0;var timer=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(15)};timer.Tick+=(_,_)=>beat++;timer.Start();await Task.Delay(150);timer.Stop();Check(beat>=3&&Search.IsEnabled,"pending game shelves keep the WPF input dispatcher responsive");

        gates["popular||1|"].SetResult(new(games,false));await Settle();
        Check(carousel is {} banner&&banner.Slides[0].Id==spotlight.Id&&banner.Slides.Length==5&&banner.Slides[4].Id==game.Id&&banner.Eyebrow.Text=="В центре внимания"&&Equals(banner.About.Tag,spotlight),"the popular shelf becomes a five-slide banner with its first game in the spotlight");
        Check(carousel!.Meta.Children.OfType<StackPanel>().Any(x=>x.Name=="GameReview")&&carousel.Meta.Children.OfType<FrameworkElement>().Any(x=>x.Name=="GamePrice"),"the banner shows Steam's verdict and the price straight from the list row");
        Check(games.Take(5).All(x=>x.CardMeta.Length>0&&x.CardMeta!="Windows")&&games[5].CardMeta=="28 окт. 2026"&&(games[0] with{Store=null,Year=0,Genre="Windows"}).CardMeta=="","a game card shows its year and genre, a coming game its date, and no placeholder caption");
        Check(gameShelfViews["popular"].Row.Content is CatalogRow popularRow&&popularRow.Items.Any(x=>x.Id==game.Id)&&gameShelfViews["popular"].Row.ContentTemplate==FindResource("GameRow"),"the popular shelf lists games on wide capsule cards");
        Check(gameShelfViews["new"].Row.Visibility!=Visibility.Visible&&FindVisual<TextBlock>(Body,x=>x.Text=="Новинки")!=null,"the new releases shelf waits for its own request under its own title");
        if(!prefs.LiteMode)
        {
            for(var attempt=0;attempt<40&&carousel!.Poster.Source==null;attempt++)await Task.Delay(100);
            Check(carousel!.Poster.Source!=null,"the banner draws Steam's wide hero picture of the spotlight game");
        }
        for(var attempt=0;attempt<40&&!(carousel is {Bound: {} first}&&gameDetails.ContainsKey(first.Id));attempt++)await Task.Delay(100);
        Check(VisualElements<TextBlock>(Body).Any(x=>x.Text.StartsWith("Metacritic"))&&VisualElements<TextBlock>(Body).Any(x=>x.Text.StartsWith("Разработчик"))&&VisualElements<Border>(Body).Any(x=>x.Child is TextBlock {Text:"1 999 руб."}),"the banner adds the Metacritic score, studio and exact price once the store details arrive");
        gates["specials||1|"].SetResult(Rotate(1));gates["new||1|"].SetResult(Rotate(5));await Settle();await Task.Delay(300);await Settle();
        Check(GameShelves.Home.All(spec=>queries.Count(x=>x==spec.Sort+"|"+spec.Genre+"|1|")==1)&&peak<=3,"each shelf is requested once, at most three at a time, and fills in without rebuilding the page");
        Check(gameShelfViews.Values.All(view=>view.Row.Content is CatalogRow row&&row.Items.Length>0)&&new[]{"Популярное","Со скидкой","Новинки","Скоро выйдут","Экшены","Ролевые игры","Стратегии","Играть вместе"}.All(title=>FindVisual<TextBlock>(Body,x=>x.Text==title)!=null),"eight shelves are titled and filled: the four main ones and four genres");
        var arts=VisualElements<Border>(Body).Where(x=>x.Name=="GameCardArt"&&x.ActualWidth>0).ToArray();
        Check(arts.Length>0&&arts.All(x=>Math.Abs(x.ActualHeight-x.ActualWidth*215/460)<2),"capsules keep the 460 by 215 shape of Steam's store header");
        bool Shown(string name)=>VisualElements<FrameworkElement>(Body).Any(x=>x.Name==name&&x.IsVisible&&x.ActualWidth>0);
        Check(Shown("GamePriceChip")&&Shown("GameDiscount")&&Shown("GameSoon")&&Shown("GameReviewText"),"cards show the price, the discount, a coming-soon mark and the review score where Steam gives them");
        Check(VisualElements<TextBlock>(Body).Any(x=>x.Name=="GameReviewText"&&x.IsVisible&&x.Text=="👍 93%")&&VisualElements<TextBlock>(Body).Any(x=>x.Name=="GameReviewText"&&x.IsVisible&&x.Text=="👍 41%"),"the review score names the share of positive reviews");
        Check(VisualElements<Button>(Body).Count(b=>b.Name.StartsWith("GameGenre"))==GameGenres.All.Length&&VisualElements<Button>(Body).Any(b=>b.Name=="GameChipAll")&&VisualElements<Button>(Body).Count(b=>b.Name.StartsWith("GameChip"))==GameBrowse.Sorts.Length+1,"the sorts and the genres sit under the search as two rows of chips");
        Shot("games-home-dark");
        foreach(var width in new[]{510d,1280d})
        {
            await Size(width,width<700?800:900);
            var cards=VisualElements<Button>(Body).Where(b=>b.Tag is MediaItem item&&item.Section=="Игры").ToArray();
            Check(cards.Length>0&&cards.All(b=>b.ActualWidth>0)&&catalogColumns==WindowSizing.GameColumns(Body.ActualWidth),"game cards remain visible and follow the capsule columns at window width "+width);
            if(width<700)Shot("games-home-narrow");
        }
        var oldTheme=prefs.Light;prefs.Light=true;ApplyTheme();Render();await Settle();Shot("games-home-light");prefs.Light=oldTheme;ApplyTheme();Render();await Settle();

        // A genre narrows the list, a sort chip changes its order and keeps the genre, a shelf's "Ещё" opens it, and "Все" returns home.
        VisualElements<Button>(Body).Single(b=>b.Name=="GameGenrerpg").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
        Check(!GameHome&&gameGenre=="rpg"&&gameShelf=="popular"&&queries.Last()=="popular|rpg|1|"&&FindVisual<TextBlock>(PageHeader,x=>x.Name=="GameListHeading")?.Text=="Ролевые игры · Популярные игры"&&catalogList?.ItemTemplate==FindResource("GameRow"),"a genre pill opens that genre's popular games as a list of cards");
        Check(FiltersPanel.IsVisible&&VisualElements<Button>(FiltersPanel).Any(b=>b.Name=="GameGenrerpg")&&VisualElements<Button>(FiltersPanel).Any(b=>ChipText(b)=="Скидки"),"the chips and the genre rail stay pinned under the search while a list is open");
        Shot("games-list-dark");
        VisualElements<Button>(FiltersPanel).Single(b=>b.Name=="GameChipspecials").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
        Check(gameShelf=="specials"&&gameGenre=="rpg"&&queries.Last()=="specials|rpg|1|"&&FindVisual<TextBlock>(PageHeader,x=>x.Name=="GameListHeading")?.Text=="Ролевые игры · Игры со скидкой","a sort chip keeps the chosen genre");
        VisualElements<Button>(FiltersPanel).Single(b=>b.Name=="GameGenrerpg").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
        Check(gameGenre==""&&gameShelf=="specials"&&queries.Last()=="specials||1|","pressing the active genre again lifts the filter");
        OpenGameList("new");await Settle();
        Check(!GameHome&&queries.Last()=="new||1|"&&FindVisual<TextBlock>(PageHeader,x=>x.Name=="GameListHeading")?.Text=="Новинки","a shelf's \"Ещё\" opens that shelf as a list of game cards");
        OpenGameList("");await Settle();Check(GameHome&&carousel!=null&&gameShelfViews.Count==GameShelves.Home.Length,"the \"Все\" chip returns to the banner and shelves");

        var button=VisualElements<Button>(Body).First(card=>card.Tag is MediaItem item&&item.Id==game.Id&&!ReferenceEquals(card,carousel?.About));button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
        Check(current?.Section=="Игры"&&gameDescription?.Text.Contains("Загружаем")==true&&gameReleasesLoading,"a game opens its own page while descriptions and releases are still pending");
        Check(VisualElements<Border>(Body).Any(x=>x.Name=="GameHero")&&VisualElements<Border>(Body).Any(x=>x.Name=="GameAbout")&&FindVisual<StackPanel>(Body,x=>x.Name=="ReleaseSkeleton")!=null&&gameGallery is {Visibility:Visibility.Collapsed},"the page has a banner, a facts card and breathing placeholders where releases will be, and no empty gallery");
        Check(VisualElements<StackPanel>(Body).Any(x=>x.Name=="GameReview")&&VisualElements<FrameworkElement>(Body).Any(x=>x.Name=="GamePrice"),"the banner shows the list row's reviews and price before the store details arrive");
        Shot("game-detail-loading");
        metadata.SetResult(new(game with{Year=2011,Genre="Приключения, Головоломки",Description="Продолжение культовой головоломки от Valve: порталы, гели и тестовые камеры Aperture Science. Одиночная кампания и совместный режим на двоих — вместе с другом или с ИИ. Проверенное описание игры.",ImageUrl=game.ImageUrl},"Минимальные:\nОС: Windows 7 / Vista / XP\nПроцессор: 3.0 GHz P4, Dual Core 2.0 или лучше\nПамять: 2 GB RAM\nВидеокарта: DirectX 9 совместимая, 128 MB\nМесто на диске: 13 GB","Valve","Valve","399 руб.",95,"799 руб.",50,ShotList(game.Id,4,8),GameRussian.Voiced,["Для одного игрока","Кооператив","Полная поддержка контроллеров"],12,"18 апр. 2011 г.",false,new GameReview(98,390_000,9)));await Settle();
        Check(gameDescription?.Text.StartsWith("Продолжение культовой")==true&&gameDescription.Visibility==Visibility.Visible&&VisualElements<TextBlock>(Body).Any(x=>x.Text=="2 GB RAM")&&gameReleasesLoading,"metadata and requirements appear without waiting for torrent sources");
        Check(VisualElements<TextBlock>(Body).Any(x=>x.Name=="GameYear"&&x.Text=="2011")&&VisualElements<TextBlock>(Body).Any(x=>x.Text.Contains("Metacritic"))&&VisualElements<Border>(Body).Any(x=>x.Child is TextBlock {Text:"399 руб."}),"the banner shows the year, the Metacritic score and the price from the store details");
        Check(VisualElements<TextBlock>(Body).Any(x=>x.Text=="−50%")&&VisualElements<Border>(Body).Any(x=>x.Name=="GameRussian"&&x.Child is TextBlock {Text:"Русская озвучка"})&&VisualElements<TextBlock>(Body).Any(x=>x.Text=="12+"),"the banner marks the discount, the Russian voice-over and the age limit");
        Check(VisualElements<TextBlock>(Body).Any(x=>x.Text=="Русский язык")&&VisualElements<TextBlock>(Body).Any(x=>x.Text=="Интерфейс, субтитры и озвучка")&&VisualElements<TextBlock>(Body).Any(x=>x.Text=="Для одного игрока, Кооператив, Полная поддержка контроллеров")&&VisualElements<TextBlock>(Body).Any(x=>x.Text.StartsWith("Крайне положительные · 98% из")),"the facts card lists the language, the modes and Steam's reviews");
        for(var attempt=0;attempt<40&&(gameShotImage?.Source==null);attempt++)await Task.Delay(100);
        if(!prefs.LiteMode)
        {
            Check(gameGallery is {Visibility:Visibility.Visible}&&gameShotCounter?.Text=="1 / 8"&&VisualElements<Button>(Body).Count(b=>b.Name.StartsWith("GameShot")&&b.Name.Length>"GameShot".Length&&char.IsDigit(b.Name["GameShot".Length]))==8&&gameShotImage?.Source!=null,"the gallery shows the first screenshot large and every thumbnail under it");
            VisualElements<Button>(Body).Single(b=>b.Name=="GameShotNext").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
            Check(gameShotCounter?.Text=="2 / 8"&&gameShotIndex==1,"the next arrow moves to the second screenshot");
            VisualElements<Button>(Body).Single(b=>b.Name=="GameShot6").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
            Check(gameShotCounter?.Text=="6 / 8"&&gameShotIndex==5,"a thumbnail jumps to its screenshot");
            VisualElements<Button>(Body).Single(b=>b.Name=="GameShotPrevious").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));VisualElements<Button>(Body).Single(b=>b.Name=="GameShotPrevious").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
            Check(gameShotCounter?.Text=="4 / 8","the previous arrow steps back");
            VisualElements<Button>(Body).Single(b=>b.Name=="GameShot1").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
            VisualElements<Button>(Body).Single(b=>b.Name=="GameShotPrevious").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
            Check(gameShotCounter?.Text=="8 / 8","the gallery wraps around from the first screenshot to the last");
            VisualElements<Button>(Body).Single(b=>b.Name=="GameShot1").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));await Settle();
        }
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
        if(gameGallery is {Visibility:Visibility.Visible} gallery&&gameScroll!=null){gallery.BringIntoView();await Settle();gameScroll.ScrollToVerticalOffset(Math.Max(0,gallery.TransformToAncestor((FrameworkElement)gameScroll.Content).Transform(new Point()).Y-20));await Settle();Shot("game-detail-gallery");}
        gameScroll!.ScrollToVerticalOffset(gameScroll.ScrollableHeight);await Settle();Shot("game-detail-releases");gameScroll.ScrollToVerticalOffset(0);await Settle();
        await Size(700,900);Shot("game-detail-narrow");
        // A wide window lays the releases out as a table with sortable columns instead of cards.
        await Size(1760,900);gameScroll!.ScrollToVerticalOffset(0);await Settle();Shot("game-detail-wide");
        Check(VisualElements<Button>(Body).Any(b=>b.Content is string heading&&heading.StartsWith("ОТДАЮТ"))&&VisualElements<Button>(Body).Any(b=>b.Content is string heading&&heading.StartsWith("РАЗМЕР")),"a wide page lists releases in a table with sortable columns");
        gameScroll.ScrollToVerticalOffset(gameScroll.ScrollableHeight);await Settle();Shot("game-detail-releases-wide");gameScroll.ScrollToVerticalOffset(0);
        await Size(1280,900);
        var old=prefs.LiveFavorites.ToList();var oldIds=new HashSet<int>(prefs.Favorites);
        try{ToggleSaved(current!);Check(SavedItems().Any(item=>item.Section=="Игры"&&item.Id==game.Id),"saved collection includes a game alongside films and series");}
        finally{prefs.LiveFavorites=old;prefs.Favorites=oldIds;prefs.Save();}
        GameBack();await Settle();Check(GameHome,"going back from a game returns to the home page");
        Search.Text="Portal";SubmitSearch(SearchSubmitButton,new RoutedEventArgs());await Settle();Check(gameQuery=="Portal"&&queries.Last()=="popular||1|Portal"&&section=="Игры"&&!GameHome&&FindVisual<TextBlock>(PageHeader,x=>x.Name=="GameListHeading")?.Text=="Результаты поиска","game search stays in Games and does not start a film/series search");
        ClearSearch(ClearSearchButton,new RoutedEventArgs());await Settle();Check(gameQuery==""&&Search.Text==""&&GameHome,"clearing game search restores the home page");

        var blocked=new TaskCompletionSource<GameCatalogPage>(TaskCreationOptions.RunContinuationsAsynchronously);CancellationToken gameToken=default;
        gameCatalogProvider=(_,ct)=>{gameToken=ct;return blocked.Task.WaitAsync(ct);};RefreshGames();await Settle();
        Check(gameShelfLoading.Count>0,"refreshing the home page requests the shelves again");
        ShowCatalogSection("Фильмы");await Settle();Check(gameToken.IsCancellationRequested&&gameShelfLoading.Count==0&&gameShelfViews.Count==0,"leaving Games cancels its unfinished shelf requests");
        await File.WriteAllTextAsync(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Checks=checks,WindowsGames=true,GamepadIcon=true,HomeBanner=true,Shelves=true,Genres=true,Prices=true,Reviews=true,Gallery=true,GameCardLikeFilm=true,SeparateSearch=true,ResponsiveInput=true,DownloadAction=true,SavedGames=true}));Close();
    }
}
