using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
namespace Kachalka;

public sealed record GameCatalogPage(MediaItem[] Items,bool HasNext);
public sealed record GameDetails(MediaItem Item,string Requirements,string Developers,string Publishers,string Price="",int Metacritic=0,string OriginalPrice="",int Discount=0,GameShot[]? Shots=null,GameRussian Russian=GameRussian.Unknown,string[]? Features=null,int Age=0,string ReleaseDate="",bool ComingSoon=false,GameReview? Review=null)
{
    public GameShot[] Gallery=>Shots??[];
    public string[] Traits=>Features??[];
}
public sealed class GameCatalog(SourceClient client,string language="ru")
{
    public const int PageSize=40;
    // Steam tag 113 is "Free to Play": browsing never lists those games. A title search still finds any game by name.
    public const int FreeToPlayTag=113;
    public static Uri Query(GameBrowse browse,string language="ru")
    {
        var steamLanguage=AppLanguage.Normalize(language)=="en"?"english":"russian";
        var text=browse.Query.Trim();
        if(text.Length>0)return new("https://store.steampowered.com/api/storesearch/?term="+Uri.EscapeDataString(text)+"&l="+steamLanguage);
        var tag=GameGenres.ByKey(browse.Genre)?.Tag;
        var sort=browse.Sort switch
        {
            GameBrowse.Specials=>"&specials=1&filter=topsellers",
            GameBrowse.New=>"&filter=popularnew",
            GameBrowse.Soon=>"&filter=popularwishlist",
            _=>"&sort_by=_ASC&filter=topsellers"
        };
        return new("https://store.steampowered.com/search/results/?query&l="+steamLanguage+"&infinite=1&category1=998&os=win&untags="+FreeToPlayTag+(tag!=null?"&tags="+tag:"")+"&count="+PageSize+"&start="+((Math.Clamp(browse.Page,1,500)-1)*PageSize)+sort);
    }
    public static bool TryId(string? page,out int id)
    {
        id=0;if(!Uri.TryCreate(page,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="store.steampowered.com"||!uri.IsDefaultPort||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0)return false;
        var match=Regex.Match(uri.AbsolutePath,@"^/app/(\d{1,9})/?$");return match.Success&&int.TryParse(match.Groups[1].Value,out id)&&id>0;
    }
    // Steam keeps a wide hero picture for most games; where it is missing the banner falls back to the blurred capsule.
    public static string? HeroImage(MediaItem game)=>TryId(game.PageUrl,out var id)?"https://cdn.akamai.steamstatic.com/steam/apps/"+id+"/library_hero.jpg":null;
    // Steam library artwork is a real 600×900 poster, independent of the store's landscape capsule.
    public static string? PosterImage(MediaItem game)=>TryId(game.PageUrl,out var id)?"https://cdn.akamai.steamstatic.com/steam/apps/"+id+"/library_600x900.jpg":game.ImageUrl;
    public static MediaItem Item(int id,string name,string? image=null)=>new(-id,name,"Игры","Windows",0,"—","—","#526B69")
        {PageUrl="https://store.steampowered.com/app/"+id+"/",OnlineId="steam:"+id,ImageUrl=image,OriginalTitle=name};
    static string? String(JsonElement row,string name)=>row.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
    static string? Image(string? url)=>Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.UserInfo.Length==0&&(uri.Host.EndsWith(".steamstatic.com",StringComparison.OrdinalIgnoreCase)||uri.Host.EndsWith(".steamusercontent.com",StringComparison.OrdinalIgnoreCase))?uri.AbsoluteUri:null;
    static string Clean(string? html)=>Regex.Replace(HtmlEntity.DeEntitize(html??""),@"\s+"," ").Trim();
    // The price, the date and the reviews of a search row. A row without a price (a game not yet on sale) has only a date.
    static (GameStoreInfo Store,int Year,int[] Tags) Row(HtmlNode row,DateTime today)
    {
        var released=Clean(row.SelectSingleNode(".//*[contains(@class,'search_released')]")?.InnerText);
        var (year,soon)=GameRelease.Parse(released,today);
        var final=row.SelectSingleNode(".//*[contains(@class,'discount_final_price')]");
        var free=final!=null&&final.GetAttributeValue("class","").Split(' ',StringSplitOptions.RemoveEmptyEntries).Contains("free");
        var price=free?"":Clean(final?.InnerText);var original=Clean(row.SelectSingleNode(".//*[contains(@class,'discount_original_price')]")?.InnerText);
        var discount=int.TryParse(row.SelectSingleNode(".//*[@data-discount]")?.GetAttributeValue("data-discount",""),out var percent)?Math.Clamp(percent,0,100):0;
        var review=row.SelectSingleNode(".//*[contains(@class,'search_review_summary')]");int reviewPercent=0,reviewCount=0;var tone="";var label="";
        if(review!=null)
        {
            var classes=review.GetAttributeValue("class","").Split(' ',StringSplitOptions.RemoveEmptyEntries);
            tone=classes.Contains("positive")?"positive":classes.Contains("mixed")?"mixed":classes.Contains("negative")?"negative":"";
            var parts=HtmlEntity.DeEntitize(review.GetAttributeValue("data-tooltip-html","")).Split("<br>",StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
            label=parts.Length>0&&!parts[0].Contains('%')?parts[0]:"";
            var match=Regex.Match(string.Join(" ",parts),@"(\d{1,3})%\s+из\s+([\d,. \s]+?)\s+обзор");
            if(!match.Success)match=Regex.Match(string.Join(" ",parts),@"(\d{1,3})%\s+of\s+(?:the\s+)?([\d,. \s]+?)\s+user reviews",RegexOptions.IgnoreCase);
            if(match.Success){reviewPercent=Math.Clamp(int.Parse(match.Groups[1].Value),0,100);reviewCount=GameNumbers.Digits(match.Groups[2].Value);}
        }
        var tags=Regex.Matches(row.GetAttributeValue("data-ds-tagids",""),@"\d+").Select(x=>int.TryParse(x.Value,out var tag)?tag:0).Where(x=>x>0).ToArray();
        return (new(price,discount>0?original:"",discount,free,released,soon,reviewPercent,reviewCount,tone,label),year,tags);
    }
    public static GameCatalogPage ParsePage(byte[] bytes,DateTime? now=null)
    {
        var today=(now??DateTime.UtcNow).Date;
        using var json=JsonDocument.Parse(bytes);var root=json.RootElement;
        var items=new List<MediaItem>();var count=0;
        if(root.TryGetProperty("items",out var rows)&&rows.ValueKind==JsonValueKind.Array)
        {
            count=rows.GetArrayLength();
            foreach(var row in rows.EnumerateArray().Take(PageSize))
            {
                if(row.ValueKind!=JsonValueKind.Object)continue;
                if(row.TryGetProperty("platforms",out var platforms)&&platforms.ValueKind==JsonValueKind.Object&&(!platforms.TryGetProperty("windows",out var windows)||windows.ValueKind!=JsonValueKind.True))continue;
                var name=String(row,"name");var image=Image(String(row,"logo")??String(row,"tiny_image"));var id=0;
                if(row.TryGetProperty("id",out var value)&&value.TryGetInt32(out var parsed))id=parsed;
                else if(image!=null)int.TryParse(Regex.Match(image,@"/apps/(\d{1,9})/").Groups[1].Value,out id);
                if(id>0&&name is {Length:>0 and <=300})items.Add(Item(id,name,image) with{Store=SearchPrice(row)});
            }
        }
        else if(String(root,"results_html") is {} html)
        {
            var doc=new HtmlDocument();doc.LoadHtml(html);
            foreach(var row in doc.DocumentNode.SelectNodes("//a[contains(@class,'search_result_row')]")??new HtmlNodeCollection(null))
            {
                count++;var raw=row.GetAttributeValue("data-ds-appid","");
                // A bundle lists several app ids ("1,2"): it is not one game.
                if(!int.TryParse(raw,out var id)||id<=0)continue;
                var name=HtmlEntity.DeEntitize(row.SelectSingleNode(".//*[contains(@class,'title')]")?.InnerText??"").Trim();
                if(name.Length==0)continue;
                var (store,year,tags)=Row(row,today);
                // Games that cost nothing never enter the catalogue, whether or not Steam tags them as free to play.
                if(store.Free)continue;
                var genre=GameGenres.Names(tags);
                items.Add(Item(id,name,RowImage(row)) with{Store=store,Year=year,Genre=genre.Length>0?genre:"Windows"});
            }
        }
        else throw new InvalidDataException("Steam вернул ответ без каталога игр.");
        var hasNext=root.TryGetProperty("total_count",out var total)&&total.TryGetInt32(out var totalCount)&&root.TryGetProperty("start",out var start)&&start.TryGetInt32(out var offset)?offset+PageSize<totalCount:count>=PageSize;
        return new(items.DistinctBy(x=>x.Id).Take(PageSize).ToArray(),root.TryGetProperty("total",out _)?false:hasNext);
    }
    // The small capsule of a search row exists for every game. Games whose artwork still sits in the plain folder also have the 460 by 215 header the cards are shaped for, at a fixed address.
    static string? RowImage(HtmlNode row)
    {
        var capsule=Image(row.SelectSingleNode(".//*[contains(@class,'search_capsule')]//img")?.GetAttributeValue("src","")??row.SelectSingleNode(".//img")?.GetAttributeValue("src",""));
        var plain=capsule==null?null:Regex.Match(capsule,@"^(https://[^/?#]+/store_item_assets/steam/apps/\d{1,9}/)capsule_231x87\.jpg(\?[^#]*)?$");
        return plain is {Success:true}?plain.Groups[1].Value+"header.jpg"+plain.Groups[2].Value:capsule;
    }
    // The title search answers with prices in cents; the same card chips are drawn from them.
    static GameStoreInfo? SearchPrice(JsonElement row)
    {
        if(!row.TryGetProperty("price",out var price)||price.ValueKind!=JsonValueKind.Object||!price.TryGetProperty("final",out var final)||!final.TryGetInt64(out var cents)||cents<=0)return null;
        var currency=String(price,"currency");var initial=price.TryGetProperty("initial",out var first)&&first.TryGetInt64(out var before)?before:cents;
        var discount=initial>cents?(int)Math.Round((initial-cents)*100d/initial):0;
        return new(GamePrice.Format(currency,cents),discount>0?GamePrice.Format(currency,initial):"",discount);
    }
    public Task<GameCatalogPage> Browse(GameBrowse browse,CancellationToken ct)=>Task.Run(async()=>
    {
        var uri=Query(browse,language);var file=Path.Combine(Preferences.DataDir,"games",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))+".json");
        if(File.Exists(file)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(file)<TimeSpan.FromMinutes(15))return ParsePage(await CacheFiles.ReadAllBytesAsync(file,ct));
        try{var bytes=await client.Read(uri,3*1024*1024,ct);var result=ParsePage(bytes);Directory.CreateDirectory(Path.GetDirectoryName(file)!);await CacheFiles.WriteAllBytesAsync(file,bytes,ct);return result;}
        catch(Exception)when(!ct.IsCancellationRequested&&File.Exists(file)){return ParsePage(await CacheFiles.ReadAllBytesAsync(file,ct));}
    },ct);
    public static string Plain(string html)
    {
        var doc=new HtmlDocument();doc.LoadHtml(Regex.Replace(html,@"(?i)<(?:br\s*/?|/p|/li|/h\d)>","\n"));
        foreach(var node in doc.DocumentNode.SelectNodes("//script|//style")??new HtmlNodeCollection(null))node.Remove();
        var text=HtmlEntity.DeEntitize(doc.DocumentNode.InnerText);return Regex.Replace(text,@"[ \t\r]+"," ").Trim();
    }
    // Store features worth a glance, by Steam's category id: single player, multiplayer, PvP, co-op, split screen, full controller support.
    static readonly int[] FeatureIds=[2,1,49,9,24,28];
    static GameRussian Russian(string? languages)
    {
        if(string.IsNullOrWhiteSpace(languages))return GameRussian.Unknown;
        var match=Regex.Match(languages,@"(?:русск\p{L}*|Russian)(<strong>\*</strong>)?",RegexOptions.IgnoreCase);
        return !match.Success?GameRussian.Absent:match.Groups[1].Success?GameRussian.Voiced:GameRussian.Interface;
    }
    public static GameDetails ParseDetail(byte[] bytes,MediaItem item)
    {
        if(!TryId(item.PageUrl,out var id))throw new InvalidDataException("Неизвестный адрес игры.");
        using var json=JsonDocument.Parse(bytes);
        if(!json.RootElement.TryGetProperty(id.ToString(),out var root)||!root.TryGetProperty("success",out var success)||success.ValueKind!=JsonValueKind.True||!root.TryGetProperty("data",out var data))throw new InvalidDataException("Описание игры пока недоступно.");
        if(String(data,"type")!="game"||!data.TryGetProperty("platforms",out var platforms)||!platforms.TryGetProperty("windows",out var win)||win.ValueKind!=JsonValueKind.True)throw new InvalidDataException("Эта запись не является игрой для Windows.");
        string Join(string key)=>data.TryGetProperty(key,out var values)&&values.ValueKind==JsonValueKind.Array?string.Join(", ",values.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString())):"";
        var genre=data.TryGetProperty("genres",out var genres)&&genres.ValueKind==JsonValueKind.Array?string.Join(", ",genres.EnumerateArray().Select(x=>String(x,"description")).Where(x=>!string.IsNullOrWhiteSpace(x))):"Windows";
        var year=0;var releaseDate="";var soon=false;
        if(data.TryGetProperty("release_date",out var date)&&date.ValueKind==JsonValueKind.Object)
        {
            releaseDate=Clean(String(date,"date"));int.TryParse(Regex.Match(releaseDate,@"\b(19|20)\d{2}\b").Value,out year);
            soon=date.TryGetProperty("coming_soon",out var coming)&&coming.ValueKind==JsonValueKind.True;
        }
        var description=Plain(String(data,"short_description")??String(data,"about_the_game")??"");
        var requirements=data.TryGetProperty("pc_requirements",out var req)&&req.ValueKind==JsonValueKind.Object?Plain(String(req,"minimum")??""):"";
        var price=data.TryGetProperty("is_free",out var free)&&free.ValueKind==JsonValueKind.True?"Бесплатно":data.TryGetProperty("price_overview",out var overview)&&overview.ValueKind==JsonValueKind.Object?Plain(String(overview,"final_formatted")??""):"";
        var original="";var discount=0;
        if(data.TryGetProperty("price_overview",out var prices)&&prices.ValueKind==JsonValueKind.Object&&prices.TryGetProperty("discount_percent",out var off)&&off.TryGetInt32(out var percent)&&percent is >0 and <=100){discount=percent;original=Plain(String(prices,"initial_formatted")??"");}
        var score=data.TryGetProperty("metacritic",out var metacritic)&&metacritic.ValueKind==JsonValueKind.Object&&metacritic.TryGetProperty("score",out var points)&&points.TryGetInt32(out var parsedScore)&&parsedScore is >0 and <=100?parsedScore:0;
        var shots=new List<GameShot>();
        if(data.TryGetProperty("screenshots",out var screens)&&screens.ValueKind==JsonValueKind.Array)
            foreach(var shot in screens.EnumerateArray().Take(12))
                if(shot.ValueKind==JsonValueKind.Object&&Image(String(shot,"path_thumbnail")) is {} thumb&&Image(String(shot,"path_full")) is {} full)shots.Add(new(thumb,full));
        var features=new List<string>();
        if(data.TryGetProperty("categories",out var categories)&&categories.ValueKind==JsonValueKind.Array)
            foreach(var category in categories.EnumerateArray())
                if(category.ValueKind==JsonValueKind.Object&&category.TryGetProperty("id",out var categoryId)&&categoryId.TryGetInt32(out var featureId)&&FeatureIds.Contains(featureId)&&String(category,"description") is {Length:>0 and <=60} featureName&&!features.Contains(featureName))features.Add(featureName);
        var age=0;if(data.TryGetProperty("required_age",out var required))age=required.ValueKind==JsonValueKind.Number&&required.TryGetInt32(out var ageNumber)?ageNumber:required.ValueKind==JsonValueKind.String&&int.TryParse(required.GetString(),out var ageText)?ageText:0;
        return new(item with{Title=String(data,"name")??item.Title,Genre=genre,Year=year,Description=description[..Math.Min(4000,description.Length)],ImageUrl=Image(String(data,"header_image"))??item.ImageUrl},requirements[..Math.Min(8000,requirements.Length)],Join("developers"),Join("publishers"),price[..Math.Min(40,price.Length)],score,
            original[..Math.Min(40,original.Length)],discount,shots.ToArray(),Russian(String(data,"supported_languages")),features.ToArray(),Math.Clamp(age,0,21),releaseDate[..Math.Min(40,releaseDate.Length)],soon);
    }
    // The review summary is its own small request; a game with fewer than ten reviews has none to show.
    public static GameReview? ParseReview(byte[] bytes)
    {
        using var json=JsonDocument.Parse(bytes);
        if(!json.RootElement.TryGetProperty("query_summary",out var summary)||summary.ValueKind!=JsonValueKind.Object)return null;
        int Number(string name)=>summary.TryGetProperty(name,out var value)&&value.TryGetInt32(out var number)?Math.Max(0,number):0;
        var positive=Number("total_positive");var negative=Number("total_negative");var total=positive+negative;
        return total<10?null:new((int)Math.Round(positive*100d/total),total,Math.Clamp(Number("review_score"),0,9));
    }
    async Task<GameReview?> Review(int id,CancellationToken ct)
    {
        var file=Path.Combine(Preferences.DataDir,"games","review-"+id+".json");
        try
        {
            if(File.Exists(file)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(file)<TimeSpan.FromDays(1))return ParseReview(await CacheFiles.ReadAllBytesAsync(file,ct));
            var bytes=await client.Read(new Uri("https://store.steampowered.com/appreviews/"+id+"?json=1&language=all&purchase_type=all&num_per_page=0"),256*1024,ct);
            var review=ParseReview(bytes);Directory.CreateDirectory(Path.GetDirectoryName(file)!);await CacheFiles.WriteAllBytesAsync(file,bytes,ct);return review;
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch(Exception)
        {
            // Reviews are a nicety: an old figure is better than none, and none is better than a failed page.
            try{return File.Exists(file)?ParseReview(await CacheFiles.ReadAllBytesAsync(file,ct)):null;}catch(Exception)when(!ct.IsCancellationRequested){return null;}
        }
    }
    public Task<GameDetails> Detail(MediaItem item,CancellationToken ct)=>Task.Run(async()=>
    {
        if(!TryId(item.PageUrl,out var id))throw new InvalidDataException("Неизвестная игра.");
        var reviews=Review(id,ct);
        var file=Path.Combine(Preferences.DataDir,"games","detail-"+AppLanguage.Normalize(language)+"-"+id+".json");
        GameDetails detail;
        if(File.Exists(file)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(file)<TimeSpan.FromDays(7))detail=ParseDetail(await CacheFiles.ReadAllBytesAsync(file,ct),item);
        else
        {
            try{var bytes=await client.Read(new Uri("https://store.steampowered.com/api/appdetails/?appids="+id+"&l="+(AppLanguage.Normalize(language)=="en"?"english":"russian")),3*1024*1024,ct);detail=ParseDetail(bytes,item);Directory.CreateDirectory(Path.GetDirectoryName(file)!);await CacheFiles.WriteAllBytesAsync(file,bytes,ct);}
            catch(Exception)when(!ct.IsCancellationRequested&&File.Exists(file)){detail=ParseDetail(await CacheFiles.ReadAllBytesAsync(file,ct),item);}
        }
        var review=await reviews;
        return review==null?detail:detail with{Review=review};
    },ct);
}
