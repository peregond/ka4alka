using Kachalka;

static class ReleaseSearchTests
{
    sealed class Reporter(Action<ReleaseSearchUpdate> report):IProgress<ReleaseSearchUpdate>{public void Report(ReleaseSearchUpdate value)=>report(value);}
    public static async Task Run()
    {
        void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS: "+name);}
        var hash=new string('a',40);
        var row=new SourceEntry("fast","Moonrise (2026) WEB-DL 1080p","Fast","","magnet:?xt=urn:btih:"+hash,null,1024,5);
        var slow=new TaskCompletionSource<IReadOnlyList<SourceEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first=new TaskCompletionSource<ReleaseSearchUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reports=new List<ReleaseSearchUpdate>();
        var reporter=new Reporter(update=>{lock(reports)reports.Add(update);if(update.Items.Length>0&&!update.Complete)first.TrySetResult(update);});
        var scan=ReleaseSearch.RunAsync([
            new("Fast",_=>Task.FromResult<IReadOnlyList<SourceEntry>>([row])),
            new("Broken",_=>Task.FromException<IReadOnlyList<SourceEntry>>(new HttpRequestException("offline"))),
            new("Slow",_=>slow.Task)
        ],reporter);
        var partial=await first.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(partial.Items.Single()==row&&!scan.IsCompleted,"fast source publishes usable release before slow source finishes");
        slow.SetResult([]);var result=await scan;
        Check(result.Items.Length==1&&result.Sources.Single(x=>x.Name=="Broken").State==SourceState.Unavailable,"failed source does not remove working source results");
        Check(result.Sources.Single(x=>x.Name=="Slow") is {State:SourceState.Empty,LastSuccessUtc:not null},"empty successful reply is distinct from unavailable source");
        Check(reports.Last().Complete&&reports.Last().Sources.All(x=>x.State!=SourceState.Searching),"progress ends with every attempted source settled");
        var timed=await ReleaseSearch.RunAsync([new("Timeout",async ct=>{await Task.Delay(Timeout.InfiniteTimeSpan,ct);return [];}),new("Fast",_=>Task.FromResult<IReadOnlyList<SourceEntry>>([row]))],timeout:TimeSpan.FromMilliseconds(60));
        Check(timed.Sources.Single(x=>x.Name=="Timeout").State==SourceState.TimedOut&&timed.Items.Length==1,"timeout remains local to one source");
        using(var cancel=new CancellationTokenSource())
        {
            var canceled=ReleaseSearch.RunAsync([new("Canceled",async ct=>{await Task.Delay(Timeout.InfiniteTimeSpan,ct);return [];})],ct:cancel.Token);
            cancel.Cancel();try{await canceled;throw new Exception("Cancellation was swallowed");}catch(OperationCanceledException){Console.WriteLine("PASS: caller cancellation stops release search");}
        }
        var saved=row with{Id="saved",Seeds=900};var fresh=row with{Seeds=2};
        Check(ReleaseSearch.WithSaved([fresh],[saved]).Single().Seeds==2,"fresh seeder count replaces older higher cached count");
        Check(ReleaseSearch.Distinct([row,row with{Source="Other",Seeds=10}]).Single().Seeds==10,"same torrent from multiple live sources is deduplicated");
        var fallback=row with{Id="other",TorrentUrl="magnet:?xt=urn:btih:"+new string('b',40)};
        Check(ReleaseSearch.WithSaved([fresh],[saved,fallback]).Length==2,"partial source success retains distinct saved alternatives");

        var movie=new MediaItem(-33,"Лунный берег","Фильмы","",2026,"—","—","#526B69"){OriginalTitle="Moonrise",PageUrl="https://w6.zona.plus/movies/moonrise"};
        Check(!LiveCatalog.Matches(movie,row with{Source="Internet Archive",Title="Moonrise (2025) 1080p"}),"wrong declared film year is rejected beyond RuTor");
        Check(LiveCatalog.Matches(movie,row with{Title="Moonrise 1080p WEB-DL"}),"quality suffix without declared year can match title");
        Check(!LiveCatalog.Matches(movie,row with{Title="Лунный берег / Moonrise (2025) WEB-DL"}),"wrong declared year after original title alias is rejected");
        var numbered=movie with{Title="2001: Космическая одиссея",OriginalTitle="2001 A Space Odyssey",Year=1968};
        Check(LiveCatalog.Matches(numbered,row with{Title="2001 A Space Odyssey 1080p BluRay"}),"number in film title is not mistaken for its release year");
        var directory=Path.Combine("test-output","release-cache-"+Guid.NewGuid().ToString("N"));var index=new CatalogIndex(directory);
        await index.CacheReleasesAsync(movie,[row],result.Sources);
        var loaded=new CatalogIndex(directory).CachedReleaseSnapshot(movie);
        Check(loaded is {Items.Length:1,Sources.Length:3}&&loaded.Sources.Single(x=>x.Name=="Fast").LastSuccessUtc.HasValue,"release cache restores receipt time and per-source evidence");
        await index.CacheReleasesAsync(movie,[]);
        Check(new CatalogIndex(directory).CachedReleaseSnapshot(movie)?.SavedUtc==loaded!.SavedUtc,"empty failed refresh does not rewrite useful cache timestamp");
        // Older application versions saved the same envelope without the optional Sources property.
        var legacyPath=Directory.GetFiles(Path.Combine(directory,"release-index"),"*.json").Single();
        await File.WriteAllTextAsync(legacyPath,System.Text.Json.JsonSerializer.Serialize(new{loaded.SavedUtc,loaded.Items}));
        Check(new CatalogIndex(directory).CachedReleaseSnapshot(movie) is {Items.Length:1,Sources:null},"older release cache remains readable after source status upgrade");
    }
}
