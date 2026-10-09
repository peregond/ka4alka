using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kachalka;

public static class ZonaMovieMetadataTests
{
    const string Photo="https://img4.zonapic.com/images/actor/972/972478.jpg";
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS: "+name);}
    static byte[] Json(MediaItem item,string? description="Описание фильма из собственного источника каталога.",string? score="6.5",string? photo=Photo,string actorId="972478",string? name=null,int? year=null,bool credits=true,string countryIds="25 1 11")
    {
        var detail=new Dictionary<string,object?>{{"id","7178187"},{"name_rus",name??item.Title},{"name_original",item.OriginalTitle??"Runner"},{"year",year??item.Year},{"serial",item.Section=="Сериалы"},{"description",description},{"rating_kinopoisk",score},{"rating_imdb",6.7},{"rating",9.9},{"genreId","3 6 16"},{"country_id",countryIds}};
        return JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string,object?>
        {
            [item.Section=="Сериалы"?"serial":"movie"]=detail,
            ["genres"]=new object[]{new{id=16,name="криминал",translit="kriminal"},new{id="6",name="комедия",translit="komedia"},new{id=3,name="боевик",translit="boevik"},new{id=8,name="драма",translit="drama"}},
            ["countries"]=new object[]{new{id=11,name="Великобритания",translit="velikobritaniya"},new{id="1",name="США",translit="ssha"},new{id=25,name="Австралия",translit="avstraliya"},new{id=6,name="Канада",translit="kanada"}},
            ["persons"]=credits?new Dictionary<string,object>
            {
                ["actors"]=new[]{new Dictionary<string,object?>{{"i",actorId},{"name","Алан Ричсон"},{"name_eng","Alan Ritchson"},{"cover",photo},{"r","4"}}},
                ["director"]=new[]{new Dictionary<string,object>{{"i",3640},{"name","Скотт Во"},{"name_eng","Scott Waugh"},{"r",1}}},
                ["producer"]=new[]{new{name="Чужая профессия",i="1"}}
            }:new Dictionary<string,object>()
        });
    }
    sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> reply):HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Interlocked.Increment(ref Calls);return reply(request,ct);}
    }
    static HttpResponseMessage Reply(byte[] bytes)=>new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
    static MediaItem Film()=>new(-77101,"Курьер","Фильмы","",2026,"—","—","#526B69"){PageUrl="https://w6.zona.plus/movies/json-fixture-"+Guid.NewGuid().ToString("N"),OriginalTitle="Runner"};
    static string Cache(MediaItem item)=>Path.Combine(Preferences.DataDir,"details",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.PageUrl!)))+".json");
    public static async Task Run()
    {
        var film=Film();var parsed=ZonaMovieMetadata.Parse(Json(film),film)!;
        Check(parsed.Kinopoisk=="6.5"&&parsed.Imdb=="6.7"&&parsed.OriginalTitle=="Runner"&&parsed.Description!.StartsWith("Описание фильма",StringComparison.Ordinal),"Zona JSON supplies the identified film's own description, original title and separate Kinopoisk/IMDb scores");
        Check(parsed.People is [var actor,var director]&&actor is {Name:"Алан Ричсон",Role:"Актёры",PhotoUrl:Photo,OriginalName:"Alan Ritchson",SourcePersonId:"972478",PageUrl:""}&&director is {Role:"Режиссёры",OriginalName:"Scott Waugh",SourcePersonId:"3640",PhotoUrl:null},"Zona JSON preserves actor photo, official English name and source ID while leaving a director without a source photo unset");
        Check(parsed.Genre=="криминал, комедия, боевик"&&parsed.GenreKeys.SequenceEqual(new[]{"kriminal","komedia","boevik"})&&parsed.Country=="Великобритания, США, Австралия"&&!parsed.CountryKeys.Contains("kanada"),"Zona genres and countries include only rows selected by the identified film's exact source IDs");
        var partialCountries=ZonaMovieMetadata.Parse(Json(film,countryIds:"1 99"),film)!;
        Check(parsed.CountryKeysComplete&&!partialCountries.CountryKeysComplete&&partialCountries.CountryKeys.SequenceEqual(["ssha"])&&!CatalogRegions.IsForeign(partialCountries,"rossiia"),"an unmatched selected country ID keeps the country list partial instead of falsely classifying a coproduction as foreign");
        Check(!ZonaMovieMetadata.Parse(Json(film,countryIds:"1 invalid"),film)!.CountryKeysComplete,"an invalid selected country identifier cannot become proof of a complete production-country list");
        Check(ZonaMovieMetadata.Parse(Json(film,score:"-10"),film)?.Kinopoisk=="—"&&ZonaMovieMetadata.Parse(Json(film,score:"11"),film)?.Kinopoisk=="—"&&ZonaMovieMetadata.Parse(Json(film,score:null),film)?.Kinopoisk=="—","missing or invalid source scores stay unknown without borrowing the unrelated aggregate rating");
        Check(ZonaMovieMetadata.Parse(Json(film,name:"Другой фильм"),film with{OriginalTitle=null})==null&&ZonaMovieMetadata.Parse(Json(film,year:2025),film)==null,"Zona JSON rejects another title or another release year before using any cast or ratings");
        Check(ZonaMovieMetadata.Parse(Json(film,name:"Другое название"),film)?.People.Length==2,"an exact verified original title and year can identify a film with another localized title");
        Check(ZonaMovieMetadata.Parse(Encoding.UTF8.GetBytes("<div itemprop='description'>HTML</div>"),film)==null&&ZonaMovieMetadata.Parse(Encoding.UTF8.GetBytes("[]"),film)==null,"HTML and unrelated JSON shapes fall through to the existing source parser");
        Check(ZonaMovieMetadata.Parse(Json(film),film with{PageUrl="https://w6.zona.plus.evil.test/movies/movie"})==null,"structured movie metadata only applies to the trusted catalog's detail route");
        Check(ZonaMovieMetadata.Parse(Json(film,photo:"https://img4.zonapic.com.evil.test/images/actor/972/972478.jpg"),film)?.People[0].PhotoUrl==null&&ZonaMovieMetadata.Parse(Json(film,actorId:"123"),film)?.People[0].PhotoUrl==null,"Zona actor photos reject a lookalike host and a different source person's image ID");
        var series=film with{Title="Укрытие",Section="Сериалы",Year=2023,OriginalTitle="Silo",PageUrl="https://w6.zona.plus/tvseries/json-silo-"+Guid.NewGuid().ToString("N")};
        Check(ZonaMovieMetadata.Parse(Json(series),series)?.People[0].PhotoUrl==Photo&&ZonaMovieMetadata.Parse(Json(film),series)==null,"series metadata uses the actual serial root and cannot consume a film object's credits");

        await Headers(film);
        var handler=new Handler((request,_)=>
        {
            Check(request.Headers.TryGetValues("X-Requested-With",out var values)&&values.Single()=="XMLHttpRequest","film details request the catalog's richer JSON representation");
            return Task.FromResult(Reply(Json(film)));
        });
        using(var client=new SourceClient(handler))
        {
            var first=await new LiveCatalog(client).Detail(film,CancellationToken.None);
            Check(first.People[0].PhotoUrl==Photo&&handler.Calls==1,"complete Zona JSON loads credits and photographs with one source request");
        }
        var offline=new Handler((_,_)=>throw new HttpRequestException("offline"));
        using(var client=new SourceClient(offline))
        {
            var restored=await new LiveCatalog(client).Detail(film,CancellationToken.None);
            Check(offline.Calls==0&&restored.People[0] is {PhotoUrl:Photo,SourcePersonId:"972478",OriginalName:"Alan Ritchson"},"the recent detail cache retains source photographs and exact actor identity fields offline");
        }
        var htmlFilm=Film();var html=Encoding.UTF8.GetBytes("<div itemprop='description'>Описание HTML.</div><span itemprop='actor'><span itemprop='name'>Алан Ричсон</span></span>");
        var htmlHandler=new Handler((_,_)=>Task.FromResult(Reply(html)));
        using(var client=new SourceClient(htmlHandler))
            Check((await new LiveCatalog(client).Detail(htmlFilm,CancellationToken.None)).Description=="Описание HTML."&&htmlHandler.Calls==1,"a server that ignores JSON headers uses its returned HTML without a second request");
        var rejectedFilm=Film();var rejectedHandler=new Handler((request,_)=>Task.FromResult(request.Headers.Contains("X-Requested-With")?new HttpResponseMessage(HttpStatusCode.ServiceUnavailable):Reply(html)));
        using(var client=new SourceClient(rejectedHandler))
            Check((await new LiveCatalog(client).Detail(rejectedFilm,CancellationToken.None)).Description=="Описание HTML."&&rejectedHandler.Calls==2,"a server that rejects the JSON request can still load HTML using a plain second request within the original source budget");
        var canceledFilm=Film();var started=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceledHandler=new Handler(async(_,token)=>{started.TrySetResult(true);await Task.Delay(Timeout.Infinite,token);return Reply(html);});
        using(var client=new SourceClient(canceledHandler))using(var canceled=new CancellationTokenSource())
        {
            var pending=new LiveCatalog(client).Detail(canceledFilm,canceled.Token);await started.Task.WaitAsync(TimeSpan.FromSeconds(3));canceled.Cancel();
            try{await pending.WaitAsync(TimeSpan.FromSeconds(3));throw new Exception("Canceled JSON detail launched HTML fallback");}catch(OperationCanceledException){}
            Check(canceledHandler.Calls==1,"canceling a JSON detail request stops loading without starting a fallback HTML request");
        }
        var partialFilm=Film();var partialHandler=new Handler((request,_)=>Task.FromResult(Reply(request.Headers.Contains("X-Requested-With")?Json(partialFilm,description:null):html)));
        using(var client=new SourceClient(partialHandler))
        {
            var supplemented=await new LiveCatalog(client).Detail(partialFilm,CancellationToken.None);
            Check(supplemented.Description=="Описание HTML."&&supplemented.Kinopoisk=="6.5"&&supplemented.People[0].PhotoUrl==Photo&&partialHandler.Calls==2,"an incomplete valid JSON response can supplement its missing description from HTML without losing source photos or ratings");
        }
        var fallbackFilm=Film();var fallbackHandler=new Handler((request,_)=>request.Headers.Contains("X-Requested-With")?Task.FromResult(Reply(Json(fallbackFilm,description:null))):throw new HttpRequestException("HTML unavailable"));
        using(var client=new SourceClient(fallbackHandler))
            Check((await new LiveCatalog(client).Detail(fallbackFilm,CancellationToken.None)).People[0].PhotoUrl==Photo&&fallbackHandler.Calls==2,"an optional HTML failure retains already verified JSON credits and actor photographs");
        File.SetLastWriteTimeUtc(Cache(film),DateTime.UtcNow.AddHours(-7));var before=await File.ReadAllTextAsync(Cache(film));
        var unrelated=new Handler((_,_)=>Task.FromResult(Reply(Json(film with{OriginalTitle="Unrelated"},name:"Другой фильм"))));
        using(var client=new SourceClient(unrelated))
            Check((await new LiveCatalog(client).Detail(film,CancellationToken.None)).People[0].PhotoUrl==Photo&&await File.ReadAllTextAsync(Cache(film))==before,"an unrelated JSON response cannot overwrite a verified expired detail cache");
        var merged=ZonaMovieMetadata.MergePeople([new("Алан Ричсон","Актёры","/person/original",PhotoUrl:Photo,SourcePersonId:"972478"),new("Оператор","Операторы","")],parsed.People);
        Check(merged.Length==3&&merged[0].PhotoUrl==Photo&&merged.Any(x=>x.Role=="Операторы"),"richer structured credits preserve previously verified crew roles and portrait fields");
        Check(ZonaMovieMetadata.MergePeople([new("Тёзка","Актёры","",SourcePersonId:"1")],[new("Тёзка","Актёры","",SourcePersonId:"2")]).Length==2,"cast merging does not combine namesakes with different explicit source IDs");
        Check(ZonaMovieMetadata.MergePeople([new("Алан Ричсон","Актёры","",PhotoUrl:Photo)],[new("Алан Ричсон","Актёры","",SourcePersonId:"123")]).Single().PhotoUrl==null,"cast merging cannot inherit a name-only portrait belonging to another explicit source ID");
        Check(ZonaMovieMetadata.MergePeople([new("Тёзка","Актёры","",ProfileUrl:"https://kino-teatr.ua/person/one-1.phtml")],[new("Тёзка","Актёры","",ProfileUrl:"https://kino-teatr.ua/person/two-2.phtml")]).Length==2,"cast merging preserves distinct verified profile identities of namesakes");
        var combined=await MediaMetadata.Load(film,_=>Task.FromResult(film with{People=[new("Оператор","Операторы","")]}),_=>Task.FromResult(parsed));
        Check(combined.Item.People.Any(x=>x.Role=="Операторы")&&combined.Item.People.Any(x=>x.PhotoUrl==Photo),"combined index and direct metadata retain the index's verified cinematographer together with the direct source's actor photos");
        var cast=JsonSerializer.SerializeToUtf8Bytes(new{movie=new{name_rus=film.Title,name_original="Runner",year=2026},persons=new{actors=Enumerable.Range(1,80).Select(id=>new{name="Участник "+id,i=id}),director=new[]{new{name="Скотт Во",i=3640}}}});
        Check(ZonaMovieMetadata.Parse(cast,film) is {People.Length:80} capped&&capped.People.Any(x=>x.Role=="Режиссёры"),"a large actor list cannot displace the director from bounded movie credits");
    }
    static async Task Headers(MediaItem film)
    {
        var seen=new List<(string Host,bool Requested,bool Json)>();
        using var client=new SourceClient(new Handler((request,_)=>
        {
            lock(seen)seen.Add((request.RequestUri!.Host,request.Headers.Contains("X-Requested-With"),request.Headers.Accept.Any(x=>x.MediaType=="application/json")));
            return Task.FromResult(Reply(Encoding.UTF8.GetBytes("1234567890")));
        }));
        await Task.WhenAll(client.ReadCinemaDetail(new Uri(film.PageUrl!),100,CancellationToken.None),client.Read(new Uri("https://example.test/file.torrent"),100,CancellationToken.None),client.ReadCinemaDetail(new Uri("https://w6.zona.plus/search-form?query=movie"),100,CancellationToken.None));
        Check(seen.Count(x=>x.Requested&&x.Json)==1&&seen.Any(x=>x.Host=="example.test"&&!x.Requested&&!x.Json)&&seen.Any(x=>x.Host=="w6.zona.plus"&&!x.Requested&&!x.Json),"JSON headers are scoped to a single trusted detail request and cannot leak into concurrent torrent or catalog searches");
        try{await client.ReadCinemaDetail(new Uri(film.PageUrl!),4,CancellationToken.None);throw new Exception("JSON response size limit was bypassed");}catch(InvalidDataException){}
        Check(true,"JSON detail requests enforce the same bounded response size as other sources");
    }
}
