using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Kachalka;
public partial class MainWindow
{
    public async Task LayoutSmokeTest(string output)
    {
        Directory.CreateDirectory(output);
        MinWidth=360;MinHeight=300;
        var layouts=new List<object>();
        foreach(var (name,width,height) in new[]{("wide",1520d,950d),("standard",1220d,800d),("scaled",680d,360d),("small",510d,340d)})
        {
            Width=Math.Min(width,MaxWidth-24);Height=Math.Min(height,MaxHeight-24);
            await Task.Delay(160);section="Фильмы";current=null;Render();UpdateLayout();await Task.Delay(100);
            var expected=WindowSizing.PosterColumns(Body.ActualWidth);
            if(catalogColumns!=expected)throw new Exception($"{name}: {catalogColumns} columns instead of {expected}");
            if(ActualWidth>MaxWidth+1||ActualHeight>MaxHeight+1)throw new Exception($"{name}: window exceeds its monitor");
            var bmp=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(this);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(Path.Combine(output,name+".png")))png.Save(f);
            layouts.Add(new{Name=name,Width=Math.Round(ActualWidth),Height=Math.Round(ActualHeight),BodyWidth=Math.Round(Body.ActualWidth),Columns=catalogColumns,FiltersVisible=FiltersPanel.Visibility==Visibility.Visible});
        }
        File.WriteAllText(Path.Combine(output,"layout.json"),JsonSerializer.Serialize(layouts,new JsonSerializerOptions{WriteIndented=true}));Close();
    }
    public async Task BroadcastSmokeTest(string output)
    {
        Directory.CreateDirectory(output);
        var counts=new Dictionary<string,int>();
        foreach(var tab in new[]{"Радио","ТВ-каналы","Спорт"}){
            section=tab;current=null;Render();var until=DateTime.UtcNow.AddSeconds(45);
            while(broadcastLoading&&DateTime.UtcNow<until)await Task.Delay(200);
            if(broadcasts.Count<10)throw new Exception($"{tab}: {broadcasts.Count} streams; {broadcastError}");
            counts[tab]=broadcasts.Count;
        }
        UpdateLayout();var bmp=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(Path.Combine(output,"sports.png")))encoder.Save(f);
        File.WriteAllText(Path.Combine(output,"broadcast.json"),JsonSerializer.Serialize(counts));Close();
    }
    public async Task LiveSmokeTest(string output)
    {
        Directory.CreateDirectory(output);var limit=DateTime.UtcNow.AddSeconds(100);while(liveLoading&&DateTime.UtcNow<limit)await Task.Delay(200);
        if(liveItems.Count==0)throw new Exception("Default live catalog is empty: "+liveError);
        if(liveLoading||liveItems.Count!=40)throw new Exception("Initial catalog did not reach 40 cards: "+liveItems.Count+" · "+liveError);
        if(Navigation.Children.Count!=2)throw new Exception("Only film and series navigation should be visible.");
        await Task.Delay(2000);UpdateLayout();var wideColumns=catalogColumns;var wideFilters=FiltersPanel.Visibility==Visibility.Visible;var bmp=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(this);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(Path.Combine(output,"live-catalog.png")))enc.Save(f);
        var oldWidth=Width;var oldHeight=Height;MinWidth=360;MinHeight=300;Width=680;Height=360;await Task.Delay(250);UpdateLayout();var smallColumns=catalogColumns;
        bmp=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(this);enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(Path.Combine(output,"live-small.png")))enc.Save(f);
        Width=oldWidth;Height=oldHeight;await Task.Delay(250);UpdateLayout();
        var count=liveItems.Count;var first=liveItems[0];var originalIds=liveItems.Select(x=>x.Id).ToArray();
        var catalogScroll=FindVisual<ScrollViewer>(Body,_=>true)!;catalogScroll.ScrollToVerticalOffset(120);await Task.Delay(100);UpdateLayout();var beforeMoreOffset=catalogScroll.VerticalOffset;
        var more=FindVisual<Button>(Body,b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Страница 2")??throw new Exception("Page navigation is missing.");
        more.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));limit=DateTime.UtcNow.AddSeconds(100);while(liveLoading&&DateTime.UtcNow<limit)await Task.Delay(200);UpdateLayout();
        if(liveLoading||liveItems.Count!=40||liveItems.Select(x=>x.Id).Intersect(originalIds).Any())throw new Exception("Page 2 did not replace the first page: "+liveItems.Count+" · "+liveError);
        await Task.Delay(100);var afterMoreOffset=FindVisual<ScrollViewer>(Body,_=>true)!.VerticalOffset;if(afterMoreOffset>2)throw new Exception("Page change did not scroll to top.");
        var afterMoreCount=liveItems.Count;
        current=first;Render();limit=DateTime.UtcNow.AddSeconds(60);while(releaseViews.TryGetValue(first.Id,out var searchingView)&&searchingView.Checking&&DateTime.UtcNow<limit)await Task.Delay(200);
        if(!releaseViews.TryGetValue(first.Id,out var sourceView)||sourceView.Checking)throw new Exception("Release source scan did not complete.");
        sourceView.Expanded=true;RefreshDetail(first.Id);UpdateLayout();FindVisual<TextBlock>(Body,t=>t.Name=="SourceSummary")?.BringIntoView();await Task.Delay(100);
        UpdateLayout();bmp=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(this);enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(Path.Combine(output,"live-detail.png")))enc.Save(f);
        prefs.Favorites.Add(first.Id);prefs.LiveFavorites.Add(first);favoritesOnly=true;current=null;Render();
        if(((ListBox)Body.Children[0]).Items.Count!=1)throw new Exception("Favorite view lost the saved movie.");
        section="Сериалы";favoritesOnly=false;livePage=1;Render();limit=DateTime.UtcNow.AddSeconds(100);while(liveLoading&&DateTime.UtcNow<limit)await Task.Delay(200);
        if(liveItems.Count==0)throw new Exception("Series view is empty: "+liveError);
        if(liveLoading||liveItems.Count!=40)throw new Exception("Series selection did not reach 40 cards: "+liveItems.Count+" · "+liveError);
        File.WriteAllText(Path.Combine(output,"live.json"),JsonSerializer.Serialize(new{Movies=count,SecondPage=afterMoreCount,PagesDoNotOverlap=true,PageChangeScrollsToTop=true,Series=liveItems.Count,First=first.Title,Releases=liveReleases.GetValueOrDefault(first.Id)?.Count,Sources=sourceView.Sources,SourceScanComplete=!sourceView.Checking,WideColumns=wideColumns,SmallColumns=smallColumns,WideFilters=wideFilters,OnlyCinemaSections=Navigation.Children.Count==2,FavoriteVisible=true,EngineCreated=downloads.EngineCreated}));Close();
    }
    public async Task SmokeTest(string output)
    {
        Directory.CreateDirectory(output);
        await Task.Delay(1800);
        var baseline=Process.GetCurrentProcess();baseline.Refresh();var startupMemory=Math.Round(baseline.WorkingSet64/1048576d,1);
        void Shot(string name){UpdateLayout();var bmp=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using var f=File.Create(Path.Combine(output,name+".png"));encoder.Save(f);}
        section="Фильмы";current=null;Render();Shot("catalog");
        Search.Text="Глубина";await Task.Delay(450);
        if(((ListBox)Body.Children[0]).Items.Count!=1)throw new Exception("Search test failed");
        Search.Text="";searchDelay.Stop();current=Catalog.Items[0];Render();Shot("details");
        section="Загрузки";current=null;Render();Shot("downloads");
        section="Фильмы";Render();
        await Task.Delay(3000);var proc=Process.GetCurrentProcess();proc.Refresh();var cpu=proc.TotalProcessorTime;var watch=Stopwatch.StartNew();await Task.Delay(3000);proc.Refresh();
        var result=new{Search="PASS",Screens="catalog, details, downloads",EngineCreated=downloads.EngineCreated,StartupWorkingSetMB=startupMemory,AfterScreenshotsWorkingSetMB=Math.Round(proc.WorkingSet64/1048576d,1),PrivateMemoryMB=Math.Round(proc.PrivateMemorySize64/1048576d,1),IdleCpuOneCorePercent=Math.Round((proc.TotalProcessorTime-cpu).TotalMilliseconds/watch.Elapsed.TotalMilliseconds*100,2)};
        File.WriteAllText(Path.Combine(output,"smoke.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
        Close();
    }
}
