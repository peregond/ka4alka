using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Kachalka;

public partial class MainWindow
{
    public async Task FeatureSummarySmokeTest(string output)
    {
        Directory.CreateDirectory(output);section="Настройки";current=null;activePerson=null;
        liveRequest?.Cancel();liveLoading=false;searchDelay.Stop();catalogRefreshTimer.Stop();ResetDiscoveryData();CancelCatalogQualityCheck();CancelPeopleSearch();CancelDetailMetadata(true);BeginCatalogNavigationRender();StopCoverViewport();
        catalogList=null;discoveryHero=null;designFixedViewport=true;
        var originalProvider=featureSummaryProvider;var originalGameProvider=gameDetailProvider;var originalLight=prefs.Light;var originalLite=prefs.LiteMode;
        var calls=new List<int>();var tokens=new Dictionary<int,CancellationToken>();var pending=new Dictionary<int,TaskCompletionSource<MediaItem>>();var checks=new List<string>();
        void Check(bool good,string text){if(!good)throw new Exception(text);checks.Add(text);}
        async Task Settle(){UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);await Task.Delay(50);UpdateLayout();}
        async Task Until(Func<bool> condition,string message)
        {
            var end=DateTime.UtcNow.AddSeconds(5);
            while(!condition()&&DateTime.UtcNow<end)await Settle();
            Check(condition(),message);
        }
        featureSummaryProvider=(item,ct)=>{calls.Add(item.Id);tokens[item.Id]=ct;return pending[item.Id].Task;};
        MediaItem Item(int id,string category,string? description=null)=>new(id,category=="Игры"?"Мир за горизонтом":"История одного путешествия",category,"Приключения",2026,"8.1","7.8","#315A66"){Description=description};
        FeatureCarouselView Mount(params MediaItem[] slides)
        {
            Body.Children.Clear();Body.RowDefinitions.Clear();PageHeader.Children.Clear();FilterControls.Children.Clear();FiltersPanel.Visibility=Visibility.Collapsed;
            section=slides[0].Section;var frame=BuildFeatureCarousel(slides);Body.Children.Add(new ScrollViewer{Content=frame,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});return carousel!;
        }
        void Shot(string name)
        {
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(this);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));png.Save(file);
        }
        var description="Двое друзей отправляются в незнакомый город и оказываются в центре необычной истории. Чтобы вернуться домой, им придётся довериться друг другу и сделать выбор, который изменит их жизнь.";
        try
        {
            prefs.LiteMode=true;ApplyLiteMode();MaxWidth=1500;MaxHeight=1000;MinWidth=360;MinHeight=300;Width=1200;Height=850;
            foreach(var category in new[]{"Фильмы","Сериалы"})
            {
                var item=Item(category=="Фильмы"?-966001:-966002,category);pending[item.Id]=new(TaskCreationOptions.RunContinuationsAsynchronously);
                var view=Mount(item);await Until(()=>calls.Contains(item.Id),category+": only the active banner asks for missing text");await Settle();
                var beforeHeight=view.Frame.ActualHeight;var renders=renderCount;
                var beats=0;var input=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(15)};input.Tick+=(_,_)=>beats++;input.Start();await Task.Delay(120);input.Stop();
                Check(beats>=3&&view.SummarySlot.Visibility==Visibility.Collapsed,category+": pending text leaves the original banner intact and input responsive");
                pending[item.Id].SetResult(item with{Description=description});await Until(()=>view.Summary.Text==description,category+": real source synopsis fills its short text block");
                Check(view.Frame.ActualHeight-beforeHeight<=75&&view.SummarySlot.Visibility==Visibility.Visible&&renderCount==renders,category+": real text adds only one bounded block without rebuilding the page");
                Shot(category=="Фильмы"?"movie-summary":"series-summary");
            }
            var slow=Item(-966003,"Фильмы");pending[slow.Id]=new(TaskCreationOptions.RunContinuationsAsynchronously);
            var next=Item(-966004,"Фильмы",description);var switched=Mount(slow,next);
            await Until(()=>calls.Contains(slow.Id),"delayed synopsis started");ShowFeatureSlide(switched,1);await Settle();
            Check(tokens[slow.Id].IsCancellationRequested&&switched.Summary.Text==description,"switching slides cancels the old subscriber and displays the new synopsis");
            pending[slow.Id].SetResult(slow with{Description="Чужое запоздавшее описание"});await Settle();
            Check(switched.Summary.Text==description,"a late previous-slide reply cannot overwrite the current teaser");
            var cached=Item(-966005,"Сериалы");cardMetadata[cached.Id]=Task.FromResult(cached with{Description=description});var count=calls.Count;
            var cachedView=Mount(cached);await Settle();Check(cachedView.Summary.Text==description&&calls.Count==count,"cached card descriptions are reused without a new request");

            // A late synopsis above an already scrolled catalog must preserve its visible title.
            var scrolled=Item(-966007,"Фильмы");pending[scrolled.Id]=new(TaskCreationOptions.RunContinuationsAsynchronously);
            BeginCatalogNavigationRender();Body.Children.Clear();section="Фильмы";
            var content=new StackPanel();content.Children.Add(BuildFeatureCarousel([scrolled]));var scrollView=carousel!;
            for(var index=0;index<20;index++)content.Children.Add(new Button{Tag=Item(-967000-index,"Фильмы"),Content="Карточка "+index,Height=80,Margin=new(0,4,0,4)});
            var scroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Body.Children.Add(scroll);EndCatalogNavigationRender();
            await Until(()=>calls.Contains(scrolled.Id),"scrolled catalog synopsis lookup started");await Settle();scroll.ScrollToVerticalOffset(scrollView.Frame.ActualHeight+100);await Settle();
            var anchor=CaptureCatalogPosition();Check(anchor?.ItemId!=null,"scrolled fixture has a visible catalog anchor");
            pending[scrolled.Id].SetResult(scrolled with{Description=description});await Until(()=>scrollView.Summary.Text==description,"offscreen banner receives its real synopsis");await Settle();
            var retained=CaptureCatalogPosition();Check(retained?.ItemId==anchor!.ItemId&&Math.Abs(retained!.Top-anchor.Top)<1.5,"description arrival above the viewport preserves the visible catalog card");

            var game=Item(-966006,"Игры");var gamePending=new TaskCompletionSource<GameDetails>(TaskCreationOptions.RunContinuationsAsynchronously);var gameCalls=0;
            gameDetailProvider=(_,_)=>{gameCalls++;return gamePending.Task;};var gameView=Mount(game);StartGameBannerDetails([game]);
            gamePending.SetResult(new(game with{Description=description},"","Студия","Издатель"));await Until(()=>gameView.Summary.Text==description,"Steam's existing detail lookup populates the game teaser");
            Check(gameCalls==1&&!calls.Contains(game.Id),"game summaries do not create a second metadata request");

            foreach(var category in new[]{"Фильмы","Сериалы","Игры"})foreach(var width in new[]{1200d,700d,360d})
            {
                Width=width;prefs.Light=width==700;prefs.LiteMode=width==360;ApplyLiteMode();ApplyTheme();
                var view=Mount(Item(-966010,category,description));await Settle();ArrangeFeatureCarousel(Body.ActualWidth);await Settle();
                var slot=FindVisual<Grid>(view.Frame,x=>x.Name=="FeatureSummarySlot")!;
                Check(Math.Abs(slot.ActualHeight-60)<.5&&view.Summary.Text==description&&view.Summary.TextWrapping==TextWrapping.Wrap&&view.Summary.TextTrimming==TextTrimming.CharacterEllipsis,category+": three synopsis lines stay bounded at "+width);
                foreach(var action in new[]{view.About,view.Favorite})
                {
                    var bounds=action.TransformToAncestor(view.Frame).TransformBounds(new Rect(new Point(),action.RenderSize));
                    Check(bounds.Left>=-.5&&bounds.Right<=view.Frame.ActualWidth+.5&&bounds.Bottom<=view.Frame.ActualHeight+.5,category+": banner actions fit the responsive synopsis at "+width);
                }
                Shot((category=="Фильмы"?"movies":category=="Сериалы"?"series":"games")+"-"+width);
            }
            var failed=Item(-966020,"Фильмы");pending[failed.Id]=new(TaskCreationOptions.RunContinuationsAsynchronously);var failedView=Mount(failed);await Settle();var unavailableHeight=failedView.Frame.ActualHeight;
            await Until(()=>calls.Contains(failed.Id),"unavailable synopsis lookup started");pending[failed.Id].SetException(new IOException("Fixture unavailable"));await Until(()=>failedView.SummaryRequest==null,"failed synopsis settles without a loading placeholder");
            Check(failedView.Summary.Text.Length==0&&failedView.SummarySlot.Visibility==Visibility.Collapsed&&Math.Abs(failedView.Frame.ActualHeight-unavailableHeight)<.5,"unavailable descriptions leave neither text, placeholders nor extra banner space");
            File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Checks=checks,MoviesSeriesGames=true,MissingSummaryPreservesOriginalBanner=true,NoPageRebuild=true,NativeInput=true,CachedMetadata=true,SingleGameLookup=true,StaleReplyRejected=true,ResponsiveWidths=new[]{360,700,1200}},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            if(carousel!=null)DetachFeatureWatch(carousel);gameBannerRequest?.Cancel();
            featureSummaryProvider=originalProvider;gameDetailProvider=originalGameProvider;prefs.Light=originalLight;prefs.LiteMode=originalLite;ApplyLiteMode();Close();
        }
    }
}
