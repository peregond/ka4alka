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
    readonly OnlineIndexClient online;readonly SharedCatalog shared;readonly AwardsCatalog awards;
    readonly CatalogIndex index=new(Preferences.DataDir,deferRestore:true);
    readonly DownloadService downloads;readonly CancellationTokenSource lifetime=new();
    readonly Dictionary<string,MediaItem> cards=[];readonly Dictionary<string,SourceEntry> releases=[];
    readonly Dictionary<string,CancellationTokenSource> requests=[];
    readonly Dictionary<string,FeaturedGroup> groups=[];
    readonly List<SourceConfig> configuredSources=[];
    readonly Dictionary<Guid,TaskCompletionSource<bool>> pairing=[];
    readonly List<Task> running=[];
    LanService? lan;Task? ticker;Task? warming;bool shuttingDown;
    public DesktopEngine()
    {
        AppLanguage.Set(prefs.UiLanguage);PerformanceBudget.LiteMode=prefs.LiteMode;
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
        try{Reply(id,await Handle(id,method,args,request.Token));}
        catch(OperationCanceledException){Write(new{id,error=new{code="cancelled",message="Cancelled"}});}
        catch(Exception error){ErrorLog.Write(error);Write(new{id,error=new{code="failed",message=AppLanguage.T(error.Message)}});}
        finally{requests.Remove(id);request.Dispose();}
    }
    async Task<object> Handle(string request,string method,JsonElement p,CancellationToken ct)
    {
        switch(method)
        {
            case "bootstrap":
                warming??=Warm();return new{protocol=1,version=typeof(DesktopEngine).Assembly.GetName().Version?.ToString(3),settings=Settings(),genres=CatalogChoices.Genres.Select(Choice),countries=CatalogChoices.Countries.Select(Choice),gameGenres=GameGenres.All.Select(x=>new{key=x.Key,label=AppLanguage.T(x.Name)}),engineCreated=downloads.EngineCreated};
            case "catalog":return await Catalog(p,ct);
            case "featured":return await Featured(p,ct);
            case "artwork":
                var artCard=Card(S(p,"id"));var artId=OnlineIndexClient.IdFor(artCard);
                if(artCard.Section=="Игры")return new{url=GameCatalog.HeroImage(artCard)};
                return new{url=artId==null?null:FeatureBackdrop.FromResponse(await online.ReadSite("api/backdrop?id="+Uri.EscapeDataString(artId)+"&language="+prefs.UiLanguage,128*1024,ct),artId)};
            case "detail":return await Detail(Card(S(p,"id")),ct);
            case "releases":return await Releases(request,Card(S(p,"id")),ct);
            case "person":
                var person=p.GetProperty("person").Deserialize<CinemaPerson>(Json)!;
                var profile=await new CinemaPeople(source).Load(person,null,shared.All,ct);
                return new{person=profile.Person,description=profile.Description,items=Remember(profile.Filmography),sourceUrl=profile.SourceUrl};
            case "awards":
                await awards.RestoreAsync(ct);await awards.RefreshNomineesAsync(ct);
                var award=awards.All.FirstOrDefault(x=>x.Id==S(p,"award"));
                var entries=(award?.Entries??[]).Concat(awards.NomineesOf(S(p,"award"))).Where(x=>I(p,"year")==0||x.Year==I(p,"year")).DistinctBy(x=>(x.Year,x.ImdbId)).ToArray();
                return new{years=(award?.Entries??[]).Concat(awards.NomineesOf(S(p,"award"))).Select(x=>x.Year).Distinct().OrderDescending(),items=Remember(entries.Select(entry=>AwardsCatalog.Item(entry)).ToArray()),winners=Remember((award?.Entries??[]).Where(x=>I(p,"year")==0||x.Year==I(p,"year")).Select(entry=>AwardsCatalog.Item(entry))),nominees=Remember(awards.NomineesOf(S(p,"award")).Where(x=>(I(p,"year")==0||x.Year==I(p,"year"))&&!(award?.Entries??[]).Any(winner=>winner.Year==x.Year&&winner.ImdbId==x.ImdbId)).Select(entry=>AwardsCatalog.Item(entry)))};
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
                await downloads.ReplaceTorrentAsync(existing,await source.TorrentFile(entry,ct));return Downloads();
            case "sources":return new{items=configuredSources.Select(x=>new{x.Name,x.Endpoint,hasKey=x.ApiKey.Length>0})};
            case "sourceAdd":
                var endpoint=SourceClient.WebUri(S(p,"endpoint")).AbsoluteUri;var name=S(p,"name").Trim();
                if(name.Length is 0 or >100||configuredSources.Count>=20)throw new ArgumentException("Invalid source name or limit exceeded.");
                configuredSources.RemoveAll(x=>x.Name==name);configuredSources.Add(new(name,endpoint,S(p,"apiKey")));SaveSources();return new{};
            case "sourceRemove":configuredSources.RemoveAll(x=>x.Name==S(p,"name"));SaveSources();return new{};
            case "logs":return new{text=await DiagnosticReport.CreateAsync(prefs,downloads,ct)};
            case "refresh":online.RetryNow();await shared.RefreshAsync(ct,true);groups.Clear();return new{};
            case "lan":return await Lan(p,ct);
            case "prepareUpdate":
                using(var updater=new MacUpdateClient(UpdateTrust.PublicKey))
                {
                    var destination=Path.Combine(Preferences.DataDir,"updates",Guid.NewGuid().ToString("N"));
                    return await Task.Run(()=>updater.PrepareAsync(Convert.FromBase64String(S(p,"manifest")),Convert.FromBase64String(S(p,"signature")),S(p,"architecture"),S(p,"installed"),destination,new Progress<MacUpdateProgress>(value=>Event("updateProgress",value)),ct),ct);
                }
            case "shutdown":shuttingDown=true;await downloads.Close();return new{};
            default:throw new ArgumentException("Unknown desktop command: "+method);
        }
    }
    async Task Warm()
    {
        try
        {
            await BundledCatalog.EnsureLoadedAsync(lifetime.Token);await shared.RestoreAsync(lifetime.Token);await index.RestoreAsync(lifetime.Token);
            Event("catalogReady",new{});
            if(prefs.AutoResumeDownloads)await downloads.ResumePendingAsync();
            if(prefs.LanEnabled)await EnsureLan(lifetime.Token);
            if(await shared.RefreshAsync(lifetime.Token))Event("catalogReady",new{});
        }
        catch(OperationCanceledException){}catch(Exception error){ErrorLog.Write(error);}
    }
    async Task<object> Catalog(JsonElement p,CancellationToken ct)
    {
        var section=S(p,"section","movies");var page=Math.Clamp(I(p,"page",1),1,500);var query=S(p,"query");
        if(section=="games")
        {
            var result=await new GameCatalog(source,prefs.UiLanguage).Browse(new(query,S(p,"sort","popular"),S(p,"genre"),page),ct);
            return new{items=Remember(result.Items.Where(Visible)),hasNext=result.HasNext,offline=false};
        }
        await BundledCatalog.EnsureLoadedAsync(ct);var category=section=="series"?"Сериалы":"Фильмы";
        var selection=new CatalogSelection(S(p,"genre"),S(p,"country"),I(p,"year")==0?null:I(p,"year"),I(p,"rating"),S(p,"sort","date"));
        if(query.Length>0)
        {
            var result=await UnifiedSearch.FindAsync(query,(kind,t)=>online.Browse(kind,query,1,t),kind=>shared.Search(kind,query),ct,
                ImdbSuggestions.Applies(query)?t=>ImdbSuggestions.SearchAsync(query,t):null,preferredLanguage:prefs.UiLanguage);
            var filtered=result.Items.Where(Visible).Where(selection.Matches).Where(item=>S(p,"kind").Length==0||item.Section==(S(p,"kind")=="series"?"Сериалы":"Фильмы")).ToArray();
            return new{items=Remember(filtered),hasNext=false,offline=result.Offline};
        }
        var local=await Task.Run(()=>shared.All.Where(item=>item.Section==category&&Visible(item)&&selection.Matches(item)).ToArray(),ct);
        if(local.Length>0)
        {
            IEnumerable<MediaItem> rows=local;rows=selection.Order=="rating"?rows.OrderByDescending(item=>Rating(item)):rows.OrderByDescending(item=>item.Year);
            var offset=(page-1)*CatalogPaging.Size;var data=rows.Skip(offset).Take(CatalogPaging.Size).ToArray();return new{items=Remember(data),hasNext=offset+data.Length<local.Length,offline=false};
        }
        var remote=await online.BrowsePage(category,page,ct);return new{items=Remember(remote.Items.Where(Visible)),hasNext=remote.HasNext,offline=false};
    }
    static double Rating(MediaItem item)=>double.TryParse(item.CardRating.Replace(',','.'),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out var value)?value:0;
    bool Visible(MediaItem item)=>!HiddenCatalog.Hidden(item,prefs,CatalogChoices.Genres)&&(!prefs.HidePoorQuality||!item.OnlyPoorQuality);
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
            var group=groups[S(p,"id")];return new{title=group.Collection.Label,description=group.Collection.Summary,items=Remember(group.Items.Where(Visible)),hasNext=false};
        }
        return new{groups=groups.Values.Select(group=>new{id=group.Collection.Id,title=group.Collection.Label,description=group.Collection.Summary,layout=group.Collection.Layout,items=Remember(group.Items.Where(Visible).Take(10)),count=group.Items.Length}).Where(group=>group.items.Length>0).Take(18)};
    }
    async Task<object> Detail(MediaItem item,CancellationToken ct)
    {
        string? backdrop=null;GameDetails? game=null;
        if(item.Section=="Игры"){game=await new GameCatalog(source,prefs.UiLanguage).Detail(item,ct);item=game.Item;backdrop=GameCatalog.HeroImage(item);}
        else
        {
            var result=await MediaMetadata.Load(item,t=>ImdbSuggestions.IsImdbOnly(item)?online.Title(item,t):online.Detail(item,t),t=>new LiveCatalog(source).Detail(item,t),ct:ct);item=result.Item;
            if(prefs.UiLanguage=="en")
            {
                try
                {
                    var id=OnlineIndexClient.IdFor(item)??item.ImdbId;
                    if(id!=null){var kind=item.Section=="Сериалы"?"series":"movies";var bytes=await online.ReadSite("api/localized-media?language=en&id="+Uri.EscapeDataString(id)+"&section="+kind,2*1024*1024,ct,restSites:false);var english=EnglishMedia.Parse(bytes,id,kind);item=item with{English=english};backdrop=english?.Backdrop;}
                }catch(Exception) when(!ct.IsCancellationRequested){}
            }
            if(backdrop==null)try{var id=OnlineIndexClient.IdFor(item);if(id!=null)backdrop=FeatureBackdrop.FromResponse(await online.ReadSite("api/backdrop?id="+Uri.EscapeDataString(id),128*1024,ct),id);}catch(Exception)when(!ct.IsCancellationRequested){}
        }
        Remember([item]);return new{item=CardDto(item),backdrop,people=item.DisplayPeople,awards=item.Awards,game=game==null?null:new{game.Requirements,game.Developers,game.Publishers,game.Price,game.Metacritic,gallery=game.Gallery,game.Traits,platform="Windows"}};
    }
    async Task<object> Releases(string request,MediaItem item,CancellationToken ct)
    {
        var providers=item.Section=="Игры"?GameReleaseSources.Create(source,item,configuredSources):NativeReleaseSources.Create(source,item);
        var sources=providers.ToList();if(item.Section!="Игры")sources.AddRange(configuredSources.Select(config=>new ReleaseSource(config.Name,t=>source.SearchTorznab(config,item.OriginalTitle??item.Title,t,item.Section=="Сериалы"?5000:2000))));if(item.Section!="Игры"&&OnlineIndexClient.IdFor(item)!=null)sources.Insert(0,new("Онлайн-индекс",t=>online.Releases(item,t,true)));
        var result=await ReleaseSearch.RunAsync(sources,new Progress<ReleaseSearchUpdate>(update=>Event("releases",new{request,items=ReleaseDtos(update.Items),complete=update.Complete,sources=update.Sources.Select(x=>new{x.Name,state=x.State.ToString()})})),ct);
        await index.CacheReleasesAsync(item,result.Items,result.Sources,ct);return new{items=ReleaseDtos(result.Items),complete=true,sources=result.Sources.Select(x=>new{x.Name,state=x.State.ToString()})};
    }
    object[] ReleaseDtos(IEnumerable<SourceEntry> items)=>SearchLanguage.AudioOrder(items,prefs.UiLanguage).Where(x=>!prefs.HidePoorQuality||!ReleaseQuality.Poor(x,prefs.MinimumReleaseHeight)).Select(entry=>
    {
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Source+"|"+entry.Id+"|"+entry.TorrentUrl)))[..24];
        if(releases.Count>3000)releases.Remove(releases.Keys.First());releases[key]=entry;
        return (object)new{id=key,title=entry.Title,source=entry.Source,size=entry.Size,seeds=entry.Seeds,leechers=entry.Leechers,quality=entry.Quality,type=entry.Type,audio=entry.Voice,studios=ReleaseAudio.StudioNames(entry),audioTypes=ReleaseAudio.TypeNames(entry),codec=entry.Codec,subs=entry.Subs,hdr=entry.Hdr,poor=ReleaseQuality.Poor(entry,prefs.MinimumReleaseHeight),downloadable=ReleaseAvailability.Downloadable(entry)};
    }).ToArray();
    object Downloads()=>new{items=downloads.Items.OrderByDescending(x=>x.AddedUtc).Select(item=>new{id=item.Id,title=item.DisplayName,releaseTitle=item.Name,poster=item.ImageUrl,progress=item.Progress,status=AppLanguage.T(item.Status),hint=AppLanguage.T(item.Hint),paused=item.Paused,complete=item.Completed,missing=item.FilesMissing,lowSpace=item.LowSpacePaused,downloadRate=item.DownloadRate,uploadRate=item.UploadRate,totalBytes=item.TotalBytes,addedUtc=item.AddedUtc,folder=item.Folder,mediaId=DownloadMetadata.Card(item) is {} media?Remember([media])[0].id:null,canUpdateRelease=item.CanUpdateRelease,peers=item.PeersText,files=item.Files.Count})};
    object Settings()=>new{prefs.Folder,prefs.FolderConfigured,prefs.UiLanguage,prefs.Light,prefs.LiteMode,prefs.AutoResumeDownloads,prefs.AutoRecoverDownloads,prefs.NotifyDownloads,prefs.CheckForUpdates,prefs.AutoUpdate,prefs.HidePoorQuality,prefs.MinimumReleaseHeight,prefs.MaxDownloadKbps,prefs.MaxUploadKbps,prefs.LanEnabled,hidden=prefs.HiddenTitles.Select(x=>new{id=x.Identity,title=x.Title}),hiddenGenres=prefs.HiddenGenres,savedIds=prefs.LiveFavorites.Select(Key)};
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
        if(p.TryGetProperty("hiddenGenres",out var hidden))prefs.HiddenGenres=hidden.Deserialize<HashSet<string>>(Json)??[];
        if(p.TryGetProperty("lanEnabled",out var enabled)){prefs.LanEnabled=enabled.GetBoolean();if(prefs.LanEnabled)await EnsureLan(ct);else if(lan!=null){await lan.DisposeAsync();lan=null;}}
        await downloads.SetLimitsAsync(prefs.MaxDownloadKbps,prefs.MaxUploadKbps);prefs.Save();Event("personal",Settings());return Settings();
    }
    async Task EnsureLan(CancellationToken ct)
    {
        if(lan!=null)return;lan=new(new LanServiceOptions{StateDirectory=Path.Combine(Preferences.DataDir,"lan"),DeviceName=Environment.MachineName});
        var receiver=new LanDownloadReceiver(downloads,prefs,Path.Combine(Preferences.DataDir,"lan-inbox"));
        var context=SynchronizationContext.Current!;
        lan.ReceiveDownloadAsync=(request,token)=>OnOwner(()=>receiver.ReceiveAsync(request,token),context);
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
                var request=new LanDownloadRequest{Magnet=file.StartsWith("magnet:")?file:null,TorrentBytes=file.StartsWith("magnet:")?null:await File.ReadAllBytesAsync(file,ct),Title=entry.Title};
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
    async Task Tick()
    {
        try{using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1));while(await timer.WaitForNextTickAsync(lifetime.Token)){downloads.SetNetworkAvailable(NetworkInterface.GetIsNetworkAvailable());if(downloads.EngineCreated){downloads.Update();Event("downloads",Downloads());}}}catch(OperationCanceledException){}catch(Exception error){ErrorLog.Write(error);}
    }
    object Choice(CatalogChoice item)=>new{key=item.Key,label=AppLanguage.T(item.Label)};
    string Key(MediaItem item)=>(item.Section=="Игры"?"games":item.Section=="Сериалы"?"series":"movies")+":"+(item.OnlineId??item.ImdbId??item.Id.ToString());
    CardView[] Remember(IEnumerable<MediaItem> items)=>items.Select(item=>{var key=Key(item);if(cards.Count>6000)cards.Remove(cards.Keys.First());cards[key]=item;return CardDto(item);}).ToArray();
    bool IsSaved(MediaItem item)=>prefs.LiveFavorites.Any(x=>MediaIdentity.Same(x,item));
    CardView CardDto(MediaItem item)=>new(Key(item),item.DisplayTitle,item.Section=="Игры"?"games":item.Section=="Сериалы"?"series":"movies",item.Year,item.CardRating,item.DisplayImageUrl??(prefs.UiLanguage=="en"?null:item.ImageUrl),item.DisplayDescription??"",item.CardGenre,item.HasPosterQuality?item.PosterQuality:null,IsSaved(item),item.Section=="Игры"?"Windows":null);
    MediaItem Card(string id)=>cards.GetValueOrDefault(id)??throw new KeyNotFoundException("Карточка устарела. Обнови страницу.");
    DownloadItem Download(string id)=>downloads.Items.Single(item=>item.Id==id);
    static string S(JsonElement p,string key,string fallback="")=>p.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??fallback:fallback;
    static int I(JsonElement p,string key,int fallback=0)=>p.TryGetProperty(key,out var value)&&value.TryGetInt32(out var result)?result:fallback;
    static bool B(JsonElement p,string key)=>p.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.True;
    void Reply(string id,object result)=>Write(new{id,result});
    void Event(string kind,object data)=>Write(new{ @event=kind,data});
    void Write(object value)=>Console.Out.WriteLine(JsonSerializer.Serialize(value,Json));
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();foreach(var pending in requests.Values.ToArray())pending.Cancel();
        if(lan!=null)await lan.DisposeAsync();
        await Task.WhenAll(running.Concat(new[]{ticker,warming}.OfType<Task>()));
        await downloads.Close();prefs.Save();source.Dispose();lifetime.Dispose();
    }
}
sealed record CardView(string id,string title,string section,int year,string rating,string? poster,string description,string genre,string? quality,bool saved,string? platform);
