using System.Net;
using System.Text;
using Kachalka;
public static class AwardsCatalogTests
{
    const string Sample="""
    {"schemaVersion":1,"generatedAtUtc":"2026-10-17T05:00:00Z","awards":[
      {"id":"oscar-best-picture","title":"Оскар","titleEn":"Academy Awards","description":"Лучший фильм","descriptionEn":"Best Picture","entries":[
        {"year":2024,"imdbId":"tt15398776","title":"Оппенгеймер","titleEn":"Oppenheimer","originalTitle":"Oppenheimer","filmYear":2023,"poster":"https://upload.example/p.jpg"},
        {"year":2025,"imdbId":"tt11271038","title":"Анора","titleEn":"Anora","originalTitle":"Anora","filmYear":2024,"poster":"http://insecure.example/p.jpg"},
        {"year":2025,"imdbId":"tt11271038","title":"Анора","titleEn":"Anora","originalTitle":"Anora","filmYear":2024}
      ]}]}
    """;
    sealed class Fake(Func<HttpRequestMessage,HttpResponseMessage> answer):HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;return Task.FromResult(answer(request));}
    }
    public static void Run()=>RunAsync().GetAwaiter().GetResult();
    static async Task RunAsync()
    {
        void Check(bool pass,string message){if(!pass)throw new Exception(message);}
        void Rejects(string json,string message){try{AwardsCatalog.Parse(Encoding.UTF8.GetBytes(json));}catch(InvalidDataException){return;}throw new Exception(message);}
        var parsed=AwardsCatalog.Parse(Encoding.UTF8.GetBytes(Sample));
        var oscar=parsed.Single();var entries=oscar.Entries;
        Check(entries.Select(entry=>entry.ImdbId).SequenceEqual(new[]{"tt11271038","tt15398776"}),"Winners are listed newest year first and a repeated IMDb id is read once.");
        Check(entries[0].Poster==null&&entries[1].Poster=="https://upload.example/p.jpg","Only https posters are kept.");
        Rejects("""{"schemaVersion":2,"awards":[]}""","An unknown schema version is refused.");
        Rejects("""{"schemaVersion":1,"awards":[{"id":"Bad Id","title":"x","entries":[{"year":2000,"imdbId":"tt1234567","title":"A"}]}]}""","A badly formed award id is refused.");
        Rejects("""{"schemaVersion":1,"awards":[{"id":"a","title":"x","entries":[{"year":2000,"imdbId":"nm1234567","title":"A"}]}]}""","Only title ids of IMDb are accepted.");
        Rejects("""{"schemaVersion":1,"awards":[{"id":"a","title":"x","entries":[{"year":1800,"imdbId":"tt1234567","title":"A"}]}]}""","A year out of range is refused.");
        Rejects("""{"schemaVersion":1,"awards":[{"id":"a","title":"x","entries":[]}]}""","An award without entries is refused.");
        Rejects("""{"schemaVersion":1,"awards":[{"id":"a","title":"x","entries":[{"year":2000,"imdbId":"tt1234567","title":"A"}]},{"id":"a","title":"y","entries":[{"year":2000,"imdbId":"tt1234568","title":"B"}]}]}""","A repeated award id is refused.");
        Rejects(new string(' ',AwardsCatalog.MaxBytes+1),"An oversized file is refused.");

        // A winner is an IMDb-only card, so details and releases load the usual way; a title of the Russian catalog keeps its own card.
        var card=AwardsCatalog.Item(entries[0]);
        Check(ImdbSuggestions.IsImdbOnly(card)&&card.ImdbId=="tt11271038"&&card.Title=="Анора"&&card.OriginalTitle=="Anora"&&card.Year==2024&&card.Section=="Фильмы","A winner becomes an IMDb-only card.");
        Check(AwardsCatalog.Item(entries[0]).Id==card.Id,"The card id is stable.");
        var known=new MediaItem(5,"Из каталога","Фильмы","",2024,"7","7","#123456"){ImdbId="tt11271038",OnlineId="movies:anora"};
        Check(ReferenceEquals(AwardsCatalog.Item(entries[0],new Dictionary<string,MediaItem>{["tt11271038"]=known}),known),"A title the catalog has keeps its own card.");
        var groups=FeaturedCatalog.Resolve([],null,parsed);
        var group=groups.Single(row=>row.Collection.Id=="award-oscar-best-picture");
        Check(group.Items.Length==2&&group.Collection.Label=="Оскар"&&group.Collection.Kicker.Length>0&&!group.Collection.IsGames,"The featured page gets a shelf per award.");

        // The backend file is fetched once a day, cached on disk and survives a missing feed.
        var folder=Path.Combine(Path.GetTempPath(),"kachalka-awards-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var previous=Environment.GetEnvironmentVariable("KACHALKA_DATA");Environment.SetEnvironmentVariable("KACHALKA_DATA",folder);
        try
        {
            var now=DateTime.UtcNow;var handler=new Fake(_=>new HttpResponseMessage(HttpStatusCode.NotFound));
            using(var client=new SourceClient(handler))
            {
                var missing=new AwardsCatalog(client,()=>now);
                Check(!await missing.RefreshAsync()&&missing.All.Count==0&&!File.Exists(Path.Combine(folder,"awards.json")),"A feed that is not published yet leaves no award lists and no file.");
            }
            var served=new Fake(_=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Sample,Encoding.UTF8,"application/json")});
            using(var client=new SourceClient(served))
            {
                var fresh=new AwardsCatalog(client,()=>now);
                Check(await fresh.RefreshAsync()&&fresh.All.Single().Entries.Length==2&&File.Exists(Path.Combine(folder,"awards.json")),"A published feed is read and saved.");
                Check(!await fresh.RefreshAsync()&&served.Calls==1,"The feed is not asked again within a day.");
            }
            using var offline=new SourceClient(new Fake(_=>throw new HttpRequestException("offline")));
            var restored=new AwardsCatalog(offline,()=>now.AddDays(2));
            Check(!await restored.RefreshAsync()&&restored.All.Single().Entries.Length==2,"Without network the saved lists are used.");
            File.WriteAllText(Path.Combine(folder,"awards.json"),"{broken");
            var damaged=new AwardsCatalog(offline,()=>now.AddDays(2));await damaged.RestoreAsync();
            Check(damaged.All.Count==0,"A damaged saved file is ignored.");
        }
        finally{Environment.SetEnvironmentVariable("KACHALKA_DATA",previous);try{Directory.Delete(folder,true);}catch(IOException){}}
        Console.WriteLine("PASS: award lists parsing, strict validation, IMDb-only cards, catalog preference, daily refresh and offline fallback");
    }
}
