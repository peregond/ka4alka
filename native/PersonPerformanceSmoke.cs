using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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
    sealed class PersonPerformanceHandler(byte[] biography,byte[] filmography):HttpMessageHandler
    {
        public readonly TaskCompletionSource<bool> Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release=new();
        public int RequestThread;public bool TimedOut;int disposed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message,CancellationToken ct)
        {
            var works=message.RequestUri!.AbsolutePath.Contains("person_films/",StringComparison.Ordinal);
            if(!works)
            {
                RequestThread=Environment.CurrentManagedThreadId;Started.TrySetResult(true);
                // A source/cache may complete synchronously. Holding this
                // fixture exposes UI-thread service entry even before await.
                TimedOut=!Release.Wait(TimeSpan.FromSeconds(5),ct);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(works?filmography:biography)});
        }
        protected override void Dispose(bool disposing){if(disposing&&Interlocked.Exchange(ref disposed,1)==0){Release.Set();Release.Dispose();}base.Dispose(disposing);}
    }

    public async Task PersonPerformanceSmokeTest(string output)
    {
        Directory.CreateDirectory(output);liveRequest?.Cancel();searchDelay.Stop();personRequest?.Cancel();
        var uiThread=Environment.CurrentManagedThreadId;var beats=0;
        var heartbeat=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(15)};
        heartbeat.Tick+=(_,_)=>beats++;heartbeat.Start();
        try
        {
            var imageBytes=await Task.Run(()=>
            {
                const int width=2048,height=3072;var pixels=new byte[width*height*4];
                for(var y=0;y<height;y++)for(var x=0;x<width;x++)
                {
                    var offset=(y*width+x)*4;pixels[offset]=(byte)(x/8);pixels[offset+1]=(byte)(y/12);pixels[offset+2]=110;pixels[offset+3]=255;
                }
                var bitmap=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);bitmap.Freeze();
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
            });
            var imagePath=Path.Combine(Preferences.DataDir,"portraits","person-performance.img");await CacheFiles.WriteAllBytesAsync(imagePath,imageBytes);
            var decoding=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);using var releaseDecode=new ManualResetEventSlim();var decodeThread=0;var decodeTimedOut=false;
            var imageLoad=CoverCache.Load(imagePath,2*1024*1024,_=>throw new Exception("A disk-cached photo must not access the network."),bytes=>
            {
                decodeThread=Environment.CurrentManagedThreadId;decoding.TrySetResult(true);decodeTimedOut=!releaseDecode.Wait(TimeSpan.FromSeconds(5));
                using var stream=new MemoryStream(bytes);var result=new BitmapImage();result.BeginInit();result.CacheOption=BitmapCacheOption.OnLoad;result.DecodePixelWidth=300;result.StreamSource=stream;result.EndInit();result.Freeze();return result;
            },CancellationToken.None);
            try
            {
                await decoding.Task.WaitAsync(TimeSpan.FromSeconds(6));var before=beats;
                await Task.Delay(120);await Dispatcher.InvokeAsync(()=>beats++,DispatcherPriority.Input);
                if(decodeThread==uiThread||decodeTimedOut||imageLoad.IsCompleted||beats-before<3)throw new Exception("A cached person photo blocks native UI input while decoding.");
            }
            finally{releaseDecode.Set();}
            var photo=await imageLoad;
            if(!photo.Image.IsFrozen||photo.Image.PixelWidth!=300||photo.Downloaded)throw new Exception("Photo decoding lost its frozen cross-thread bitmap or bounded resolution.");
            // Accessing the bitmap through an actual Dispatcher-owned Image
            // proves it can cross the worker boundary safely.
            var visualPhoto=new Image{Source=photo.Image,Width=150,Height=190};Body.Children.Clear();Body.Children.Add(visualPhoto);UpdateLayout();

            var person=new CinemaPerson("Проверка Производительности "+Guid.NewGuid().ToString("N"),"Актёры","",ProfileUrl:"https://kino-teatr.ua/ru/person/performance-974901.phtml");
            var description="Актёр кино и телевидения. "+string.Join(' ',Enumerable.Repeat("Образование, творческий путь и подтверждённые работы.",32));
            var identity=JsonSerializer.Serialize(new Dictionary<string,object>{["@type"]="Person",["url"]=person.ProfileUrl!,["name"]=person.Name,["jobTitle"]="Actor",["description"]=description});
            // A sizeable, realistic DOM forces the provider's HTML/JSON parser
            // to run; no stub PersonProfile substitutes for the source pipeline.
            var profileBytes=Encoding.UTF8.GetBytes("<script type='application/ld+json'>"+identity+"</script><h1>"+person.Name+"</h1><a href='/ru/person_films/performance-974901.phtml'>Фильмография</a><div>"+string.Concat(Enumerable.Repeat("<span class='credit'>Сведения об участнике</span>",12000))+"</div>");
            using var handler=new PersonPerformanceHandler(profileBytes,Encoding.UTF8.GetBytes("<h1>Фильмография "+person.Name+"</h1>"));using var client=new SourceClient(handler);var progressThreads=new List<int>();
            var profileLoad=LoadPersonInBackground(client,person,CancellationToken.None,null,[],_=>{lock(progressThreads)progressThreads.Add(Environment.CurrentManagedThreadId);});
            try
            {
                await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(6));var before=beats;
                await Task.Delay(120);await Dispatcher.InvokeAsync(()=>beats++,DispatcherPriority.Input);
                if(handler.RequestThread==uiThread||handler.TimedOut||profileLoad.IsCompleted||beats-before<3)throw new Exception("Actor biography source work blocks native UI input.");
            }
            finally{handler.Release.Set();}
            var profile=await profileLoad.WaitAsync(TimeSpan.FromSeconds(10));
            if(profile.Description.Length<50||profile.SourceUrl!=person.ProfileUrl||progressThreads.Count==0||progressThreads.Contains(uiThread))throw new Exception("The worker profile pipeline did not parse/publish the verified professional biography outside the UI thread.");

            // A biography-only progress update must retain the existing poster
            // rows instead of replacing all containers and reloading artwork.
            var films=Enumerable.Range(1,24).Select(id=>new MediaItem(-974900-id,"Работа "+id,"Фильмы","Драма",2020+id%6,"7.2","—","#526B69")).ToArray();
            var rows=new ObservableCollection<CatalogRow>();var gallery=new PersonGalleryView(new Border(),rows);gallery.Resize(700);gallery.Update(films);var originalRows=rows.ToArray();var changes=0;rows.CollectionChanged+=(_,_)=>changes++;
            for(var tick=0;tick<20;tick++)gallery.Update(films.Select(film=>film with{}).ToArray());
            if(changes!=0||!rows.SequenceEqual(originalRows))throw new Exception("Unchanged actor-filmography progress recreates poster rows.");

            using(var canceled=new CancellationTokenSource())
            {
                canceled.Cancel();try{await LoadPersonInBackground(client,person,canceled.Token,null,[]);throw new Exception("Canceled person navigation restarted a profile request.");}catch(OperationCanceledException){}
            }
            await File.WriteAllTextAsync(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{CachedPhotoWorker=true,FrozenBitmap=true,BoundedDecodePixels=photo.Image.PixelWidth,NativeInputDuringPhoto=true,BiographyWorker=true,NativeInputDuringBiography=true,ProfessionalBiography=true,StableFilmographyRows=true,CanceledNavigation=true,HeartbeatTicks=beats,ProfileFixtureBytes=profileBytes.Length}));
        }
        finally{heartbeat.Stop();}
        Close();
    }
}
