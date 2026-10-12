using System.Net;
using System.Text;
using Kachalka;
static class GameCatalogTests
{
    sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>action(request,ct);}
    static byte[] Bytes(string text)=>Encoding.UTF8.GetBytes(text);
    public static async Task Run(string root)
    {
        void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
        var page=GameCatalog.ParsePage(Bytes("""
            {"items":[{"name":"Portal 2","logo":"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/620/abc/capsule_sm_120.jpg"},{"id":400,"name":"Portal","platforms":{"windows":true},"tiny_image":"https://cdn.akamai.steamstatic.com/steam/apps/400/header.jpg"},{"id":12,"name":"Mac only","platforms":{"windows":false}},{"name":"Invalid","logo":"https://other.test/apps/123/file.jpg"}]}
            """));
        Check(page.Items.Select(x=>x.OnlineId).SequenceEqual(["steam:620","steam:400"]),"Steam compact and search rows retain app identity and reject non-Windows or foreign image identities");
        Check(GameCatalog.Query("",2,false).Query.Contains("start=40")&&GameCatalog.Query("",2,false).Query.Contains("os=win")&&GameCatalog.Query("Portal & 2",1,false).AbsoluteUri.Contains("api/storesearch/")&&GameCatalog.Query("Portal & 2",1,false).Query.Contains("Portal%20%26%202"),"Windows game pagination and escaped title search use their verified Steam endpoints");
        Check(GameCatalog.TryId(page.Items[0].PageUrl,out var id)&&id==620&&!GameCatalog.TryId("https://store.steampowered.com.evil.test/app/620/",out _)&&!GameCatalog.TryId("https://store.steampowered.com/app/620/?x=1",out _),"game card and download identities accept only canonical Steam pages");
        var raw=Bytes("""
            {"620":{"success":true,"data":{"type":"game","name":"Portal 2","platforms":{"windows":true},"short_description":"<b>Головоломка</b><script>bad()</script>","header_image":"https://cdn.akamai.steamstatic.com/steam/apps/620/header.jpg","release_date":{"date":"18 апр. 2011 г."},"genres":[{"id":"25","description":"Приключения"}],"pc_requirements":{"minimum":"<strong>ОС:</strong> Windows<br>2 GB"},"developers":["Valve"],"publishers":["Valve"]}}}
            """);
        Check(GameCatalog.Query("",1,false).Query.Contains("untags=113")&&GameCatalog.Query("",1,true).Query.Contains("untags=113")&&!GameCatalog.Query("Portal",1,false).Query.Contains("untags"),"browsing leaves out Free to Play games while a title search still finds any game by name");
        Check(GameCatalog.HeroImage(page.Items[0])=="https://cdn.akamai.steamstatic.com/steam/apps/620/library_hero.jpg"&&GameCatalog.HeroImage(new MediaItem(1,"x","Игры","",0,"","","#000000"){PageUrl="https://example.test/app/620/"})==null,"the banner's wide picture is Steam's library hero of that exact app");
        var detail=GameCatalog.ParseDetail(raw,page.Items[0]);
        Check(detail.Item.Year==2011&&detail.Item.Description=="Головоломка"&&detail.Requirements.Contains("\n")&&detail.Developers=="Valve", "Steam details preserve localized release year and requirements without executable HTML");
        var priced=GameCatalog.ParseDetail(Bytes(Encoding.UTF8.GetString(raw).Replace("\"developers\"","\"price_overview\":{\"final_formatted\":\"399 руб.\"},\"metacritic\":{\"score\":95},\"developers\"")),page.Items[0]);
        var free=GameCatalog.ParseDetail(Bytes(Encoding.UTF8.GetString(raw).Replace("\"developers\"","\"is_free\":true,\"metacritic\":{\"score\":0},\"developers\"")),page.Items[0]);
        Check(priced.Price=="399 руб."&&priced.Metacritic==95&&detail.Price==""&&detail.Metacritic==0&&free.Price=="Бесплатно"&&free.Metacritic==0,"store details carry the price and the Metacritic score, and ignore a missing or empty score");
        var rejected=false;try{GameCatalog.ParseDetail(Bytes(Encoding.UTF8.GetString(raw).Replace("\"game\"","\"dlc\"")),page.Items[0]);}catch(InvalidDataException){rejected=true;}
        Check(rejected,"a DLC response cannot be treated as the requested base Windows game");
        SourceEntry Row(string title)=>new("fixture",title,"Fixture","","magnet:?xt=urn:btih:"+new string('a',40),null);
        Check(GameReleaseSources.Matches(detail.Item,Row("Portal 2 (2011) PC RePack"))&&!GameReleaseSources.Matches(detail.Item,Row("Portal 3 (2026) PC"))&&!GameReleaseSources.Matches(GameCatalog.Item(400,"Portal"),Row("Portal Knights (2017) PC")),"game release matching rejects numbered sequels and another title sharing a prefix");
        Check(!GameReleaseSources.Matches(detail.Item,Row("Portal 2 (2011) Xbox360"))&&!GameReleaseSources.Matches(detail.Item,Row("Portal 2 Update v1.0 PC")),"console and update-only packages stay out of Windows game offers");
        SourceEntry Release(string title,int? seeds=100,long size=5_000_000_000)=>new("id-"+title,title,"Fixture","","magnet:?xt=urn:btih:"+new string('b',40),null,size,seeds);
        Check(GameReleaseInfo.Kind(Release("Portal 2 (2011) PC RePack by xatab [RUS/ENG]"))=="Репак"&&GameReleaseInfo.Group(Release("Portal 2 (2011) PC RePack by xatab [RUS/ENG]"))=="xatab"&&GameReleaseInfo.Russian(Release("Portal 2 (2011) PC RePack by xatab [RUS/ENG]"))&&!GameReleaseInfo.Russian(Release("Portal 2 (2011) PC [ENG]"))&&GameReleaseInfo.Kind(Release("Portal 2 (2011) PC GOG"))=="Лицензия"&&GameReleaseInfo.Kind(Release("Portal 2 (2011) PC"))=="Игра","a release row names its kind, author and language from its own title");
        Check(GameReleaseInfo.Version(Release("Portal 2 v1.2.3 RePack"))=="v1.2.3"&&GameReleaseInfo.Version(Release("Portal 2 Build 12345"))=="Build 12345"&&GameReleaseInfo.Version(Release("Portal 2 (2011)"))==null&&GameReleaseInfo.Group(Release("Portal 2 [FitGirl Repack]"))=="FitGirl"&&GameReleaseInfo.Group(Release("Portal 2 RePack by SomeoneNew"))=="SomeoneNew"&&GameReleaseInfo.WithDlc(Release("Portal 2 + All DLCs")),"versions, repack authors and bundled add-ons are read from the title only");
        var plain=Release("Portal 2 (2011) PC",150);var russian=Release("Portal 2 (2011) PC RePack [RUS]",110);var rare=Release("Portal 2 (2011) PC [RUS]",60);
        Check(GameReleaseRecommendation.Pick([plain,rare,russian])?.Entry==russian&&GameReleaseRecommendation.Pick([plain,rare])?.Entry==plain&&GameReleaseRecommendation.Pick([Release("Portal 2 (2011) PC",5)])==null&&GameReleaseRecommendation.Pick([Release("Portal 2 (2011) PS4",500)])==null,"the recommended release is the best seeded one, a Russian-language release wins among the well seeded, and few seeders recommend nothing");
        Check(GameReleaseRecommendation.Pick([plain,russian])!.Reason.Contains("русский язык")&&GameReleaseRecommendation.Pick([plain])!.Reason.StartsWith("Больше всего отдающих"),"the reason names only facts that are in the release row");
        var card=DownloadMetadata.Card(new DownloadItem{MediaSection="Игры",MediaPageUrl=detail.Item.PageUrl,MediaTitle=detail.Item.Title,MediaYear=2011,ImageUrl=detail.Item.ImageUrl});
        Check(card?.OnlineId=="steam:620"&&card.Title==detail.Item.Title&&card.Year==2011,"queued games reopen the same Steam card with their title and poster");
        var calls=0;using var client=new SourceClient(new Handler((request,ct)=>{calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(raw)});}));
        await new GameCatalog(client).Detail(page.Items[0],CancellationToken.None);await new GameCatalog(client).Detail(page.Items[0],CancellationToken.None);
        Check(calls==1,"reopening a game reuses its persisted metadata rather than downloading it again");
        using var slow=new SourceClient(new Handler(async(_,ct)=>{await Task.Delay(Timeout.Infinite,ct);throw new Exception("Unreachable");}));using var cancel=new CancellationTokenSource(40);var stopped=false;
        try{await new GameCatalog(slow).Browse("Cancellation fixture",1,false,cancel.Token);}catch(OperationCanceledException){stopped=true;}
        Check(stopped,"leaving a game catalog cancels an unfinished HTTP request");
    }
}
