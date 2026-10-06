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
    int designCachedPosters;
    string? designCatalogFile;

    public void PrepareDesignSmoke()
    {
        if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KACHALKA_DATA")))
            throw new InvalidOperationException("Design smoke needs an isolated KACHALKA_DATA directory.");
        if(downloads.Items.Count!=0)throw new InvalidOperationException("Design smoke needs an empty test download queue.");
        // Prevent completion of the startup request from replacing the local fixture.
        liveKey="design-smoke-preparing";liveRequest?.Cancel();searchDelay.Stop();
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
        if(designCatalogFallback)cached=Catalog.Items.Where(x=>x.Section=="Фильмы").Select(x=>x with{PageUrl="https://example.invalid/design/catalog/"+x.Id}).ToArray();
        var cards=new List<MediaItem>();
        foreach(var original in cached)
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
            cardMetadata[item.Id]=Task.FromResult(item);requestedDetails.Add(item.Id);cards.Add(item);
        }
        section="Фильмы";current=null;favoritesOnly=false;catalogYear=null;livePage=1;Search.Text="";searchDelay.Stop();
        liveItems=cards;liveKey="design-smoke-prepared";liveLoading=false;liveError="";
    }

    public async Task DesignSmokeTest(string output)
    {
        Directory.CreateDirectory(output);
        var originalLight=prefs.Light;
        var checks=new List<object>();
        MinWidth=360;MinHeight=300;
        async Task Settle()
        {
            UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(90);UpdateLayout();
        }
        async Task Size(double width,double height)
        {
            Width=Math.Min(width,MaxWidth-24);Height=Math.Min(height,MaxHeight-24);await Settle();
        }
        void Shot(string name)
        {
            UpdateLayout();
            var bitmap=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling(ActualWidth)),Math.Max(1,(int)Math.Ceiling(ActualHeight)),96,96,PixelFormats.Pbgra32);bitmap.Render(this);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name+".png"));png.Save(stream);
        }
        ComboBox QualityFilter()=>FindVisual<ComboBox>(Body,x=>AutomationProperties.GetName(x)=="Раздачи: Качество")??throw new Exception("Quality filter is missing.");
        void CheckFilter(string stage)
        {
            var filter=QualityFilter();
            if(filter.SelectedItem?.ToString()!="1080p")throw new Exception(stage+": selected 1080p filter was lost.");
            if(!filter.IsVisible||filter.ActualWidth<=0)throw new Exception(stage+": quality filter is not visible.");
            var buttons=VisualElements<Button>(Body).Where(x=>x.Tag is SourceEntry).ToArray();
            if(buttons.Length!=1||buttons[0].Tag is not SourceEntry entry||entry.Quality!="1080p")throw new Exception(stage+": quality filter did not leave exactly the 1080p release.");
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
            checks.Add(new{Stage=stage,WindowWidth=Math.Round(ActualWidth),BodyWidth=Math.Round(Body.ActualWidth),TitleWidth=Math.Round(title.ActualWidth),SynopsisWidth=Math.Round(synopsis.ActualWidth),Filter="1080p",DownloadActionVisible=true});
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
            await Settle();liveKey="Фильмы||1";
            await Size(1760,950);
            Render();await Settle();
            var wheelScrolling=await CheckWheelScrolling();
            var searchSettings=await CheckSearchAndSettings(output);
            Search.Text="";searchDelay.Stop();liveKey="Фильмы||1";
            prefs.Light=true;ApplyTheme();current=null;Render();await Settle();Shot("catalog-light");
            var catalogColumnsLight=catalogColumns;
            prefs.Light=false;ApplyTheme();Render();await Settle();Shot("catalog-dark");
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
            current=movie;Render();await Settle();Shot("detail-wide-dark");
            prefs.Light=true;ApplyTheme();Render();await Settle();Shot("detail-wide-light");
            QualityFilter().SelectedItem="1080p";await Settle();CheckFilter("selected");
            Render();await Settle();CheckFilter("rendered");
            var sourceToggle=FindVisual<Button>(Body,b=>AutomationProperties.GetName(b)=="Показать состояние источников")??throw new Exception("Source status toggle missing.");
            sourceToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();
            if(!sourceView.Expanded||!VisualElements<TextBlock>(Body).Any(x=>x.Text.Contains("Не ответил вовремя")))throw new Exception("Per-source timeout is not visible.");
            VisualElements<TextBlock>(Body).Last(x=>x.Text=="Временно недоступен").BringIntoView();await Settle();Shot("sources-light");
            prefs.Light=false;ApplyTheme();RefreshDetail(movie.Id);await Settle();Shot("sources-dark");
            var qualityBefore=QualityFilter();qualityBefore.IsDropDownOpen=true;await Settle();
            sourceView.Checking=true;RefreshDetail(movie.Id);
            if(!ReferenceEquals(qualityBefore,QualityFilter())||!qualityBefore.IsDropDownOpen)throw new Exception("Source progress interrupted an open filter.");
            qualityBefore.IsDropDownOpen=false;await Settle();CheckFilter("after-source-progress");
            var sourceRetry=FindVisual<Button>(Body,b=>AutomationProperties.GetName(b)=="Обновить варианты загрузки")??throw new Exception("Source retry action missing.");
            if(sourceRetry.IsEnabled)throw new Exception("Source retry should be disabled during a scan.");
            sourceView.Checking=false;RefreshDetail(movie.Id);await Settle();CheckFilter("source-complete");
            if(FindVisual<Button>(Body,b=>AutomationProperties.GetName(b)=="Обновить варианты загрузки")?.IsEnabled!=true)throw new Exception("Source retry did not enable after completion.");
            var sourceScroller=FindVisual<ScrollViewer>(Body,_=>true)??throw new Exception("Detail scroller missing.");sourceScroller.ScrollToVerticalOffset(60);await Settle();var sourceOffset=sourceScroller.VerticalOffset;
            RefreshDetail(movie.Id);await Settle();
            if(Math.Abs((FindVisual<ScrollViewer>(Body,_=>true)?.VerticalOffset??0)-sourceOffset)>1)throw new Exception("Source progress lost the reading position.");
            await Size(510,820);VisualElements<TextBlock>(Body).Last(x=>x.Text=="Временно недоступен").BringIntoView();await Settle();Shot("sources-510");
            sourceView.Expanded=false;prefs.Light=true;ApplyTheme();Render();await Settle();
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
            if(detailSynopsis==null||detailSynopsis.MaxHeight>60.1)throw new Exception("Short description: fixture did not start in its collapsed state.");
            descriptionButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Settle();
            var expandedSynopsis=detailSynopsis??throw new Exception("Short description: synopsis disappeared after expansion.");
            if(!double.IsPositiveInfinity(expandedSynopsis.MaxHeight)||expandedSynopsis.ActualHeight<=60||expandedSynopsis.Text!=shortDescription)
                throw new Exception("Short description: expansion did not reveal the complete text beyond 60 px.");
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
            var activeDownload=new DownloadItem{Name="Big Buck Bunny · 1080p",Folder=@"C:\Downloads\Качалка",Paused=false};
            DownloadPresentation.Apply(activeDownload,new(MonoTorrent.Client.TorrentState.Downloading,false,42,2L*1024*1024*1024,1800*1024,128*1024,12,5,fixtureNow.AddMinutes(-3),fixtureNow),fixtureNow);
            var waitingDownload=new DownloadItem{Name="Sintel · 2160p",Folder=activeDownload.Folder,Paused=false};
            DownloadPresentation.Apply(waitingDownload,new(MonoTorrent.Client.TorrentState.Downloading,false,8,4L*1024*1024*1024,0,0,3,0,fixtureNow.AddMinutes(-2),null),fixtureNow);
            var completedDownload=new DownloadItem{Name="Tears of Steel · 1080p",Folder=activeDownload.Folder,Paused=false};
            DownloadPresentation.Apply(completedDownload,new(MonoTorrent.Client.TorrentState.Seeding,false,100,1024L*1024*1024,0,256*1024,2,0,fixtureNow.AddMinutes(-20),fixtureNow),fixtureNow);
            downloads.Items.Add(activeDownload);downloads.Items.Add(waitingDownload);downloads.Items.Add(completedDownload);
            prefs.Light=true;ApplyTheme();Render();await Settle();Shot("downloads-light");
            prefs.Light=false;ApplyTheme();Render();await Settle();
            var expectedInk=((SolidColorBrush)FindResource("Text")).Color;
            foreach(var block in VisualElements<TextBlock>(Body).Where(x=>x.Text==activeDownload.Name||x.Text==activeDownload.Status))
                if(block.Foreground is not SolidColorBrush ink||ink.Color!=expectedInk)throw new Exception("Download text does not follow dark theme.");
            Shot("downloads-dark");
            await Size(510,820);Render();await Settle();Shot("downloads-510");
            var downloadStats=VisualElements<TextBlock>(Body).Where(x=>x.Text==activeDownload.Stats||x.Text==waitingDownload.Stats).ToArray();
            if(downloadStats.Length==0)throw new Exception("Download telemetry is not visible.");
            foreach(var block in downloadStats)
            {
                var origin=block.TransformToAncestor(Body).Transform(new Point());
                if(origin.X<-.5||origin.X+block.ActualWidth>Body.ActualWidth+1)throw new Exception("Download telemetry overflows narrow window.");
            }
            if(downloads.EngineCreated)throw new Exception("Download visual fixtures unexpectedly started an engine.");
            downloads.Items.Clear();
            File.WriteAllText(Path.Combine(output,"design.json"),JsonSerializer.Serialize(new
            {
                CatalogFallback=designCatalogFallback,CatalogCacheFile=designCatalogFile,CatalogCount=liveItems.Count,CachedPosters=designCachedPosters,CatalogColumns=catalogColumnsLight,
                ExternalSourcesRequired=false,FixtureHasCachedPoster=preview!=null,FilterPersistedAfterRender=true,FilterPersistedAfterResize=true,DownloadStarted=false,EngineCreated=downloads.EngineCreated,
                ShortDescriptionLength=shortDescription.Length,ShortDescriptionExpanded=true,ShortDescriptionExpandedHeight=shortDescriptionExpandedHeight,SeriesSeasonFilter=true,
                DownloadTelemetryFitsNarrowWindow=true,DownloadTextFollowsTheme=true,
                SearchAndSettings=searchSettings,
                WheelScrolling=wheelScrolling,
                SourceFailureVisible=true,SourceProgressPreservesOpenFilter=true,SourceProgressPreservesScroll=true,SourceRetryState=true,
                WidthChecks=checks,Screens=new[]{"sources-light","sources-dark","sources-510","search-light","search-dark","search-small","search-minimum","settings-light","settings-dark","settings-small","settings-minimum","catalog-light","catalog-dark","detail-wide-light","detail-wide-dark","detail-720","release-720","detail-510","release-510","short-description-expanded","series-season-filter","downloads-empty","downloads-light","downloads-dark","downloads-510"}
            },new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{prefs.Light=originalLight;Close();}
    }

}
