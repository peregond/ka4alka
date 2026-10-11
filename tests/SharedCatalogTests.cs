using System.Net;
using System.Text.Json;
using Kachalka;

static class SharedCatalogTests
{
    sealed class FeedHandler(byte[] bytes,bool offline=false):HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            // These fixtures exercise the compatibility feed; manifest requests must be a real 404.
            if(request.RequestUri!=SharedCatalog.FeedUri)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            Calls++;if(offline)throw new HttpRequestException("offline");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});
        }
    }
    sealed class PendingFeedHandler(byte[] bytes):HttpMessageHandler
    {
        public int Calls;
        public readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.RequestUri!=SharedCatalog.FeedUri)return new(HttpStatusCode.NotFound);
            Calls++;Started.TrySetResult();await Release.Task.WaitAsync(token);
            return new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
        }
    }
    public static async Task Run()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var before=new DateTime(2026,10,7,4,59,0,DateTimeKind.Utc);var after=before.AddMinutes(1);
        Check(SharedCatalog.BoundaryUtc(before)==new DateTime(2026,10,6,5,0,0,DateTimeKind.Utc)&&SharedCatalog.BoundaryUtc(after)==after,"discovery page memory still expires at 05:00 UTC independently of the weekly snapshot check");
        var now=new DateTime(2026,10,11,12,0,0,DateTimeKind.Utc);var initial=now;
        var rows=new[]{"movies","series"}.SelectMany(section=>Enumerable.Range(1,section=="movies"?2000:300).Select(id=>new
        {
            id=section+":shared-"+id,section,title="Новое кино "+id,year=id==1?1992:2026,pageUrl="https://w6.zona.plus/"+(section=="movies"?"movies":"tvseries")+"/shared-"+id
        })).ToArray();
        byte[] Feed(DateTime generated,int schema=1)=>JsonSerializer.SerializeToUtf8Bytes(new{schemaVersion=schema,generatedAtUtc=generated,items=rows});
        var data=Feed(now.AddDays(-3));
        var parsed=SharedCatalog.Parse(data,now);
        Check(parsed.Items.Length==2300&&parsed.Items[0].Year==2026&&parsed.Items[^1].Year==1992,"shared catalog validates both libraries and places newer releases before older titles");
        foreach(var invalid in new[]{Feed(now.AddDays(1)),Feed(now,2),JsonSerializer.SerializeToUtf8Bytes(new{schemaVersion=1,generatedAtUtc=now,items=rows.Take(1)})})
        {
            bool rejected=false;try{SharedCatalog.Parse(invalid,now);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"malformed, future or incomplete feed cannot replace the complete library");
        }
        var rich=JsonSerializer.SerializeToUtf8Bytes(new{schemaVersion=1,generatedAtUtc=now,items=rows.Select(row=>new{row.id,row.section,row.title,row.year,row.pageUrl,description=new string('d',2100)})});
        Check(rich.Length>4*1024*1024&&SharedCatalog.Parse(rich,now).Items.Length==2300,"rich text snapshots above the old 4 MB limit remain supported");
        var tooLarge=false;try{SharedCatalog.Parse(new byte[SharedCatalog.MaxBytes+1],now);}catch(InvalidDataException){tooLarge=true;}
        Check(tooLarge,"snapshot byte limit is checked before parsing oversized input");
        var tooMany=false;try{SharedCatalog.Parse(JsonSerializer.SerializeToUtf8Bytes(new{schemaVersion=1,generatedAtUtc=now,items=Enumerable.Repeat(rows[0],15001)}),now);}catch(InvalidDataException){tooMany=true;}
        Check(tooMany,"the 15000 record limit remains enforced independently of the text byte limit");
        var prior=Environment.GetEnvironmentVariable("KACHALKA_DATA");
        try
        {
            var folder=Path.Combine(Preferences.DataDir,"shared-feed-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);Environment.SetEnvironmentVariable("KACHALKA_DATA",folder);
            var pending=new PendingFeedHandler(data);using var client=new SourceClient(pending);var shared=new SharedCatalog(client,()=>now);
            var first=await shared.PageAsync("Фильмы",1,default).WaitAsync(TimeSpan.FromSeconds(2));
            var refresh=shared.RefreshAsync();await pending.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var second=await shared.PageAsync("Фильмы",2,default,true).WaitAsync(TimeSpan.FromSeconds(2));
            Check(first is{Items.Length:40}&&second is{Items.Length:40}&&!refresh.IsCompleted&&pending.Calls==1,"embedded pages remain immediately available while all refresh callers share one blocked download");
            pending.Release.TrySetResult();Check(await refresh,"a successful background refresh publishes a newer complete snapshot");
            var latest=await shared.PageAsync("Фильмы",1,default);var last=await shared.PageAsync("Фильмы",50,default);
            Check(latest is{Items.Length:40,HasNext:true}&&last is{Items.Length:40,HasNext:false}&&!latest.Items.Select(item=>item.Id).Intersect(last.Items.Select(item=>item.Id)).Any()&&pending.Calls==1,"one checked snapshot provides all pages without repeated downloads or overlapping movies");
            Check(shared.LastCheckedUtc==now&&!shared.RefreshDue&&shared.Search("Сериалы","Новое кино 20").Count>0,"successful check time, rather than the older feed generation time, controls freshness");
            now=initial.AddDays(7).AddTicks(-1);
            Check(!shared.RefreshDue&&!await shared.RefreshAsync()&&pending.Calls==1,"a successful check suppresses automatic downloads for a full seven days");
            now=initial.AddDays(7);
            Check(shared.RefreshDue&&!await shared.RefreshAsync()&&pending.Calls==2&&shared.LastCheckedUtc==now&&!shared.RefreshDue,"an unchanged valid response records a successful weekly check without claiming a changed snapshot");
            var offline=new FeedHandler([],true);using var offlineClient=new SourceClient(offline);var reopened=new SharedCatalog(offlineClient,()=>now);
            await reopened.RestoreAsync();
            Check((await reopened.PageAsync("Сериалы",1,default))?.Items.Length==40&&offline.Calls==0&&!reopened.RefreshDue,"persisted successful checks survive restart even when generated data is older than a week");
            var manualHandler=new FeedHandler(data);using var manualClient=new SourceClient(manualHandler);var manual=new SharedCatalog(manualClient,()=>now);
            await manual.RestoreAsync();Check(!manual.RefreshDue&&!await manual.RefreshAsync(force:true)&&manualHandler.Calls==1,"manual refresh bypasses the weekly gate and checks an unchanged feed");
            now=initial.AddDays(15);
            var cachedPending=new PendingFeedHandler(Feed(now));using var cachedClient=new SourceClient(cachedPending);var cached=new SharedCatalog(cachedClient,()=>now);
            var cachedPage=await cached.PageAsync("Фильмы",1,default).WaitAsync(TimeSpan.FromSeconds(2));var cachedRefresh=cached.RefreshAsync();
            await cachedPending.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(cachedPage?.Items.First().Title.StartsWith("Новое кино")==true&&!cachedRefresh.IsCompleted,"an expired persisted snapshot remains visible before its refresh finishes");
            cachedPending.Release.TrySetResult();await cachedRefresh;
            File.WriteAllBytes(Path.Combine(folder,"catalog-shared.json"),Feed(initial.AddDays(-2)));
            File.Delete(Path.Combine(folder,"catalog-shared-sync.json"));
            var expired=new SharedCatalog(offlineClient,()=>now);
            Check((await expired.PageAsync("Фильмы",1,default))?.Items.Length==40,"a failed refresh keeps the previous complete library");
            await expired.RefreshAsync();
            Check(offline.Calls==1&&!expired.RefreshDue,"offline refresh failure enters the retry cooldown without advancing successful check time");
            Check((await expired.PageAsync("Фильмы",2,default))?.Items.Length==40&&offline.Calls==1,"offline retry delay avoids downloading the same failed feed for every page");
            await expired.RefreshAsync(force:true);Check(offline.Calls==2,"manual refresh also bypasses the offline retry cooldown");
            File.WriteAllBytes(Path.Combine(folder,"catalog-shared.json"),Feed(now,2));
            File.WriteAllText(Path.Combine(folder,"catalog-shared-sync.json"),JsonSerializer.Serialize(new{LastCheckedUtc=now}));
            var repairHandler=new FeedHandler(Feed(now));using var repairClient=new SourceClient(repairHandler);var repaired=new SharedCatalog(repairClient,()=>now);
            await repaired.RestoreAsync();Check(repaired.RefreshDue,"a check timestamp cannot conceal an invalid persisted catalog");
            await repaired.RefreshAsync();Check(repairHandler.Calls==1&&repaired.All.Count==2300,"an invalid persisted catalog is repaired by a complete feed");
            using var cancel=new CancellationTokenSource();cancel.Cancel();bool cancelled=false;
            try{await repaired.PageAsync("Фильмы",1,cancel.Token);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"cancelled browsing cannot publish a catalog page");
        }
        finally{Environment.SetEnvironmentVariable("KACHALKA_DATA",prior);}
    }
}
