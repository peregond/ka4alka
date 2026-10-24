using System.Text.Json;
using Kachalka.Lan;

static class PhoneBrowserFixture
{
    public static async Task Run()
    {
        var added=false;var paused=false;var missing=true;var failedOnce=false;
        await using var server=new PhoneRemoteServer((method,args,ct)=>Task.FromResult<object>(method switch
        {
            "catalog"=>Catalog(args),
            "releases"=>new{items=new[]{
                new{id="release",title="Test movie · Full HD · Dub",source="Local test",quality="Full HD",audio="Dub",size=1073741824L,seeds=(int?)10,downloadable=true,recommended=true},
                new{id="original",title="Test movie · 4K · Original",source="Local test",quality="4K",audio="Original",size=3221225472L,seeds=(int?)40,downloadable=true,recommended=false},
                new{id="unavailable",title="Unavailable fixture",source="Local test",quality="Full HD",audio="Original",size=2147483648L,seeds=(int?)null,downloadable=false,recommended=false}}},
            "add"=>Add(),
            "downloads"=>new{items=added?new[]{
                new{id="download",title="Test movie",status=paused?"Paused":"Downloading",statusKind=paused?"paused":"downloading",progress=42,paused,filesMissing=false,busy=false,complete=false,downloadRate=5242880L,uploadRate=102400L,totalBytes=3221225472L,poster=(string?)null},
                new{id="missing",title="Missing movie",status=missing?"Files missing":"Downloading",statusKind=missing?"error":"downloading",progress=missing?100:0,paused=missing,filesMissing=missing,busy=false,complete=false,downloadRate=0L,uploadRate=0L,totalBytes=1073741824L,poster=(string?)null},
                new{id="complete",title="Completed movie",status="Complete",statusKind="paused",progress=100,paused=true,filesMissing=false,busy=false,complete=true,downloadRate=0L,uploadRate=0L,totalBytes=2147483648L,poster=(string?)null}}:[]},
            "control"=>Control(args),
            _=>throw new ArgumentException()
        }),0,true);
        object Catalog(JsonElement args)
        {
            var section=args.TryGetProperty("section",out var s)?s.GetString()??"movies":"movies";
            var query=args.TryGetProperty("query",out var q)?q.GetString()??"":"";
            var page=args.TryGetProperty("page",out var p)?p.GetInt32():1;
            if(query=="network-error"&&!failedOnce){failedOnce=true;throw new InvalidOperationException("Fixture failure must not be exposed.");}
            var count=query=="nothing"?0:page==1?8:1;
            return new{items=Enumerable.Range(0,count).Select(index=>new
            {
                id=section+"-"+page+"-"+index,
                title=index==0?"Тестовый фильм / Test movie":index==1?"<img src=x onerror=alert('unsafe')>":"Test title "+(index+1),
                year=2026,poster=(string?)null,kind=section,description="A browser test fixture, not a live catalog item.",rating="8.1",ratingSource="IMDb"
            }).ToArray(),hasNext=count>0&&page==1};
        }
        object Add(){added=true;return new{id="download"};}
        object Control(JsonElement args)
        {
            var id=args.GetProperty("id").GetString();var action=args.GetProperty("action").GetString();
            if(id=="download"&&action is "pause" or "resume")paused=action=="pause";
            else if(id=="missing"&&action=="redownload"&&args.GetProperty("confirmed").GetBoolean())missing=false;
            else throw new ArgumentException();
            return new{id};
        }
        server.Start();
        // Test harness pipe only; this line is captured in memory, not included in test logs.
        var state=JsonSerializer.SerializeToElement(server.OpenPairing());
        Console.WriteLine(JsonSerializer.Serialize(new{port=server.Port,code=state.GetProperty("code").GetString()}));
        await Task.Run(Console.ReadLine);
    }
}
