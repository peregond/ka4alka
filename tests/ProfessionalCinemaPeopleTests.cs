using System.Net;
using System.Text;
using System.Text.Json;
using Kachalka;

public static class ProfessionalCinemaPeopleTests
{
    const string Source="https://kino-teatr.ua/person/fixture-person-91001.phtml";
    const string Image="https://kino-teatr.ua/public/main/persons/x2_photo_fixture.jpg";
    const string Form="<form method='GET' action='/ru/main/search.phtml'><input></form><form method='GET' action='/ru/main/persons/order_by/fio.asc.phtml'><input name='lastname'></form>";
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS: "+name);}
    static byte[] Bytes(string value)=>Encoding.UTF8.GetBytes(value);
    static byte[] Profile(string name,string source=Source,string? photo=Image,string role="Режиссер",string? alias=null,string description="Тестовая краткая биография профессионального участника кино. Проверенные сведения о карьере.")=>Bytes("<h1>"+name+"</h1><script type='application/ld+json'>"+JsonSerializer.Serialize(new Dictionary<string,object?>
    {
        ["@context"]="https://schema.org",["@graph"]=new object[]{new Dictionary<string,object>{{"@type","WebSite"},{"name","Kino-Teatr.ua"}},new Dictionary<string,object?>{{"@type","Person"},{"name",name},{"alternateName",alias},{"url",source},{"image",photo},{"jobTitle",role},{"description",description}}}
    })+"</script><a href='/person_films/fixture-person-"+source.Split('-').Last()+"'>Фильмография</a>");
    static HttpResponseMessage Reply(byte[] bytes)=>new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
    sealed class Handler(Func<Uri,CancellationToken,Task<HttpResponseMessage>> response):HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Interlocked.Increment(ref Calls);return response(request.RequestUri!,ct);}
    }
    static string Row(string title,string original,int year,int id)=>"<div class='uk-margin-small-top'><a href='/film/fixture-"+id+".phtml'>"+title+"</a><br><span class='uk-text-muted uk-text-small'>"+original+" ("+year+")</span></div>";
    static byte[] Filmography()=>Bytes("<h1>Фильмография Скотт Во</h1><h2>Режиссер</h2>"+Row("Перевозчик","Runner",2026,92001)+Row("Жажда скорости","Need for Speed",2014,92002)+"<h2>Продюсер</h2>"+Row("Другая работа","Other Work",2018,92003)+"<h2>Актер</h2>"+Row("Актёрская работа","Acting Work",2001,92004)+"<h3>Новинки кино</h3>"+Row("Посторонний фильм","Unrelated Film",2026,92005));

    public static async Task Run()
    {
        var scott=new CinemaPerson("Скотт Во","Режиссёры","");
        var parsed=ProfessionalCinemaPeople.Parse(Profile(scott.Name,alias:"Scott Waugh"),scott,Source);
        Check(parsed is {PhotoUrl:Image,SourceUrl:Source,SourceName:"Kino-Teatr.ua"}&&parsed.Aliases.Contains("Scott Waugh"),"professional JSON-LD supplies an exact name, official English alias, photograph and credited source");
        Check(ProfessionalCinemaPeople.Parse(Profile(scott.Name,alias:"Scott Waugh"),scott with{Name="Scott Waugh"},Source)!=null,"an official professional alias identifies the same person without transliteration guesses");
        Check(ProfessionalCinemaPeople.Parse(Profile(scott.Name,alias:"Scott Waugh"),scott with{OriginalName="Ric Roman Waugh"},Source)==null&&ProfessionalCinemaPeople.Parse(Profile(scott.Name,alias:"Scott Waugh"),scott with{OriginalName="Scott Waugh"},Source)!=null,"a declared Zona original name must agree with the professional source's English identity alias");
        Check(ProfessionalCinemaPeople.CachePath(scott with{SourcePersonId="123"},null)!=ProfessionalCinemaPeople.CachePath(scott with{SourcePersonId="456"},null)&&ProfessionalCinemaPeople.CachePath(scott,null)!=ProfessionalCinemaPeople.CachePath(scott with{OriginalName="Scott Waugh"},null),"professional profile cache keys separate exact source IDs and declared original names");
        Check(ProfessionalCinemaPeople.Parse(Profile("Рик Роман Во"),scott,Source)==null&&ProfessionalCinemaPeople.Parse(Profile(scott.Name,role:"Актер"),scott,Source)==null,"professional identities reject another name and an unrelated profession");
        Check(ProfessionalCinemaPeople.Parse(Profile(scott.Name,role:"Оператор"),scott with{Role="Операторы"},Source)!=null,"the professional cinema source's plain operator job title identifies a cinematographer");
        Check(ProfessionalCinemaPeople.Parse(Profile(scott.Name,role:"Director of photography"),scott,Source)==null&&ProfessionalCinemaPeople.Parse(Profile(scott.Name,role:"Director of photography"),scott with{Role="Операторы"},Source)!=null,"a director of photography belongs to cinematographers rather than film directors");
        Check(ProfessionalCinemaPeople.Parse(Profile(scott.Name,role:"Актриса",description:"Известная актриса, жена режиссера кино."),scott,Source)==null,"an explicit professional job title takes precedence over another person's profession mentioned in the biography");
        var wrongHeading=Bytes("<h1>Скотт Во</h1>"+Encoding.UTF8.GetString(Profile("Рик Роман Во")));
        Check(ProfessionalCinemaPeople.Parse(wrongHeading,scott,Source)==null,"a page heading cannot override the professional source's structured identity");
        Check(ProfessionalCinemaPeople.Parse(Profile(scott.Name,photo:"https://kino-teatr.ua.evil.test/public/main/persons/person.jpg"),scott,Source)?.PhotoUrl==null,"a professional profile cannot supply an untrusted image host");
        Check(ProfessionalCinemaPeople.ProfileUrl("https://user@kino-teatr.ua/person/a-1.phtml")==null&&ProfessionalCinemaPeople.ProfileUrl("https://kino-teatr.ua:8080/person/a-1.phtml")==null&&ProfessionalCinemaPeople.ProfileUrl("https://kino-teatr.ua/person/a-1.phtml?redirect=other")==null,"professional source identities reject credentials, ports and query redirects");
        Check(ProfessionalCinemaPeople.PhotoUrl("https://kino-teatr.ua/public/main/posters/person.jpg")==null&&ProfessionalCinemaPeople.PhotoUrl("https://kino-teatr.ua/public/main/persons/person.svg")==null,"professional portrait validation restricts both image type and people-image directory");
        var primitive=Bytes("<h1>Скотт Во</h1><script type='application/ld+json'>null</script><div itemprop='description'>Американский режиссёр кино, автор известных фильмов.</div>");
        Check(ProfessionalCinemaPeople.Parse(primitive,scott,Source)!=null,"unrelated primitive structured data does not break an otherwise verified profile");
        var directorWorks=ProfessionalCinemaPeople.Works(Filmography(),"Режиссёры");
        Check(directorWorks.Length==2&&directorWorks.Any(x=>x.Title=="Перевозчик"&&x.OriginalTitle=="Runner"&&x.Year==2026),"professional filmography preserves original title and year for catalog translation matching");
        Check(ProfessionalCinemaPeople.Works(Filmography(),"Актёры") is [var actorWork]&&actorWork.Title=="Актёрская работа","professional filmography retains only the requested role and excludes footer recommendations");
        Check(ProfessionalCinemaPeople.Works(Bytes("<h1>Скотт Во</h1><h2>Режиссер</h2>"+Row("Посторонний фильм","Unrelated",2026,92006))).Length==0,"a person's profile recommendations are never accepted as their filmography");

        var person=new CinemaPerson("Проверка Профессиональная "+Guid.NewGuid().ToString("N"),"Режиссёры","",Source);
        var held=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);var entered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler=new Handler(async(uri,ct)=>{if(uri.AbsolutePath.Contains("person_films",StringComparison.Ordinal))throw new Exception("A portrait must not wait for filmography.");entered.TrySetResult(true);await held.Task.WaitAsync(ct);return Reply(Profile(person.Name));});
        using(var client=new SourceClient(handler))
        {
            var provider=new ProfessionalCinemaPeople(client);using var caller=new CancellationTokenSource();var first=provider.Load(person,null,caller.Token);await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var second=provider.Load(person,null,CancellationToken.None);caller.Cancel();
            try{await first;throw new Exception("Canceled professional subscriber completed");}catch(OperationCanceledException){}
            held.TrySetResult(true);var result=await second.WaitAsync(TimeSpan.FromSeconds(3));
            Check(result?.PhotoUrl==Image&&handler.Calls==1,"concurrent biography and portrait requests share one lookup while a canceled subscriber leaves the other lookup active");
            Check((await provider.Load(person,null,CancellationToken.None)) is {FromCache:true,PhotoUrl:Image}&&handler.Calls==1,"a verified professional profile is reused from the managed cache");
            Check((await provider.Load(person,null,CancellationToken.None,true)) is {FromCache:false,PhotoUrl:Image}&&handler.Calls==2,"manual profile refresh bypasses the fresh professional cache");
        }
        var offline=new Handler((_,_)=>throw new HttpRequestException("offline"));
        using(var client=new SourceClient(offline))
            Check((await new ProfessionalCinemaPeople(client).Load(person,null,CancellationToken.None)) is {FromCache:true,PhotoUrl:Image}&&offline.Calls==0,"verified professional biography and portrait remain available without network access");
        File.SetLastWriteTimeUtc(ProfessionalCinemaPeople.CachePath(person,null),DateTime.UtcNow.AddDays(-8));
        using(var client=new SourceClient(offline))
            Check((await new ProfessionalCinemaPeople(client).Load(person,null,CancellationToken.None)) is {FromCache:true,PhotoUrl:Image},"an expired positive professional profile survives a temporary source outage");

        var retry=person with{Name="Повтор Источника "+Guid.NewGuid().ToString("N")};var attempts=0;
        var recovering=new Handler((_,_)=>++attempts==1?throw new HttpRequestException("temporarily unavailable"):Task.FromResult(Reply(Profile(retry.Name))));
        using(var client=new SourceClient(recovering))
        {
            var provider=new ProfessionalCinemaPeople(client);
            try{await provider.Load(retry,null,CancellationToken.None);throw new Exception("Transient professional error was hidden");}catch(HttpRequestException){}
            Check((await provider.Load(retry,null,CancellationToken.None))?.PhotoUrl==Image&&attempts==2,"temporary source errors do not become cached missing identities");
        }
        var challengePerson=person with{Name="Проверка Ответа "+Guid.NewGuid().ToString("N")};var challengeAttempts=0;
        using(var client=new SourceClient(new Handler((_,_)=>Task.FromResult(Reply(++challengeAttempts==1?Bytes("<html>Checking your browser</html>"):Profile(challengePerson.Name))))))
        {
            var provider=new ProfessionalCinemaPeople(client);
            try{await provider.Load(challengePerson,null,CancellationToken.None);throw new Exception("Challenge response was hidden");}catch(InvalidDataException){}
            Check((await provider.Load(challengePerson,null,CancellationToken.None))?.PhotoUrl==Image,"an HTTP 200 challenge page is retryable instead of a confirmed missing actor");
        }
        var absent=person with{Name="Проверка Отсутствия "+Guid.NewGuid().ToString("N")};var absenceResolved=false;var absenceHandler=new Handler((_,_)=>Task.FromResult(Reply(Profile(absenceResolved?absent.Name:"Другой человек"))));
        using(var client=new SourceClient(absenceHandler))
        {
            var provider=new ProfessionalCinemaPeople(client);
            Check(await provider.Load(absent,null,CancellationToken.None)==null&&await provider.Load(absent,null,CancellationToken.None)==null&&absenceHandler.Calls==1,"a confirmed name mismatch is briefly cached to avoid repeated actor requests");
            absenceResolved=true;
            Check((await provider.Load(absent,null,CancellationToken.None,true))?.PhotoUrl==Image&&(await provider.Load(absent,null,CancellationToken.None)) is {PhotoUrl:Image,FromCache:true}&&absenceHandler.Calls==2,"a successful manual refresh clears the earlier missing identity cache");
        }
        var noPhoto=person with{Name="Проверка Фото "+Guid.NewGuid().ToString("N")};var imageAttempts=0;
        using(var client=new SourceClient(new Handler((_,_)=>Task.FromResult(Reply(Profile(noPhoto.Name,photo:++imageAttempts==1?null:Image))))))
        {
            var provider=new ProfessionalCinemaPeople(client);Check((await provider.Load(noPhoto,null,CancellationToken.None))?.PhotoUrl==null,"a verified profile can retain biography while the image is unavailable");
            File.SetLastWriteTimeUtc(ProfessionalCinemaPeople.CachePath(noPhoto,null),DateTime.UtcNow.AddHours(-2));
            Check((await provider.Load(noPhoto,null,CancellationToken.None))?.PhotoUrl==Image&&imageAttempts==2,"a profile without a photo refreshes after an hour rather than keeping an empty portrait for a week");
        }

        var filmsHandler=new Handler((_,_)=>Task.FromResult(Reply(Filmography())));
        using(var client=new SourceClient(filmsHandler))
        {
            var provider=new ProfessionalCinemaPeople(client);var director=parsed!;
            var actor=director with{Person=director.Person with{Role="Актёры"}};
            Check((await provider.Filmography(director,CancellationToken.None)).Length==2&&(await provider.Filmography(actor,CancellationToken.None)) is [var only]&&only.Title=="Актёрская работа"&&filmsHandler.Calls==2,"professional filmography caches remain separate for different roles of the same person");
            Check((await provider.Filmography(director,CancellationToken.None)).Length==2&&filmsHandler.Calls==2,"professional filmography is reused without another source request");
        }
        var filmsRetry=person with{Name="Проверка Фильмографии "+Guid.NewGuid().ToString("N"),ProfileUrl="https://kino-teatr.ua/person/fixture-person-91011.phtml"};
        using(var client=new SourceClient(new Handler((uri,_)=>uri.AbsolutePath.Contains("person_films")?throw new HttpRequestException("filmography temporarily unavailable"):Task.FromResult(Reply(Profile(filmsRetry.Name,source:filmsRetry.ProfileUrl!))))))
        {
            var provider=new ProfessionalCinemaPeople(client);var accepted=await provider.Load(filmsRetry,null,CancellationToken.None);
            try{await provider.Filmography(accepted!,CancellationToken.None);throw new Exception("Incomplete filmography was treated as complete");}catch(HttpRequestException){}
            Check((await provider.Load(filmsRetry,null,CancellationToken.None)) is {PhotoUrl:Image,FromCache:true},"a failed optional filmography request remains retryable and retains the already cached biography and portrait");
        }
        await SearchAndIdentity();
        await Pagination();
        await ConcurrentSearch();
    }

    static async Task SearchAndIdentity()
    {
        const string other="https://kino-teatr.ua/person/fixture-person-91002.phtml";
        var name="Полный Проверочный";var formCalls=0;string? searched=null;
        var handler=new Handler((uri,_)=>Task.FromResult(Reply(uri.AbsolutePath=="/persons.phtml"?FormResponse():uri.AbsolutePath.Contains("/main/persons/")?SearchResponse(uri):Profile("Проверочный Полный",source:uri.AbsoluteUri,alias:"Full Verified"))));
        byte[] FormResponse(){formCalls++;return Bytes(Form);}
        byte[] SearchResponse(Uri uri){searched=Uri.UnescapeDataString(uri.Query);return Bytes("<h1>Персоны в кино</h1><a href='"+Source+"'>Проверочный Полный Full Verified</a><a href='"+other+"'>Проверочный Другой Other Verified</a>");}
        using(var client=new SourceClient(handler))
        {
            var provider=new ProfessionalCinemaPeople(client);var result=await provider.SearchPeople(name,CancellationToken.None);
            Check(result is [var found]&&found.Role=="Режиссёры"&&found.ProfileUrl==Source&&found.PageUrl==""&&searched=="?lastname=Проверочный","global professional search verifies exact names and roles using the discovered public surname form");
            var english=await provider.SearchPeople("Full Verified",CancellationToken.None);
            Check(english is [var alias]&&alias.Name=="Full Verified"&&alias.ProfileUrl==Source&&formCalls==1,"professional search preserves a verified English alias and shares the public form discovery");
        }
        var ambiguous=new CinemaPerson("Однофамилец Проверочный "+Guid.NewGuid().ToString("N"),"Режиссёры","");
        var origin=new MediaItem(-91005,"Курьер","Фильмы","",2026,"—","—","#123456"){OriginalTitle="Runner"};
        var ambiguityHandler=new Handler((uri,_)=>Task.FromResult(Reply(uri.AbsolutePath=="/persons.phtml"?Bytes(Form):uri.AbsolutePath.Contains("/main/persons/")?Bytes("<h1>Персоны в кино</h1><a href='"+Source+"'>"+ambiguous.Name+"</a><a href='"+other+"'>"+ambiguous.Name+"</a>"):uri.AbsolutePath.Contains("person_films")?uri.AbsolutePath.Contains("91001")?Filmography():Bytes("<h1>Фильмография Однофамилец</h1><h2>Режиссер</h2>"+Row("Другой фильм","Other Film",2026,93001)):Profile(ambiguous.Name,source:uri.AbsoluteUri))));
        using(var client=new SourceClient(ambiguityHandler))
        {
            var provider=new ProfessionalCinemaPeople(client);
            Check(await provider.Load(ambiguous,null,CancellationToken.None)==null,"same-name professionals are not selected without an identifying credit");
            Check((await provider.Load(ambiguous,origin,CancellationToken.None))?.SourceUrl==Source,"a same-name professional is identified by the exact original film title and year despite a different translated catalog title");
        }
    }
    static async Task Pagination()
    {
        var surname="Страница"+Guid.NewGuid().ToString("N");var person=new CinemaPerson("Скотт "+surname,"Режиссёры","");
        const string profile="https://kino-teatr.ua/person/fixture-person-91021.phtml";var pages=0;
        var pager="https://kino-teatr.ua/ru/main/persons/lastname/"+Uri.EscapeDataString(surname)+"/page/";
        var handler=new Handler((uri,_)=>
        {
            if(uri.AbsolutePath=="/persons.phtml")return Task.FromResult(Reply(Bytes(Form)));
            if(uri.AbsolutePath.Contains("/main/persons/"))
            {
                pages++;
                return Task.FromResult(Reply(Bytes(uri.AbsolutePath.EndsWith("/2.phtml",StringComparison.Ordinal)?"<h1>Персоны в кино</h1><a href='"+profile+"'>"+person.Name+"</a>":"<h1>Персоны в кино</h1><a href='"+pager+"2.phtml'>2</a><a href='https://kino-teatr.ua/ru/main/persons/lastname/Другой/page/2.phtml'>Другой фильтр</a><a href='https://kino-teatr.ua.evil.test/ru/main/persons/lastname/"+surname+"/page/2.phtml'>Подмена</a><a href='https://kino-teatr.ua/ru/main/page/id/54.phtml'>Условия</a>")));
            }
            if(uri.AbsoluteUri==profile)return Task.FromResult(Reply(Profile(person.Name,source:profile)));
            throw new Exception("Untrusted pagination link followed: "+uri);
        });
        using(var client=new SourceClient(handler))
            Check((await new ProfessionalCinemaPeople(client).Load(person,null,CancellationToken.None)) is {SourceUrl:profile,PhotoUrl:Image}&&pages==2,"professional surname lookup follows the real page-two route while ignoring another filter, footer links and a lookalike host");
        var largeSurname="Длинная"+Guid.NewGuid().ToString("N");var large=new CinemaPerson("Проверка "+largeSurname,"Режиссёры","");var scans=0;var resolved=false;
        var manyPages=string.Join("",Enumerable.Range(2,7).Select(number=>"<a href='https://kino-teatr.ua/ru/main/persons/lastname/"+largeSurname+"/page/"+number+".phtml'>"+number+"</a>"));
        using(var client=new SourceClient(new Handler((uri,_)=>
        {
            if(uri.AbsolutePath=="/persons.phtml")return Task.FromResult(Reply(Bytes(Form)));
            if(uri.AbsolutePath.Contains("/main/persons/")){scans++;return Task.FromResult(Reply(Bytes("<h1>Персоны в кино</h1>"+(resolved?"<a href='"+profile+"'>"+large.Name+"</a>":manyPages))));}
            return Task.FromResult(Reply(Profile(large.Name,source:profile)));
        })))
        {
            var provider=new ProfessionalCinemaPeople(client);
            try{await provider.Load(large,null,CancellationToken.None);throw new Exception("Incomplete search became a confirmed missing actor");}catch(InvalidDataException){}
            Check(scans==3,"professional surname lookup is bounded to three pages rather than scanning an entire directory");
            resolved=true;
            Check((await provider.Load(large,null,CancellationToken.None))?.PhotoUrl==Image&&scans==4,"an incomplete bounded surname lookup remains retryable and never caches a false missing identity");
        }
    }
    static async Task ConcurrentSearch()
    {
        var surname="Параллельный"+Guid.NewGuid().ToString("N");var started=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);var reads=0;
        var list="<h1>Персоны в кино</h1>"+string.Join("",Enumerable.Range(1,3).Select(number=>"<a href='https://kino-teatr.ua/person/parallel-person-"+(91030+number)+".phtml'>Участник "+number+" "+surname+"</a>"));
        using var client=new SourceClient(new Handler(async(uri,ct)=>
        {
            if(uri.AbsolutePath=="/persons.phtml")return Reply(Bytes(Form));
            if(uri.AbsolutePath.Contains("/main/persons/"))return Reply(Bytes(list));
            if(Interlocked.Increment(ref reads)==3)started.TrySetResult(true);
            await release.Task.WaitAsync(ct);
            if(uri.AbsolutePath.EndsWith("91033.phtml",StringComparison.Ordinal))throw new HttpRequestException("one profile temporarily unavailable");
            return Reply(Profile("Участник "+(uri.AbsolutePath.EndsWith("91031.phtml",StringComparison.Ordinal)?1:2)+" "+surname,source:uri.AbsoluteUri));
        }));
        var search=new ProfessionalCinemaPeople(client).SearchPeople(surname,CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));release.TrySetResult(true);
        Check((await search.WaitAsync(TimeSpan.FromSeconds(3))).Length==2&&reads==3,"professional surname search reads three profiles concurrently and retains verified results when another profile is unavailable");
        using(var unavailable=new SourceClient(new Handler((uri,_)=>uri.AbsolutePath.Contains("/main/persons/")?Task.FromResult(Reply(Bytes(list))):throw new HttpRequestException("temporarily unavailable"))))
        {
            try{await new ProfessionalCinemaPeople(unavailable).SearchPeople(surname,CancellationToken.None);throw new Exception("Unavailable profiles became empty confirmed results");}catch(HttpRequestException){}
            Check(true,"a global search with unavailable profile pages remains retryable rather than becoming confirmed empty results");
        }
        var waiting=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using(var canceledClient=new SourceClient(new Handler(async(uri,ct)=>
        {
            if(uri.AbsolutePath.Contains("/main/persons/"))return Reply(Bytes(list));
            waiting.TrySetResult(true);await Task.Delay(Timeout.Infinite,ct);return Reply(Bytes(""));
        })))using(var canceled=new CancellationTokenSource())
        {
            var pending=new ProfessionalCinemaPeople(canceledClient).SearchPeople(surname,canceled.Token);await waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));canceled.Cancel();
            try{await pending.WaitAsync(TimeSpan.FromSeconds(3));throw new Exception("Canceled professional search returned stale results");}catch(OperationCanceledException){}
            Check(true,"changing the global search cancels unfinished professional profile reads and publishes no stale results");
        }
    }
}
