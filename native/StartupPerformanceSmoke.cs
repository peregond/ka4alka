using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;
namespace Kachalka;

public partial class MainWindow
{
    sealed class StartupSourceHandler:HttpMessageHandler
    {
        public readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release=new();
        public int Thread;public bool TimedOut;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Thread=Environment.CurrentManagedThreadId;Started.TrySetResult();
            TimedOut=!Release.Wait(TimeSpan.FromSeconds(5),ct);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"items\":[],\"hasMore\":false}")});
        }
        protected override void Dispose(bool disposing){if(disposing)Release.Dispose();base.Dispose(disposing);}
    }
    public async Task StartupPerformanceSmokeTest(string output)
    {
        // Local performance fixtures must not be replaced by a late startup catalog refresh.
        section="Настройки";metadataLifetime.Cancel();
        Directory.CreateDirectory(output);var ui=Environment.CurrentManagedThreadId;var beats=0;
        var heartbeat=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(15)};
        heartbeat.Tick+=(_,_)=>beats++;heartbeat.Start();
        try
        {
            using(var handler=new StartupSourceHandler())
            using(var client=new SourceClient(handler))
            {
                var task=new OnlineIndexClient(client,new Uri("https://catalog.invalid/")).BrowsePage("Фильмы",1,CancellationToken.None);
                try
                {
                    await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(6));var before=beats;
                    await Task.Delay(150);await Dispatcher.InvokeAsync(()=>beats++,DispatcherPriority.Input);
                    if(handler.Thread==ui||handler.TimedOut||task.IsCompleted||beats-before<3)throw new Exception("Catalog loading blocks window input.");
                }
                finally{handler.Release.Set();}
                await task;
            }
            using(var release=new ManualResetEventSlim())
            {
                var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var worker=0;var timedOut=false;
                var task=ReleaseSearch.RunAsync([new("Busy parser",ct=>
                {
                    worker=Environment.CurrentManagedThreadId;started.TrySetResult();timedOut=!release.Wait(TimeSpan.FromSeconds(5),ct);
                    return Task.FromResult<IReadOnlyList<SourceEntry>>([]);
                })]);
                try
                {
                    await started.Task.WaitAsync(TimeSpan.FromSeconds(6));var before=beats;
                    await Task.Delay(150);await Dispatcher.InvokeAsync(()=>beats++,DispatcherPriority.Input);
                    if(worker==ui||timedOut||task.IsCompleted||beats-before<3)throw new Exception("Release source work blocks window input.");
                }
                finally{release.Set();}
                await task;
            }
            var indexFolder=Path.Combine(output,"catalog-index-fixture");
            var original=new CatalogIndex(indexFolder);
            var known=new MediaItem(-987401,"Проверка сохранённого каталога","Фильмы","",2026,"—","—","#123456"){PageUrl="https://w6.zona.plus/movies/startup-performance"};
            await original.AddAsync([known]);
            var restored=new CatalogIndex(indexFolder,deferRestore:true);await restored.RestoreAsync();
            if(restored.Search("Фильмы","Проверка").Single().Id!=known.Id)throw new Exception("Deferred catalog restoration lost saved titles.");
            await restored.ClearMemoryAsync();await restored.RestoreAsync();
            if(restored.Recent("Фильмы").Count!=0)throw new Exception("A late catalog restore repopulates cleared state.");
            await BundledCatalog.EnsureLoadedAsync();
            static string Normalize(string value)=>Regex.Replace(value.Replace('ё','е').Replace('Ё','Е').ToLowerInvariant(),@"[^\p{L}\p{N}]+"," ").Trim();
            var queries=BundledCatalog.All.Take(8).Select(x=>(x.Section,x.Title)).Append(("Фильмы","несуществующий запрос ёжик")).ToArray();
            MediaItem[] Baseline(string section,string query)
            {
                var words=Normalize(query).Split(' ',StringSplitOptions.RemoveEmptyEntries);
                return BundledCatalog.All.Where(x=>x.Section==section&&words.All(word=>Normalize(x.Title+" "+x.OriginalTitle+" "+x.Year).Split(' ').Any(part=>part.StartsWith(word,StringComparison.Ordinal)))).Take(80).ToArray();
            }
            // Compare equivalent real queries with the previous algorithm, after both paths are warm.
            foreach(var (kind,query) in queries)
                if(!Baseline(kind,query).Select(x=>x.Id).SequenceEqual(BundledCatalog.Search(kind,query).Select(x=>x.Id)))throw new Exception("Prepared search changed catalog matches.");
            var measurements=await Task.Run(()=>
            {
                var watch=Stopwatch.StartNew();var allocated=GC.GetAllocatedBytesForCurrentThread();
                foreach(var (kind,query) in queries)Baseline(kind,query);
                var oldMs=watch.Elapsed.TotalMilliseconds;var oldBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
                watch.Restart();allocated=GC.GetAllocatedBytesForCurrentThread();
                foreach(var (kind,query) in queries)BundledCatalog.Search(kind,query);
                return new {PreviousMs=oldMs,PreparedMs=watch.Elapsed.TotalMilliseconds,PreviousAllocatedBytes=oldBytes,PreparedAllocatedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated};
            });
            if(measurements.PreparedAllocatedBytes>=measurements.PreviousAllocatedBytes/2)throw new Exception("Prepared title search still repeatedly allocates normalized names.");
            await File.WriteAllTextAsync(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new {DeferredCatalogPreserved=true,ClearNotRepopulated=true,CatalogWorker=true,ReleaseWorker=true,NativeInputWhileLoading=true,HeartbeatTicks=beats,SearchResultsUnchanged=true,Queries=queries.Length,Records=BundledCatalog.All.Count,Search=measurements}));
        }
        finally{heartbeat.Stop();}
        Close();
    }
}
