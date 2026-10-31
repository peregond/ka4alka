using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kachalka;
using Kachalka.Lan;
using Kachalka.MacUpdates;
using Kachalka.Updates;

sealed class DesktopEngine:IAsyncDisposable
{
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    readonly SourceClient source=new();readonly Preferences prefs=Preferences.Load();
    readonly ReleaseAudioRulesClient releaseAudioRules;
    readonly OnlineIndexClient online;readonly SharedCatalog shared;readonly AwardsCatalog awards;
    readonly CatalogIndex index=new(Preferences.DataDir,deferRestore:true);
    readonly DownloadService downloads;readonly CancellationTokenSource lifetime=new();
    readonly Dictionary<string,MediaItem> cards=[];readonly Dictionary<string,SourceEntry> releases=[];
    readonly Dictionary<string,CancellationTokenSource> requests=[];
    readonly Dictionary<string,FeaturedGroup> groups=[];
    readonly List<SourceConfig> configuredSources=[];
    readonly Dictionary<Guid,TaskCompletionSource<bool>> pairing=[];
    readonly List<Task> running=[];
    readonly SharedAsyncWork<EnglishMedia?> englishWork=new();
    readonly SharedAsyncWork<MediaMetadataResult> detailWork=new();
    readonly SemaphoreSlim metadataSlots;
    readonly Dictionary<string,DateTime> localizedAttempts=[];
    PhoneRemoteServer? phone;PhoneRemoteCatalog? phoneCatalog;
    LanService? lan;Task? ticker;Task? warming;Task? reliability;bool? lastNetwork;string? lastDownloadEvent;bool shuttingDown;
    public DesktopEngine()
    {
        AppLanguage.Set(prefs.UiLanguage);prefs.LiteMode=PerformanceBudget.DefaultLiteMode(prefs.LiteModeConfigured,prefs.LiteMode);PerformanceBudget.LiteMode=prefs.LiteMode;metadataSlots=new(PerformanceBudget.Constrained?1:3);
        releaseAudioRules=new(source);
        online=new(source,relays:new IndexRelays(source));source.TorrentRelay=online.RelayTorrent;
        shared=new(source);awards=new(source);
        downloads=new(null,prefs.MaxDownloadKbps,prefs.MaxUploadKbps);
        var sourcePath=Path.Combine(Preferences.DataDir,"sources.json");
        try{if(File.Exists(sourcePath)&&new FileInfo(sourcePath).Length<256*1024)configuredSources.AddRange(JsonSerializer.Deserialize<SourceConfig[]>(File.ReadAllText(sourcePath),Json)??[]);}catch(Exception error){ErrorLog.Write(error);}
        downloads.Notice+=notice=>Event("notice",new{kind=notice.Kind.ToString(),name=notice.Name,message=AppLanguage.T(notice.Message),downloadId=notice.DownloadId});
    }
    public async Task Run()
    {
        ticker=Tick();
        string? line;
        while(!shuttingDown&&(line=await Task.Run(Console.ReadLine))!=null)
        {
            if(line.Length>2*1024*1024){Event("error",new{message="Request exceeds the IPC limit."});continue;}
            try
            {
                using var parsed=JsonDocument.Parse(line);var root=parsed.RootElement;
                var id=root.GetProperty("id").GetString()!;var method=root.GetProperty("method").GetString()!;
                var args=root.TryGetProperty("params",out var parameters)?parameters.Clone():JsonSerializer.SerializeToElement(new{});
                if(method=="cancel"){if(requests.TryGetValue(S(args,"id"),out var pending))pending.Cancel();Reply(id,new{});continue;}
                var request=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);request.CancelAfter(method=="prepareUpdate"?TimeSpan.FromMinutes(20):TimeSpan.FromSeconds(90));requests[id]=request;
                var task=Dispatch(id,method,args,request);running.RemoveAll(task=>task.IsCompleted);running.Add(task);
            }
            catch(Exception error){Event("error",new{message=error.Message});}
        }
    }
    async Task Dispatch(string id,string method,JsonElement args,CancellationTokenSource request)
    {
        try{var result=await Handle(id,method,args,request.Token);if(!shuttingDown||method=="shutdown")Reply(id,result);}
        catch(OperationCanceledException){if(!shuttingDown)Write(new{id,error=new{code="cancelled",message="Cancelled"}});}
        catch(Exception error){ErrorLog.Write(error);if(!shuttingDown)Write(new{id,error=new{code="failed",message=AppLanguage.T(error.Message)}});}
        finally{requests.Remove(id);request.Dispose();}
    }
    async Task<object> Handle(string request,string method,JsonElement p,CancellationToken ct)
    {
        switch(method)
        {
            case "bootstrap":
                warming??=Warm();return new{protocol=1,version=typeof(DesktopEngine).Assembly.GetName().Version?.ToString(3),settings=Settings(),genres=CatalogChoices.Genres.Select(Choice),countries=CatalogChoices.Countries.Select(Choice),gameGenres=GameGenres.All.Select(x=>new{key=x.Key,label=AppLanguage.T(x.Name)}),engineCreated=downloads.EngineCreated};
            case "gameShelves":return new{items=GameShelves.Home.Select(spec=>new{id=spec.Key,title=AppLanguage.T(spec.Title)})};
            case "gameShelf":
                var shelf=GameShelves.Home.Single(x=>x.Key==S(p,"id"));
                var shelfPage=await new GameCatalog(source,prefs.UiLanguage).Browse(new("",shelf.Sort,shelf.Genre,Math.Clamp(I(p,"page",1),1,500)),ct);
                return new{items=Remember(shelfPage.Items.Where(Visible)),hasNext=shelfPage.HasNext,title=AppLanguage.T(shelf.Title)};
            case "catalog":return await Catalog(p,ct);
            case "featured":return await Featured(p,ct);
            case "coverFallback":
                var coverUrl=S(p,"url");var proxy=CoverSource.ProxyPath(coverUrl)??throw new ArgumentException("No trusted image relay for this address.");
                var coverPath=Path.Combine(Preferences.DataDir,"covers",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(coverUrl)))+".img");
                await CoverCache.Load(coverPath,2*1024*1024,token=>online.ReadSite(proxy,2*1024*1024,token),bytes=>bytes.Length,ct);
                return new{path=coverPath};
            case "artwork":
                var artCard=Card(S(p,"id"));var artId=FeatureBackdrop.IdentityFor(artCard);
                if(artCard.Section=="Игры")return new{url=GameCatalog.HeroImage(artCard)};
                return new{url=artId==null?null:FeatureBackdrop.ReadResponse(await online.ReadSite(FeatureBackdrop.RequestPath(artId,prefs.UiLanguage),128*1024,ct),artId)};
            case "localizedCard":
                var localized=Card(S(p,"id"));await Localize(localized,ct);return CardDto(localized);
            case "franchise":
                var franchiseCard=Card(S(p,"id"));var franchise=await MovieFranchiseCatalog.Load(franchiseCard,online,prefs.UiLanguage,ct);
                return new{name=prefs.UiLanguage=="en"?franchise?.EnglishName??franchise?.Name??"":franchise?.Name??"",items=Remember(MovieFranchiseCatalog.OtherParts(franchise,franchiseCard,prefs.UiLanguage).Where(Visible))};
            case "detail":return await Detail(Card(S(p,"id")),ct);
            case "cast":
                var castCard=await CharacterCredits.Load(online,Card(S(p,"id")),prefs.UiLanguage,ct);
                Remember([castCard]);return castCard.DisplayPeople;
            case "releases":return await Releases(request,Card(S(p,"id")),ct);
            case "person":
                var person=p.GetProperty("person").Deserialize<CinemaPerson>(Json)!;
                var profile=await new CinemaPeople(source).Load(person,null,shared.All,ct);
                return new{person=profile.Person,description=profile.Description,items=Remember(profile.Filmography),sourceUrl=profile.SourceUrl};
            case "awards":
                await awards.RestoreAsync(ct);await awards.RefreshNomineesAsync(ct);
                var award=awards.All.FirstOrDefault(x=>x.Id==S(p,"award"));
                var entries=(award?.Entries??[]).Concat(awards.NomineesOf(S(p,"award"))).Where(x=>I(p,"year")==0||x.Year==I(p,"year")).DistinctBy(x=>(x.Year,x.ImdbId)).ToArray();
                return new{years=(award?.Entries??[]).Concat(awards.NomineesOf(S(p,"award"))).Select(x=>x.Year).Distinct().OrderDescending(),items=Remember(entries.Select(entry=>AwardsCatalog.Item(entry)).Where(Visible).ToArray()),winners=Remember((award?.Entries??[]).Where(x=>I(p,"year")==0||x.Year==I(p,"year")).Select(entry=>AwardsCatalog.Item(entry)).Where(Visible)),nominees=Remember(awards.NomineesOf(S(p,"award")).Where(x=>(I(p,"year")==0||x.Year==I(p,"year"))&&!(award?.Entries??[]).Any(winner=>winner.Year==x.Year&&winner.ImdbId==x.ImdbId)).Select(entry=>AwardsCatalog.Item(entry)).Where(Visible))};
            case "share":
                var sharedCard=Card(S(p,"id"));return new{url=ShareLink.Create(sharedCard)};
            case "openLink":
                var targetCard=ShareLink.Parse(S(p,"url"))??throw new ArgumentException("Invalid Ka4alka link.");
                var linked=DownloadMetadata.Card(new(){MediaTitle=targetCard.Title.Length>0?targetCard.Title:targetCard.Slug,MediaSection=targetCard.Section,MediaPageUrl=targetCard.PageUrl,MediaYear=targetCard.Year})??throw new ArgumentException("Invalid shared card.");
                var enriched=DownloadMetadata.EnrichMedia(linked,new[]{shared.Find(linked)}.OfType<MediaItem>().Concat(prefs.LiveFavorites).Concat(index.Recent(linked.Section,200)).Concat(BundledCatalog.Search(linked.Section,linked.Title)));
                return Remember([enriched])[0];
            case "cacheUsage":return await Task.Run(()=>CacheFiles.Current.UsageAsync(ct),ct);
            case "clearCache":
                var usage=await Task.Run(()=>CacheFiles.Current.MaintainAsync(clear:true,ct:ct),ct);
                index.ClearReleaseMemory();await index.ClearMemoryAsync();await shared.ClearMemoryAsync();groups.Clear();return usage;
            case "saved":return new{items=Remember(prefs.LiveFavorites),hasNext=false};
            case "save":
                var saved=Card(S(p,"id"));if(IsSaved(saved)){prefs.LiveFavorites.RemoveAll(item=>MediaIdentity.Same(item,saved));prefs.Favorites.Remove(saved.Id);}else{prefs.LiveFavorites.Add(saved);prefs.Favorites.Add(saved.Id);}prefs.Save();Event("personal",Settings());return CardDto(saved);
            case "hide":
                var hidden=Card(S(p,"id"));if(!HiddenCatalog.TitleHidden(hidden,prefs.HiddenTitles))prefs.HiddenTitles.Add(HiddenCatalog.Title(hidden));prefs.Save();Event("personal",Settings());return new{};
            case "unhide":prefs.HiddenTitles.RemoveAll(x=>x.Identity==S(p,"id"));prefs.Save();return Settings();
            case "settings":return Settings();
            case "setSettings":return await SetSettings(p,ct);
            case "folder":prefs.Folder=DownloadFolders.Prepare(S(p,"path"));prefs.FolderConfigured=true;prefs.Save();return Settings();
            case "downloads":downloads.Update();return Downloads();
            case "add":
                if(!prefs.FolderConfigured)throw new InvalidOperationException("Сначала выбери папку загрузок.");
                var card=p.TryGetProperty("card",out var cardId)&&cardId.ValueKind==JsonValueKind.String?Card(cardId.GetString()!):null;
                SourceEntry? release=releases.GetValueOrDefault(S(p,"release"));
                var target=release!=null?await source.TorrentFile(release,ct):S(p,"source");
                if(target.Length==0)throw new ArgumentException("Укажи magnet-ссылку или торрент-файл.");
                var download=await downloads.Add(target,prefs.Folder,card,card?.DisplayImageUrl,release);Event("downloads",Downloads());return new{id=download.Id};
            case "toggle":await downloads.Toggle(Download(S(p,"id")));return Downloads();
            case "remove":await downloads.Remove(Download(S(p,"id")),B(p,"deleteFiles"));return Downloads();
            case "redownload":await downloads.RedownloadAsync(Download(S(p,"id")));return Downloads();
            case "retry":await downloads.RetryConnectionsAsync(Download(S(p,"id")),ct);return Downloads();
            case "diagnostics":
                var snapshot=downloads.GetDiagnostics(Download(S(p,"id")));return new{snapshot,diagnosis=DownloadDiagnostics.Explain(snapshot)};
            case "files":return new{files=Download(S(p,"id")).Files};
            case "batch":await downloads.SetGroupAsync(B(p,"completed"),B(p,"paused"));return Downloads();
            case "refreshTorrent":
                var existing=Download(S(p,"id"));var media=DownloadMetadata.Card(existing)??throw new InvalidOperationException("No catalog identity for this download.");
                var entry=new SourceEntry(existing.ReleaseId??"",existing.ReleaseTitle??existing.Name,existing.ReleaseSource??"",existing.ReleasePageUrl??"",existing.ReleaseUrl,null);
                if(entry.Source is "RuTor" or "NNM-Club" or "MegaPeer" or "BigFanGroup")entry=await RefreshSourceEntry(entry,media,ct);
                var replaced=await downloads.ReplaceTorrentAsync(existing,await source.TorrentFile(entry,ct));return new{items=DownloadItems(),updated=replaced==DownloadService.TorrentUpdate.Updated};
            case "sources":return new{items=configuredSources.Select(x=>new{x.Name,x.Endpoint,hasKey=x.ApiKey.Length>0})};
            case "sourceAdd":
                var endpoint=SourceClient.WebUri(S(p,"endpoint")).AbsoluteUri;var name=S(p,"name").Trim();
                if(name.Length is 0 or >100||configuredSources.Count>=20)throw new ArgumentException("Invalid source name or limit exceeded.");
                configuredSources.RemoveAll(x=>x.Name==name);configuredSources.Add(new(name,endpoint,S(p,"apiKey")));SaveSources();return new{};
            case "sourceRemove":configuredSources.RemoveAll(x=>x.Name==S(p,"name"));SaveSources();return new{};
            case "logs":return new{text=await DiagnosticReport.CreateAsync(prefs,downloads,ct)};
            case "refresh":online.RetryNow();await shared.RefreshAsync(ct,true);groups.Clear();return new{};
            case "phone":return await Phone(p,ct);
            case "lan":return await Lan(p,ct);
            case "prepareUpdate":
                using(var updater=new MacUpdateClient(UpdateTrust.PublicKey))
                {
                    var destination=Path.Combine(Preferences.DataDir,"updates",Guid.NewGuid().ToString("N"));
                    return await Task.Run(()=>updater.PrepareAsync(Convert.FromBase64String(S(p,"manifest")),Convert.FromBase64String(S(p,"signature")),S(p,"architecture"),S(p,"installed"),destination,new Progress<MacUpdateProgress>(value=>Event("updateProgress",value)),ct),ct);
                }
            case "shutdown":shuttingDown=true;if(phone!=null){await phone.DisposeAsync();phone=null;}phoneCatalog?.Dispose();phoneCatalog=null;lifetime.Cancel();foreach(var pending in requests.Values.ToArray())pending.Cancel();await downloads.Close();return new{};
            default:throw new ArgumentException("Unknown desktop command: "+method);
        }
    }
    async Task Warm()
    {
        try
        {
            await BundledCatalog.EnsureLoadedAsync(lifetime.Token);await shared.RestoreAsync(lifetime.Token);await index.RestoreAsync(lifetime.Token);
            groups.Clear();Event("catalogReady",new{});
            if(prefs.AutoResumeDownloads)await downloads.ResumePendingAsync();
            if(prefs.LanEnabled)await EnsureLan(lifetime.Token);
            if(await shared.RefreshAsync(lifetime.Token)){groups.Clear();Event("catalogReady",new{});}
        }
        catch(OperationCanceledException){}catch(Exception error){ErrorLog.Write(error);}
    }
    async Task<object> Catalog(JsonElement p,CancellationToken ct)
    {
        var section=S(p,"section","movies");var page=Math.Clamp(I(p,"page",1),1,500);var query=S(p,"query");
        if(section=="games"&&query.Length==0)
        {
            var result=await new GameCatalog(source,prefs.UiLanguage).Browse(new(query,S(p,"sort","popular"),S(p,"genre")=="all"?"":S(p,"genre"),page),ct);
            return new{items=Remember(result.Items.Where(Visible)),hasNext=result.HasNext,offline=false};
        }
        await BundledCatalog.EnsureLoadedAsync(ct);var category=section is "series" or "shows"?"Сериалы":"Фильмы";
        var selection=new CatalogSelection(S(p,"genre"),S(p,"country"),I(p,"year")==0?null:I(p,"year"),I(p,"rating"),S(p,"sort","date"));
        var qualityHeight=S(p,"quality") switch{"HD Ready"=>720,"Full HD"=>1080,"4K"=>2160,_=>0};
        if(section=="shows"&&query.Length==0)
        {
            // The source offers no "TV show" listing, so the page is built from the show genres of the series catalogue and always comes from the live source.
            var scope=S(p,"scope");
            var shows=selection with{Collection=scope=="popular"?"popular":"all"};
            var browsed=scope is "native" or "foreign"
                ?await new RegionalCatalog(source).BrowsePage(CatalogSections.Shows,page,scope,prefs.HomeCountry,ct,shows)
                :await new LiveCatalog(source).BrowsePage(CatalogSections.Shows,page,shows,ct);
            var listed=await Task.Run(()=>browsed.Items.Where(Visible).Where(item=>qualityHeight==0||QualityMatches(item,qualityHeight)).ToArray(),ct);
            return new{items=Remember(listed),hasNext=browsed.HasNext,offline=false};
        }
        if(query.Length>0)
        {
            var result=await UnifiedSearch.FindAsync(query,(kind,t)=>online.Browse(kind,query,1,t),kind=>shared.Search(kind,query),ct,
                ImdbSuggestions.Applies(query)?t=>ImdbSuggestions.SearchAsync(query,t):null,preferredLanguage:prefs.UiLanguage,
                games:async t=>(await new GameCatalog(source,prefs.UiLanguage).Browse(new(query,GameBrowse.Popular,"",1),t)).Items,
                additional:(kind,t)=>new LiveCatalog(source).Browse(kind,query,1,t));
            var kind=S(p,"kind") switch{"games"=>"Игры","series"=>"Сериалы","movies"=>"Фильмы",_=>""};
            var filtered=result.Items.Where(Visible).Where(selection.Matches).Where(item=>kind.Length==0||item.Section==kind).Where(item=>qualityHeight==0||QualityMatches(item,qualityHeight)).ToArray();
            return new{items=Remember(filtered),hasNext=false,offline=result.Offline};
        }
        var candidates=await Task.Run(()=>shared.All.Where(item=>item.Section==category&&Visible(item)&&selection.Matches(item)).ToArray(),ct);
        var local=qualityHeight==0?candidates:await Task.Run(()=>candidates.Where(item=>QualityMatches(item,qualityHeight)).ToArray(),ct);
        if(candidates.Length>0)
        {
            IEnumerable<MediaItem> rows=local;rows=selection.Order=="rating"?rows.OrderByDescending(item=>Rating(item)):selection.Order=="date"?rows.OrderByDescending(item=>item.Year):rows;
            var offset=(page-1)*CatalogPaging.Size;var data=rows.Skip(offset).Take(CatalogPaging.Size).ToArray();return new{items=Remember(data),hasNext=offset+data.Length<local.Length,offline=false};
        }
        var remote=await online.BrowsePage(category,page,ct);return new{items=Remember(remote.Items.Where(Visible)),hasNext=remote.HasNext,offline=false};
    }
    static double Rating(MediaItem item)=>double.TryParse(item.CardRating.Replace(',','.'),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out var value)?value:0;
    bool Visible(MediaItem item)
    {
        var known=index.RememberedReleases(item);
        return !HiddenCatalog.Hidden(item,prefs,CatalogChoices.Genres)&&!ReleaseAvailability.Hidden(known,DateTime.UtcNow)&&(!prefs.HidePoorQuality||known==null||!ReleaseQuality.OnlyPoor(known.Items,prefs.MinimumReleaseHeight));
    }
    bool QualityMatches(MediaItem item,int height)
    {
        if(height==0)return true;
        ApplyKnownQuality(item);return item.HasReleaseResolution(height);
    }
    void ApplyKnownQuality(MediaItem item)=>item.SetReleaseQuality(index.RememberedReleases(item)?.Items??[],prefs.MinimumReleaseHeight);
    async Task<MediaItem> Metadata(MediaItem item,CancellationToken ct)=>
        (await detailWork.Run(Key(item),token=>MediaMetadata.Load(item,t=>ImdbSuggestions.IsImdbOnly(item)?online.Title(item,t):online.Detail(item,t),t=>new LiveCatalog(source).Detail(item,t),ct:token),ct)).Item;
    async Task<object> Featured(JsonElement p,CancellationToken ct)
    {
        await BundledCatalog.EnsureLoadedAsync(ct);await awards.RestoreAsync(ct);
        if(groups.Count==0)
        {
            var all=await Task.Run(()=>FeaturedCatalog.Resolve(shared.All,shared.ActiveCollections,awards.All),ct);
            foreach(var group in all)groups[group.Collection.Id]=group;
        }
        if(S(p,"id").Length>0)
        {
            if(S(p,"id").StartsWith("steam-shelf:",StringComparison.Ordinal))
            {
                var spec=GameShelves.Home.Single(x=>x.Key==S(p,"id")[12..]);
                var result=await new GameCatalog(source,prefs.UiLanguage).Browse(new("",spec.Sort,spec.Genre,Math.Clamp(I(p,"page",1),1,500)),ct);
                return new{title=AppLanguage.T(spec.Title),description="",items=Remember(result.Items.Where(Visible)),hasNext=result.HasNext};
            }
            var group=groups[S(p,"id")];return new{title=group.Collection.Label,description=group.Collection.Summary,items=Remember(group.Items.Where(Visible)),hasNext=false};
        }
        return new{groups=groups.Values.Select(group=>new{id=group.Collection.Id,title=group.Collection.Label,description=group.Collection.Summary,layout=group.Collection.Layout,items=Remember(group.Items.Where(Visible).Take(10)),count=group.Items.Length}).Where(group=>group.items.Length>0)};
    }
    async Task<object> Detail(MediaItem item,CancellationToken ct)
    {
        string? backdrop=null;GameDetails? game=null;
        if(item.Section=="Игры"){game=await new GameCatalog(source,prefs.UiLanguage).Detail(item,ct);item=game.Item;backdrop=GameCatalog.HeroImage(item);}
        else
        {
            item=await Metadata(item,ct);
            if(prefs.UiLanguage=="en")
            {
                await Localize(item,ct,requireCredits:true);backdrop=item.English?.Backdrop;
            }
            if(backdrop==null)try{var id=FeatureBackdrop.IdentityFor(item);if(id!=null)backdrop=FeatureBackdrop.ReadResponse(await online.ReadSite(FeatureBackdrop.RequestPath(id,prefs.UiLanguage),128*1024,ct),id);}catch(Exception)when(!ct.IsCancellationRequested){}
        }
        Remember([item]);return new{item=CardDto(item),backdrop,people=item.DisplayPeople,awards=item.Awards,originalTitle=item.OriginalTitle,genres=item.AllGenres,country=item.DisplayCountry,resolutions=item.ReleaseResolutions.OrderByDescending(x=>x).ToArray(),game=game==null?null:new{game.Requirements,game.Developers,game.Publishers,game.Price,game.Metacritic,gallery=game.Gallery,game.Traits,game.OriginalPrice,game.Discount,game.ReleaseDate,game.Age,game.Review,russian=game.Russian.ToString(),steamUrl=item.PageUrl,steamDbUrl=GameCatalog.TryId(item.PageUrl,out var steamId)?"https://steamdb.info/app/"+steamId:null,platform="Windows"}};
    }
    async Task Localize(MediaItem item,CancellationToken ct,bool requireCredits=false)
    {
        if(prefs.UiLanguage!="en"||!item.Cinema||!EnglishMedia.NeedsLookup(item.English,requireCredits))return;
        var key=Key(item);
        if(localizedAttempts.TryGetValue(key,out var attempt)&&attempt>DateTime.UtcNow.AddMinutes(-5))return;
        try
        {
            var english=await englishWork.Run(key,async token=>
            {
                await metadataSlots.WaitAsync(token);
                try{return await EnglishMedia.LoadAsync(online,item,false,token);}
                finally{metadataSlots.Release();}
            },ct);
            if(localizedAttempts.Count>1000)localizedAttempts.Remove(localizedAttempts.Keys.First());
            localizedAttempts[key]=DateTime.UtcNow;item.SetEnglish(english);
        }
        catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch(Exception error){ErrorLog.Write(error);localizedAttempts[key]=DateTime.UtcNow;}
    }
    async Task<object> Releases(string request,MediaItem item,CancellationToken ct)
    {
        var cardKey=Key(item);
        item=MediaIdentity.Enrich(item);
        var cached=await Task.Run(()=>index.CachedReleaseSnapshot(item),ct);
        var saved=cached?.Items??[];
        // Russian sources start immediately. As in WPF, only sources needing
        // the original title briefly join the already running metadata lookup.
        var metadata=item.Cinema&&string.IsNullOrWhiteSpace(item.OriginalTitle)&&item.Title.Any(c=>c is >= '\u0400' and <= '\u04FF')?Metadata(item,ct):null;
        Task<MediaItem> Resolved(CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(metadata?.IsCompletedSuccessfully==true?metadata.Result:item);}
        async Task<MediaItem> Aliases(CancellationToken token)
        {
            if(metadata==null)return item;
            try{return await metadata.WaitAsync(TimeSpan.FromSeconds(8),token);}
            catch(TimeoutException){return item;}
        }
        var providers=item.Section=="Игры"?GameReleaseSources.Create(source,item,configuredSources):NativeReleaseSources.Create(source,Resolved,item.Section=="Сериалы",Aliases);
        var sources=providers.ToList();if(item.Section!="Игры")sources.AddRange(configuredSources.Select(config=>new ReleaseSource(config.Name,t=>source.SearchTorznab(config,item.OriginalTitle??item.Title,t,item.Section=="Сериалы"?5000:2000))));if(item.Section!="Игры"&&OnlineIndexClient.IdFor(item)!=null)sources.Insert(0,new("Онлайн-индекс",t=>online.Releases(item,t,true)));
        var rules=releaseAudioRules.Refresh(ct);
        var result=await ReleaseSearch.RunAsync(sources,new Progress<ReleaseSearchUpdate>(update=>Event("releases",new{request,card=cardKey,items=ReleaseDtos(ReleaseSearch.WithSaved(update.Items,saved),item.Section=="Игры"),complete=update.Complete,sources=update.Sources.Select(x=>new{x.Name,state=x.State.ToString(),count=x.Count})})),ct);
        await rules;
        var combined=ReleaseSearch.WithSaved(result.Items,saved);
        item.SetReleaseQuality(combined,prefs.MinimumReleaseHeight);
        if(result.Items.Length>0||ReleaseAvailability.ConfirmedEmpty(result.Items,result.Sources))
            await index.CacheReleasesAsync(item,result.Items.Length==0?[]:combined,result.Sources,ct);
        return new{items=ReleaseDtos(combined,item.Section=="Игры"),complete=true,sources=result.Sources.Select(x=>new{x.Name,state=x.State.ToString(),count=x.Count})};
    }
    object[] ReleaseDtos(IEnumerable<SourceEntry> items,bool games=false)
    {
        var rows=items.ToArray();
        var choice=games?GameReleaseRecommendation.Pick(rows,prefs.UiLanguage):ReleaseRecommendation.Pick(rows,prefs.MinimumReleaseHeight,prefs.UiLanguage);
        var ranked=new Dictionary<SourceEntry,int>(ReferenceEqualityComparer.Instance);
        if(!games){var order=ReleaseRecommendation.Ranked(rows,prefs.MinimumReleaseHeight,prefs.UiLanguage);for(var i=0;i<order.Length;i++)ranked[order[i]]=i;}
        return SearchLanguage.AudioOrder(rows,prefs.UiLanguage).Where(x=>games||!prefs.HidePoorQuality||!ReleaseQuality.Poor(x,prefs.MinimumReleaseHeight)).Select(entry=>
        {
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Source+"|"+entry.Id+"|"+entry.TorrentUrl)))[..24];
        if(releases.Count>3000)releases.Remove(releases.Keys.First());releases[key]=entry;
        return (object)new{id=key,entryId=entry.Id,title=entry.Title,source=entry.Source,size=entry.Size,seeds=entry.Seeds,leechers=entry.Leechers,quality=entry.Quality,type=entry.Type,audio=entry.Voice,studios=ReleaseAudio.StudioNames(entry),audioTypes=ReleaseAudio.TypeNames(entry),codec=entry.Codec,subs=entry.Subs,hdr=entry.Hdr,poor=ReleaseQuality.Poor(entry,prefs.MinimumReleaseHeight),downloadable=ReleaseAvailability.Downloadable(entry),recommended=choice?.Entry==entry,recommendationOrder=ranked.TryGetValue(entry,out var rank)?(int?)rank:null,season=entry.Series.Season,episode=entry.Series.Episode,multipleSeasons=entry.Series.MultipleSeasons,multipleEpisodes=entry.Series.MultipleEpisodes,englishAudio=SearchLanguage.EnglishAudioRank(entry)>=2,russianAudio=RussianAudio.Rank(entry)>=2,russianDub=RussianAudio.Rank(entry)==3&&RussianAudio.DubbedAudio(entry),qualityLabel=ReleaseQuality.Label(entry),gameKind=games?GameReleaseInfo.Kind(entry):null,gameGroup=games?GameReleaseInfo.Group(entry):null,gameVersion=games?GameReleaseInfo.Version(entry):null};
        }).ToArray();
    }
    object Downloads()=>new{items=DownloadItems()};
    object[] DownloadItems()=>downloads.Items.OrderByDescending(x=>x.AddedUtc).Select(item=>(object)new{id=item.Id,title=item.DisplayName,releaseTitle=item.Name,poster=item.ImageUrl,progress=item.Progress,status=AppLanguage.T(item.Status),hint=AppLanguage.T(item.Hint),paused=item.Paused,complete=item.Completed,missing=item.FilesMissing,lowSpace=item.LowSpacePaused,downloadRate=item.DownloadRate,uploadRate=item.UploadRate,totalBytes=item.TotalBytes,addedUtc=item.AddedUtc,folder=item.Folder,mediaId=DownloadMetadata.Card(item) is {} media?Remember([media])[0].id:null,canUpdateRelease=item.CanUpdateRelease,peers=item.PeersText,files=item.Files.Count,statusKind=item.StatusKind.ToString().ToLowerInvariant(),remaining=AppLanguage.T(item.Remaining),indeterminate=item.Indeterminate,releaseId=item.ReleaseId,releaseSource=item.ReleaseSource}).ToArray();
    object Settings()=>new{prefs.Folder,prefs.FolderConfigured,prefs.UiLanguage,prefs.Light,prefs.LiteMode,prefs.AutoResumeDownloads,prefs.AutoRecoverDownloads,prefs.NotifyDownloads,prefs.CheckForUpdates,prefs.AutoUpdate,prefs.HidePoorQuality,prefs.MinimumReleaseHeight,prefs.MaxDownloadKbps,prefs.MaxUploadKbps,prefs.LanEnabled,prefs.DownloadSort,prefs.HomeCountry,hidden=prefs.HiddenTitles.Select(x=>new{id=x.Identity,title=x.Title}),hiddenGenres=prefs.HiddenGenres,savedIds=prefs.LiveFavorites.Select(Key)};
    async Task<object> SetSettings(JsonElement p,CancellationToken ct)
    {
        if(p.TryGetProperty("uiLanguage",out var language)){prefs.UiLanguage=AppLanguage.Normalize(language.GetString());AppLanguage.Set(prefs.UiLanguage);groups.Clear();}
        if(p.TryGetProperty("light",out var light))prefs.Light=light.GetBoolean();
        if(p.TryGetProperty("liteMode",out var lite)){prefs.LiteMode=lite.GetBoolean();prefs.LiteModeConfigured=true;PerformanceBudget.LiteMode=prefs.LiteMode;}
        if(p.TryGetProperty("hidePoorQuality",out var poor))prefs.HidePoorQuality=poor.GetBoolean();
        if(p.TryGetProperty("autoResumeDownloads",out var resume))prefs.AutoResumeDownloads=resume.GetBoolean();
        if(p.TryGetProperty("autoRecoverDownloads",out var recover))prefs.AutoRecoverDownloads=recover.GetBoolean();
        if(p.TryGetProperty("notifyDownloads",out var notify))prefs.NotifyDownloads=notify.GetBoolean();
        if(p.TryGetProperty("checkForUpdates",out var check))prefs.CheckForUpdates=check.GetBoolean();
        if(p.TryGetProperty("autoUpdate",out var auto))prefs.AutoUpdate=auto.GetBoolean();
        if(p.TryGetProperty("maxDownloadKbps",out var down))prefs.MaxDownloadKbps=Math.Max(0,down.GetInt32());
        if(p.TryGetProperty("maxUploadKbps",out var up))prefs.MaxUploadKbps=Math.Max(0,up.GetInt32());
        if(p.TryGetProperty("downloadSort",out var downloadSort)&&new[]{"newest","name","speed","size","progress"}.Contains(downloadSort.GetString()))prefs.DownloadSort=downloadSort.GetString()!;
        if(p.TryGetProperty("homeCountry",out var homeCountry)&&homeCountry.ValueKind==JsonValueKind.String){var previousHome=prefs.HomeCountry;prefs.HomeCountry=CatalogRegions.HomeCountry(homeCountry.GetString());if(prefs.HomeCountry!=previousHome)groups.Clear();}
        if(p.TryGetProperty("hiddenGenres",out var hidden))prefs.HiddenGenres=hidden.Deserialize<HashSet<string>>(Json)??[];
        if(p.TryGetProperty("lanEnabled",out var enabled)){prefs.LanEnabled=enabled.GetBoolean();if(prefs.LanEnabled)await EnsureLan(ct);else if(lan!=null){await lan.DisposeAsync();lan=null;}}
        await downloads.SetLimitsAsync(prefs.MaxDownloadKbps,prefs.MaxUploadKbps);prefs.Save();Event("personal",Settings());return Settings();
    }
    async Task<object> Phone(JsonElement p,CancellationToken ct)
    {
        if(shuttingDown)throw new OperationCanceledException(ct);
        var action=S(p,"action");
        if(action=="stop")
        {
            var server=phone;phone=null;var catalog=phoneCatalog;phoneCatalog=null;
            if(server!=null)await server.DisposeAsync();catalog?.Dispose();
            return new{running=false,urls=Array.Empty<string>(),code=(string?)null,connected=0};
        }
        if(action=="start")
        {
            if(phone==null)
            {
                var context=SynchronizationContext.Current!;
                var catalog=new PhoneRemoteCatalog(downloads,prefs);
                var server=new PhoneRemoteServer((method,args,token)=>OnOwner(async()=>
                {
                    token.ThrowIfCancellationRequested();
                    var result=await catalog.Handle(method,args,token);
                    if(method is "add" or "control")Event("downloads",Downloads());return result;
                },context));
                try{server.Start();phoneCatalog=catalog;phone=server;}
                catch{await server.DisposeAsync();catalog.Dispose();throw;}
            }
            phone.OpenPairing();
        }
        return phone==null?(object)new{running=false,urls=Array.Empty<string>(),code=(string?)null,connected=0}:new{running=true,state=phone.State()};
    }
    async Task EnsureLan(CancellationToken ct)
    {
        if(lan!=null)return;lan=new(new LanServiceOptions{StateDirectory=Path.Combine(Preferences.DataDir,"lan"),DeviceName=Environment.MachineName});
        var receiver=new LanDownloadReceiver(downloads,prefs,Path.Combine(Preferences.DataDir,"lan-inbox"));
        var context=SynchronizationContext.Current!;
        lan.ReceiveDownloadAsync=(request,token)=>OnOwner(()=>prefs.LanEnabled&&!shuttingDown?receiver.ReceiveAsync(request,token):Task.FromResult(new LanDownloadReceipt{RequestId=request.RequestId,Message="Receiving downloads is disabled."}),context);
        lan.ConfirmPairingAsync=async(request,token)=>
        {
            var approval=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            context.Post(_=>{pairing[request.Device.Id]=approval;Event("pairing",new{id=request.Device.Id,name=request.Device.Name,code=request.VerificationCode});},null);
            try{return await approval.Task.WaitAsync(token);}finally{context.Post(_=>pairing.Remove(request.Device.Id),null);}
        };
        await lan.StartAsync(ct);
    }
    static Task<T> OnOwner<T>(Func<Task<T>> action,SynchronizationContext owner)
    {var result=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);owner.Post(async _=>{try{result.TrySetResult(await action());}catch(Exception error){result.TrySetException(error);}},null);return result.Task;}
    async Task<object> Lan(JsonElement p,CancellationToken ct)
    {
        await EnsureLan(ct);switch(S(p,"action"))
        {
            case "rename":lan!.Name=S(p,"name");break;
            case "discover":await lan!.DiscoverAsync(ct);break;
            case "allow":lan!.AllowPairingFor(TimeSpan.FromMinutes(2));break;
            case "approve":if(pairing.TryGetValue(Guid.Parse(S(p,"id")),out var answer))answer.TrySetResult(B(p,"approved"));break;
            case "pair":await lan!.PairAsync(lan.Devices.Single(x=>x.Id.ToString()==S(p,"id")),ct);break;
            case "send":
                var device=lan!.Devices.Single(x=>x.Id.ToString()==S(p,"device"));var entry=releases[S(p,"release")];var file=await source.TorrentFile(entry,ct);
                var request=await LanDownloadPayload.CreateAsync(file,S(p,"card").Length>0?Card(S(p,"card")):null,entry,ct);
                var receipt=await lan.SendDownloadAsync(device,request,ct);if(!receipt.Accepted)throw new IOException(receipt.Message);break;
        }
        return new{name=lan!.Name,devices=lan.Devices};
    }
    void SaveSources()
    {
        Directory.CreateDirectory(Preferences.DataDir);var path=Path.Combine(Preferences.DataDir,"sources.json");
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};
            if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
            using(var stream=new FileStream(temporary,options))JsonSerializer.Serialize(stream,configuredSources,Json);
            File.Move(temporary,path,true);
        }
        finally{File.Delete(temporary);}
    }
    async Task<SourceEntry> RefreshSourceEntry(SourceEntry prior,MediaItem media,CancellationToken ct)
    {
        var result=await ReleaseSearch.RunAsync(media.Section=="Игры"?GameReleaseSources.Create(source,media,configuredSources):NativeReleaseSources.Create(source,media),ct:ct);
        return result.Items.FirstOrDefault(x=>x.Source==prior.Source&&(x.Id==prior.Id||x.PageUrl==prior.PageUrl))??prior;
    }
    async Task Reliability()
    {
        try
        {
            var available=NetworkInterface.GetIsNetworkAvailable();
            if(lastNetwork!=available)
            {
                lastNetwork=available;await downloads.RecoverConnectionsAsync(available,false,lifetime.Token,prefs.AutoRecoverDownloads);
            }
            await downloads.PollReliabilityAsync(lifetime.Token,prefs.AutoRecoverDownloads);
        }
        catch(OperationCanceledException)when(lifetime.IsCancellationRequested){}
        catch(Exception error){ErrorLog.Write(error);}
    }
    async Task Tick()
    {
        try
        {
            using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));
            while(await timer.WaitForNextTickAsync(lifetime.Token))
            {
                if(downloads.Items.Count==0)continue;
                downloads.Update();
                if(reliability==null||reliability.IsCompleted)reliability=Reliability();
                var snapshot=JsonSerializer.Serialize(Downloads(),Json);
                if(snapshot!=lastDownloadEvent)
                {
                    lastDownloadEvent=snapshot;
                    Console.Out.WriteLine("{\"event\":\"downloads\",\"data\":"+snapshot+"}");
                }
            }
        }
        catch(OperationCanceledException){}catch(Exception error){ErrorLog.Write(error);}
    }
    object Choice(CatalogChoice item)=>new{key=item.Key,label=AppLanguage.T(item.Label)};
    string Key(MediaItem item)=>(item.Section=="Игры"?"games":item.Section=="Сериалы"?"series":"movies")+":"+(item.OnlineId??item.ImdbId??item.Id.ToString());
    CardView[] Remember(IEnumerable<MediaItem> items)=>items.Select(item=>{var key=Key(item);if(cards.Count>6000)cards.Remove(cards.Keys.First());cards[key]=item;return CardDto(item);}).ToArray();
    bool IsSaved(MediaItem item)=>prefs.LiveFavorites.Any(x=>MediaIdentity.Same(x,item));
    CardView CardDto(MediaItem item)
    {
        ApplyKnownQuality(item);
        return new(Key(item),item.DisplayTitle,item.Section=="Игры"?"games":item.Section=="Сериалы"?"series":"movies",item.Year,item.CardRating,item.DisplayImageUrl??(prefs.UiLanguage=="en"?null:item.ImageUrl),item.DisplayDescription??"",item.CardGenre,item.HasPosterQuality?item.PosterQuality:null,IsSaved(item),item.Section=="Игры"?"Windows":null,item.Store,item.KpAvailable?item.KpValue:null,item.ImdbAvailable?item.ImdbValue:null,item.CardRatingSource,item.ImdbId,item.Section=="Игры"?item.ImageUrl:null);
    }
    MediaItem Card(string id)=>cards.GetValueOrDefault(id)??throw new KeyNotFoundException("Карточка устарела. Обнови страницу.");
    DownloadItem Download(string id)=>downloads.Items.Single(item=>item.Id==id);
    static string S(JsonElement p,string key,string fallback="")=>p.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??fallback:fallback;
    static int I(JsonElement p,string key,int fallback=0)=>p.TryGetProperty(key,out var value)&&value.TryGetInt32(out var result)?result:fallback;
    static bool B(JsonElement p,string key)=>p.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.True;
    void Reply(string id,object result)=>Write(new{id,result});
    void Event(string kind,object data){if(!shuttingDown)Write(new{ @event=kind,data});}
    void Write(object value)=>Console.Out.WriteLine(JsonSerializer.Serialize(value,Json));
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();foreach(var pending in requests.Values.ToArray())pending.Cancel();
        if(lan!=null)await lan.DisposeAsync();
        if(phone!=null)await phone.DisposeAsync();phoneCatalog?.Dispose();
        await Task.WhenAll(running.Concat(new[]{ticker,warming,reliability}.OfType<Task>()));
        await downloads.Close();prefs.Save();source.Dispose();lifetime.Dispose();
    }
}
sealed record CardView(string id,string title,string section,int year,string rating,string? poster,string description,string genre,string? quality,bool saved,string? platform,GameStoreInfo? store,string? kp,string? imdb,string? ratingSource,string? imdbId,string? posterFallback=null);
