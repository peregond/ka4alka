using Kachalka;
static class SearchLanguageTests
{
    public static void Run()
    {
        void Check(bool valid,string message){if(!valid)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        SourceEntry Row(string id,string title,string source="The Pirate Bay",int seeds=200)=>new(id,title,source,"https://example.test/"+id,"magnet:?xt=urn:btih:"+new string('a',40),null,Seeds:seeds);
        var initialLanguage=AppLanguage.Current;
        try
        {
            var card=new MediaItem(-101,"Дюна","Фильмы","фантастика",2021,"8.0","8.0","#112233"){OriginalTitle="Dune",PageUrl="https://w6.zona.plus/movies/dyuna",OnlineId="movies:dyuna",ImdbId="tt1160419",Description="Русское описание",ImageUrl="https://example.test/ru.jpg"};
            var json=System.Text.Encoding.UTF8.GetBytes("""{"id":"movies:dyuna","section":"movies","item":{"language":"en","title":"Dune","description":"English synopsis","poster":"https://image.tmdb.org/t/p/w500/en.jpg","backdrop":"https://image.tmdb.org/t/p/w1280/art.jpg","genres":["Adventure"],"countries":["United States"],"actors":["Timothée Chalamet"],"directors":["Denis Villeneuve"]}}""");
            var content=EnglishMedia.Parse(json,"movies:dyuna","movies");
            card.SetEnglish(content);AppLanguage.Set("en");
            Check(card.DisplayTitle=="Dune"&&card.DisplayDescription=="English synopsis"&&card.DisplayImageUrl!.EndsWith("/en.jpg")&&card.DisplayPeople[0].Name=="Denis Villeneuve","English UI uses localized title, synopsis, poster and credits together");
            Check(card.Title=="Дюна"&&card.Description=="Русское описание"&&card.OnlineId=="movies:dyuna","localization never mutates canonical identity or Russian metadata");
            Check(!System.Text.Json.JsonSerializer.Serialize(card).Contains("English synopsis"),"localized data cannot contaminate the shared Russian catalog or saved metadata");
            Check(MediaIdentity.Queries(card)[0]=="Dune","English release discovery asks English aliases first");
            AppLanguage.Set("ru");Check(card.DisplayTitle=="Дюна"&&card.DisplayImageUrl==card.ImageUrl&&card.DisplayDescription==card.Description,"switching back restores Russian presentation immediately");
            Check(EnglishMedia.PosterUrl("https://evil.test/t/p/w500/en.jpg")==null&&EnglishMedia.PosterUrl("https://image.tmdb.org/t/p/w500/../en.jpg")==null,"localized image host and path are restricted");
            var rejected=false;try{EnglishMedia.Parse(json,"movies:other","movies");}catch(System.IO.InvalidDataException){rejected=true;}Check(rejected,"a localized response for a different card is rejected");
            Check(GameCatalog.Query(new GameBrowse("Portal",GameBrowse.Popular,"",1),"en").Query.Contains("l=english")&&GameCatalog.Query(new GameBrowse("",GameBrowse.Popular,"",1),"en").Query.Contains("l=english"),"Steam search and home browsing both request English content");
        }
        finally{AppLanguage.Set(initialLanguage);}
        var english=Row("en","Dune (2021) 1080p ENG",seeds:50);
        var russian=Row("ru","Дюна (2021) 1080p RUS","RuTor",900);
        var unknown=Row("unknown","Dune (2021) 1080p",seeds:500);
        var japanese=Row("jp","Dune (2021) 1080p JPN",seeds:1000);
        var dead=english with{Id="dead",Seeds=0};
        var rows=new[]{russian,unknown,japanese,dead,english};
        Check(SearchLanguage.AudioOrder(rows,"en").Select(x=>x.Id).SequenceEqual(["en","unknown","jp","ru","dead"]),"English preference keeps healthy English audio first and retains every other language with dead swarms last");
        Check(SearchLanguage.AudioOrder(rows,"ru").SequenceEqual(RussianAudio.Order(rows)),"Russian mode preserves its established audio ordering");
        foreach(var title in new[]{"Dune 1080p RUS Subs: ENG","Dune 1080p RUS [Subtitles: English]","Dune 1080p JPN ENG.SUBS","Dune 1080p Japanese English subtitles"})
            Check(SearchLanguage.EnglishAudioRank(Row("subs",title))==0,"English subtitles do not imply English audio: "+title);
        Check(SearchLanguage.EnglishAudioRank(Row("multi","Dune 1080p RUS ENG"))==3,"dual-audio releases qualify when an English audio track is advertised");
        Check(SearchLanguage.EnglishAudioRank(Row("original","Dune 1080p original JPN"))==0,"original Japanese audio is never assumed to be English");
        Check(ReleaseRecommendation.Pick(rows,preferredLanguage:"en")?.Entry==english,"the primary download recommendation follows English audio preference");
        Check(ReleaseRecommendation.Pick(rows,preferredLanguage:"ru")?.Entry==russian,"the primary download recommendation retains Russian priority in Russian mode");
        Check(ReleaseRecommendation.Pick([russian],preferredLanguage:"en")?.Entry==russian,"a Russian fallback remains downloadable when no English release exists");
        Check(ReleaseRecommendation.Pick([dead],preferredLanguage:"en")==null,"language preference cannot bypass minimum seeder safeguards");
        var gameEn=Row("game-en","Portal 2 [ENG]",seeds:50);var gameRu=Row("game-ru","Portal 2 [RUS]","RuTor",500);
        Check(GameReleaseRecommendation.Pick([gameRu,gameEn],"en")?.Entry==gameEn&&GameReleaseRecommendation.Pick([gameRu,gameEn],"ru")?.Entry==gameRu,"game recommendation follows interface language without losing seeder safeguards");
        Check(ReleaseRecommendation.Pick(rows,preferredLanguage:"en")!.Reason.Contains("английская озвучка",StringComparison.OrdinalIgnoreCase),"recommendations describe evidenced English audio only");
    }
}
