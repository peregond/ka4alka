using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
namespace Kachalka;

public sealed record GameCatalogPage(MediaItem[] Items,bool HasNext);
public sealed record GameDetails(MediaItem Item,string Requirements,string Developers,string Publishers,string Price="",int Metacritic=0);
public sealed class GameCatalog(SourceClient client)
{
    public const int PageSize=40;
    // Steam tag 113 is "Free to Play": browsing never lists those games. A title search still finds any game by name.
    public const int FreeToPlayTag=113;
    public static Uri Query(string query,int page,bool newest)=>query.Trim().Length>0?
        new("https://store.steampowered.com/api/storesearch/?term="+Uri.EscapeDataString(query.Trim())+"&l=russian"):
        new("https://store.steampowered.com/search/results/?json=1&l=russian&category1=998&os=win&untags="+FreeToPlayTag+"&count=40&start="+((Math.Clamp(page,1,500)-1)*PageSize)+"&sort_by="+(newest?"Released_DESC":"_ASC")+(newest?"":"&filter=topsellers"));
    public static bool TryId(string? page,out int id)
    {
        id=0;if(!Uri.TryCreate(page,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="store.steampowered.com"||!uri.IsDefaultPort||uri.UserInfo.Length>0||uri.Query.Length>0||uri.Fragment.Length>0)return false;
        var match=Regex.Match(uri.AbsolutePath,@"^/app/(\d{1,9})/?$");return match.Success&&int.TryParse(match.Groups[1].Value,out id)&&id>0;
    }
    // Steam keeps a wide hero picture for most games; where it is missing the banner falls back to the blurred capsule.
    public static string? HeroImage(MediaItem game)=>TryId(game.PageUrl,out var id)?"https://cdn.akamai.steamstatic.com/steam/apps/"+id+"/library_hero.jpg":null;
    public static MediaItem Item(int id,string name,string? image=null)=>new(-id,name,"Игры","Windows",0,"—","—","#526B69")
        {PageUrl="https://store.steampowered.com/app/"+id+"/",OnlineId="steam:"+id,ImageUrl=image,OriginalTitle=name};
    static string? String(JsonElement row,string name)=>row.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
    static string? Image(string? url)=>Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.UserInfo.Length==0&&(uri.Host.EndsWith(".steamstatic.com",StringComparison.OrdinalIgnoreCase)||uri.Host.EndsWith(".steamusercontent.com",StringComparison.OrdinalIgnoreCase))?uri.AbsoluteUri:null;
    public static GameCatalogPage ParsePage(byte[] bytes)
    {
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
                if(id>0&&name is {Length:>0 and <=300})items.Add(Item(id,name,image));
            }
        }
        else if(String(root,"results_html") is {} html)
        {
            var doc=new HtmlDocument();doc.LoadHtml(html);
            foreach(var row in doc.DocumentNode.SelectNodes("//a[contains(@class,'search_result_row')]")??new HtmlNodeCollection(null))
            {
                count++;var raw=row.GetAttributeValue("data-ds-appid","");
                if(!int.TryParse(raw,out var id)||id<=0)continue;
                var name=HtmlEntity.DeEntitize(row.SelectSingleNode(".//*[contains(@class,'title')]")?.InnerText??"").Trim();
                if(name.Length>0)items.Add(Item(id,name,Image(row.SelectSingleNode(".//img")?.GetAttributeValue("src",""))));
            }
        }
        else throw new InvalidDataException("Steam вернул ответ без каталога игр.");
        var hasNext=root.TryGetProperty("total_count",out var total)&&total.TryGetInt32(out var totalCount)&&root.TryGetProperty("start",out var start)&&start.TryGetInt32(out var offset)?offset+PageSize<totalCount:count>=PageSize;
        return new(items.DistinctBy(x=>x.Id).Take(PageSize).ToArray(),root.TryGetProperty("total",out _)?false:hasNext);
    }
    public Task<GameCatalogPage> Browse(string query,int page,bool newest,CancellationToken ct)=>Task.Run(async()=>
    {
        var uri=Query(query,page,newest);var file=Path.Combine(Preferences.DataDir,"games",Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)))+".json");
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
    public static GameDetails ParseDetail(byte[] bytes,MediaItem item)
    {
        if(!TryId(item.PageUrl,out var id))throw new InvalidDataException("Неизвестный адрес игры.");
        using var json=JsonDocument.Parse(bytes);
        if(!json.RootElement.TryGetProperty(id.ToString(),out var root)||!root.TryGetProperty("success",out var success)||success.ValueKind!=JsonValueKind.True||!root.TryGetProperty("data",out var data))throw new InvalidDataException("Описание игры пока недоступно.");
        if(String(data,"type")!="game"||!data.TryGetProperty("platforms",out var platforms)||!platforms.TryGetProperty("windows",out var win)||win.ValueKind!=JsonValueKind.True)throw new InvalidDataException("Эта запись не является игрой для Windows.");
        string Join(string key)=>data.TryGetProperty(key,out var values)&&values.ValueKind==JsonValueKind.Array?string.Join(", ",values.EnumerateArray().Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>x.GetString())):"";
        var genre=data.TryGetProperty("genres",out var genres)&&genres.ValueKind==JsonValueKind.Array?string.Join(", ",genres.EnumerateArray().Select(x=>String(x,"description")).Where(x=>!string.IsNullOrWhiteSpace(x))):"Windows";
        var year=0;if(data.TryGetProperty("release_date",out var date))int.TryParse(Regex.Match(String(date,"date")??"",@"\b(19|20)\d{2}\b").Value,out year);
        var description=Plain(String(data,"short_description")??String(data,"about_the_game")??"");
        var requirements=data.TryGetProperty("pc_requirements",out var req)&&req.ValueKind==JsonValueKind.Object?Plain(String(req,"minimum")??""):"";
        var price=data.TryGetProperty("is_free",out var free)&&free.ValueKind==JsonValueKind.True?"Бесплатно":data.TryGetProperty("price_overview",out var overview)&&overview.ValueKind==JsonValueKind.Object?Plain(String(overview,"final_formatted")??""):"";
        var score=data.TryGetProperty("metacritic",out var metacritic)&&metacritic.ValueKind==JsonValueKind.Object&&metacritic.TryGetProperty("score",out var points)&&points.TryGetInt32(out var parsedScore)&&parsedScore is >0 and <=100?parsedScore:0;
        return new(item with{Title=String(data,"name")??item.Title,Genre=genre,Year=year,Description=description[..Math.Min(4000,description.Length)],ImageUrl=Image(String(data,"header_image"))??item.ImageUrl},requirements[..Math.Min(8000,requirements.Length)],Join("developers"),Join("publishers"),price[..Math.Min(40,price.Length)],score);
    }
    public Task<GameDetails> Detail(MediaItem item,CancellationToken ct)=>Task.Run(async()=>
    {
        if(!TryId(item.PageUrl,out var id))throw new InvalidDataException("Неизвестная игра.");
        var file=Path.Combine(Preferences.DataDir,"games","detail-"+id+".json");
        if(File.Exists(file)&&DateTime.UtcNow-File.GetLastWriteTimeUtc(file)<TimeSpan.FromDays(7))return ParseDetail(await CacheFiles.ReadAllBytesAsync(file,ct),item);
        try{var bytes=await client.Read(new Uri("https://store.steampowered.com/api/appdetails/?appids="+id+"&l=russian"),3*1024*1024,ct);var detail=ParseDetail(bytes,item);Directory.CreateDirectory(Path.GetDirectoryName(file)!);await CacheFiles.WriteAllBytesAsync(file,bytes,ct);return detail;}
        catch(Exception)when(!ct.IsCancellationRequested&&File.Exists(file)){return ParseDetail(await CacheFiles.ReadAllBytesAsync(file,ct),item);}
    },ct);
}
