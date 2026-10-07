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
            Calls++;if(offline)throw new HttpRequestException("offline");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});
        }
    }
    public static async Task Run()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var before=new DateTime(2026,10,7,4,59,0,DateTimeKind.Utc);var after=before.AddMinutes(1);
        Check(SharedCatalog.BoundaryUtc(before)==new DateTime(2026,10,6,5,0,0,DateTimeKind.Utc)&&SharedCatalog.BoundaryUtc(after)==after,"daily catalog refresh changes at 05:00 UTC rather than on every navigation");
        var rows=new[]{"movies","series"}.SelectMany(section=>Enumerable.Range(1,section=="movies"?2000:300).Select(id=>new
        {
            id=section+":shared-"+id,section,title="Новое кино "+id,year=id==1?1992:2026,pageUrl="https://w6.zona.plus/"+(section=="movies"?"movies":"tvseries")+"/shared-"+id
        })).ToArray();
        byte[] Feed(DateTime generated,int schema=1)=>JsonSerializer.SerializeToUtf8Bytes(new{schemaVersion=schema,generatedAtUtc=generated,items=rows});
        var data=Feed(DateTime.UtcNow);
        var parsed=SharedCatalog.Parse(data,DateTime.UtcNow);
        Check(parsed.Items.Length==2300&&parsed.Items[0].Year==2026&&parsed.Items[^1].Year==1992,"shared catalog validates both libraries and places newer releases before older titles");
        foreach(var invalid in new[]{Feed(DateTime.UtcNow.AddDays(1)),Feed(DateTime.UtcNow,2),JsonSerializer.SerializeToUtf8Bytes(new{schemaVersion=1,generatedAtUtc=DateTime.UtcNow,items=rows.Take(1)})})
        {
            bool rejected=false;try{SharedCatalog.Parse(invalid,DateTime.UtcNow);}catch(IOException){rejected=true;}
            Check(rejected,"malformed, future or incomplete daily feed cannot replace the complete library");
        }
        var prior=Environment.GetEnvironmentVariable("KACHALKA_DATA");
        try
        {
            var folder=Path.Combine(Preferences.DataDir,"shared-feed-test");Directory.CreateDirectory(folder);Environment.SetEnvironmentVariable("KACHALKA_DATA",folder);
            var handler=new FeedHandler(data);using var client=new SourceClient(handler);var shared=new SharedCatalog(client);
            Check(shared.RefreshDue,"a library without a complete feed is eligible for a background refresh");
            var first=await shared.PageAsync("Фильмы",1,default);var last=await shared.PageAsync("Фильмы",50,default);
            Check(first is{Items.Length:40,HasNext:true}&&last is{Items.Length:40,HasNext:false}&&!first.Items.Select(item=>item.Id).Intersect(last.Items.Select(item=>item.Id)).Any()&&handler.Calls==1,"one daily feed provides all 50 pages without repeated network downloads or overlapping movies");
            Check(!shared.RefreshDue,"a current complete feed does not trigger repeated background refreshes");
            Check(shared.Search("Сериалы","Новое кино 20").Count>0,"newly indexed series are available to the existing unified search");
            var offline=new FeedHandler([],true);using var offlineClient=new SourceClient(offline);var reopened=new SharedCatalog(offlineClient);
            Check((await reopened.PageAsync("Сериалы",1,default))?.Items.Length==40&&offline.Calls==0,"a fresh persisted library remains available after restart without a network request");
            File.WriteAllBytes(Path.Combine(folder,"catalog-shared.json"),Feed(DateTime.UtcNow.AddDays(-2)));
            var expired=new SharedCatalog(offlineClient);
            Check((await expired.PageAsync("Фильмы",1,default))?.Items.Length==40&&offline.Calls==1,"failed daily refresh keeps the previous complete library instead of clearing it");
            Check(!expired.RefreshDue,"background refresh honors the offline retry delay");
            Check((await expired.PageAsync("Фильмы",2,default))?.Items.Length==40&&offline.Calls==1,"offline retry delay avoids fetching the same failed feed for every page");
            using var cancel=new CancellationTokenSource();cancel.Cancel();bool cancelled=false;
            try{await expired.PageAsync("Фильмы",1,cancel.Token);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"cancelled browsing cannot publish a daily catalog result");
        }
        finally{Environment.SetEnvironmentVariable("KACHALKA_DATA",prior);}
    }
}
