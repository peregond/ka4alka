using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kachalka.Lan;
using MonoTorrent;

namespace Kachalka;

/// <summary>Shared phone catalog; all entry points run on the desktop download owner's context.</summary>
public sealed class PhoneRemoteCatalog(DownloadService downloads, Preferences prefs, Func<string, MediaItem?>? findCatalogItem = null) : IDisposable
{
    readonly SourceClient source = new();
    readonly Dictionary<string,MediaItem> cards = [];
    readonly Dictionary<string,(SourceEntry Entry,string Card)> releases = [];
    OnlineIndexClient? online;
    static string S(JsonElement p,string key) => p.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String ? value.GetString()??"" : "";
    static int Page(JsonElement p) => p.TryGetProperty("page",out var value)&&value.TryGetInt32(out var page) ? Math.Clamp(page,1,100) : 1;
    public async Task<object> Handle(string method,JsonElement p,CancellationToken ct)
    {
        online??=new(source,relays:new IndexRelays(source)); source.TorrentRelay=online.RelayTorrent;
        switch(method)
        {
            case "catalog":
                var section=S(p,"section");var query=S(p,"query").Trim();if(query.Length>200)throw new ArgumentException("Query too long.");
                if(section=="games")
                {
                    var games=await new GameCatalog(source,prefs.UiLanguage).Browse(new(query,"popular","",Page(p)),ct);
                    return new{items=Remember(games.Items),hasNext=games.HasNext};
                }
                var category=section=="series"?"Сериалы":"Фильмы";
                if(query.Length>0)
                {
                    await BundledCatalog.EnsureLoadedAsync(ct);
                    var matches=await UnifiedSearch.FindAsync(query,(kind,t)=>online.Browse(kind,query,1,t),kind=>BundledCatalog.Search(kind,query),ct,
                        ImdbSuggestions.Applies(query)?t=>ImdbSuggestions.SearchAsync(query,t):null,preferredLanguage:prefs.UiLanguage,
                        additional:(kind,t)=>new LiveCatalog(source).Browse(kind,query,1,t));
                    return new{items=Remember(matches.Items.Where(x=>x.Section==category)),hasNext=false};
                }
                var catalog=await online.BrowsePage(category,Page(p),ct);
                return new{items=Remember(catalog.Items),hasNext=catalog.HasNext};
            case "releases":
                var id=S(p,"id");var card=cards.GetValueOrDefault(id)??throw new ArgumentException("Unknown card.");
                card=MediaIdentity.Enrich(card);
                var providers=card.Section=="Игры"?GameReleaseSources.Create(source,card,[]):NativeReleaseSources.Create(source,_=>Task.FromResult(card),card.Section=="Сериалы",_=>Task.FromResult(card));
                var sources=providers.ToList();if(card.Cinema&&OnlineIndexClient.IdFor(card)!=null)sources.Insert(0,new("Онлайн-индекс",t=>online.Releases(card,t,true)));
                var found=await ReleaseSearch.RunAsync(sources,null,ct);
                var rows=found.Items.Where(x=>ReleaseAvailability.Downloadable(x)&&(!prefs.HidePoorQuality||card.Section=="Игры"||!ReleaseQuality.Poor(x,prefs.MinimumReleaseHeight))).ToArray();
                var recommendation=card.Section=="Игры"?GameReleaseRecommendation.Pick(rows,prefs.UiLanguage)?.Entry:ReleaseRecommendation.Pick(rows,prefs.MinimumReleaseHeight,prefs.UiLanguage)?.Entry;
                return new{items=SearchLanguage.AudioOrder(rows,prefs.UiLanguage).OrderByDescending(x=>x==recommendation).Take(250).Select(entry=>
                {
                    var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id+"|"+entry.Source+"|"+entry.Id)))[..24];
                    if(releases.Count>=2000)releases.Remove(releases.Keys.First());releases[key]=(entry,id);
                    return new{id=key,title=entry.Title,source=entry.Source,size=entry.Size,seeds=entry.Seeds,quality=entry.Quality,audio=entry.Voice,downloadable=true,recommended=entry==recommendation};
                }).ToArray()};
            case "add":
                if(!prefs.FolderConfigured||!Directory.Exists(prefs.Folder))throw new InvalidOperationException("Choose a download folder on your computer.");
                MediaItem? media=null;SourceEntry? release=null;string target;
                if(S(p,"release") is {Length:>0} releaseId)
                {
                    if(!releases.TryGetValue(releaseId,out var known)||known.Card!=S(p,"card"))throw new ArgumentException("Unknown release.");
                    release=known.Entry;media=cards.GetValueOrDefault(known.Card)??throw new ArgumentException("Unknown card.");
                    target=await source.TorrentFile(release,ct);
                }
                else
                {
                    target=S(p,"source");if(target.Length>32768||!target.StartsWith("magnet:?",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Only magnet links are allowed.");
                    MagnetLink.Parse(target);
                }
                ct.ThrowIfCancellationRequested();
                var hashes=target.StartsWith("magnet:",StringComparison.OrdinalIgnoreCase)?MagnetLink.Parse(target).InfoHashes:(await Torrent.LoadAsync(target)).InfoHashes;
                var hash=hashes.V1?.ToHex()??hashes.V2?.ToHex();
                var existing=downloads.Items.FirstOrDefault(x=>hash!=null&&string.Equals(x.InfoHash,hash,StringComparison.OrdinalIgnoreCase));
                if(existing!=null)return new{id=existing.Id}; // A retried phone request never restarts missing/deleted files.
                var added=await downloads.Add(target,prefs.Folder,media,media?.DisplayImageUrl,release);
                return new{id=added.Id};
            case "control":
                var task=downloads.Items.FirstOrDefault(x=>x.Id==S(p,"id"))??throw new ArgumentException("Unknown download.");
                var action=S(p,"action");
                if(task.Busy)throw new InvalidOperationException("Download busy.");
                switch(action)
                {
                    case "pause":await downloads.SetPausedAsync(task,true);break;
                    case "resume":await downloads.SetPausedAsync(task,false);break;
                    case "redownload":
                        if(!p.TryGetProperty("confirmed",out var confirmed)||confirmed.ValueKind!=JsonValueKind.True)throw new ArgumentException("Confirmation required.");
                        await downloads.CheckCompletedFilesAsync(true);
                        if(task.FilesMissing)await downloads.RedownloadAsync(task);
                        break;
                    default:throw new ArgumentException("Unknown download action.");
                }
                return new{id=task.Id};
            case "downloads":
                await downloads.CheckCompletedFilesAsync(true);
                downloads.Update();return new{items=downloads.Items.OrderByDescending(x=>x.AddedUtc).Take(200).Select(x=>new
                {
                    id=x.Id,title=x.DisplayName,status=AppLanguage.T(x.Status),statusKind=x.StatusKind.ToString().ToLowerInvariant(),
                    progress=double.IsFinite(x.Progress)?Math.Clamp(x.Progress,0,100):0,paused=x.Paused,filesMissing=x.FilesMissing,busy=x.Busy,complete=x.Completed,
                    poster=RemotePoster(x.ImageUrl),downloadRate=Math.Max(0,x.DownloadRate),uploadRate=Math.Max(0,x.UploadRate),totalBytes=x.TotalBytes
                }).ToArray()};
            case "files":
                var owner=downloads.Items.FirstOrDefault(x=>x.Id==S(p,"id"))??throw new ArgumentException("Unknown download.");
                return new{items=owner.Files.Select((file,index)=>new{index,name=file.Name,size=file.Size,complete=file.Progress>=100&&File.Exists(file.FullPath)}).Where(x=>VideoFile(x.name)).Take(500).ToArray()};
            case "saved":
                return SyncFavorites(p);
            default:throw new ArgumentException("Unknown phone operation.");
        }
    }
    static readonly string[] VideoExtensions=[".mkv",".mp4",".avi",".m4v",".mov",".ts",".m2ts",".webm",".wmv",".mpg",".mpeg"];
    static bool VideoFile(string name)=>VideoExtensions.Contains(Path.GetExtension(name),StringComparer.OrdinalIgnoreCase);
    static string ContentType(string name)=>Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".mp4" or ".m4v"=>"video/mp4",".mkv"=>"video/x-matroska",".webm"=>"video/webm",".avi"=>"video/x-msvideo",".mov"=>"video/quicktime",".ts" or ".m2ts"=>"video/mp2t",_=>"video/mpeg"
    };

    /// <summary>
    /// The file a paired device asked to play. Only a finished video inside the folder of that download qualifies: the index comes from
    /// the download's own file list, the path is checked against the download folder (no links, no escapes), and nothing else is served.
    /// </summary>
    public MediaFile? Media(string downloadId,int index)
    {
        var item=downloads.Items.FirstOrDefault(x=>x.Id==downloadId);
        if(item==null||index<0||index>=item.Files.Count)return null;
        var file=item.Files[index];
        if(!VideoFile(file.Name)||file.Progress<100||string.IsNullOrWhiteSpace(file.FullPath))return null;
        try{DownloadFiles.ValidatePath(item.Folder,file.FullPath);}catch(IOException){return null;}
        return File.Exists(file.FullPath)?new MediaFile(file.FullPath,ContentType(file.Name)):null;
    }

    object SyncFavorites(JsonElement p)
    {
        var now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string? Id(MediaItem item)=>OnlineIndexClient.IdFor(item);
        FavoriteSync.Reconcile(prefs.FavoriteMarks,prefs.LiveFavorites.Select(Id).OfType<string>().ToArray(),now);
        var incoming=new List<FavoriteMark>();
        if(p.TryGetProperty("marks",out var rows)&&rows.ValueKind==JsonValueKind.Array)
            foreach(var row in rows.EnumerateArray().Take(FavoriteSync.MaxMarks))
                if(row.ValueKind==JsonValueKind.Object&&S(row,"id") is {Length:>0} id&&row.TryGetProperty("at",out var at)&&at.TryGetInt64(out var time)&&row.TryGetProperty("removed",out var removed)&&removed.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    incoming.Add(new(id,time,removed.ValueKind==JsonValueKind.True));
        var (add,remove)=FavoriteSync.Merge(prefs.FavoriteMarks,incoming);
        foreach(var id in remove)
            foreach(var item in prefs.LiveFavorites.Where(x=>Id(x)==id).ToArray()){prefs.LiveFavorites.Remove(item);prefs.Favorites.Remove(item.Id);}
        foreach(var id in add)
        {
            if(prefs.LiveFavorites.Any(x=>Id(x)==id))continue;
            var found=findCatalogItem?.Invoke(id);
            // A title this computer's catalog does not know yet cannot be listed here; forget its mark so it is offered again at the next sync.
            if(found==null){prefs.FavoriteMarks.Remove(id);continue;}
            prefs.LiveFavorites.Add(found);prefs.Favorites.Add(found.Id);
        }
        prefs.Save();
        return new{marks=prefs.FavoriteMarks.Values.Select(x=>new{id=x.Id,at=x.At,removed=x.Removed}).ToArray()};
    }
    object[] Remember(IEnumerable<MediaItem> items)=>items.Where(x=>!HiddenCatalog.Hidden(x,prefs,CatalogChoices.Genres)).Take(60).Select(item=>
    {
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MediaIdentity.StableKey(item))))[..24];
        if(cards.Count>=1000)cards.Remove(cards.Keys.First());cards[key]=item;
        return (object)new
        {
            id=key,title=item.DisplayTitle,year=item.Year,poster=RemotePoster(item.DisplayImageUrl),
            kind=item.Section=="Игры"?"games":item.Section=="Сериалы"?"series":"movies",description=item.DisplayDescription,
            rating=item.HasCardRating?item.CardRating:null,ratingSource=item.HasCardRating?(item.KpAvailable?"КП":"IMDb"):null
        };
    }).ToArray();
    static string? RemotePoster(string? value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme==Uri.UriSchemeHttps&&uri.UserInfo.Length==0?value:null;
    public void Dispose()=>source.Dispose();
}
