using System.Text.Json;
using System.Net;
using System.Net.Http;
using Kachalka;

static class CharacterCreditsTests
{
    sealed class Handler(string body,HttpStatusCode status=HttpStatusCode.OK):HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {Calls++;return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(body)});}
    }
    static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
    public static void Run()
    {
        CinemaPerson[] people=[new("Адам Скотт","Актёры","",OriginalName:"Adam Scott",PhotoUrl:"https://example.test/actor.jpg"),new("Неизвестный","Актёры",""),new("Adam Scott","Режиссёры","")];
        CastCredit[] cast=[new("Adam Scott",Character:"Mark Scout"),new("Other Actor",Character:"Other Character")];
        var result=CharacterCredits.Apply(people,cast);
        Check(result[0].Character=="Mark Scout"&&result[0].Name==people[0].Name&&result[0].PhotoUrl==people[0].PhotoUrl,"an exact original actor name adds this work's character without replacing the localized name or portrait");
        Check(result[1].Character==null&&result[2].Character==null,"unknown actors and directors cannot borrow the next actor's character");
        Check(CharacterCredits.Apply(people,[new("Adam Scott",Character:"One"),new("Adam Scott",Character:"Two")])[0].Character==null,"ambiguous matching actor names do not select a character by cast order");
        Check(CharacterCredits.Apply(people,[new("Adam Scot",Character:"Wrong")])[0].Character==null,"a similar surname cannot supply another person's role");
        Check(new CinemaPerson("Actor","Актёры","",Character:" ").CreditLabel==AppLanguage.T("В ролях")&&result[0].CreditLabel=="Mark Scout","missing characters retain the translated Cast label and known characters display as source text");
        using var data=JsonDocument.Parse("""{"cast":[{"name":"Тимоти Шаламе","originalName":"Timothée Chalamet","character":"Paul Atreides"}]}""");
        Check(CharacterCredits.Read(data.RootElement).Single().Character=="Paul Atreides","structured server credits preserve character names");
        Check(CharacterCredits.Read(JsonDocument.Parse("{}").RootElement).Length==0,"older server replies remain compatible");
        var english=new EnglishMedia("en","Dune","",null,null,[],[],["Timothée Chalamet"],[],CharacterCredits.Read(data.RootElement));
        Check(english.People.Single().Character=="Paul Atreides","English cards carry the same character through their localized actor identity");
        var old=JsonSerializer.Deserialize<CinemaPerson>("""{"Name":"Actor","Role":"Актёры","PageUrl":""}""")!;
        Check(old.Character==null,"existing saved actors deserialize without requiring a new field");
        var game=GameCatalog.Item(620,"Portal 2","https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/620/header.jpg");
        Check(game.DisplayImageUrl=="https://cdn.akamai.steamstatic.com/steam/apps/620/library_600x900.jpg"&&game.ImageUrl!.EndsWith("header.jpg")&&GameCatalog.HeroImage(game)!.EndsWith("library_hero.jpg"),"game posters use Steam's portrait artwork while retaining the landscape fallback and independent wide hero");
        Check(GameCatalog.PosterImage(game with{PageUrl="https://example.test/app/620/"})==game.ImageUrl,"an unrelated page cannot generate a trusted Steam artwork address");
    }
    public static async Task LoadTests()
    {
        var previous=Environment.GetEnvironmentVariable("KACHALKA_DATA");var root=Path.Combine(Path.GetTempPath(),"kachalka-cast-"+Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("KACHALKA_DATA",root);
        try
        {
            var movie=ImdbSuggestions.Card("tt11280740","Разделение","Severance","Сериалы",2022,null) with{People=[new("Адам Скотт","Актёры","",OriginalName:"Adam Scott")]};
            const string answer="""{"id":"tt11280740","section":"series","item":{"cast":[{"name":"Adam Scott","character":"Mark Scout"}]}}""";
            var handler=new Handler(answer);using var source=new SourceClient(handler);var online=new OnlineIndexClient(source,new Uri("https://index.example.test/"));
            var enriched=await CharacterCredits.Load(online,movie,"ru",CancellationToken.None);
            Check(enriched.People[0].Character=="Mark Scout","optional character enrichment reaches the displayed actor without replacing the card");
            await CharacterCredits.Load(online,movie,"ru",CancellationToken.None);
            Check(handler.Calls==1,"reopening a card reuses its cached character credits without per-actor requests");
            var wrong=new Handler(answer.Replace("tt11280740","tt0000001"));using var wrongSource=new SourceClient(wrong);
            Check((await CharacterCredits.Load(new OnlineIndexClient(wrongSource,new Uri("https://index.example.test/")),movie,"ru",CancellationToken.None,true)).People[0].Character==null,"credits from another title are refused even when the actor's name matches");
            var old=new Handler("{}",HttpStatusCode.NotFound);using var oldSource=new SourceClient(old);
            Check((await CharacterCredits.Load(new OnlineIndexClient(oldSource,new Uri("https://index.example.test/")),movie,"ru",CancellationToken.None,true)).People[0].Name=="Адам Скотт","an older or unavailable server leaves the already loaded cast usable");
        }
        finally{Environment.SetEnvironmentVariable("KACHALKA_DATA",previous);if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
