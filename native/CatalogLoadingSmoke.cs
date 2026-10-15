using System.Diagnostics;
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
    public async Task CatalogLoadingSmokeTest(string output)
    {
        Directory.CreateDirectory(output);liveRequest?.Cancel();searchDelay.Stop();catalogRefreshTimer.Stop();refresh.Stop();metadataLifetime.Cancel();
        var checks=new List<string>();var samples=new List<object>();
        void Check(bool condition,string message){if(!condition)throw new Exception(message);checks.Add(message);}
        async Task Until(Func<bool> condition,string message)
        {for(var attempt=0;attempt<200;attempt++){UpdateLayout();if(condition())return;await Task.Delay(20);}throw new Exception(message);}
        BitmapImage Art()
        {
            var pixels=Enumerable.Repeat((byte)120,32*48*4).ToArray();for(var i=3;i<pixels.Length;i+=4)pixels[i]=255;
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(32,48,96,96,PixelFormats.Bgra32,null,pixels,128)));
            using var bytes=new MemoryStream();encoder.Save(bytes);bytes.Position=0;
            var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=bytes;bitmap.EndInit();bitmap.Freeze();return bitmap;
        }
        var bitmap=Art();MaxWidth=1600;MaxHeight=1000;MinWidth=1100;MinHeight=700;Width=1280;Height=900;
        foreach(var lite in new[]{false,true})foreach(var category in new[]{"Фильмы","Сериалы"})
        {
            CancelCatalogQualityCheck();ResetDiscoveryData();qualitySnapshots.Clear();catalogQualityAttempts.Clear();catalogIndex.ClearReleaseMemory();
            prefs.LiteMode=lite;ApplyLiteMode();section=category;current=null;activePerson=null;submittedQuery="";Search.Text="";searchDelay.Stop();favoritesOnly=false;livePage=1;ResetCatalogFilters();
            prefs.CatalogQualityHeight=0;prefs.HidePoorQuality=false;prefs.HiddenTitles=[];prefs.HiddenGenres=[];
            var home=CatalogRegions.HomeCountry(prefs.HomeCountry);
            var cards=Enumerable.Range(1,32).Select(index=>new MediaItem(-998000-index,"Карточка "+index,category,"драма",2026,"7.0","7.0","#526B69")
            {PageUrl=LiveCatalog.Base+(category=="Фильмы"?"/movies/":"/tvseries/")+"loading-fixture-"+index,ImageUrl="https://example.invalid/stable-poster-"+index+".png",GenreKeys=["drama"],CountryKeys=[home],Description="Описание",People=[new CinemaPerson("Актёр","Актёры","")]}).ToArray();
            ReleaseCache Evidence(MediaItem item)=>new(DateTime.UtcNow,[new SourceEntry("loading-"+item.Id,item.Title+" WEB-DL 1080p","Fixture","","magnet:?xt=urn:btih:"+new string('a',40),null)], [new SourceCheck("Fixture",SourceState.Ready,1,DateTime.UtcNow,DateTime.UtcNow)]);
            foreach(var card in cards){coverCache.Put(card.ImageUrl+"|280",bitmap);cardMetadata[card.Id]=Task.FromResult(card);}
            foreach(var card in cards.Take(8))qualitySnapshots[card.Id]=Evidence(card);
            liveItems=cards;liveLoading=false;liveError="";catalogPages[category+"||1"]=new(cards.Take(8).ToArray(),false,CatalogChoices.Genres,CatalogChoices.Countries);
            SetDiscoveryRegionalItems(category,home,[],cards);liveKey=CurrentCatalogKey;Render();UpdateLayout();
            await Until(()=>VisualElements<Image>(Body).Any(image=>image.Source!=null),"Initial cached posters did not arrive.");
            var scroll=navigationScroll;var list=catalogList;var baseline=renderCount;
            var retained=VisualElements<Image>(discoveryShelves[DiscoveryShelfKind.Popular].Row).ToArray();
            await Until(()=>retained.Any(image=>image.Source!=null),"Visible popular posters did not arrive.");
            var images=retained.Where(image=>image.Source!=null).Select(image=>(Image:image,Source:image.Source)).ToArray();
            Check(images.Length>0,"visible posters exist before delayed evidence: "+category+" lite="+lite);
            using var recording=new CancellationTokenSource();var inputDelays=new List<double>();
            var input=Task.Run(async()=>
            {
                try{while(!recording.IsCancellationRequested){await Task.Delay(25,recording.Token);var sent=Stopwatch.GetTimestamp();await Dispatcher.InvokeAsync(()=>inputDelays.Add(Stopwatch.GetElapsedTime(sent).TotalMilliseconds),DispatcherPriority.Input,recording.Token);}}
                catch(OperationCanceledException){}
            });
            var frames=new List<double>();long lastFrame=0;
            EventHandler rendered=(_,_)=>{var now=Stopwatch.GetTimestamp();if(lastFrame!=0)frames.Add(Stopwatch.GetElapsedTime(lastFrame,now).TotalMilliseconds);lastFrame=now;};
            CompositionTarget.Rendering+=rendered;
            try
            {
                foreach(var chunk in cards.Skip(8).Chunk(3))
                {
                    foreach(var card in chunk)qualitySnapshots[card.Id]=Evidence(card);
                    ScheduleCatalogEvidenceRefresh();
                    await Until(()=>catalogDisplay.Any(item=>item.Id==chunk[^1].Id),"Delayed evidence did not reach existing catalog.");
                    Check(ReferenceEquals(scroll,navigationScroll)&&ReferenceEquals(list,catalogList)&&renderCount==baseline,"background evidence retains page, scroll viewer and row controls: "+category+" lite="+lite+" count="+catalogDisplay.Count);
                    Check(images.All(pair=>pair.Image.IsLoaded&&ReferenceEquals(pair.Image.Source,pair.Source)),"ready posters remain drawn while neighbouring cards arrive: "+category+" lite="+lite);
                    foreach(var card in cards.Take(8))card.SetScores("8.1","7.0","драма");
                    await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.Background);
                }
            }
            finally{CompositionTarget.Rendering-=rendered;recording.Cancel();await input;}
            inputDelays.Sort();frames.Sort();
            var p95=inputDelays.Count==0?0:inputDelays[(int)((inputDelays.Count-1)*.95)];
            Check(inputDelays.Count>10&&p95<200,"native input continues during slow catalog loading: "+category+" lite="+lite);
            samples.Add(new{Section=category,Lite=lite,BackgroundPageRebuilds=renderCount-baseline,RetainedPosters=images.Length,InputSamples=inputDelays.Count,InputP95Ms=p95,InputMaxMs=inputDelays.DefaultIfEmpty().Max(),FrameP95Ms=frames.Count==0?0:frames[(int)((frames.Count-1)*.95)]});
        }
        await File.WriteAllTextAsync(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{Checks=checks,Samples=samples},new JsonSerializerOptions{WriteIndented=true}));Close();
    }
}
