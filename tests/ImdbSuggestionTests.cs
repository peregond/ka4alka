using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Kachalka;

// October 2026 report: "Solar Opposites" could not be found. The Russian catalog has no page for it
// and its search does not understand the English name; IMDb suggestions now fill that gap.
static class ImdbSuggestionTests
{
    sealed class SiteHandler(Func<Uri,HttpResponseMessage> answer):HttpMessageHandler
    {
        public readonly List<Uri> Requests=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Requests.Add(request.RequestUri!);return Task.FromResult(answer(request.RequestUri!));}
    }
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS: "+message);}
    public static async Task Run()
    {
        const string json="""
        {"d":[
          {"i":{"height":1500,"imageUrl":"https://m.media-amazon.com/images/M/MV5BSolar@._V1_.jpg","width":1000},"id":"tt8910922","l":"Solar Opposites","q":"TV series","qid":"tvSeries","rank":800,"s":"Justin Roiland","y":2020,"yr":"2020-"},
          {"id":"tt36034547","l":"You and I Are Polar Opposites","q":"TV series","y":2026},
          {"id":"tt34937026","l":"Polar Opposites","q":"TV movie","y":2025},
          {"id":"tt1234567","l":"Solar Opposites: The Game","q":"video game","y":2024},
          {"id":"nm0000001","l":"Solar Opposites Person","q":"feature","y":2020},
          {"id":"tt7654321","l":"Solar Opposites Christmas","q":"feature","y":2023}
        ]}
        """;
        var found=ImdbSuggestions.Parse(json,"solar opposites");
        Check(found.Length==2&&found[0].Title=="Solar Opposites"&&found[0].Section=="Сериалы"&&found[0].Year==2020&&found[1].Section=="Фильмы",
            "an English query finds the series and a matching film, and skips games, people and titles that only look similar");
        var series=found[0];
        Check(series.ImdbId=="tt8910922"&&series.PageUrl=="https://www.imdb.com/title/tt8910922/"&&series.OriginalTitle=="Solar Opposites"&&ImdbSuggestions.IsImdbOnly(series)&&series.IsLive,
            "an IMDb title becomes a live card with its original name for the release search");
        Check(series.ImageUrl=="https://m.media-amazon.com/images/M/MV5BSolar@._V1_QL75_UX400_.jpg","the poster is requested at card size");
        Check(series.Id<0&&series.Id==ImdbSuggestions.Parse(json,"solar opposites")[0].Id,"the card id is stable between searches");
        Check(ImdbSuggestions.Applies("Солнечные противоположности")&&ImdbSuggestions.Applies("solar opposites")&&!ImdbSuggestions.Applies("Придуманное название")&&!ImdbSuggestions.Applies("  "),"Latin titles and verified Russian aliases ask IMDb without translating arbitrary queries");
        // The report's second half: the queued Solar Opposites row could not be opened because only Zona pages had a card.
        var queued=new DownloadItem{Name="Solar.Opposites.S06.1080p.WEB-DL.mkv",MediaTitle=series.Title,MediaSection=series.Section,MediaYear=series.Year,MediaPageUrl=series.PageUrl,ImageUrl=series.ImageUrl,
            ReleaseTitle="Солнечные оппозиты / Solar Opposites / Сезон 6 (WEB-DL 1080p)"};
        var queuedCard=DownloadMetadata.Card(queued);
        Check(queued.HasMediaCard&&queuedCard!=null&&queuedCard.Id==series.Id&&queuedCard.ImdbId==series.ImdbId&&queuedCard.OriginalTitle==series.Title&&queuedCard.Section=="Сериалы"&&ImdbSuggestions.IsImdbOnly(queuedCard),
            "a queued IMDb-only title reopens the same card the search showed");
        Check(DownloadMetadata.Card(new DownloadItem{MediaSection="Сериалы",MediaPageUrl="https://www.imdb.com/name/nm0000001/"})==null&&
            DownloadMetadata.Card(new DownloadItem{MediaSection="Сериалы",MediaPageUrl="https://www.imdb.com/title/tt8910922/?ref=x"})==null&&
            DownloadMetadata.Card(new DownloadItem{MediaSection="Сериалы",MediaPageUrl="https://evil.example/title/tt8910922/"})==null,
            "only a plain IMDb title page opens a card");
        Check(queued.SeasonLabel=="6 сезон"&&queued.YearSeasonLabel=="2020 · 6 сезон","a series download names its season beside the year");
        var episode=new DownloadItem{Name="Show.S02E05.mkv",MediaSection="Сериалы",MediaYear=2021};
        Check(episode.SeasonLabel=="2 сезон, 5 серия"&&new DownloadItem{Name="Show Complete Series 1080p.mkv",MediaSection="Сериалы"}.SeasonLabel=="Несколько сезонов"&&new DownloadItem{Name="Show.1080p.mkv",MediaSection="Сериалы",MediaYear=2021}.YearSeasonLabel=="2021"
            &&new DownloadItem{Name="Movie.S01.mkv",MediaSection="Фильмы",MediaYear=2019}.YearSeasonLabel=="2019",
            "single episodes and packs are labelled, unknown seasons and films show only the year");
        // TVmaze fills the empty card of a series the Russian catalog lacks.
        var maze="""
        {"id":38963,"name":"Solar Opposites","premiered":"2020-05-08","genres":["Comedy","Science-Fiction","Adult"],
         "network":null,"webChannel":{"id":2,"name":"Hulu","country":null},"externals":{"tvrage":null,"thetvdb":368010,"imdb":"tt8910922"},
         "image":{"medium":"https://static.tvmaze.com/uploads/images/medium_portrait/1/1.jpg","original":"https://static.tvmaze.com/uploads/images/original_untouched/1/1.jpg"},
         "summary":"<p><b>Solar Opposites</b> follows a team of aliens &amp; their pet.<br>They escaped a doomed planet.</p><p>Season two.</p>"}
        """u8.ToArray();
        var mazeCast="""[{"person":{"id":1,"name":"Justin Roiland"},"character":{"name":"Korvo"}},{"person":{"id":2,"name":"Thomas Middleditch"},"character":{"name":"Terry"}},{"person":{"id":1,"name":"Justin Roiland"},"character":{"name":"Yumyulack"}}]"""u8.ToArray();
        var filled=TvMazeMetadata.Apply(series,maze,mazeCast);
        Check(filled.Description!.StartsWith("Solar Opposites follows a team of aliens & their pet.")&&!filled.Description.Contains('<')&&filled.Description.Contains("Season two."),"TVmaze text loses its markup but keeps its paragraphs");
        Check(filled.Genre=="комедия, фантастика, для взрослых"&&filled.Year==2020&&filled.ImageUrl==series.ImageUrl&&filled.Id==series.Id&&filled.ImdbId==series.ImdbId,"genres are shown in Russian and the card keeps its identity and poster");
        Check(filled.People.Length==2&&filled.People.All(x=>x.Role=="Актёры")&&filled.People[0].Name=="Justin Roiland","the cast lists each actor once");
        var bare=TvMazeMetadata.Apply(series with{ImageUrl=null,Year=0},maze);
        Check(bare.ImageUrl=="https://static.tvmaze.com/uploads/images/original_untouched/1/1.jpg"&&bare.Year==2020,"a missing poster and year come from TVmaze");
        var other=series with{ImdbId="tt0000001"};
        try{TvMazeMetadata.Apply(other,maze);Check(false,"a show with another IMDb id must be refused");}catch(InvalidDataException){Check(true,"a show with another IMDb id is refused");}
        // The site answers with TheTVDB text for a series and TMDB text for a film.
        using var site=JsonDocument.Parse("""{"item":{"imdbId":"tt8910922","source":"TheTVDB","section":"series","year":2020,"originalTitle":"Solar Opposites","overview":"Инопланетяне сбежали с планеты.","genres":["Animation","Comedy","Science Fiction"],"countries":["usa"],"actors":["Justin Roiland","Thomas Middleditch"],"directors":[]}}""");
        var described=TitleMetadata.Apply(series,site.RootElement.GetProperty("item"));
        Check(described.Description=="Инопланетяне сбежали с планеты."&&described.Genre=="мультфильм, комедия, фантастика"&&described.Country=="США"&&described.People.Length==2&&described.People.All(x=>x.Role=="Актёры")&&described.Id==series.Id,
            "TheTVDB text, Russian genres, country and cast fill an IMDb-only series card");
        using var filmRow=JsonDocument.Parse("""{"item":{"imdbId":"tt1160419","section":"movies","year":2021,"originalTitle":"Dune","overview":"Пол Атрейдес.","genres":["фантастика"],"countries":["США","Канада"],"actors":["Тимоти Шаламе"],"directors":["Дени Вильнёв"]}}""");
        var dune=new MediaItem(-9,"Dune","Фильмы","",2021,"—","—","#526B69"){PageUrl="https://www.imdb.com/title/tt1160419/",ImdbId="tt1160419",OriginalTitle="Dune"};
        var describedFilm=TitleMetadata.Apply(dune,filmRow.RootElement.GetProperty("item"));
        Check(describedFilm.Country=="США, Канада"&&describedFilm.People.Any(x=>x.Role=="Режиссёры"&&x.Name=="Дени Вильнёв")&&describedFilm.People.Any(x=>x.Role=="Актёры"),"TMDB text for a film keeps its Russian country names and lists director and cast");
        Check(TitleMetadata.Apply(series with{People=[new("Известный","Актёры","")],Genre="драма"},site.RootElement.GetProperty("item")) is {} kept&&kept.People.Length==1&&kept.Genre=="драма","credits and genres another source supplied are kept");
        try{TitleMetadata.Apply(series with{ImdbId="tt0000001"},site.RootElement.GetProperty("item"));Check(false,"an answer about another title must be refused");}catch(InvalidDataException){Check(true,"an answer about another title is refused");}
        try{TitleMetadata.Apply(dune with{Section="Сериалы"},filmRow.RootElement.GetProperty("item"));Check(false,"an answer of the other kind must be refused");}catch(InvalidDataException){Check(true,"an answer of the other kind is refused");}
        var handler=new SiteHandler(uri=>uri.AbsolutePath=="/api/title"
            ?new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("""{"item":{"imdbId":"tt8910922","section":"series","overview":"Текст от сайта."}}""",Encoding.UTF8,"application/json")}
            :new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client=new SourceClient(handler);
        var online=new OnlineIndexClient(client,new Uri("https://index.example.test/"));
        var fromSite=await online.Title(series,CancellationToken.None);
        Check(fromSite.Description=="Текст от сайта."&&handler.Requests.Single().Query=="?imdb=tt8910922&section=series","the card asks the site for its own IMDb id and section only");
        var offline=new SiteHandler(_=>new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var offlineClient=new SourceClient(offline);
        var rested=new OnlineIndexClient(offlineClient,new Uri("https://index.example.test/"));
        try{await rested.Title(series,CancellationToken.None);Check(false,"a source that is not connected must report a failure");}catch(HttpRequestException){Check(true,"a source that is not connected reports a failure");}
        try{await rested.BrowsePage("Фильмы",1,CancellationToken.None);}catch(HttpRequestException){}
        Check(offline.Requests.Count(x=>x.AbsolutePath=="/api/catalog")==1,"a failed description lookup does not make the site rest for the catalog");
        var zona=new MediaItem(-5,"Во все тяжкие","Сериалы","",2008,"—","—","#526B69"){OriginalTitle="Breaking Bad",PageUrl=LiveCatalog.Base+"/tvseries/vo-vse-tyazhkie-2008"};
        var imdbBreakingBad=ImdbSuggestions.Parse("""{"d":[{"id":"tt0903747","l":"Breaking Bad","q":"TV series","y":2008}]}""","breaking bad");
        Check(MediaIdentity.Merge(new[]{zona}.Concat(imdbBreakingBad)).Length==1&&MediaIdentity.Merge(new[]{zona}.Concat(found)).Length==3,"a title the catalog already has is not added twice");
        var merged=await UnifiedSearch.FindAsync("solar opposites",(_,_)=>Task.FromResult<IReadOnlyList<MediaItem>>([]),_=>[],CancellationToken.None,_=>Task.FromResult<IReadOnlyList<MediaItem>>(found));
        Check(merged.Items.Length==2&&merged.Items[0].Title=="Solar Opposites","search results include the IMDb titles the catalog lacks");
        var failing=await UnifiedSearch.FindAsync("breaking bad",(_,_)=>Task.FromResult<IReadOnlyList<MediaItem>>([zona]),_=>[],CancellationToken.None,_=>throw new HttpRequestException("offline"));
        Check(failing.Items.Length==1&&!failing.Offline,"an unreachable IMDb never hides catalog results");
        await Kachalka.Tests.MediaIdentityTests.Run();
    }
}
