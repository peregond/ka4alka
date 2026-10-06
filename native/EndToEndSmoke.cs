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
        var server=new TcpListener(IPAddress.Loopback,0);server.Start();
        try
        {
            var port=((IPEndPoint)server.LocalEndpoint).Port;
            var serve=Task.Run(async()=>
            {
                using var client=await server.AcceptTcpClientAsync();
                await using var stream=client.GetStream();var request=new byte[4096];await stream.ReadAtLeastAsync(request.AsMemory(),1,throwOnEndOfStream:true);
                var header=Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/x-bittorrent\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header);await stream.WriteAsync(bytes);
            });
            liveRequest?.Cancel();section="Фильмы";Search.Text="Тестовый фильм";searchDelay.Stop();livePage=1;
            liveKey="Фильмы|Тестовый фильм|1";
            var movie=new MediaItem(-987654321,"Тестовый фильм","Фильмы","",2026,"—","—","#526B69"){PageUrl="https://example.invalid/test",Description="Локальная проверка интерфейса загрузки."};
            var release=new SourceEntry("local-test","Тестовый фильм (2026) WEB-DL 1080p","Локальный тест","",$"http://127.0.0.1:{port}/test-film.torrent",null,256*1024,1);
            var larger=release with{Id="large-test",Title="Тестовый фильм (2026) WEB-DL 2160p",Size=3*1024*1024,Seeds=12};
            var smaller=release with{Id="small-test",Title="Тестовый фильм (2026) BluRay 720p",Size=100*1024,Seeds=4};
            liveItems=[movie];requestedDetails.Add(movie.Id);liveReleases[movie.Id]=[release,larger,smaller];current=null;Render();UpdateLayout();
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
            await serve;
            File.WriteAllText(Path.Combine(output,"end-to-end.json"),JsonSerializer.Serialize(new{CardOpened=true,ReleaseChosen=true,TorrentFetched=true,DownloadStarted=true,ConfiguredFolderUsed=true,NoReleaseDialog=true,QueueCount=downloads.Items.Count,Name=downloads.Items[0].Name,Status=downloads.Items[0].Status}));
        }
        finally{server.Stop();Close();}
    }
}
