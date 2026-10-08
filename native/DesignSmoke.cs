using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Kachalka;

public partial class MainWindow
{
    bool designCatalogFallback;
    bool designFixedViewport;
    int designCachedPosters;
    string? designCatalogFile;

    public void PrepareDesignSmoke()
    {
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KACHALKA_DATA")))
            throw new InvalidOperationException("Design smoke needs an isolated KACHALKA_DATA directory.");
        if(downloads.Items.Count!=0)throw new InvalidOperationException("Design smoke needs an empty test download queue.");
        designFixedViewport=true;
        // Prevent completion of the startup request from replacing the local fixture.
        liveKey="design-smoke-preparing";liveRequest?.Cancel();ResetDiscoveryData();searchDelay.Stop();
        var folder=Path.Combine(Preferences.DataDir,"catalog");
        IReadOnlyList<MediaItem> cached=[];
        if(Directory.Exists(folder))
        {
            foreach(var file in Directory.EnumerateFiles(folder,"*.html").OrderByDescending(File.GetLastWriteTimeUtc).Take(12))
            {
                try{cached=LiveCatalog.Parse(File.ReadAllBytes(file),"Фильмы");}catch(IOException){continue;}
                if(cached.Count==0)continue;
                designCatalogFile=Path.GetFileName(file);break;
            }
        }
        designCatalogFallback=cached.Count==0;
        if(designCatalogFallback)cached=BundledCatalog.Page("Фильмы",1).Items.Select(x=>x with{PageUrl="https://example.invalid/design/catalog/"+x.Id}).ToArray();
        MediaItem CachePoster(MediaItem original)
        {
            var item=original;
            if(!string.IsNullOrWhiteSpace(item.ImageUrl))
            {
                var path=Path.Combine(Preferences.DataDir,"covers",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.ImageUrl)))+".img");
                var loaded=false;
                if(File.Exists(path))
                {
                    try
                    {
                        using var stream=File.OpenRead(path);
                        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=280;image.StreamSource=stream;image.EndInit();image.Freeze();
                        coverCache[item.ImageUrl]=image;designCachedPosters++;loaded=true;
                    }
                    catch(Exception error) when(error is IOException or NotSupportedException or System.IO.FileFormatException){ }
                }
                if(!loaded)item=item with{ImageUrl=null};
            }
            cardMetadata[item.Id]=Task.FromResult(item);requestedDetails.Add(item.Id);return item;
        }
        var cards=cached.Select(CachePoster).ToList();
        PrepareDiscoverySmokeRegions("Фильмы",cards);
        catalogPages["Фильмы|sort-date|1"]=new(cards.ToArray(),false,CatalogChoices.Genres,CatalogChoices.Countries);
        catalogPages["Фильмы||1"]=new(cards.OrderByDescending(CatalogScore).ToArray(),false,CatalogChoices.Genres,CatalogChoices.Countries);
        var shows=BundledCatalog.Page("Сериалы",1).Items.Select((x,index)=>CachePoster(x with{PageUrl="https://example.invalid/design/catalog/"+x.Id,Genre=index%3==0?"\nбоевик\r\n, \n криминал, \n драма\n":index%3==1?"детектив":""})).ToArray();
        foreach(var show in shows){cardMetadata[show.Id]=Task.FromResult(show);requestedDetails.Add(show.Id);}
        catalogPages["Сериалы|sort-date|1"]=new(shows,false,CatalogChoices.Genres,CatalogChoices.Countries);
        catalogPages["Сериалы||1"]=new(shows,false,CatalogChoices.Genres,CatalogChoices.Countries);
        PrepareDiscoverySmokeRegions("Сериалы",shows);
        searchProvider=(kind,_,_)=>Task.FromResult<IReadOnlyList<MediaItem>>(kind=="Фильмы"?cards:shows);
        section="Фильмы";current=null;favoritesOnly=false;ResetCatalogFilters();livePage=1;Search.Text="";searchDelay.Stop();submittedQuery="";searchCategory="";
        liveItems=cards;liveKey="design-smoke-prepared";liveLoading=false;liveError="";
    }

    public async Task DesignSmokeTest(string output)
    {
        Directory.CreateDirectory(output);
        var originalLight=prefs.Light;
        var checks=new List<object>();
        var releaseToolbarChecks=new List<object>();
        // Render a full desktop viewport even when the hosted runner's monitor is smaller.
        MaxWidth=1800;MaxHeight=1000;MinWidth=360;MinHeight=300;
        async Task Settle()
        {
            UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(90);UpdateLayout();
        }
        async Task Size(double width,double height)
        {
            // Windows Server's virtual monitor also limits native max-track size.
            // A fixture minimum forces this specific viewport without changing app defaults.
            MinWidth=Math.Min(width,MaxWidth-24);MinHeight=Math.Min(height,MaxHeight-24);
            Width=MinWidth;Height=MinHeight;await Settle();
        }
        void Shot(string name,double rasterScale=1)
        {
            UpdateLayout();
            var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(ActualWidth*rasterScale)),Math.Max(1,(int)Math.Ceiling(ActualHeight*rasterScale)),96*rasterScale,96*rasterScale,PixelFormats.Pbgra32);bitmap.Render(this);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name+".png"));png.Save(stream);
        }
        ComboBox QualityFilter()=>FindVisual<ComboBox>(Body,x=>AutomationProperties.GetName(x)=="Раздачи: Качество")??throw new Exception("Quality filter is missing.");
        void CheckQualityInk()
        {
            var hide=FindVisual<CheckBox>(Body,x=>AutomationProperties.GetName(x)=="Скрыть плохое качество")??throw new Exception("Poor quality toggle is missing.");
            if(hide.Foreground is not SolidColorBrush ink||ink.Color!=((SolidColorBrush)FindResource("Text")).Color)
                throw new Exception("Poor quality toggle does not follow the current theme.");
        }
        void CheckFilter(string stage)
        {
            CheckQualityInk();
            var filter=QualityFilter();
            if(filter.SelectedItem?.ToString()!="Full HD")throw new Exception(stage+": selected Full HD filter was lost.");
            if(!filter.IsVisible||filter.ActualWidth<=0)throw new Exception(stage+": quality filter is not visible.");
            var buttons=VisualElements<Button>(Body).Where(x=>x.Tag is SourceEntry).ToArray();
            if(buttons.Length!=1||buttons[0].Tag is not SourceEntry entry||entry.Quality!="Full HD")throw new Exception(stage+": quality filter did not leave exactly the Full HD release.");
            if(!buttons[0].IsVisible||buttons[0].ActualWidth<=0)throw new Exception(stage+": download action is not visible.");
        }
        void CheckHero(string stage)
        {
            var title=FindVisual<TextBlock>(Body,x=>x.Name=="DetailTitle")??throw new Exception(stage+": DetailTitle was not found.");
            var synopsis=detailSynopsis??throw new Exception(stage+": detailSynopsis was not found.");
            foreach(var (name,element) in new[]{("title",title),("synopsis",synopsis)})
            {
                var origin=element.TransformToAncestor(Body).Transform(new Point());
                if(element.ActualWidth<=0||element.ActualWidth>Body.ActualWidth+1||origin.X<-.5||origin.X+element.ActualWidth>Body.ActualWidth+1)
                    throw new Exception($"{stage}: {name} overflows body ({origin.X:F1}+{element.ActualWidth:F1}>{Body.ActualWidth:F1}).");
            }
            checks.Add(new{Stage=stage,WindowWidth=Math.Round(ActualWidth),BodyWidth=Math.Round(Body.ActualWidth),TitleWidth=Math.Round(title.ActualWidth),SynopsisWidth=Math.Round(synopsis.ActualWidth),Filter="Full HD",DownloadActionVisible=true});
        }
        async Task RevealDownload(string name)
        {
            var button=FindVisual<Button>(Body,x=>x.Tag is SourceEntry)??throw new Exception("Download button is missing.");
            button.BringIntoView();await Settle();
            var origin=button.TransformToAncestor(Body).Transform(new Point());
            if(origin.X<-.5||origin.X+button.ActualWidth>Body.ActualWidth+1||origin.Y+button.ActualHeight<0||origin.Y>Body.ActualHeight)
                throw new Exception(name+": download button is outside the visible viewport.");
            Shot(name);
        }
        try
        {
            await Settle();liveKey=CurrentCatalogKey;
            await Size(1760,950);
            Render();await Settle();
            if(TextOptions.GetTextFormattingMode(this)!=TextFormattingMode.Ideal||TextOptions.GetTextRenderingMode(this)!=TextRenderingMode.Grayscale)throw new Exception("Text does not use smooth grayscale fractional metrics.");
            var wheelScrolling=await CheckWheelScrolling();
            MinWidth=360;MinHeight=300;
            var searchSettings=await CheckSearchAndSettings(output);
            var uiActions=await CheckUiActions(output);
            await Size(1760,950);
            System.Windows.Input.Keyboard.ClearFocus();
            Search.Text="";searchDelay.Stop();liveKey=CurrentCatalogKey;
            foreach(var (item,index) in liveItems.Take(2).Select((item,index)=>(item,index)))
            {
                liveReleases[item.Id]=[new("quality-preview-"+index,item.Title+(index==0?" WEB-DL 1080p":" WEB-DL 2160p"),"Preview fixture","","https://example.invalid/preview.torrent",null)];
                releaseViews[item.Id]=new(){ReceivedUtc=DateTime.UtcNow};
            }
            prefs.Light=true;ApplyTheme();current=null;Render();await Settle();Shot("catalog-light");
            var backgroundLight=CheckBackgroundTheme();
            CheckDiscoveryShelves((ok,message)=>{if(!ok)throw new Exception(message);},"light-movie-shelves");
            Shot("catalog-light-125",1.25);
            var catalogColumnsLight=catalogColumns;
            prefs.Light=false;ApplyTheme();Render();await Settle();Shot("catalog-dark");
            var backgroundDark=CheckBackgroundTheme();
            CheckDiscoveryShelves((ok,message)=>{if(!ok)throw new Exception(message);},"dark-movie-shelves");
            foreach(var scale in new[]{1.2,1.25,1.5})Shot("catalog-dark-"+(int)(scale*100),scale);
            foreach(var badge in VisualElements<Border>(Body).Where(x=>x.Name=="PosterQualityBadge"&&x.IsVisible))
            {
                if(badge.HorizontalAlignment!=HorizontalAlignment.Left||badge.VerticalAlignment!=VerticalAlignment.Top||badge.Parent is not Grid poster||poster.Children.OfType<Image>().Count()!=1)throw new Exception("Quality badge is not on the poster's top-left corner.");
            }
            if(!VisualElements<Border>(Body).Any(x=>x.Name=="PosterQualityBadge"&&x.IsVisible))throw new Exception("Known quality badge is missing.");
            CheckCatalogFilterLine((ok,message)=>{if(!ok)throw new Exception(message);},"cinematic-desktop");
            if(ActualWidth<1700||ActualHeight<900||discoveryHero is not {ActualHeight:>180}||discoveryHero.Children.Count!=2||discoveryShelf is not {ActualWidth:>500})throw new Exception($"Cinematic desktop catalog is missing its banners or curated row ({ActualWidth}x{ActualHeight}, hero={discoveryHero?.ActualHeight}, shelf={discoveryShelf?.ActualWidth}).");
            if(VisualElements<UIElement>(Body).Any(x=>x.Effect!=null))throw new Exception("Cinematic catalog adds an expensive blur or shadow effect.");
            var bannerCornerChecks=CheckBannerCorners(output);
            var featurePosterWidths=VisualElements<Image>(discoveryHero!).Select(x=>(x.Source as BitmapSource)?.PixelWidth??0).ToArray();
            var movies=liveItems;
            section="Сериалы";liveItems=catalogPages["Сериалы|sort-date|1"].Items;liveKey=CurrentCatalogKey;
            Render();await Settle();
            void CheckSeriesLayout()
            {
                var genres=VisualElements<TextBlock>(Body).Where(x=>x.Name=="CardGenreText"&&x.IsVisible).ToArray();
                if(genres.Length==0||genres.Any(x=>x.Text.Contains('\n')||x.Text.Contains('\r')||x.ActualHeight>17))throw new Exception("Series genres create multiline poster cards.");
                var posters=VisualElements<Border>(discoveryShelf!).Where(x=>x.Name=="CardPoster").Select(x=>x.TransformToAncestor(discoveryShelf!).TransformBounds(new Rect(new Point(),x.RenderSize))).ToArray();
                if(posters.Length<2||posters.Max(x=>x.Top)-posters.Min(x=>x.Top)>1||posters.Max(x=>x.Height)-posters.Min(x=>x.Height)>1)throw new Exception("Series shelf posters are not aligned at the top.");
            }
            CheckSeriesLayout();
            // Reproduce metadata arriving after the row was already measured.
            foreach(var item in liveItems.Take(8))item.SetScores(item.Kinopoisk,item.Imdb,"\nбоевик\r\n, \n криминал, \n драма\n");
            await Settle();CheckSeriesLayout();CheckDiscoveryShelves((ok,message)=>{if(!ok)throw new Exception(message);},"dark-series-shelves");Shot("series-catalog-dark");
            prefs.Light=true;ApplyTheme();Render();await Settle();CheckSeriesLayout();CheckDiscoveryShelves((ok,message)=>{if(!ok)throw new Exception(message);},"light-series-shelves");Shot("series-catalog-light");
            section="Фильмы";liveItems=movies;liveKey=CurrentCatalogKey;prefs.Light=false;ApplyTheme();Render();await Settle();
            var preview=liveItems.FirstOrDefault(x=>x.ImageUrl!=null)?.ImageUrl;
            var movie=new MediaItem(-987654320,"За пределами тишины: невероятное путешествие через время, которое начинается с одного случайного письма","Фильмы","Приключения · Драма",2026,"8,7","8,5","#526B69")
            {
                PageUrl="https://example.invalid/design/long-detail",ImageUrl=preview,
                Description="После неожиданной находки в старом доме молодая исследовательница отправляется в небольшую обсерваторию на краю света. Одно письмо связывает судьбы людей, живших в разные эпохи, и постепенно меняет её представление о семье, памяти и времени. Вместе с новым знакомым ей предстоит восстановить исчезнувший маршрут, научиться доверять друг другу и решить, какие воспоминания стоит сохранить. Это длинное описание специально проверяет перенос текста, доступность действий и поведение карточки на небольшом экране при увеличенном масштабе Windows."
            };
            var release=new SourceEntry("design-1080","За пределами тишины (2026) WEB-DL 1080p H.264 DUB subs","Тестовый источник","https://example.invalid/design/release","https://example.invalid/design/1080.torrent",null,8_400_000_000,342);
            var ultra=release with{Id="design-2160",Title="За пределами тишины (2026) BluRay 2160p H.265 HDR10 Original subs",Size=24_100_000_000,Seeds=128};
            var small=release with{Id="design-720",Title="За пределами тишины (2026) WEB-DL 720p H.264 MVO",Size=2_600_000_000,Seeds=87};
            requestedDetails.Add(movie.Id);cardMetadata[movie.Id]=Task.FromResult(movie);liveReleases[movie.Id]=[release,ultra,small];
            var sourceView=new ReleaseView{Saved=true,SavedUtc=DateTime.UtcNow.AddDays(-1),ReceivedUtc=DateTime.UtcNow, Sources=[new("RuTor",SourceState.Ready,3,DateTime.UtcNow,DateTime.UtcNow),new("NNM-Club",SourceState.Empty,0,DateTime.UtcNow,DateTime.UtcNow),new("MegaPeer",SourceState.TimedOut,0,DateTime.UtcNow,DateTime.UtcNow.AddDays(-2)),new("Онлайн-индекс",SourceState.Unavailable)]};
            releaseViews[movie.Id]=sourceView;
            current=movie;Render();await Settle();CheckQualityInk();Shot("detail-wide-dark");
            prefs.Light=true;ApplyTheme();Render();await Settle();CheckQualityInk();Shot("detail-wide-light");
            QualityFilter().SelectedItem="Full HD";await Settle();CheckFilter("selected");
            Render();await Settle();CheckFilter("rendered");
            releaseToolbarChecks.Add(CheckReleaseToolbar("collapsed-desktop",false,false));
            var moreReleaseFilters=FindVisual<Button>(Body,x=>AutomationProperties.GetName(x)=="Показать дополнительные фильтры раздач")??throw new Exception("Additional release filters are missing.");
            moreReleaseFilters.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();
            var sourceFilter=FindVisual<ComboBox>(Body,x=>AutomationProperties.GetName(x)=="Раздачи: Источник")??throw new Exception("Per-tracker release filter is missing.");
            sourceFilter.SelectedItem="Тестовый источник";await Settle();CheckFilter("source-filter");
            var sourceToggle=FindVisual<Button>(Body,b=>AutomationProperties.GetName(b)=="Показать состояние источников")??throw new Exception("Source status toggle missing.");
            sourceToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();
            if(!sourceView.Expanded||!VisualElements<TextBlock>(Body).Any(x=>x.Text.Contains("Не ответил вовремя")))throw new Exception("Per-source timeout is not visible.");
            releaseToolbarChecks.Add(CheckReleaseToolbar("expanded-light",false,true));
            VisualElements<TextBlock>(Body).Last(x=>x.Text=="Временно недоступен").BringIntoView();await Settle();Shot("sources-light");
            prefs.Light=false;ApplyTheme();RefreshDetail(movie.Id);await Settle();Shot("sources-dark");
            var qualityBefore=QualityFilter();qualityBefore.IsDropDownOpen=true;await Settle();
            sourceView.Checking=true;RefreshDetail(movie.Id);
            if(!ReferenceEquals(qualityBefore,QualityFilter())||!qualityBefore.IsDropDownOpen)throw new Exception("Source progress interrupted an open filter.");
            if(FindVisual<Button>(Body,b=>AutomationProperties.GetName(b)=="Обновить варианты загрузки")?.IsEnabled!=false||releaseLoadingIndicator?.Visibility!=Visibility.Visible)
                throw new Exception("Source toolbar did not update in place while a filter remained open.");
            qualityBefore.IsDropDownOpen=false;await Settle();CheckFilter("after-source-progress");
            var sourceRetry=FindVisual<Button>(Body,b=>AutomationProperties.GetName(b)=="Обновить варианты загрузки")??throw new Exception("Source retry action missing.");
            if(sourceRetry.IsEnabled)throw new Exception("Source retry should be disabled during a scan.");
            sourceView.Checking=false;RefreshDetail(movie.Id);await Settle();CheckFilter("source-complete");
            if(FindVisual<ComboBox>(Body,x=>AutomationProperties.GetName(x)=="Раздачи: Источник")?.SelectedItem?.ToString()!="Тестовый источник")throw new Exception("Source progress lost the per-tracker filter.");
            releaseToolbarChecks.Add(CheckReleaseToolbar("completed-dark",false,true));
            if(FindVisual<Button>(Body,b=>AutomationProperties.GetName(b)=="Обновить варианты загрузки")?.IsEnabled!=true)throw new Exception("Source retry did not enable after completion.");
            var sourceScroller=FindVisual<ScrollViewer>(Body,_=>true)??throw new Exception("Detail scroller missing.");sourceScroller.ScrollToVerticalOffset(60);await Settle();var sourceOffset=sourceScroller.VerticalOffset;
            RefreshDetail(movie.Id);await Settle();
            if(Math.Abs((FindVisual<ScrollViewer>(Body,_=>true)?.VerticalOffset??0)-sourceOffset)>1)throw new Exception("Source progress lost the reading position.");
            await Size(510,820);VisualElements<TextBlock>(Body).Last(x=>x.Text=="Временно недоступен").BringIntoView();await Settle();releaseToolbarChecks.Add(CheckReleaseToolbar("expanded-510",false,true));Shot("sources-510");
            sourceView.Expanded=false;prefs.Light=true;ApplyTheme();Render();await Settle();
            releaseToolbarChecks.Add(CheckReleaseToolbar("collapsed-510",false,false));
            var noSourcesMovie=movie with{Id=-987654317,Title="Проверка недоступных раздач",PageUrl="https://example.invalid/design/source-timeout"};
            requestedDetails.Add(noSourcesMovie.Id);cardMetadata[noSourcesMovie.Id]=Task.FromResult(noSourcesMovie);liveReleases[noSourcesMovie.Id]=[];
            var noSourcesView=new ReleaseView{Checking=true,Sources=[new("Первый источник",SourceState.Searching),new("Второй источник",SourceState.Searching)]};releaseViews[noSourcesMovie.Id]=noSourcesView;
            current=noSourcesMovie;Render();await Settle();releaseToolbarChecks.Add(CheckReleaseToolbar("empty-checking",true,false));
            noSourcesView.Checking=false;noSourcesView.Sources=[new("Первый источник",SourceState.TimedOut),new("Второй источник",SourceState.Unavailable)];RefreshDetail(noSourcesMovie.Id);await Settle();
            releaseToolbarChecks.Add(CheckReleaseToolbar("empty-unavailable",false,false));
            if(releaseSourceSummary?.Text!="Не ответили: 2"||!VisualElements<TextBlock>(Body).Any(x=>x.Text=="Подходящих раздач пока нет"))throw new Exception("Unavailable sources did not leave an honest retryable empty state.");
            current=movie;Render();await Settle();
            foreach(var width in new[]{720d,510d})
            {
                await Size(width,820);CheckFilter("resize-"+width);CheckHero("resize-"+width);
                var scroller=FindVisual<ScrollViewer>(Body,_=>true);scroller?.ScrollToTop();await Settle();Shot("detail-"+width);
                await RevealDownload("release-"+width);
            }
            const string shortDescription="Случайное письмо приводит исследовательницу в старую обсерваторию. Там она узнаёт историю своей семьи и отправляется в путешествие, где каждое решение меняет будущее. Ей предстоит найти пропавший дневник и вернуть доверие близких.";
            var shortMovie=movie with{Id=-987654319,Title="Письмо из прошлого",Description=shortDescription};
            requestedDetails.Add(shortMovie.Id);cardMetadata[shortMovie.Id]=Task.FromResult(shortMovie);liveReleases[shortMovie.Id]=[release,ultra,small];
            current=shortMovie;Render();await Settle();
            var descriptionButton=FindVisual<Button>(Body,x=>x.Name=="DescriptionToggle")??throw new Exception("Short description: expansion control is missing.");
            if(!descriptionButton.IsVisible||descriptionButton.ActualWidth<=0)throw new Exception("Short description: clipped text cannot be expanded at 510 px.");
            if(detailSynopsis==null||detailSynopsis.MaxHeight>80.1)throw new Exception("Short description: fixture did not start in its collapsed state.");
            descriptionButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();
            var expandedSynopsis=detailSynopsis??throw new Exception("Short description: synopsis disappeared after expansion.");
            if(!double.IsPositiveInfinity(expandedSynopsis.MaxHeight)||expandedSynopsis.ActualHeight<=80||expandedSynopsis.Text!=shortDescription)
                throw new Exception("Short description: expansion did not reveal the complete text beyond 80 px.");
            var shortDescriptionExpandedHeight=Math.Round(expandedSynopsis.ActualHeight);
            FindVisual<ScrollViewer>(Body,_=>true)?.ScrollToTop();await Settle();Shot("short-description-expanded");
            var show=movie with{Id=-987654318,Title="Разделение",OriginalTitle="Severance",Section="Сериалы",PageUrl="https://example.invalid/design/show"};
            var firstEpisode=new SourceEntry("series-s01","Severance S01E01 WEB-DL 1080p","EZTV","","magnet:?xt=urn:btih:0123456789012345678901234567890123456789",null,900_000_000,15);
            var secondEpisode=firstEpisode with{Id="series-s02",Title="Severance S02E10 WEB-DL 1080p",Seeds=35};
            requestedDetails.Add(show.Id);cardMetadata[show.Id]=Task.FromResult(show);liveReleases[show.Id]=[firstEpisode,secondEpisode];
            section="Сериалы";current=show;Render();await Settle();
            var seasonFilter=FindVisual<ComboBox>(Body,x=>AutomationProperties.GetName(x)=="Раздачи: Сезон")??throw new Exception("Series season filter is missing.");
            seasonFilter.SelectedItem="2 сезон";await Settle();
            var shownReleases=VisualElements<Button>(Body).Where(x=>x.IsVisible&&x.Tag is SourceEntry).Select(x=>(SourceEntry)x.Tag).ToArray();
            if(shownReleases.Length!=1||shownReleases[0].Id!="series-s02")throw new Exception("Series season filter selected the wrong episode.");
            FindVisual<ScrollViewer>(Body,_=>true)?.ScrollToTop();await Settle();Shot("series-season-filter");
            await Size(1760,950);section="Загрузки";current=null;Render();await Settle();
            if(downloads.Items.Count!=0||downloads.EngineCreated)throw new Exception("Design smoke unexpectedly created a download.");
            Shot("downloads-empty");
            var fixtureNow=DateTime.UtcNow;
            var downloadCards=liveItems.Where(x=>x.ImageUrl!=null&&coverCache.ContainsKey(x.ImageUrl)).Concat(liveItems).DistinctBy(x=>x.Id).Take(3).ToArray();
            DownloadItem VisualDownload(string name,int index)
            {
                var card=downloadCards.ElementAtOrDefault(index);
                var slug=card?.OnlineId?.Split(':').ElementAtOrDefault(1);
                var page=slug==null?card?.PageUrl:LiveCatalog.Base+(card!.Section=="Сериалы"?"/tvseries/":"/movies/")+slug;
                var source=card==null?name:$"{card.OriginalTitle??card.Title} ({card.Year}) WEB-DL {(name.Contains("2160p")?"2160p":"1080p")}";
                return new(){Name=source,Folder=@"C:\Downloads\Качалка",Paused=false,AddedUtc=fixtureNow.AddSeconds(-index),MediaTitle=card?.Title,MediaSection=card?.Section,MediaYear=card?.Year??0,ImageUrl=card?.ImageUrl,MediaPageUrl=page};
            }
            var activeDownload=VisualDownload("Big Buck Bunny · 1080p",0);
            DownloadPresentation.Apply(activeDownload,new(MonoTorrent.Client.TorrentState.Downloading,false,42,2L*1024*1024*1024,1800*1024,128*1024,12,5,fixtureNow.AddMinutes(-3),fixtureNow),fixtureNow);
            var waitingDownload=VisualDownload("Sintel · 2160p",1);
            DownloadPresentation.Apply(waitingDownload,new(MonoTorrent.Client.TorrentState.Downloading,false,8,4L*1024*1024*1024,0,0,3,0,fixtureNow.AddMinutes(-2),null),fixtureNow);
            var completedDownload=VisualDownload("Tears of Steel · 1080p",2);
            DownloadPresentation.Apply(completedDownload,new(MonoTorrent.Client.TorrentState.Seeding,false,100,1024L*1024*1024,0,256*1024,2,0,fixtureNow.AddMinutes(-20),fixtureNow),fixtureNow);
            downloads.Items.Add(activeDownload);downloads.Items.Add(waitingDownload);downloads.Items.Add(completedDownload);
            prefs.Light=true;ApplyTheme();Render();await Settle();Shot("downloads-light");
            prefs.Light=false;ApplyTheme();Render();await Settle();
            var expectedInk=((SolidColorBrush)FindResource("Text")).Color;
            foreach(var block in VisualElements<TextBlock>(Body).Where(x=>x.Text==activeDownload.DisplayName||x.Text==activeDownload.Status))
                if(block.Foreground is not SolidColorBrush ink||ink.Color!=expectedInk)throw new Exception("Download text does not follow dark theme.");
            var releaseInk=((SolidColorBrush)FindResource("Muted")).Color;
            foreach(var block in VisualElements<TextBlock>(Body).Where(x=>x.Text==activeDownload.Name&&AutomationProperties.GetName(x)=="Название раздачи"))
                if(block.Foreground is not SolidColorBrush ink||ink.Color!=releaseInk)throw new Exception("Secondary release name does not follow dark theme.");
            Shot("downloads-dark");
            foreach(var width in new[]{510d,360d})
            {
                await Size(width,820);Render();await Settle();Shot("downloads-"+width);
                var downloadStats=VisualElements<TextBlock>(Body).Where(x=>x.Text==activeDownload.Stats||x.Text==waitingDownload.Stats).ToArray();
                if(downloadStats.Length==0)throw new Exception("Download telemetry is not visible.");
                foreach(var block in downloadStats)
                {
                    var origin=block.TransformToAncestor(Body).Transform(new Point());
                    if(origin.X<-.5||origin.X+block.ActualWidth>Body.ActualWidth+1)throw new Exception($"Download telemetry overflows {width} px window.");
                }
                var actions=VisualElements<Button>(Body).Where(x=>x.IsVisible&&x.Tag is DownloadItem&&AutomationProperties.GetName(x) is "Пауза" or "Подробнее" or "Открыть папку" or "Удалить из загрузок" or "Удалить файлы").ToArray();
                if(actions.Length<5)throw new Exception("Download row actions are missing.");
                foreach(var action in actions)
                {
                    var bounds=action.TransformToAncestor(Body).TransformBounds(new Rect(new Point(),action.RenderSize));
                    if(bounds.Width<=0||bounds.Left<-.5||bounds.Right>Body.ActualWidth+1)throw new Exception($"Download action overflows {width} px window: "+AutomationProperties.GetName(action));
                }
            }
            if(downloads.EngineCreated)throw new Exception("Download visual fixtures unexpectedly started an engine.");
            downloads.Items.Clear();
            File.WriteAllText(Path.Combine(output,"design.json"),JsonSerializer.Serialize(new
            {
                CatalogFallback=designCatalogFallback,CatalogCacheFile=designCatalogFile,CatalogCount=liveItems.Count,CachedPosters=designCachedPosters,CatalogColumns=catalogColumnsLight,
                ExternalSourcesRequired=false,FixtureHasCachedPoster=preview!=null,FeaturePosterWidths=featurePosterWidths,FilterPersistedAfterRender=true,FilterPersistedAfterResize=true,DownloadStarted=false,EngineCreated=downloads.EngineCreated,
                ShortDescriptionLength=shortDescription.Length,ShortDescriptionExpanded=true,ShortDescriptionExpandedHeight=shortDescriptionExpandedHeight,SeriesSeasonFilter=true,
                ReleaseToolbarChecks=releaseToolbarChecks,
                DownloadTelemetryFitsNarrowWindow=true,DownloadTextFollowsTheme=true,
                BackgroundTheme=new{Light=backgroundLight,Dark=backgroundDark},
                TextRendering="Grayscale",TextFormatting="Ideal",FractionalRasterScales=new[]{1.2,1.25,1.5},QualityBadgeOnPoster=true,SeriesPosterAlignment=true,MultilineSeriesMetadataContained=true,
                SearchAndSettings=searchSettings,
                UiActions=uiActions,
                WheelScrolling=wheelScrolling,
                SourceFailureVisible=true,SourceProgressPreservesOpenFilter=true,SourceProgressPreservesScroll=true,SourceRetryState=true,
                WidthChecks=checks,Screens=new[]{"sources-light","sources-dark","sources-510","search-light","search-dark","search-small","search-minimum","settings-light","settings-dark","settings-small","settings-minimum","catalog-light","catalog-dark","detail-wide-light","detail-wide-dark","detail-720","release-720","detail-510","release-510","short-description-expanded","series-season-filter","downloads-empty","downloads-light","downloads-dark","downloads-510","downloads-360"}
            },new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception error)
        {
            // Closing the final WPF window can end the dispatcher before the outer
            // startup handler resumes, so retain the actual assertion first.
            File.WriteAllText(Path.Combine(output,"error.txt"),error.ToString());
            throw;
        }
        finally{prefs.Light=originalLight;searchProvider=null;Close();}
    }

}
