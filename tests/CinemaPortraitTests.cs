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
        const string cdnPhoto="https://thumb.wikimedia.org/wikipedia/commons/thumb/person.jpg/500px-person.jpg?utm_source=api";
        Check(CinemaPeople.ParseIdentities(Page("Холланд, Том (актёр)",cdnPhoto),tom).Single().PhotoUrl==cdnPhoto&&
              CinemaPeople.PhotoUrl("https://thumb.wikimedia.org.evil.test/person.jpg")==null&&
              CinemaPeople.PhotoUrl("http://thumb.wikimedia.org/person.jpg")==null&&
              CinemaPeople.PhotoUrl("https://user@thumb.wikimedia.org/person.jpg")==null,
              "modern Wikimedia thumbnail CDN is accepted while lookalike, HTTP and credential URLs are rejected");
        const string professionalPhoto="https://kino-teatr.ua/public/main/persons/x2_photo_59e70f3e0fb79.jpg";
        Check(CinemaPeople.PhotoUrl(professionalPhoto)==professionalPhoto&&
              CinemaPeople.PhotoUrl("https://kino-teatr.ua.evil.test/public/main/persons/person.jpg")==null&&
              CinemaPeople.PhotoUrl("https://kino-teatr.ua/public/main/posters/person.jpg")==null&&
              CinemaPeople.PhotoUrl("https://kino-teatr.ua/public/main/persons/person.svg")==null&&
              CinemaPeople.PhotoUrl("https://user@kino-teatr.ua/public/main/persons/person.jpg")==null&&
              CinemaPeople.PhotoUrl("https://kino-teatr.ua:8080/public/main/persons/person.jpg")==null,
              "professional portraits accept only the confirmed KinoUA image host and people-image paths");
        const string zonaPhoto="https://img4.zonapic.com/images/actor/972/972478.jpg";
        Check(CinemaPeople.PhotoUrl(zonaPhoto)==zonaPhoto&&
              Enumerable.Range(1,4).All(host=>CinemaPeople.ZonaPhotoUrl($"https://img{host}.zonapic.com/images/actor/972/972478.webp")!=null)&&
              new[]{"https://img4.zonapic.com/images/film_240/972/972478.jpg","https://img5.zonapic.com/images/actor/972/972478.jpg",
                  "https://img4.zonapic.com.evil.test/images/actor/972/972478.jpg","http://img4.zonapic.com/images/actor/972/972478.jpg",
                  "https://user@img4.zonapic.com/images/actor/972/972478.jpg","https://img4.zonapic.com:8080/images/actor/972/972478.jpg",
                  zonaPhoto+"?redirect=other",zonaPhoto+"#other","https://img4.zonapic.com/images/actor/972/972478.svg",
                  "https://img4.zonapic.com/images/actor/972/%39%37%32%34%37%38.jpg"}.All(url=>CinemaPeople.PhotoUrl(url)==null),
              "Zona portraits require the four confirmed image hosts and exact numeric actor-image paths without credentials, redirects or film posters");
        const string professionalProfile="https://kino-teatr.ua/person/Waugh-Scott-6293.phtml";
        var professionalPerson=new CinemaPerson("Скотт Во","Режиссёры","",professionalProfile);
        var professionalHtml=Encoding.UTF8.GetBytes("<h1>Скотт Во</h1><span itemprop='alternateName'>Scott Waugh</span><img alt='Персона Скотт Во' src='/public/main/persons/x2_photo_59e70f3e0fb79.jpg'><div itemprop='description'>Американский кинорежиссёр, актёр и каскадёр. Родился в семье кинематографистов.</div>");
        var professionalHandler=new Handler((uri,_)=>Task.FromResult(Reply(uri.Host=="kino-teatr.ua"?professionalHtml:throw new Exception("A confirmed professional portrait must not query the fallback source."))));
        using(var client=new SourceClient(professionalHandler))
        {
            var resolver=new CinemaPeople(client);var first=await resolver.ResolvePortrait(professionalPerson,CancellationToken.None);
            var cached=await resolver.ResolvePortrait(professionalPerson,CancellationToken.None);
            Check(first is {Status:PortraitStatus.Available,Url:professionalPhoto,SourceUrl:professionalProfile,FromCache:false}&&
                  cached is {Status:PortraitStatus.Available,Url:professionalPhoto,FromCache:true}&&professionalHandler.Calls==1,
                  "Scott Waugh uses the exact professional credit name and portrait, with cached reuse and no speculative Wikipedia alias");
        }
        var professionalOffline=new Handler((_,_)=>throw new HttpRequestException("fixture unavailable"));
        using(var client=new SourceClient(professionalOffline))
            Check((await new CinemaPeople(client).ResolvePortrait(professionalPerson,CancellationToken.None)) is {Status:PortraitStatus.Available,Url:professionalPhoto,FromCache:true}&&professionalOffline.Calls==0,
                  "a verified professional portrait remains available offline");
        var directPerson=professionalPerson with{PhotoUrl=zonaPhoto,SourcePersonId="972478"};
        var directOrigin=new MediaItem(-34,"Курьер","Фильмы","",2026,"—","—","#526B69"){
            PageUrl="https://w6.zona.plus/movies/kurer-2026",People=[directPerson]
        };
        var directFallback=JsonSerializer.Deserialize<ProfessionalPerson>(
            await CacheFiles.ReadAllTextAsync(ProfessionalCinemaPeople.CachePath(professionalPerson,null)))!
            with{Person=directPerson with{ProfileUrl=professionalProfile}};
        foreach(var fallbackOrigin in new MediaItem?[]{null,directOrigin})
            await CacheFiles.WriteAllTextAsync(ProfessionalCinemaPeople.CachePath(directPerson,fallbackOrigin),JsonSerializer.Serialize(directFallback));
        var directHandler=new Handler((_,_)=>throw new Exception("A film-scoped Zona portrait must not perform a name lookup."));
        using(var client=new SourceClient(directHandler))
        {
            var resolver=new CinemaPeople(client);
            Check((await resolver.ResolvePortrait(directPerson,CancellationToken.None,directOrigin)) is {Status:PortraitStatus.Available,Url:zonaPhoto,SourceUrl:"https://w6.zona.plus/movies/kurer-2026"}&&directHandler.Calls==0,
                  "a film's exact credited person uses the supplied Zona portrait immediately without profile requests");
            var restored=JsonSerializer.Deserialize<MediaItem>(JsonSerializer.Serialize(directOrigin))!;
            Check((await resolver.ResolvePortrait(restored.People.Single(),CancellationToken.None,restored)).Url==zonaPhoto&&restored.People[0].SourcePersonId=="972478",
                  "cached movie detail preserves the source-bound portrait and person identity");
            var badOrigins=new MediaItem?[]{null,directOrigin with{PageUrl="https://w6.zona.plus.evil.test/movies/kurer-2026"},
                directOrigin with{PageUrl="/movies/kurer-2026"},directOrigin with{People=[]},
                directOrigin with{People=[directPerson with{Name="Другой участник"}]},
                directOrigin with{People=[directPerson with{Role="Актёры"}]},
                directOrigin with{People=[directPerson with{PhotoUrl="https://img4.zonapic.com/images/actor/1/1001.jpg"}]},
                directOrigin with{People=[directPerson with{SourcePersonId="1001"}]}};
            var badResults=await Task.WhenAll(badOrigins.Select(origin=>resolver.ResolvePortrait(directPerson,CancellationToken.None,origin)));
            Check(badResults.All(result=>result is {Status:PortraitStatus.Available,Url:professionalPhoto}),
                  "an unscoped, spoofed or mismatched movie credit cannot supply a direct portrait and falls back to the verified professional profile");
            var foreignDirect=directPerson with{PhotoUrl=Photo};
            Check((await resolver.ResolvePortrait(foreignDirect,CancellationToken.None,directOrigin with{People=[foreignDirect]})).Url==professionalPhoto,
                  "a portrait from a different provider cannot masquerade as film-scoped Zona metadata");
            using var cancelledDirect=new CancellationTokenSource();cancelledDirect.Cancel();
            try{await resolver.ResolvePortrait(directPerson,cancelledDirect.Token,directOrigin);throw new Exception("A cancelled direct portrait caller did not stop.");}
            catch(OperationCanceledException){}
            Check(directHandler.Calls==0,"cancelled direct portraits stop before work while validated cached fallback stays offline");
        }
        var directBytes=Encoding.UTF8.GetBytes("verified Zona actor portrait bytes");
        var directPath=Path.Combine(Preferences.DataDir,"portraits",Key(zonaPhoto)+".img");var imageRequests=0;
        byte[] DecodeDirect(byte[] bytes)=>bytes.AsSpan().SequenceEqual(directBytes)?bytes:throw new InvalidDataException("Corrupted portrait fixture.");
        var firstImage=await CoverCache.Load(directPath,2*1024*1024,_=>{imageRequests++;return Task.FromResult(directBytes);},DecodeDirect,CancellationToken.None);
        File.SetLastWriteTimeUtc(directPath,DateTime.UtcNow.AddDays(-8));
        var cachedImage=await CoverCache.Load(directPath,2*1024*1024,_=>throw new HttpRequestException("offline portrait source"),DecodeDirect,CancellationToken.None);
        Check(firstImage.Downloaded&&!cachedImage.Downloaded&&imageRequests==1&&cachedImage.Image.AsSpan().SequenceEqual(directBytes),
              "validated Zona portrait bytes reuse the normal image cache offline, including an older readable image");
        await File.WriteAllTextAsync(directPath,"damaged portrait");
        var repairedImage=await CoverCache.Load(directPath,2*1024*1024,_=>{imageRequests++;return Task.FromResult(directBytes);},DecodeDirect,CancellationToken.None);
        Check(repairedImage.Downloaded&&imageRequests==2&&repairedImage.Image.AsSpan().SequenceEqual(directBytes),
              "a damaged cached Zona photograph is replaced once rather than retained as an empty tile");
        var unavailablePrimary=professionalPerson with{Name="Проф источник "+Guid.NewGuid().ToString("N"),ProfileUrl="https://kino-teatr.ua/person/Fixture-Actor-700001.phtml",Role="Актёры"};
        var unavailableHandler=new Handler((uri,_)=>uri.Host=="kino-teatr.ua"?throw new HttpRequestException("temporary professional source outage"):Task.FromResult(Reply(Encoding.UTF8.GetBytes("""{"query":{"pages":{"-1":{"missing":""}}}}"""))));
        using(var client=new SourceClient(unavailableHandler))
            Check((await new CinemaPeople(client).ResolvePortrait(unavailablePrimary,CancellationToken.None)).Status==PortraitStatus.Unavailable,
                  "a temporary professional source outage can retry even when the fallback has no matching identity");
        var ivan=new CinemaPerson("Иван Янковский","Актёры","");
        var redirect=Encoding.UTF8.GetBytes("""{"query":{"redirects":[{"from":"Иван Янковский","to":"Янковский, Иван Филиппович"}],"pages":{"1":{"title":"Янковский, Иван Филиппович","extract":"Российский актёр","thumbnail":{"source":"https://upload.wikimedia.org/wikipedia/commons/person.jpg"}}}}}""");
        Check(CinemaPeople.ParseIdentities(redirect,ivan).Single().ResolvedName==ivan.Name,"an explicit Wikipedia alias safely resolves a canonical name containing a patronymic");
        Check(CinemaPeople.ParseIdentities(redirect,ivan with{Name="Олег Янковский"}).Length==0,"another namesake's redirect cannot identify the requested actor");
        var aliasHandler=new Handler((_,_)=>Task.FromResult(Reply(redirect)));
        using(var client=new SourceClient(aliasHandler))
            Check((await new CinemaPeople(client).ResolveWikiPortrait(ivan,CancellationToken.None)).Status==PortraitStatus.Available,"trusted redirect evidence supplies the actor portrait");
        using(var client=new SourceClient(new Handler((_,_)=>throw new HttpRequestException("fixture unavailable"))))
            Check((await new CinemaPeople(client).ResolveWikiPortrait(ivan,CancellationToken.None)) is {Status:PortraitStatus.Available,FromCache:true},"canonical patronymic identity retains its accepted alias in the offline cache");

        var person=new CinemaPerson("Проверочный актёр "+Guid.NewGuid().ToString("N"),"Актёры","");
        var people=Path.Combine(Preferences.DataDir,"people");Directory.CreateDirectory(people);
        await File.WriteAllTextAsync(Path.Combine(people,Key(person.Name)+".portrait.json"),"null");
        Uri? requested=null;
        var success=new Handler((uri,_)=>{requested=uri;return Task.FromResult(Reply(Page(person.Name)));});
        using(var client=new SourceClient(success))
        {
            var result=await new CinemaPeople(client).ResolveWikiPortrait(person,CancellationToken.None);
            Check(result is {Status:PortraitStatus.Available,Url:Photo}&&success.Calls==1,"legacy week-long empty portrait entries are refreshed immediately");
            Check(requested!.Host=="ru.wikipedia.org"&&Uri.UnescapeDataString(requested.Query).Contains("|",StringComparison.Ordinal)&&!requested.Query.Contains("search",StringComparison.Ordinal),"portrait resolution only queries bounded exact identity variants");
        }
        var offline=new Handler((_,_)=>throw new HttpRequestException("fixture unavailable"));
        using(var client=new SourceClient(offline))
            Check((await new CinemaPeople(client).ResolveWikiPortrait(person,CancellationToken.None)) is {Status:PortraitStatus.Available,FromCache:true}&&offline.Calls==0,"fresh confirmed identity thumbnails are available offline without requests");
        var stalePath=Directory.EnumerateFiles(people,"*.identity.json").Single(path=>File.ReadAllText(path).Contains(JsonSerializer.Serialize(person.Name),StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(stalePath,DateTime.UtcNow.AddDays(-8));
        using(var client=new SourceClient(offline))
            Check((await new CinemaPeople(client).ResolveWikiPortrait(person,CancellationToken.None)) is {Status:PortraitStatus.Available,FromCache:true},"expired positive portrait identities survive source outages");

        var retryPerson=person with{Name="Повтор актёр "+Guid.NewGuid().ToString("N")};
        var retry=new Handler((_,_)=>Task.FromResult(Reply(Page(retryPerson.Name))));
        using(var client=new SourceClient(new Handler((_,_)=>throw new HttpRequestException("fixture unavailable"))))
            Check((await new CinemaPeople(client).ResolveWikiPortrait(retryPerson,CancellationToken.None)).Status==PortraitStatus.Unavailable,"a transient portrait error is distinct from a confirmed absent image");
        using(var client=new SourceClient(retry))
            Check((await new CinemaPeople(client).ResolveWikiPortrait(retryPerson,CancellationToken.None)).Status==PortraitStatus.Available&&retry.Calls==1,"transient portrait errors are not persisted as missing images");
        var missingPerson=person with{Name="Нет профиля "+Guid.NewGuid().ToString("N")};
        var missingHandler=new Handler((_,_)=>Task.FromResult(Reply(Encoding.UTF8.GetBytes("""{"query":{"pages":{"-1":{"title":"Missing","missing":""}}}}"""))));
        using(var client=new SourceClient(missingHandler))
        {
            var resolver=new CinemaPeople(client);
            Check((await resolver.ResolveWikiPortrait(missingPerson,CancellationToken.None)).Status==PortraitStatus.Missing&&
                  (await resolver.ResolveWikiPortrait(missingPerson,CancellationToken.None)) is {Status:PortraitStatus.Missing,FromCache:true}&&missingHandler.Calls==1,
                  "a confirmed absent identity is cached briefly to avoid repeated requests while a crew card rerenders");
        }
        var refreshMissing=new Handler((_,_)=>Task.FromResult(Reply(Page(missingPerson.Name))));
        using(var client=new SourceClient(refreshMissing))
            Check((await new CinemaPeople(client).ResolveWikiPortrait(missingPerson,CancellationToken.None,forceRefresh:true)).Status==PortraitStatus.Available&&refreshMissing.Calls==1,
                  "an explicit portrait retry bypasses the short confirmed-missing cache");
        var noPhotoPerson=person with{Name="Нет портрета "+Guid.NewGuid().ToString("N")};
        var noPhotoHandler=new Handler((uri,_)=>Task.FromResult(Reply(uri.Host=="ru.wikipedia.org"?Page(noPhotoPerson.Name,null):Encoding.UTF8.GetBytes("""{"entities":{"Q123":{"claims":{}}}}"""))));
        using(var client=new SourceClient(noPhotoHandler))
            Check((await new CinemaPeople(client).ResolveWikiPortrait(noPhotoPerson,CancellationToken.None)).Status==PortraitStatus.Missing,"a confirmed person without an article or Wikidata photograph settles into the missing state");
        var noPhotoPath=Directory.EnumerateFiles(people,"*.identity.json").Single(path=>File.ReadAllText(path).Contains(JsonSerializer.Serialize(noPhotoPerson.Name),StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(noPhotoPath,DateTime.UtcNow.AddHours(-2));
        var recoveredPhoto=new Handler((_,_)=>Task.FromResult(Reply(Page(noPhotoPerson.Name))));
        using(var client=new SourceClient(recoveredPhoto))
            Check((await new CinemaPeople(client).ResolveWikiPortrait(noPhotoPerson,CancellationToken.None)).Status==PortraitStatus.Available&&recoveredPhoto.Calls==1,"an absent photograph is rechecked within an hour rather than remain hidden for a week");
        var ratePerson=person with{Name="Лимит актёр "+Guid.NewGuid().ToString("N")};
        var rateCalls=0;
        var limited=new Handler((_,_)=>Task.FromResult(Interlocked.Increment(ref rateCalls)==1?new HttpResponseMessage(HttpStatusCode.TooManyRequests):Reply(Page(ratePerson.Name))));
        using(var client=new SourceClient(limited))
            Check((await new CinemaPeople(client).ResolveWikiPortrait(ratePerson,CancellationToken.None)).Status==PortraitStatus.Available&&limited.Calls==2,"temporary Wikipedia rate limits receive one delayed retry without producing an empty portrait cache");

        var sharedPerson=person with{Name="Общий актёр "+Guid.NewGuid().ToString("N")};
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler=new Handler(async(_,ct)=>{started.TrySetResult();await release.Task.WaitAsync(ct);return Reply(Page(sharedPerson.Name));});
        using(var client=new SourceClient(handler))
        {
            var resolver=new CinemaPeople(client);using var cancelled=new CancellationTokenSource();
            var first=resolver.ResolveWikiPortrait(sharedPerson,cancelled.Token);await started.Task;
            var second=resolver.ResolveWikiPortrait(sharedPerson,CancellationToken.None);cancelled.Cancel();
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
            Check((await new CinemaPeople(client).ResolveWikiPortrait(fallbackPerson,CancellationToken.None)).Url==Photo&&fallback.Calls==3&&exactSitelink,"missing article thumbnails and pageprops use the exact canonical Wikidata sitelink and original Wikimedia portrait");
    }
}
