using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MonoTorrent;
namespace Kachalka;
public partial class MainWindow
{
    static T? FindVisual<T>(DependencyObject root,Func<T,bool> matches) where T:DependencyObject
    {
        if(root is T target&&matches(target))return target;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var found=FindVisual(VisualTreeHelper.GetChild(root,i),matches);
            if(found!=null)return found;
        }
        return null;
    }
    public async Task EndToEndSmokeTest(string output)
    {
        Directory.CreateDirectory(output);prefs.Folder=Path.Combine(output,"download");Directory.CreateDirectory(prefs.Folder);prefs.FolderConfigured=true;
        var payload=Path.Combine(output,"test-film.bin");await File.WriteAllBytesAsync(payload,Enumerable.Range(0,256*1024).Select(x=>(byte)(x%251)).ToArray());
        var torrentPath=Path.Combine(output,"test-film.torrent");await new TorrentCreator().CreateAsync(new TorrentFileSource(payload),torrentPath);
        var bytes=await File.ReadAllBytesAsync(torrentPath);
        var poster=new RenderTargetBitmap(56,84,96,96,PixelFormats.Pbgra32);var drawing=new DrawingVisual();using(var context=drawing.RenderOpen()){context.DrawRectangle(new SolidColorBrush(Color.FromRgb(54,100,118)),null,new Rect(0,0,56,84));context.DrawEllipse(new SolidColorBrush(Color.FromRgb(197,176,123)),null,new Point(28,32),18,18);}poster.Render(drawing);
        var posterEncoder=new PngBitmapEncoder();posterEncoder.Frames.Add(BitmapFrame.Create(poster));using var posterStream=new MemoryStream();posterEncoder.Save(posterStream);var posterBytes=posterStream.ToArray();
        using var fixtureTimeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));var posterReady=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlers=new List<Task>();Task? serve=null;int posterRequests=0,torrentRequests=0;
        var server=new TcpListener(IPAddress.Loopback,0);server.Start();
        try
        {
            var port=((IPEndPoint)server.LocalEndpoint).Port;
            var posterUrl=$"http://127.0.0.1:{port}/poster.png";
            serve=Task.Run(async()=>
            {
                for(var request=0;request<10;request++)
                {
                    var client=await server.AcceptTcpClientAsync(fixtureTimeout.Token);handlers.Add(Respond(client));
                }
                async Task Respond(TcpClient client)
                {
                    using(client)
                    try
                    {
                        await using var stream=client.GetStream();var request=new byte[8192];int received=0;
                        while(received<request.Length)
                        {
                            var count=await stream.ReadAsync(request.AsMemory(received),fixtureTimeout.Token);if(count==0)throw new EndOfStreamException();received+=count;
                            if(Encoding.ASCII.GetString(request,0,received).Contains("\r\n\r\n"))break;
                        }
                        var path=Encoding.ASCII.GetString(request,0,received).Split('\r')[0].Split(' ').ElementAtOrDefault(1);
                        byte[] response;string mime;
                        if(path=="/poster.png"){Interlocked.Increment(ref posterRequests);await posterReady.Task.WaitAsync(fixtureTimeout.Token);response=posterBytes;mime="image/png";}
                        else if(path=="/test-film.torrent"){Interlocked.Increment(ref torrentRequests);response=bytes;mime="application/x-bittorrent";}
                        else throw new IOException("Unknown local smoke fixture URL: "+path);
                        var header=Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {mime}\r\nContent-Length: {response.Length}\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(header,fixtureTimeout.Token);await stream.WriteAsync(response,fixtureTimeout.Token);
                    }
                    catch(Exception error)when(error is OperationCanceledException or IOException or SocketException){ /* Cancelled image requests are expected when navigating between views. */ }
                }
            });
            liveRequest?.Cancel();section="Фильмы";Search.Text="Тестовый фильм";searchDelay.Stop();livePage=1;
            liveKey="Фильмы|Тестовый фильм|1";
            var movie=new MediaItem(-987654321,"Тестовый фильм","Фильмы","",2026,"—","—","#526B69"){PageUrl="https://example.invalid/test",ImageUrl=posterUrl,Description="Локальная проверка интерфейса загрузки."};
            var release=new SourceEntry("local-test","Тестовый фильм (2026) WEB-DL 1080p","Локальный тест","",$"http://127.0.0.1:{port}/test-film.torrent","",256*1024,1);
            var larger=release with{Id="large-test",Title="Тестовый фильм (2026) WEB-DL 2160p",Size=3*1024*1024,Seeds=12};
            var smaller=release with{Id="small-test",Title="Тестовый фильм (2026) BluRay 720p",Size=100*1024,Seeds=4};
            liveItems=[movie];cardMetadata[movie.Id]=Task.FromResult(movie);requestedDetails.Add(movie.Id);liveReleases[movie.Id]=[release,larger,smaller];current=null;Render();UpdateLayout();
            var card=FindVisual<Button>(Body,b=>b.Tag is MediaItem x&&x.Id==movie.Id)??throw new Exception("Movie card was not shown.");
            card.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));UpdateLayout();
            if(current?.Id!=movie.Id)throw new Exception("Movie card did not open.");
            var download=FindVisual<Button>(Body,b=>b.Tag is SourceEntry x&&x.Id==release.Id)??throw new Exception("Release download button was not shown.");
            var screenshot=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);screenshot.Render(this);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(screenshot));using(var file=File.Create(Path.Combine(output,"release-picker.png")))png.Save(file);
            var releaseDialogShown=false;
            EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>
            {
                if(sender is Window other&&!ReferenceEquals(other,this)&&ReferenceEquals(other.Owner,this))releaseDialogShown=true;
            }));
            download.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var deadline=DateTime.UtcNow.AddSeconds(20);
            while((downloads.Items.Count==0||downloads.Items[0].Paused||section!="Загрузки")&&DateTime.UtcNow<deadline)await Task.Delay(100);
            if(downloads.Items.Count!=1||downloads.Items[0].Paused||section!="Загрузки")throw new Exception("Release did not start a torrent task: "+Status.Text);
            if(releaseDialogShown)throw new Exception("Release opened an extra download dialog.");
            if(downloads.Items[0].Folder!=prefs.Folder)throw new Exception("Release ignored the configured download folder.");
            var active=downloads.Items[0];if(active.MediaTitle!=movie.Title||active.ImageUrl!=posterUrl||active.Name==movie.Title||active.ReleaseTitle!=release.Title)throw new Exception("Actual release download lost the Russian card title, raw torrent name or catalog poster.");
            await downloads.Toggle(active);var restored=new DownloadService();var restoredItem=restored.Items.Single();
            if(restored.EngineCreated||restoredItem.MediaTitle!=movie.Title||restoredItem.ImageUrl!=posterUrl||restoredItem.MediaPageUrl!=movie.PageUrl||restoredItem.ReleaseTitle!=release.Title||restoredItem.Name!=active.Name)throw new Exception("Queue restart lost the selected card metadata.");
            downloads.Items.Remove(active);downloads.Items.Add(restoredItem);Render();UpdateLayout();
            if(coverCache.ContainsKey(posterUrl))throw new Exception("Poster probe was satisfied before restoring the queue.");posterReady.SetResult();
            deadline=DateTime.UtcNow.AddSeconds(10);Image? queuePoster=null;
            while(DateTime.UtcNow<deadline)
            {
                UpdateLayout();queuePoster=FindVisual<Image>(Body,image=>ReferenceEquals(image.DataContext,restoredItem));if(queuePoster?.Source!=null)break;await Task.Delay(100);
            }
            if(queuePoster?.Source==null||posterRequests==0||torrentRequests!=1)throw new Exception($"Restored download poster did not load through HTTP: images={posterRequests}, torrents={torrentRequests}.");
            File.WriteAllText(Path.Combine(output,"end-to-end.json"),JsonSerializer.Serialize(new{CardOpened=true,ReleaseChosen=true,TorrentFetched=true,DownloadStarted=true,ConfiguredFolderUsed=true,NoReleaseDialog=true,RestoredQueueMetadata=true,RestoredPosterFetchedOverHttp=true,PosterRequests=posterRequests,TorrentRequests=torrentRequests,QueueCount=downloads.Items.Count,Name=restoredItem.Name,MediaTitle=restoredItem.MediaTitle,ImageUrl=restoredItem.ImageUrl,Status=restoredItem.Status}));
        }
        finally
        {
            fixtureTimeout.Cancel();server.Stop();if(serve!=null)try{await serve;}catch(Exception error)when(error is OperationCanceledException or SocketException){}
            await Task.WhenAll(handlers);Close();
        }
    }
}
