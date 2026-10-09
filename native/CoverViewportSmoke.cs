using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Kachalka;

public partial class MainWindow
{
    sealed class FeatureLookupFixture:HttpMessageHandler
    {
        readonly ConcurrentDictionary<string,TaskCompletionSource> held=[];
        public readonly ConcurrentDictionary<string,int> Counts=[];
        public readonly ConcurrentDictionary<string,CancellationToken> Tokens=[];
        public void Hold(string id)=>held[id]=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release(string id){if(held.TryRemove(id,out var pending))pending.TrySetResult();}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var id=Uri.UnescapeDataString(request.RequestUri!.Query.Split("id=")[1]);Counts.AddOrUpdate(id,1,(_,count)=>count+1);Tokens[id]=ct;
            if(held.TryGetValue(id,out var pending))await pending.Task.WaitAsync(ct);
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{id,backdrop=new{url="https://image.tmdb.org/t/p/w1280/fixture.jpg",source="TMDB",tmdbId=1}}))};
        }
    }
    sealed class CoverViewportFixture(byte[] bytes):IAsyncDisposable
    {
        readonly TcpListener server=new(IPAddress.Loopback,0);
        readonly CancellationTokenSource lifetime=new(TimeSpan.FromSeconds(40));
        readonly ConcurrentDictionary<int,TaskCompletionSource> held=[];
        readonly ConcurrentBag<Task> responses=[];
        readonly ConcurrentDictionary<int,int> requests=[];
        Task? listener;
        public string Start()
        {
            server.Start();listener=Listen();return "http://127.0.0.1:"+((IPEndPoint)server.LocalEndpoint).Port+"/";
        }
        public int Count(int id)=>requests.GetValueOrDefault(id);
        public void Hold(int id)=>held[id]=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release(int id){if(held.TryRemove(id,out var pending))pending.TrySetResult();}
        async Task Listen()
        {
            try
            {
                while(!lifetime.IsCancellationRequested)
                {
                    var client=await server.AcceptTcpClientAsync(lifetime.Token);responses.Add(Respond(client));
                }
            }
            catch(Exception error)when(error is OperationCanceledException or SocketException or ObjectDisposedException){}
        }
        async Task Respond(TcpClient client)
        {
            using(client)
            try
            {
                await using var stream=client.GetStream();var request=new byte[8192];var length=0;
                while(length<request.Length)
                {
                    var count=await stream.ReadAsync(request.AsMemory(length),lifetime.Token);if(count==0)return;length+=count;
                    if(Encoding.ASCII.GetString(request,0,length).Contains("\r\n\r\n"))break;
                }
                var path=Encoding.ASCII.GetString(request,0,length).Split('\r')[0].Split(' ')[1];var id=int.Parse(path.Trim('/').Split('.')[0]);
                requests.AddOrUpdate(id,1,(_,count)=>count+1);
                if(held.TryGetValue(id,out var pending))await pending.Task.WaitAsync(lifetime.Token);
                var header=Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header,lifetime.Token);await stream.WriteAsync(bytes,lifetime.Token);
            }
            catch(Exception error)when(error is OperationCanceledException or IOException or SocketException or ObjectDisposedException){}
        }
        public async ValueTask DisposeAsync()
        {
            lifetime.Cancel();server.Stop();foreach(var pending in held.Values)pending.TrySetCanceled();
            if(listener!=null)await listener;await Task.WhenAll(responses.ToArray());lifetime.Dispose();
        }
    }

    public async Task CoverViewportSmokeTest(string output)
    {
        Directory.CreateDirectory(output);liveRequest?.Cancel();liveLoading=false;searchDelay.Stop();personRequest?.Cancel();catalogRefreshTimer.Stop();
        // A local IO fixture must not inherit a live catalog's startup shelves,
        // loading panel, navigation restore or monitor-dependent page height.
        // Keep the real window, ScrollViewer, image handlers and HTTP pipeline.
        ResetDiscoveryData();CancelCatalogQualityCheck();CancelPeopleSearch();BeginCatalogNavigationRender();StopCoverViewport();
        catalogList=null;catalogToolbar=null;inlineCatalogFilterScroll=null;discoveryHero=null;
        Body.Children.Clear();Body.RowDefinitions.Clear();PageHeader.Children.Clear();FilterControls.Children.Clear();
        FiltersPanel.Visibility=Visibility.Collapsed;LoadingIndicator.Visibility=Visibility.Collapsed;
        designFixedViewport=true;MaxWidth=1200;MaxHeight=900;MinWidth=620;MinHeight=420;Width=900;Height=680;
        Func<object>? failureState=null;
        async Task Until(Func<bool> ready,string failure)
        {
            for(var attempt=0;attempt<120;attempt++){UpdateLayout();if(ready())return;await Task.Delay(25);}
            var state=failureState?.Invoke();
            if(state!=null)await File.WriteAllTextAsync(Path.Combine(output,"failure-state.json"),JsonSerializer.Serialize(state));
            throw new Exception(failure+(state==null?"":Environment.NewLine+JsonSerializer.Serialize(state)));
        }
        using(var handler=new FeatureLookupFixture())
        using(var client=new SourceClient(handler))
        {
            var fixtureId=Guid.NewGuid().ToString("N");var shared="movies:backdrop-shared-"+fixtureId;var canceled="series:backdrop-canceled-"+fixtureId;handler.Hold(shared);
            using var first=new CancellationTokenSource();using var second=new CancellationTokenSource();
            var firstLookup=FeaturePosterUrl(shared,first.Token,client);var secondLookup=FeaturePosterUrl(shared,second.Token,client);
            await Until(()=>handler.Counts.GetValueOrDefault(shared)>0,"Shared feature lookup did not start.");
            if(handler.Counts[shared]!=1)throw new Exception("Two interested banners duplicated the same backdrop lookup.");
            first.Cancel();try{await firstLookup;throw new Exception("Canceled feature subscriber still completed.");}catch(OperationCanceledException){}
            if(secondLookup.IsCompleted||handler.Tokens[shared].IsCancellationRequested)throw new Exception("One canceled banner canceled another banner's shared lookup.");
            handler.Release(shared);if(await secondLookup==null)throw new Exception("The remaining feature subscriber did not receive its poster URL.");
            if(await FeaturePosterUrl(shared,CancellationToken.None,client)==null||handler.Counts[shared]!=1)throw new Exception("A completed feature lookup was not cached.");
            handler.Hold(canceled);using var canceledSubscriber=new CancellationTokenSource();var abandoned=FeaturePosterUrl(canceled,canceledSubscriber.Token,client);
            await Until(()=>handler.Counts.GetValueOrDefault(canceled)>0,"Cancelable feature lookup did not start.");canceledSubscriber.Cancel();
            try{await abandoned;throw new Exception("An abandoned lookup completed despite cancellation.");}catch(OperationCanceledException){}
            await Until(()=>handler.Tokens[canceled].IsCancellationRequested,"The last feature subscriber did not cancel its underlying request.");
            if(featurePosters.ContainsKey(canceled))throw new Exception("An offscreen canceled lookup poisoned the completed feature cache.");
            var returned=FeaturePosterUrl(canceled,CancellationToken.None,client);handler.Release(canceled);
            if(await returned==null||handler.Counts[canceled]!=2)throw new Exception("Returning to a canceled feature did not start a fresh successful lookup.");
        }
        MediaItem Card(int id,string url)=>new(-975000-id,"Постер "+id,"Фильмы","Драма",2026,"7.2","7.0","#526B69"){ImageUrl=url};
        Image Poster(int id,string url)
        {
            var image=new Image{DataContext=Card(id,url),Width=80,Height=120,Stretch=Stretch.UniformToFill,VerticalAlignment=VerticalAlignment.Top};
            image.Loaded+=SourceCover;image.DataContextChanged+=SourceCoverChanged;return image;
        }
        var drawing=new DrawingVisual();using(var context=drawing.RenderOpen())context.DrawRectangle(Brushes.Teal,null,new Rect(0,0,420,630));
        var bitmap=new RenderTargetBitmap(420,630,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var encoded=new MemoryStream();encoder.Save(encoded);
        await using var fixture=new CoverViewportFixture(encoded.ToArray());var url=fixture.Start();
        // The ephemeral local port makes every cache key unique to this run.
        Body.Children.Clear();PageHeader.Children.Clear();var content=new StackPanel();var images=new List<Image>();
        for(var id=0;id<12;id++){var image=Poster(id,url+id+".png");images.Add(image);content.Children.Add(new Border{Height=160,Child=image});}
        var scroll=new ScrollViewer{Content=content,Height=280,Width=500,VerticalAlignment=VerticalAlignment.Top,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Body.Children.Add(scroll);
        failureState=()=>new{WindowWidth=ActualWidth,WindowHeight=ActualHeight,BodyWidth=Body.ActualWidth,BodyHeight=Body.ActualHeight,
            ScrollHeight=scroll.ActualHeight,ViewportHeight=scroll.ViewportHeight,Offset=scroll.VerticalOffset,Tracked=coverViewport?.TrackedCount,Sweeps=coverViewport?.SweepCount,Slots=coverSlots.CurrentCount,
            Posters=images.Select((image,id)=>{var request=ObserveDownloadCover(image);return new{Id=id,Requests=fixture.Count(id),image.IsLoaded,image.IsVisible,image.ActualWidth,image.ActualHeight,
                Source=image.Source!=null,Position=coverViewport?.Measure(image),Pending=request.Request!=null,RetryAfter=request.RetryAfterUtc,
                Failure=request.LastFailure?.ToString()};}).ToArray()};
        await Until(()=>images.Take(3).All(image=>image.Source!=null),"Visible posters and the nearest row did not load.");
        if(Enumerable.Range(3,9).Any(id=>fixture.Count(id)!=0))throw new Exception("An offscreen poster started network IO before scrolling.");
        if(images.Take(3).Any(image=>image.Source is not BitmapImage {IsFrozen:true,PixelWidth:280}))throw new Exception("Viewport posters lost frozen, bounded worker decoding.");

        fixture.Hold(6);scroll.ScrollToVerticalOffset(960);
        await Until(()=>fixture.Count(6)>0&&images[7].Source!=null,"Scrolling did not begin requests for newly visible posters.");
        var leaving=ObserveDownloadCover(images[6]);var leavingRequest=leaving.Request??throw new Exception("Controlled poster request was not pending.");var leavingToken=leavingRequest.Token;
        scroll.ScrollToTop();await Until(()=>leavingToken.IsCancellationRequested,"Scrolling away did not cancel a poster request.");fixture.Release(6);await Task.Delay(150);
        if(images[6].Source!=null||coverCache.ContainsKey(url+"6.png"))throw new Exception("A late offscreen response changed an image or entered the decoded cache.");

        // The production poster template sizes its Grid/Border, rather than
        // its Image. An empty WPF bitmap has no render size until it loads.
        // That must not deadlock the request waiting for visible image bounds.
        Body.Children.Clear();var posterContainer=new Grid{Width=80,Height=120,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
        var emptyPoster=new Image{DataContext=Card(13,url+"13.png"),Stretch=Stretch.UniformToFill};var emptyBeforeRequest=false;var containerEligible=false;
        emptyPoster.Loaded+=SourceCover;emptyPoster.DataContextChanged+=SourceCoverChanged;
        emptyPoster.Loaded+=(_,_)=>{emptyBeforeRequest=emptyPoster.Source==null&&(emptyPoster.ActualWidth<=0||emptyPoster.ActualHeight<=0);containerEligible=coverViewport?.Measure(emptyPoster).Near==true;};
        posterContainer.Children.Add(emptyPoster);Body.Children.Add(posterContainer);
        await Until(()=>emptyPoster.Source!=null,"A source-less poster in a sized container could not start its image request.");
        if(!emptyBeforeRequest||!containerEligible)throw new Exception("The production poster fixture did not exercise zero-sized bitmap visibility through its sized container.");
        if(emptyPoster.ActualWidth<=0||emptyPoster.ActualHeight<=0||coverViewport?.Measure(emptyPoster).Visible!=true)throw new Exception("A decoded poster did not regain its own visible bounds.");

        var hiddenPoster=Poster(14,url+"14.png");hiddenPoster.Visibility=Visibility.Collapsed;posterContainer.Children.Clear();posterContainer.Children.Add(hiddenPoster);
        await Until(()=>hiddenPoster.IsLoaded,"Collapsed poster fixture did not load its native container.");
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Background);
        if(fixture.Count(14)!=0||coverViewport?.Measure(hiddenPoster).Near==true)throw new Exception("A collapsed image inherited its visible parent's IO eligibility.");
        hiddenPoster.Visibility=Visibility.Visible;await Until(()=>hiddenPoster.Source!=null,"Showing a previously collapsed poster did not start loading.");

        // Hidden banner Image is measured through its visible Grid ancestor.
        Body.Children.Clear();var banner=new Grid{Width=420,Height=210,VerticalAlignment=VerticalAlignment.Top};var feature=Poster(15,url+"15.png");feature.Width=feature.Height=0;feature.Opacity=0;feature.Tag="FeaturePoster";banner.Children.Add(feature);Body.Children.Add(banner);
        await Until(()=>coverViewport?.Measure(feature).Near==true,"The zero-sized backdrop failed to use its banner viewport.");
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Background);
        if(feature.Source!=null||fixture.Count(15)!=0)throw new Exception("A banner without a landscape identity loaded its portrait poster as a backdrop.");
        if(coverViewport?.Measure(feature).Near!=true)throw new Exception("The visible banner was treated as an offscreen zero-sized Image.");

        // An already loaded container may move when adjacent content folds.
        // Keep extent and every element's size fixed to avoid ScrollChanged or
        // SizeChanged; changing only its arrange position must start loading.
        Body.Children.Clear();var canvas=new Canvas{Width=500,Height=2000};var moved=Poster(30,url+"30.png");Canvas.SetTop(moved,900);canvas.Children.Add(moved);
        var arrangedScroll=new ScrollViewer{Content=canvas,Height=280,Width=500,VerticalAlignment=VerticalAlignment.Top,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Body.Children.Add(arrangedScroll);
        await Until(()=>moved.IsLoaded&&coverViewport?.Measure(moved).Near==false,"Layout-movement fixture did not become loaded offscreen.");
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Background);if(fixture.Count(30)!=0)throw new Exception("Layout-movement fixture loaded before it became visible.");
        Canvas.SetTop(moved,20);await Until(()=>moved.Source!=null,"An existing loaded image moved into view without triggering a poster request.");

        // Reusing the main window's SourceCover handler in an owned dialog must
        // measure against that dialog, which is a separate visual-tree root.
        var dialogPoster=Poster(31,url+"31.png");var dialog=new Window{Owner=this,Width=220,Height=210,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=new Border{Padding=new(18),Child=dialogPoster}};
        try
        {
            dialog.Show();await Until(()=>dialogPoster.Source!=null,"A poster in an owned window was measured against the wrong visual root.");
            if(CoverViewportFor(dialogPoster)==null||ReferenceEquals(CoverViewportFor(dialogPoster),coverViewport)||coverViewports.Count!=2)throw new Exception("Owned windows do not have one independent poster observer per visual root.");
        }
        finally{dialog.Close();}
        await Until(()=>!coverViewports.ContainsKey(dialog),"Closing an owned window retained its poster observer.");

        // Unloading and DataContext recycling both invalidate outstanding IO.
        Body.Children.Clear();fixture.Hold(20);var recycled=Poster(20,url+"20.png");var host=new Border{Child=recycled,Width=100,Height=140,VerticalAlignment=VerticalAlignment.Top};Body.Children.Add(host);
        await Until(()=>fixture.Count(20)>0,"Unload fixture request did not start.");var unloadToken=ObserveDownloadCover(recycled).Request!.Token;
        host.Child=null;await Until(()=>unloadToken.IsCancellationRequested,"Unloading did not cancel pending image IO.");fixture.Release(20);await Task.Delay(100);
        if(recycled.Source!=null)throw new Exception("An unloaded image received a late poster response.");

        fixture.Hold(21);recycled.DataContext=Card(21,url+"21.png");host.Child=recycled;
        await Until(()=>fixture.Count(21)>0,"Recycled context fixture did not start.");var staleToken=ObserveDownloadCover(recycled).Request!.Token;
        recycled.DataContext=Card(22,url+"22.png");await Until(()=>recycled.Source!=null,"A recycled poster did not load the replacement context.");var replacement=recycled.Source;
        fixture.Release(21);await Task.Delay(100);
        if(!staleToken.IsCancellationRequested||!ReferenceEquals(recycled.Source,replacement)||coverCache.ContainsKey(url+"21.png"))throw new Exception("A stale DataContext request replaced the current poster.");

        host.Child=null;await Task.Delay(30);recycled.Source=null;host.Child=recycled;
        await Until(()=>recycled.Source!=null,"A decoded memory cache hit did not restore its image.");
        if(fixture.Count(22)!=1||!ReferenceEquals(recycled.Source,replacement))throw new Exception("A memory cache hit fetched or decoded the poster again.");
        host.Child=null;await Task.Delay(30);coverCache.Remove(url+"22.png");recycled.Source=null;host.Child=recycled;
        await Until(()=>recycled.Source!=null,"A persistent cache hit did not restore its poster.");
        if(fixture.Count(22)!=1)throw new Exception("The on-disk poster cache accessed the network.");
        var uiThread=Environment.CurrentManagedThreadId;var decodeThread=0;
        var disk=await CoverCache.Load(CoverPath(url+"22.png"),1024*1024,_=>throw new Exception("Disk cache fixture fetched the network."),bytes=>{decodeThread=Environment.CurrentManagedThreadId;return bytes.Length;},CancellationToken.None);
        if(decodeThread==uiThread||disk.Downloaded||disk.Image==0)throw new Exception("A synchronous disk-cache hit decoded on the native UI thread.");

        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Background);var before=coverViewport!.SweepCount;
        for(var tick=0;tick<256;tick++)coverViewport.Schedule();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Background);
        if(coverViewport.SweepCount-before!=1)throw new Exception("Repeated viewport notifications were not coalesced into one background sweep.");
        Body.Children.Clear();await Task.Delay(30);
        if(coverViewport.TrackedCount!=0)throw new Exception("Unloaded posters retain viewport scroll subscriptions.");
        await File.WriteAllTextAsync(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{VisiblePosters=true,NearestRowPrefetched=true,OffscreenNetworkAvoided=true,ScrollStartsRequests=true,ScrollAwayCancels=true,EmptyPosterContainerAnchored=true,DecodedPosterOwnBounds=true,CollapsedPosterAvoidsNetwork=true,LayoutPositionStartsRequest=true,OwnedWindowPosters=true,OwnedWindowObserverCleanup=true,UnloadingCancels=true,RecycledContextRejectsLateResponse=true,HiddenFeaturePosterAnchored=true,SharedFeatureLookup=true,IndependentFeatureSubscriberCancellation=true,CanceledFeatureLookupCanRetry=true,FrozenBoundedDecode=true,MemoryCacheNoRefetch=true,DiskCacheNoRefetch=true,DiskCacheWorker=true,CoalescedViewportSweep=true,UnloadedObserverCleanup=true}));
        StopCoverViewport();Close();
    }
}
