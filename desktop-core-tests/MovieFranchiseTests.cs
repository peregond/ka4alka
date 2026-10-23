using Kachalka;
using System.Net;
using System.Net.Http;
using System.Text;
static class MovieFranchiseTests
{
    sealed class FranchiseHandler(byte[] payload):HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;
            if(request.RequestUri?.AbsolutePath!="/api/franchise")throw new Exception("Unexpected franchise search network request.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(payload)});
        }
    }
    public static async Task Run()
    {
        void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS: franchise "+name);}
        var harry=ImdbSuggestions.Card("tt0241527","Гарри Поттер и философский камень","Harry Potter and the Philosopher's Stone","Фильмы",2001,null);
        var row=MovieFranchiseCatalog.Bundled(harry)!;
        Check(row!=null&&row.Id==1241&&row.Items.Length==8,"live verified Harry Potter collection contains eight distinct films");
        var others=MovieFranchiseCatalog.OtherParts(row,harry,"ru");
        Check(others.Length==7&&others.All(x=>x.ImdbId!=harry.ImdbId)&&others.Select(x=>x.Year).SequenceEqual(others.Select(x=>x.Year).Order()),"current film excluded; remaining parts ordered by release year");
        Check(others.All(x=>ShareLink.Create(x)!=null)&&others.All(x=>x.Imdb=="—"),"parts are real shareable IMDb cards and TMDB ratings are never mislabeled IMDb");
        var catalog=harry with{ImdbId=null,PageUrl="https://w6.zona.plus/movies/harry-potter-fixture"};
        Check(MovieFranchiseCatalog.Bundled(catalog)?.Id==1241,"an exact localized title and year can resolve the verified offline collection");
        Check(MovieFranchiseCatalog.Bundled(catalog with{Year=2026})==null&&MovieFranchiseCatalog.Bundled(harry with{Section="Сериалы"})==null,"same title from a different year or media type is never merged");
        using var client=new SourceClient();var online=new OnlineIndexClient(client);
        Check((await MovieFranchiseCatalog.Load(harry,online,"ru",default))?.Id==1241,"bundled franchises open without a server request");
        var prior=AppLanguage.Current;
        try{AppLanguage.Set("en");var en=MovieFranchiseCatalog.OtherParts(row,harry,"en");Check(en[0].DisplayTitle.Contains("Harry Potter")&&en[0].DisplayImageUrl!=null,"English parts use English titles and posters");
            Check(!EnglishMedia.NeedsLookup(en[0].English)&&EnglishMedia.NeedsLookup(en[0].English,true),"poster previews do not trigger localization work but opening a partial card requests its credits");
            Check(!EnglishMedia.NeedsLookup(en[0].English! with{Actors=["Fixture actor"],Cast=[]},true),"complete English credits are reused");}
        finally{AppLanguage.Set(prior);}
        var identity="tt0241527";
        var bytes=Encoding.UTF8.GetBytes("""{"id":"tt0241527","item":{"id":1241,"name":"Harry Potter","items":[{"imdbId":"tt0241527","title":"Harry Potter","originalTitle":"Harry Potter","year":2001,"description":"","poster":"https://evil.test/poster.jpg"}]}}""");
        Check(MovieFranchiseCatalog.Parse(bytes,identity)?.Id==1241,"exact response identity accepted");
        try{MovieFranchiseCatalog.Parse(bytes,"tt0123456");throw new Exception("Wrong identity accepted");}catch(System.IO.InvalidDataException){}
        var parsed=MovieFranchiseCatalog.Parse(bytes,identity);var empty=harry with{ImdbId="tt0123456"};
        Check(MovieFranchiseCatalog.OtherParts(parsed,empty,"ru").Single().ImageUrl==null,"foreign poster addresses are refused");
        Check(MovieFranchiseCatalog.Search("звёздные войны","ru").Length==9&&MovieFranchiseCatalog.Search("аватар","ru").Length==5,"the search index covers other verified franchises as well as Harry Potter");
        Check(MovieFranchiseCatalog.Search("гарри поттер 2001","ru") is [var first]&&first.ImdbId=="tt0241527","a release year still selects the exact film instead of the whole franchise");
        Check(MovieFranchiseCatalog.Search("Несуществующее название проверки","ru").Length==0&&MovieFranchiseCatalog.Search(" ","ru").Length==0,"unrelated and empty queries do not inject franchise cards");
        var cachedBytes=Encoding.UTF8.GetBytes("""{"id":"tt1234567","item":{"id":987654,"name":"Хроники проверки","items":[{"imdbId":"tt1234567","title":"Хроники проверки: Начало","originalTitle":"Search Fixture Begins","year":2001,"description":"","poster":null},{"imdbId":"tt7654321","title":"Хроники проверки: Продолжение","originalTitle":"Search Fixture Continues","year":2003,"description":"","poster":null}]}}""");
        using var cachedHandler=new FranchiseHandler(cachedBytes);using var cachedClient=new SourceClient(cachedHandler);
        var cachedOnline=new OnlineIndexClient(cachedClient,new Uri("https://franchise.test/"));
        var cachedItem=ImdbSuggestions.Card("tt1234567","Хроники проверки: Начало","Search Fixture Begins","Фильмы",2001,null);
        await MovieFranchiseCatalog.Load(cachedItem,cachedOnline,"ru",default);
        Check(MovieFranchiseCatalog.Search("хроники проверки","ru").Length==2&&MovieFranchiseCatalog.Search("Search Fixture","en").Length==2&&cachedHandler.Calls==1,"already loaded remote collections also enter bilingual search without another request");
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        try{await MovieFranchiseCatalog.Load(harry,online,"ru",cancelled.Token);throw new Exception("Cancellation swallowed");}catch(OperationCanceledException){}
    }
}
