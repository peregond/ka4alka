using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Kachalka;
public partial class MainWindow
{
    public async Task DpiSmokeTest(string output)
    {
        Directory.CreateDirectory(output);
        liveRequest?.Cancel();liveLoading=false;current=null;favoritesOnly=true;
        prefs.LiveFavorites=Enumerable.Range(1,8).Select(id=>new MediaItem(-50000-id,"Проверка интерфейса "+id,"Фильмы","драма",2026,"8.0","7.5","#526B69"){PageUrl="https://w6.zona.plus/movies/dpi-fixture-"+id}).ToList();
        foreach(var item in prefs.LiveFavorites){cardMetadata[item.Id]=Task.FromResult(item);requestedDetails.Add(item.Id);}
        var cases=new List<object>();
        var fixtures=Enumerable.Range(1,8).Select(id=>new DownloadItem{Name="Fixture.S01E0"+id+".mkv",MediaTitle="Проверка масштабирования "+id,Paused=true,Progress=25,Stats="25% · на паузе"}).ToArray();
        foreach(var item in fixtures)downloads.Items.Add(item);
        try
        {
            foreach(var pixels in new[]{(Width:1920d,Height:1040d),(Width:1366d,Height:728d)})
            foreach(var scale in new[]{1d,1.2,1.25,1.5,1.75,2,2.5})
            {
                var fit=WindowSizing.FitPixels(pixels.Width,pixels.Height,scale,scale);
                MinWidth=fit.MinWidth;MinHeight=fit.MinHeight;MaxWidth=fit.MaxWidth;MaxHeight=fit.MaxHeight;Width=fit.Width;Height=fit.Height;
                await Task.Delay(60);
                foreach(var page in new[]{"Фильмы","Загрузки","Настройки"})
                {
                    section=page;current=null;Render();UpdateLayout();
                    if(ActualWidth>fit.MaxWidth+1||ActualHeight>fit.MaxHeight+1)throw new Exception("Window overflows scaled work area");
                    if(Body.ActualWidth<90||Body.ActualHeight<20)throw new Exception($"{page}: no usable content at {scale*100}%: body {Body.ActualWidth} x {Body.ActualHeight}, header {PageHeader.ActualHeight}, window {ActualWidth} x {ActualHeight}");
                    var lastNavigationBottom=Navigation.TransformToAncestor(SidePanel).Transform(new Point(0,Navigation.ActualHeight)).Y;
                    var footerTop=SidebarFooter.TransformToAncestor(SidePanel).Transform(new Point()).Y;
                    if(lastNavigationBottom>footerTop+.5)throw new Exception($"Sidebar navigation overlaps footer at {scale*100}%");
                    foreach(var action in VisualElements<Button>(Body).Where(b=>b.IsVisible&&b.ActualWidth>0&&AutomationProperties.GetName(b) is "Продолжить" or "Удалить файлы" or "Подробнее"))
                    {
                        var box=action.TransformToAncestor(Body).TransformBounds(new Rect(new Point(),action.RenderSize));
                        if(box.Left<-.5||box.Right>Body.ActualWidth+1)throw new Exception($"{page}: action clipped at {scale*100}%: "+AutomationProperties.GetName(action));
                    }
                    cases.Add(new{PhysicalWidth=pixels.Width,PhysicalHeight=pixels.Height,Scale=scale,Page=page,LogicalWidth=ActualWidth,LogicalHeight=ActualHeight,BodyWidth=Body.ActualWidth,BodyHeight=Body.ActualHeight});
                }
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(this);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var image=File.Create(Path.Combine(output,$"viewport-{pixels.Width}-{scale*100:F0}.png"));encoder.Save(image);
            }
            File.WriteAllText(Path.Combine(output,"dpi-viewports.json"),JsonSerializer.Serialize(cases,new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{foreach(var item in fixtures)downloads.Items.Remove(item);}
        Close();
    }
    public async Task CloseSmokeTest(string output)
    {
        Directory.CreateDirectory(output);
        liveRequest?.Cancel();liveLoading=false;
        var socket=new UdpClient(new IPEndPoint(IPAddress.Loopback,0));
        var port=((IPEndPoint)socket.Client.LocalEndPoint!).Port;
        var source="magnet:?xt=urn:btih:"+new string('d',40)+"&dn=close-fixture&tr="+Uri.EscapeDataString($"udp://127.0.0.1:{port}/announce");
        await downloads.Add(source,Path.Combine(output,"files"));
        section="Загрузки";Render();UpdateLayout();
        var watch=Stopwatch.StartNew();
        Closed+=(_,_)=>
        {
            socket.Dispose();
            var saved=JsonSerializer.Deserialize<List<DownloadItem>>(File.ReadAllText(Path.Combine(Preferences.DataDir,"queue.json")))!;
            File.WriteAllText(Path.Combine(output,"closed.json"),JsonSerializer.Serialize(new{ElapsedSeconds=watch.Elapsed.TotalSeconds,QueueSaved=saved.Any(x=>x.Source==source&&!x.Paused),UsedNativeCloseCommand=true}));
        };
        SystemCommands.CloseWindow(this);
    }
}
