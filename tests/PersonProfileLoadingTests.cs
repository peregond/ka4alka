using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kachalka;

public static class PersonProfileLoadingTests
{
    sealed class ProfileHandler(CinemaPerson person,bool holdArticle=false):HttpMessageHandler
    {
        public readonly TaskCompletionSource<bool> ArticleStarted=new(TaskCreationOptions.RunContinuationsAsynchronously),SlowFilmStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> ReleaseArticle=new(TaskCreationOptions.RunContinuationsAsynchronously),ReleaseSlowFilm=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Requests;public bool ArticleCanceled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);var url=request.RequestUri!;string body;
            if(url.Host=="ru.wikipedia.org"&&url.Query.Contains("action=query"))
                body=JsonSerializer.Serialize(new{query=new{pages=new Dictionary<string,object>{["1"]=new{title=person.Name,extract="Российский актёр. Подробная биография участника, его образование, творческий путь и известные работы.",thumbnail=new{source="https://upload.wikimedia.org/wikipedia/commons/test-profile.jpg"}}}}});
            else if(url.Host=="ru.wikipedia.org"&&url.Query.Contains("action=parse"))
            {
                ArticleStarted.TrySetResult(true);
                if(holdArticle)try{await ReleaseArticle.Task.WaitAsync(ct);}catch(OperationCanceledException){ArticleCanceled=true;throw;}
                var table="<table class='wikitable'><tr><th>Год</th><th>Название фильма</th></tr><tr><td>2025</td><td><a href='/wiki/Быстрый_фильм'>Быстрый фильм</a></td></tr><tr><td>2024</td><td><a href='/wiki/Медленный_фильм'>Медленный фильм</a></td></tr></table>";
                body=JsonSerializer.Serialize(new{parse=new{text=new Dictionary<string,string>{["*"]=table}}});
            }
            else if(url.Host=="w6.zona.plus"&&url.AbsolutePath=="/search-form")
            {
                if(Uri.UnescapeDataString(url.Query).Contains("Медленный фильм")){SlowFilmStarted.TrySetResult(true);await ReleaseSlowFilm.Task.WaitAsync(ct);body="";}
                else body="<li class='results-item-wrap'><a itemprop='url' href='/movies/fast-profile-work'><span itemprop='name'>Быстрый фильм</span></a><span class='results-item-year'>2025</span></li>";
            }
            else if(url.Host=="w6.zona.plus"&&url.AbsolutePath=="/movies/fast-profile-work")
                body="<div itemprop='description'>Описание быстрой работы.</div><span itemprop='actor'><span itemprop='name'>"+person.Name+"</span></span>";
            else throw new InvalidOperationException("Unexpected profile fixture request: "+url);
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8)};
        }
    }
    sealed class Offline:HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Requests++;return Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"));}
    }
    sealed class ProfessionalHandler(CinemaPerson person,string alias,bool failFilmography=false):HttpMessageHandler
    {
        public readonly TaskCompletionSource<bool> FilmographyStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> ReleaseFilmography=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int WikiRequests,FilmographyRequests;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var url=request.RequestUri!;string body;
            if(url.AbsoluteUri==person.ProfileUrl)
            {
                var identity=JsonSerializer.Serialize(new Dictionary<string,object>{["@type"]="Person",["url"]=person.ProfileUrl!,["name"]=person.Name,["alternateName"]=alias,["jobTitle"]="Actor",["description"]="Актёр кино и телевидения. Профессиональная биография, образование и наиболее известные работы участника."});
                body="<script type='application/ld+json'>"+identity+"</script><h1>"+person.Name+"</h1><a href='/ru/person_films/profile-progress-904221.phtml'>Фильмография</a>";
            }
            else if(url.Host=="kino-teatr.ua"&&url.AbsolutePath=="/ru/person_films/profile-progress-904221.phtml")
            {
                Interlocked.Increment(ref FilmographyRequests);FilmographyStarted.TrySetResult(true);
                if(failFilmography)throw new HttpRequestException("filmography source is temporarily unavailable");
                await ReleaseFilmography.Task.WaitAsync(ct);body="<h1>Фильмография "+person.Name+"</h1>";
            }
            else
            {
                if(url.Host=="ru.wikipedia.org")Interlocked.Increment(ref WikiRequests);
                throw new InvalidOperationException("Unexpected professional fixture request: "+url);
            }
            return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8)};
        }
    }
    static MediaItem Film(CinemaPerson person,int id,string title)=>new(id,title,"Фильмы","",2020,"7.6","—","#526B69"){People=[person],PageUrl="https://w6.zona.plus/movies/profile-loading-"+Math.Abs(id)};
    static string Cache(CinemaPerson person)=>CinemaPeople.ProfileCachePath(person);
    public static async Task Run()
    {
        void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS: "+name);}
        var person=new CinemaPerson("Тест Профиля "+Guid.NewGuid().ToString("N"),"Актёры","");var origin=Film(person,-74021,"Известная работа");
        var handler=new ProfileHandler(person,holdArticle:true);using(var client=new SourceClient(handler))using(var cancel=new CancellationTokenSource())
        {
            var biography=new TaskCompletionSource<PersonProfile>(TaskCreationOptions.RunContinuationsAsynchronously);var reports=0;
            var loading=new LiveCatalog(client).Person(person,cancel.Token,origin,[],profile=>{Interlocked.Increment(ref reports);if(profile.Description.Length>0)biography.TrySetResult(profile);});
            var early=await biography.Task.WaitAsync(TimeSpan.FromSeconds(5));await handler.ArticleStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(early.Description.Length>50&&early.Filmography.Single().Id==origin.Id&&!loading.IsCompleted,"person biography and known film appear before the filmography article finishes loading");
            var path=Cache(person);
            Check(File.Exists(path)&&JsonSerializer.Deserialize<PersonProfile>(await File.ReadAllTextAsync(path))?.Description==early.Description,"resolved biography is cached before long filmography requests finish");
            cancel.Cancel();try{await loading;throw new Exception("Person cancellation was swallowed");}catch(OperationCanceledException){}
            var count=reports;await Task.Yield();Check(handler.ArticleCanceled&&reports==count,"leaving the person page cancels unfinished filmography without later progress callbacks");
        }
        var freshPerson=person with{Name="Фильмография Прогресс "+Guid.NewGuid().ToString("N")};var fastHandler=new ProfileHandler(freshPerson);
        using(var client=new SourceClient(fastHandler))using(var cancel=new CancellationTokenSource())
        {
            var works=new TaskCompletionSource<PersonProfile>(TaskCreationOptions.RunContinuationsAsynchronously);
            var loading=new LiveCatalog(client).Person(freshPerson,cancel.Token,Film(freshPerson,-74022,"Исходный фильм"),[],profile=>{if(profile.Filmography.Any(x=>x.Title=="Быстрый фильм"))works.TrySetResult(profile);});
            var early=await works.Task.WaitAsync(TimeSpan.FromSeconds(6));await fastHandler.SlowFilmStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Check(early.Filmography.Any(x=>x.Title=="Быстрый фильм")&&!loading.IsCompleted,"a verified filmography card appears while another film source is still waiting");
            cancel.Cancel();try{await loading;throw new Exception("Filmography cancellation was swallowed");}catch(OperationCanceledException){}
        }
        var cachedPerson=person with{Name="Кэш Профиля "+Guid.NewGuid().ToString("N")};var first=Film(cachedPerson,-74023,"Первый фильм");var second=Film(cachedPerson,-74024,"Второй фильм");
        var cache=Cache(cachedPerson);Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        var saved=new PersonProfile(cachedPerson,"Сохранённая биография доступна без сети.",[first,second],"https://ru.wikipedia.org/wiki/cached-profile");
        await File.WriteAllTextAsync(cache,JsonSerializer.Serialize(saved));var offline=new Offline();using(var client=new SourceClient(offline))
        {
            PersonProfile? report=null;var result=await new LiveCatalog(client).Person(cachedPerson,CancellationToken.None,first,[],profile=>report=profile);
            Check(offline.Requests==0&&report?.Description==saved.Description&&result.Filmography.Length==2,"a complete cached person biography and verified filmography publish immediately offline");
        }
        var partialPerson=person with{Name="Неполный Профиль "+Guid.NewGuid().ToString("N")};var partialFirst=Film(partialPerson,-74025,"Первая известная работа");var partialSecond=Film(partialPerson,-74026,"Вторая известная работа");
        const string primaryCacheBiography="Ранняя сохранённая биография профессионального источника.";
        await File.WriteAllTextAsync(Cache(partialPerson),JsonSerializer.Serialize(new{Person=partialPerson,Description=primaryCacheBiography,Filmography=new[]{partialFirst,partialSecond},SourceUrl="https://kino-teatr.ua/ru/person/profile-progress-904221.phtml",FilmographyComplete=false}));
        var partialHandler=new ProfileHandler(partialPerson,holdArticle:true);using(var client=new SourceClient(partialHandler))using(var cancel=new CancellationTokenSource())
        {
            PersonProfile? report=null;var loading=new LiveCatalog(client).Person(partialPerson,cancel.Token,partialFirst,[],profile=>report=profile);
            await partialHandler.ArticleStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(report?.Filmography.Length==2&&!loading.IsCompleted,"an early cached biography with two known films still refreshes unfinished filmography");
            Check(report?.Description==primaryCacheBiography&&report.SourceUrl?.StartsWith("https://kino-teatr.ua/",StringComparison.Ordinal)==true,"a primary-provider outage does not overwrite its useful cached biography with a Wikipedia fallback");
            cancel.Cancel();try{await loading;throw new Exception("Partial profile cancellation was swallowed");}catch(OperationCanceledException){}
        }
        var translatedPerson=person with{Name="English Identity "+Guid.NewGuid().ToString("N")};var translatedCredit=translatedPerson with{Name="Русское Имя "+Guid.NewGuid().ToString("N")};
        await File.WriteAllTextAsync(Cache(translatedPerson),JsonSerializer.Serialize(new{Person=translatedPerson,Description="Сохранённая биография профессионального источника.",Filmography=new[]{Film(translatedCredit,-74027,"Работа первая"),Film(translatedCredit,-74028,"Работа вторая")},SourceUrl="https://kino-teatr.ua/ru/person/profile-progress-904221.phtml",Aliases=new[]{translatedCredit.Name},FilmographyComplete=true}));
        var aliasesOffline=new Offline();using(var client=new SourceClient(aliasesOffline))
        {
            var result=await new LiveCatalog(client).Person(translatedPerson,CancellationToken.None);
            Check(aliasesOffline.Requests==0&&result.Filmography.Length==2,"verified translated credit aliases survive reopening a cached person profile offline");
        }
        var primaryPerson=person with{Name="Primary Identity "+Guid.NewGuid().ToString("N"),ProfileUrl="https://kino-teatr.ua/ru/person/profile-progress-904221.phtml"};var primaryCredit=primaryPerson with{Name="Имя В Титрах "+Guid.NewGuid().ToString("N")};
        var primaryOrigin=Film(primaryCredit,-74029,"Подтверждённая работа");var primaryHandler=new ProfessionalHandler(primaryPerson,primaryCredit.Name);
        using(var client=new SourceClient(primaryHandler))using(var cancel=new CancellationTokenSource())
        {
            var biography=new TaskCompletionSource<PersonProfile>(TaskCreationOptions.RunContinuationsAsynchronously);
            var loading=new LiveCatalog(client).Person(primaryPerson,cancel.Token,primaryOrigin,[],profile=>{if(profile.Description.Length>0)biography.TrySetResult(profile);});
            var early=await biography.Task.WaitAsync(TimeSpan.FromSeconds(5));await primaryHandler.FilmographyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(early.SourceUrl==primaryPerson.ProfileUrl&&early.Filmography.Single().Id==primaryOrigin.Id&&!loading.IsCompleted&&primaryHandler.WikiRequests==0,"primary professional biography and translated verified credits appear before optional filmography without Wikipedia requests");
            using(var json=JsonDocument.Parse(await File.ReadAllTextAsync(Cache(primaryPerson))))
                Check(!json.RootElement.GetProperty("FilmographyComplete").GetBoolean(),"primary biography cache remains resumable while its filmography is unfinished");
            cancel.Cancel();try{await loading;throw new Exception("Professional profile cancellation was swallowed");}catch(OperationCanceledException){}
        }
        var unavailablePerson=primaryPerson with{Name="Unavailable Filmography "+Guid.NewGuid().ToString("N")};var unavailableCredit=unavailablePerson with{Name="Русские Титры "+Guid.NewGuid().ToString("N")};
        var unavailableOrigin=Film(unavailableCredit,-74031,"Подтверждённый фильм один");var unavailableSecond=Film(unavailableCredit,-74032,"Подтверждённый фильм два");var unavailableHandler=new ProfessionalHandler(unavailablePerson,unavailableCredit.Name,failFilmography:true);
        using(var client=new SourceClient(unavailableHandler))
        {
            var result=await new LiveCatalog(client).Person(unavailablePerson,CancellationToken.None,unavailableOrigin,[unavailableSecond]).WaitAsync(TimeSpan.FromSeconds(5));
            using(var json=JsonDocument.Parse(await File.ReadAllTextAsync(Cache(unavailablePerson))))
                Check(result.Description.Length>50&&result.Filmography.Length==2&&!json.RootElement.GetProperty("FilmographyComplete").GetBoolean(),"a professional filmography outage retains biography and confirmed films with a resumable cache");
            await new LiveCatalog(client).Person(unavailablePerson,CancellationToken.None,unavailableOrigin,[unavailableSecond]).WaitAsync(TimeSpan.FromSeconds(5));
            Check(unavailableHandler.FilmographyRequests==2&&unavailableHandler.WikiRequests==0,"reopening an incomplete primary profile retries filmography without replacing the successful biography provider");
        }
        var translatedFilm=Film(person,-74030,"Курьер") with{Year=2026,OriginalTitle="Runner"};var translatedWork=new CinemaPeople.Work("Перевозчик",2026,"Runner");
        Check(CinemaPeople.MatchesWork(translatedFilm,translatedWork)&&!CinemaPeople.MatchesWork(translatedFilm,translatedWork with{Year=2025})&&!CinemaPeople.MatchesWork(translatedFilm,translatedWork with{OriginalTitle="Unrelated"})&&!CinemaPeople.MatchesWork(translatedFilm,translatedWork with{Year=0}),"filmography translation matches exact original title and year and rejects unrelated or undated works");
        Check(BundledCatalog.FindWork("Фильмы",translatedWork.Title,translatedWork.Year,translatedWork.OriginalTitle).Any(x=>x.Title=="Курьер"&&x.OriginalTitle=="Runner"&&x.Year==2026),"bundled real catalog resolves a professional filmography title through its exact original title and year");
        var namesake=person with{Name="Одноимённый Участник "+Guid.NewGuid().ToString("N"),ProfileUrl="https://kino-teatr.ua/person/first-namesake-904231.phtml"};var otherNamesake=namesake with{ProfileUrl="https://kino-teatr.ua/person/second-namesake-904232.phtml"};
        var firstProfile=new PersonProfile(namesake,"Биография первого профессионального участника.",[Film(namesake,-74041,"Работа первого один"),Film(namesake,-74042,"Работа первого два")],namesake.ProfileUrl);
        var secondProfile=new PersonProfile(otherNamesake,"Биография другого одноимённого профессионального участника.",[Film(otherNamesake,-74043,"Работа второго один"),Film(otherNamesake,-74044,"Работа второго два")],otherNamesake.ProfileUrl);
        await File.WriteAllTextAsync(Cache(namesake),JsonSerializer.Serialize(firstProfile));await File.WriteAllTextAsync(Cache(otherNamesake),JsonSerializer.Serialize(secondProfile));
        // A legacy name-only profile for the first person must not leak into an
        // explicit search result that identifies the second person's page.
        await File.WriteAllTextAsync(Cache(namesake with{ProfileUrl=null}),JsonSerializer.Serialize(firstProfile));
        var namesakesOffline=new Offline();using(var client=new SourceClient(namesakesOffline))
        {
            var firstResult=await new LiveCatalog(client).Person(namesake,CancellationToken.None,known:secondProfile.Filmography);var secondResult=await new LiveCatalog(client).Person(otherNamesake,CancellationToken.None,known:firstProfile.Filmography);
            Check(namesakesOffline.Requests==0&&firstResult.Description==firstProfile.Description&&secondResult.Description==secondProfile.Description&&firstResult.Filmography.All(x=>x.Id is -74041 or -74042)&&secondResult.Filmography.All(x=>x.Id is -74043 or -74044),"same-name participants with different explicit profile URLs retain their own cached biographies and filmographies offline");
        }
        foreach(var conflicting in new[]{secondProfile with{SourceUrl=namesake.ProfileUrl},secondProfile with{Person=namesake}})
        {
            await File.WriteAllTextAsync(Cache(otherNamesake),JsonSerializer.Serialize(conflicting));
            var conflictingOffline=new Offline();using(var client=new SourceClient(conflictingOffline))
            {
                var reports=new List<PersonProfile>();var result=await new LiveCatalog(client).Person(otherNamesake,CancellationToken.None,progress:profile=>reports.Add(profile)).WaitAsync(TimeSpan.FromSeconds(5));
                Check(result.Description.Length==0&&result.Filmography.Length==0&&reports.All(x=>x.Description.Length==0),"an explicit participant rejects a cached biography with a conflicting "+(conflicting.Person.ProfileUrl==namesake.ProfileUrl?"person identity":"professional source")+" even under its correct cache filename");
            }
        }
        var legacyPath=Path.Combine(Preferences.DataDir,"people",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cachedPerson.Name+"|"+cachedPerson.Role)))+".json");
        Check(Cache(cachedPerson)==legacyPath,"profiles without an explicit source URL keep the existing offline cache filename");
        var sourceNamesake=person with{Name="Одноимённый В Титрах "+Guid.NewGuid().ToString("N"),SourcePersonId="904241"};var otherSourceNamesake=sourceNamesake with{SourcePersonId="904242"};
        var sourceFirst=new PersonProfile(sourceNamesake,"Биография первого идентификатора.",[Film(sourceNamesake,-74045,"Работа id один"),Film(sourceNamesake,-74046,"Работа id два")]);
        var sourceSecond=new PersonProfile(otherSourceNamesake,"Биография второго идентификатора.",[Film(otherSourceNamesake,-74047,"Другая работа id один"),Film(otherSourceNamesake,-74048,"Другая работа id два")]);
        await File.WriteAllTextAsync(Cache(sourceNamesake),JsonSerializer.Serialize(sourceFirst));await File.WriteAllTextAsync(Cache(otherSourceNamesake),JsonSerializer.Serialize(sourceSecond));
        var sourceOffline=new Offline();using(var client=new SourceClient(sourceOffline))
        {
            var result=await new LiveCatalog(client).Person(otherSourceNamesake,CancellationToken.None,known:sourceFirst.Filmography);
            Check(sourceOffline.Requests==0&&result.Description==sourceSecond.Description&&result.Filmography.All(x=>x.Id is -74047 or -74048),"numeric source person identities isolate same-name profile caches and reject conflicting known film credits");
        }
        var identifiedPerson=primaryPerson with{Name="Explicit Work Identity "+Guid.NewGuid().ToString("N")};var identifiedHandler=new ProfessionalHandler(identifiedPerson,identifiedPerson.Name);
        var verifiedKnown=Film(identifiedPerson,-74049,"Работа с явной идентичностью");var unidentifiedKnown=Film(identifiedPerson with{ProfileUrl=null},-74050,"Работа неустановленного тёзки");
        using(var client=new SourceClient(identifiedHandler))
        {
            var biography=new TaskCompletionSource<PersonProfile>(TaskCreationOptions.RunContinuationsAsynchronously);
            var loading=new LiveCatalog(client).Person(identifiedPerson,CancellationToken.None,known:[verifiedKnown,unidentifiedKnown],progress:profile=>{if(profile.Description.Length>0)biography.TrySetResult(profile);});
            var early=await biography.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Check(early.Filmography is [var verified]&&verified.Id==verifiedKnown.Id,"an explicit professional profile does not publish another same-name film without a matching credit identity or verified work list");
            identifiedHandler.ReleaseFilmography.TrySetResult(true);var final=await loading.WaitAsync(TimeSpan.FromSeconds(5));
            Check(final.Filmography.Length==1&&final.Filmography[0].Id==verifiedKnown.Id,"an unrelated name-only known film remains excluded after the explicit professional filmography finishes");
        }
    }
}
