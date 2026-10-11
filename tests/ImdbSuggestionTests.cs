using Kachalka;

// October 2026 report: "Solar Opposites" could not be found. The Russian catalog has no page for it
// and its search does not understand the English name; IMDb suggestions now fill that gap.
static class ImdbSuggestionTests
{
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
        Check(!ImdbSuggestions.Applies("Солнечные противоположности")&&ImdbSuggestions.Applies("solar opposites")&&!ImdbSuggestions.Applies("  "),"only Latin queries ask IMDb");
        var zona=new MediaItem(-5,"Во все тяжкие","Сериалы","",2008,"—","—","#526B69"){OriginalTitle="Breaking Bad",PageUrl=LiveCatalog.Base+"/tvseries/vo-vse-tyazhkie-2008"};
        var imdbBreakingBad=ImdbSuggestions.Parse("""{"d":[{"id":"tt0903747","l":"Breaking Bad","q":"TV series","y":2008}]}""","breaking bad");
        Check(ImdbSuggestions.Missing([zona],imdbBreakingBad).Length==0&&ImdbSuggestions.Missing([zona],found).Length==2,"a title the catalog already has is not added twice");
        var merged=await UnifiedSearch.FindAsync("solar opposites",(_,_)=>Task.FromResult<IReadOnlyList<MediaItem>>([]),_=>[],CancellationToken.None,_=>Task.FromResult<IReadOnlyList<MediaItem>>(found));
        Check(merged.Items.Length==2&&merged.Items[0].Title=="Solar Opposites","search results include the IMDb titles the catalog lacks");
        var failing=await UnifiedSearch.FindAsync("solar opposites",(_,_)=>Task.FromResult<IReadOnlyList<MediaItem>>([zona]),_=>[],CancellationToken.None,_=>throw new HttpRequestException("offline"));
        Check(failing.Items.Length==1&&!failing.Offline,"an unreachable IMDb never hides catalog results");
    }
}
