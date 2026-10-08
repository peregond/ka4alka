using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kachalka;

public static class CinemaPortraitTests
{
    const string Photo="https://upload.wikimedia.org/wikipedia/commons/person.jpg";
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS: "+message);}
    static byte[] Page(string title,string? image=Photo,string profession="Американский актёр",string? item=null)=>JsonSerializer.SerializeToUtf8Bytes(new
    {
        query=new{pages=new Dictionary<string,object>{["123"]=new{title,extract=profession,thumbnail=image==null?null:new{source=image},pageprops=new{wikibase_item=item}}}}
    });
    sealed class Handler(Func<Uri,CancellationToken,Task<HttpResponseMessage>> response):HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {Interlocked.Increment(ref Calls);return response(request.RequestUri!,ct);}
    }
    static HttpResponseMessage Reply(byte[] bytes)=>new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
    static string Key(string name)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)));

    public static async Task Run()
    {
        var tom=new CinemaPerson("Том Холланд","Актёры","");
        var titles=CinemaPeople.IdentityTitles(tom);
        Check(titles.Contains("Холланд, Том (актёр)")&&titles.Contains("Том Холланд"),"portrait lookup includes exact canonical surname and role-qualified Wikipedia titles");
        Check(CinemaPeople.ParseIdentities(Page("Холланд, Том (актёр)"),tom).Length==1,"canonical name order identifies an actor without fuzzy search");
        Check(CinemaPeople.ParseIdentities(Page("Холланд, Том (историк)",profession:"Британский историк"),tom).Length==0&&
              CinemaPeople.ParseIdentities(Page("Холланд, Джеймс",profession:"Американский актёр"),tom).Length==0,
              "portraits reject another profession and another person's exact name");
        var disambiguation=Encoding.UTF8.GetBytes("""{"query":{"pages":{"1":{"title":"Холланд, Том","extract":"Актёр","pageprops":{"disambiguation":""},"thumbnail":{"source":"https://upload.wikimedia.org/wikipedia/commons/wrong.jpg"}}}}}""");
        Check(CinemaPeople.ParseIdentities(disambiguation,tom).Length==0,"disambiguation pages never supply a photograph");
        Check(CinemaPeople.ParseIdentities(Page("Холланд, Том (актёр)","https://upload.wikimedia.org.evil.test/person.jpg"),tom).Single().PhotoUrl==null,"portrait thumbnails require the trusted Wikimedia image host");
        var ivan=new CinemaPerson("Иван Янковский","Актёры","");
        var redirect=Encoding.UTF8.GetBytes("""{"query":{"redirects":[{"from":"Иван Янковский","to":"Янковский, Иван Филиппович"}],"pages":{"1":{"title":"Янковский, Иван Филиппович","extract":"Российский актёр","thumbnail":{"source":"https://upload.wikimedia.org/wikipedia/commons/person.jpg"}}}}}""");
        Check(CinemaPeople.ParseIdentities(redirect,ivan).Single().ResolvedName==ivan.Name,"an explicit Wikipedia alias safely resolves a canonical name containing a patronymic");
        Check(CinemaPeople.ParseIdentities(redirect,ivan with{Name="Олег Янковский"}).Length==0,"another namesake's redirect cannot identify the requested actor");
        var aliasHandler=new Handler((_,_)=>Task.FromResult(Reply(redirect)));
        using(var client=new SourceClient(aliasHandler))
            Check((await new CinemaPeople(client).ResolvePortrait(ivan,CancellationToken.None)).Status==PortraitStatus.Available,"trusted redirect evidence supplies the actor portrait");
        using(var client=new SourceClient(new Handler((_,_)=>throw new HttpRequestException("fixture unavailable"))))
            Check((await new CinemaPeople(client).ResolvePortrait(ivan,CancellationToken.None)) is {Status:PortraitStatus.Available,FromCache:true},"canonical patronymic identity retains its accepted alias in the offline cache");

        var person=new CinemaPerson("Проверочный актёр "+Guid.NewGuid().ToString("N"),"Актёры","");
        var people=Path.Combine(Preferences.DataDir,"people");Directory.CreateDirectory(people);
        await File.WriteAllTextAsync(Path.Combine(people,Key(person.Name)+".portrait.json"),"null");
        Uri? requested=null;
        var success=new Handler((uri,_)=>{requested=uri;return Task.FromResult(Reply(Page(person.Name)));});
        using(var client=new SourceClient(success))
        {
            var result=await new CinemaPeople(client).ResolvePortrait(person,CancellationToken.None);
            Check(result is {Status:PortraitStatus.Available,Url:Photo}&&success.Calls==1,"legacy week-long empty portrait entries are refreshed immediately");
            Check(requested!.Host=="ru.wikipedia.org"&&Uri.UnescapeDataString(requested.Query).Contains("|",StringComparison.Ordinal)&&!requested.Query.Contains("search",StringComparison.Ordinal),"portrait resolution only queries bounded exact identity variants");
        }
        var offline=new Handler((_,_)=>throw new HttpRequestException("fixture unavailable"));
        using(var client=new SourceClient(offline))
            Check((await new CinemaPeople(client).ResolvePortrait(person,CancellationToken.None)) is {Status:PortraitStatus.Available,FromCache:true}&&offline.Calls==0,"fresh confirmed identity thumbnails are available offline without requests");
        var stalePath=Directory.EnumerateFiles(people,"*.identity.json").Single(path=>File.ReadAllText(path).Contains(JsonSerializer.Serialize(person.Name),StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(stalePath,DateTime.UtcNow.AddDays(-8));
        using(var client=new SourceClient(offline))
            Check((await new CinemaPeople(client).ResolvePortrait(person,CancellationToken.None)) is {Status:PortraitStatus.Available,FromCache:true},"expired positive portrait identities survive source outages");

        var retryPerson=person with{Name="Повтор актёр "+Guid.NewGuid().ToString("N")};
        var retry=new Handler((_,_)=>Task.FromResult(Reply(Page(retryPerson.Name))));
        using(var client=new SourceClient(new Handler((_,_)=>throw new HttpRequestException("fixture unavailable"))))
            Check((await new CinemaPeople(client).ResolvePortrait(retryPerson,CancellationToken.None)).Status==PortraitStatus.Unavailable,"a transient portrait error is distinct from a confirmed absent image");
        using(var client=new SourceClient(retry))
            Check((await new CinemaPeople(client).ResolvePortrait(retryPerson,CancellationToken.None)).Status==PortraitStatus.Available&&retry.Calls==1,"transient portrait errors are not persisted as missing images");

        var sharedPerson=person with{Name="Общий актёр "+Guid.NewGuid().ToString("N")};
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler=new Handler(async(_,ct)=>{started.TrySetResult();await release.Task.WaitAsync(ct);return Reply(Page(sharedPerson.Name));});
        using(var client=new SourceClient(handler))
        {
            var resolver=new CinemaPeople(client);using var cancelled=new CancellationTokenSource();
            var first=resolver.ResolvePortrait(sharedPerson,cancelled.Token);await started.Task;
            var second=resolver.ResolvePortrait(sharedPerson,CancellationToken.None);cancelled.Cancel();
            try{await first;throw new Exception("A cancelled portrait caller did not stop waiting.");}catch(OperationCanceledException){}
            release.SetResult();var result=await second;
            Check(result.Status==PortraitStatus.Available&&handler.Calls==1,"rerendered portraits share in-flight identity work even when the old visual is cancelled");
        }

        var fallbackPerson=person with{Name="Фото резерв "+Guid.NewGuid().ToString("N")};
        var exactSitelink=false;
        var fallback=new Handler((uri,_)=>
        {
            if(uri.Host=="www.wikidata.org")exactSitelink=uri.Query.Contains("sites=ruwiki",StringComparison.Ordinal)&&Uri.UnescapeDataString(uri.Query).Contains("titles="+fallbackPerson.Name,StringComparison.Ordinal);
            return Task.FromResult(Reply(uri.Host switch
            {
                "ru.wikipedia.org"=>Page(fallbackPerson.Name,null),
                "www.wikidata.org"=>Encoding.UTF8.GetBytes("""{"entities":{"Q123":{"claims":{"P18":[{"rank":"normal","mainsnak":{"datavalue":{"value":"Person portrait.jpg"}}}]}}}}"""),
                "commons.wikimedia.org"=>Encoding.UTF8.GetBytes("""{"query":{"pages":{"2":{"imageinfo":[{"url":"https://upload.wikimedia.org/wikipedia/commons/person.jpg"}]}}}}"""),
                _=>throw new Exception("Unexpected portrait identity host.")
            }));
        });
        using(var client=new SourceClient(fallback))
            Check((await new CinemaPeople(client).ResolvePortrait(fallbackPerson,CancellationToken.None)).Url==Photo&&fallback.Calls==3&&exactSitelink,"missing article thumbnails and pageprops use the exact canonical Wikidata sitelink and original Wikimedia portrait");
    }
}
