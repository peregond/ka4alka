using System.Net;
using System.Text;
using System.Text.Json;
using MonoTorrent;
using Kachalka;

// Trackers blocked by a network (RuTor, NNM-Club, MegaPeer, BigFanGroup and Nyaa
// are in the Russian registry) must still deliver their torrent files through
// the online index, without slowing down networks where the tracker answers.
static class TorrentRelayTests
{
    sealed class Handler(Func<Uri,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {
        public readonly List<Uri> Requests=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            lock(Requests)Requests.Add(request.RequestUri!);
            return send(request.RequestUri!,token);
        }
    }
    static HttpResponseMessage Bytes(byte[] body,HttpStatusCode status=HttpStatusCode.OK)=>new(status){Content=new ByteArrayContent(body)};

    public static async Task Run(string root)
    {
        static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
        foreach(var url in new[]{"https://nnmclub.to/forum/download.php?id=1702634","https://megapeer.vip/download/180694","https://bigfangroup.org/download.php?id=285126","https://nyaa.si/download/1951823.torrent"})
            Check(OnlineIndexClient.CanRelay(new Uri(url)),"the online index relays torrent files of "+new Uri(url).Host);
        foreach(var url in new[]{"http://nnmclub.to/forum/download.php?id=1","https://nnmclub.to/forum/viewtopic.php?t=1","https://nnmclub.to/forum/download.php?id=1&x=2","https://megapeer.vip/download/1#part",
            "https://user@megapeer.vip/download/1","https://megapeer.vip:8443/download/1","https://sub.nnmclub.to/forum/download.php?id=1","https://archive.org/download/a/a.torrent","https://knaben.org/live/dl/1","https://example.com/download/1"})
            Check(!OnlineIndexClient.CanRelay(new Uri(url)),"the online index refuses to relay "+url);
        Check(OnlineIndexClient.RelayUri(new Uri("https://megapeer.vip/download/5")).AbsoluteUri=="https://ka4alka-online-new.peregon.chatgpt.site/api/torrent?url=https%3A%2F%2Fmegapeer.vip%2Fdownload%2F5","the relay address carries the exact tracker download address");

        var payload=Path.Combine(root,"relay-payload.bin");await File.WriteAllBytesAsync(payload,RandomBytes(64*1024));
        var torrentPath=Path.Combine(root,"relay.torrent");await new TorrentCreator().CreateAsync(new TorrentFileSource(payload),torrentPath);
        var torrent=await File.ReadAllBytesAsync(torrentPath);
        var blockPage=Encoding.UTF8.GetBytes("<html><body>Доступ к информационному ресурсу ограничен на основании Федерального закона</body></html>");
        var release=new SourceEntry("MegaPeer:5","Фильм (2024) WEB-DL 1080p","MegaPeer","https://megapeer.vip/torrent/5/film","https://megapeer.vip/download/5",null);
        bool IsRelay(Uri uri)=>uri.Host=="ka4alka-online-new.peregon.chatgpt.site";

        async Task<(string? Path,Exception? Error,Handler Handler,TimeSpan Elapsed)> Fetch(SourceEntry entry,Func<Uri,CancellationToken,Task<HttpResponseMessage>> send,bool relay=true)
        {
            var handler=new Handler(send);using var client=new SourceClient(handler);var index=new OnlineIndexClient(client);
            if(relay)client.TorrentRelay=index.RelayTorrent;
            var clock=System.Diagnostics.Stopwatch.StartNew();
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try{return (await client.TorrentFile(entry,timeout.Token),null,handler,clock.Elapsed);}
            catch(Exception error){return (null,error,handler,clock.Elapsed);}
        }

        var blocked=await Fetch(release,(uri,_)=>Task.FromResult(Bytes(IsRelay(uri)?torrent:blockPage)));
        Check(blocked.Error==null&&File.ReadAllBytes(blocked.Path!).SequenceEqual(torrent)&&blocked.Handler.Requests.Any(IsRelay),"a provider block page returned with status 200 is rejected and the relayed torrent file is used");
        var relayed=blocked.Handler.Requests.Single(IsRelay);
        Check(relayed.AbsolutePath=="/api/torrent"&&Uri.UnescapeDataString(relayed.Query)=="?url=https://megapeer.vip/download/5","the relay is asked for the same tracker download address");

        var directCanceled=false;
        var hanging=await Fetch(release,async (uri,token)=>
        {
            if(IsRelay(uri))return Bytes(torrent);
            try{await Task.Delay(Timeout.Infinite,token);}catch(OperationCanceledException){directCanceled=true;throw;}
            throw new InvalidOperationException("unreachable");
        });
        await Task.Delay(100);
        Check(hanging.Error==null&&hanging.Elapsed<TimeSpan.FromSeconds(5)&&directCanceled,"a tracker that silently drops the connection does not delay the relayed file, and its request is canceled");

        var relayDown=await Fetch(release,(uri,_)=>Task.FromResult(IsRelay(uri)?Bytes(Encoding.UTF8.GetBytes("{\"error\":\"Трекер не ответил.\"}"),HttpStatusCode.BadGateway):Bytes(torrent)));
        Check(relayDown.Error==null&&File.ReadAllBytes(relayDown.Path!).SequenceEqual(torrent),"a reachable tracker still works when the online index cannot relay the file");

        var siteDown=await Fetch(release,(uri,_)=>IsRelay(uri)?throw new HttpRequestException("site offline"):Task.FromResult(Bytes(torrent)));
        Check(siteDown.Error==null,"a reachable tracker still works when the online index itself is offline");

        var both=await Fetch(release,(uri,_)=>Task.FromResult(IsRelay(uri)?Bytes([],HttpStatusCode.BadGateway):Bytes(blockPage)));
        Check(both.Error is InvalidOperationException error&&error.Message.Contains("ни из источника, ни через онлайн-индекс")&&error.InnerException is AggregateException {InnerExceptions.Count:2},"when both routes fail the user sees one clear reason instead of a parser error");

        var archive=new SourceEntry("film","Film","Internet Archive","https://archive.org/details/film","https://archive.org/download/film/film_archive.torrent",null);
        var unrelayed=await Fetch(archive,(uri,_)=>Task.FromResult(Bytes([],HttpStatusCode.NotFound)));
        Check(unrelayed.Error is HttpRequestException&&unrelayed.Handler.Requests.Count==1&&!unrelayed.Handler.Requests.Any(IsRelay),"sources outside the relay list keep their direct request and original error");

        var withoutRelay=await Fetch(release,(uri,_)=>Task.FromResult(Bytes(blockPage)),relay:false);
        Check(withoutRelay.Error!=null&&withoutRelay.Handler.Requests.Count==1,"without a configured relay only the direct address is requested");

        var magnet=release with{TorrentUrl="magnet:?xt=urn:btih:"+new string('a',40)};
        var magnetResult=await Fetch(magnet,(_,_)=>throw new InvalidOperationException("no request expected"));
        Check(magnetResult.Path==magnet.TorrentUrl&&magnetResult.Handler.Requests.Count==0,"magnet links never need the tracker site or the relay");

        var row=new{id="BigFanGroup:285126",mediaId="movies:interstellar",title="Интерстеллар / Interstellar (2014) BDRip-AVC",source="BigFanGroup",pageUrl="https://bigfangroup.org/details.php?id=285126",torrentUrl="https://bigfangroup.org/download.php?id=285126",size=4273492459L,seeds=421};
        var feed=JsonSerializer.SerializeToUtf8Bytes(new{items=new[]{row}});
        using(var client=new SourceClient(new Handler((_,_)=>Task.FromResult(Bytes(feed)))))
        {
            var media=new MediaItem(-1,"Интерстеллар","Фильмы","",2014,"—","—","#526B69"){OnlineId="movies:interstellar"};
            var rows=await new OnlineIndexClient(client).Releases(media,default);
            Check(rows.Count==1&&rows[0].TorrentUrl==row.torrentUrl&&OnlineIndexClient.CanRelay(new Uri(rows[0].TorrentUrl!)),"BigFanGroup releases found by the online index are accepted and can be relayed");
        }
    }
    // The site's hosting refuses Russia and Belarus: an opened card and a torrent
    // file then use a standalone relay whose address is published on GitHub.
    public static async Task RunRelays(string root)
    {
        static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
        byte[] List(params string[] relays)=>JsonSerializer.SerializeToUtf8Bytes(new{schemaVersion=1,relays});
        var parsed=IndexRelays.Parse(List("https://relay.example.workers.dev/","http://plain.example/","https://user@relay.example/","https://relay.example/?x=1","https://relay.example/path",
            "https://relay.example:8443/","https://ka4alka-online-new.peregon.chatgpt.site/","https://relay.example.workers.dev/","https://second.example/api/","not a url"));
        Check(parsed.Select(x=>x.AbsoluteUri).SequenceEqual(["https://relay.example.workers.dev/","https://second.example/api/"]),"the relay list keeps only distinct HTTPS base addresses other than the site");
        Check(IndexRelays.Parse(List(Enumerable.Range(1,9).Select(i=>$"https://r{i}.example/").ToArray())).Count==5,"the relay list is limited to five addresses");
        foreach(var invalid in new[]{"{}","{\"schemaVersion\":2,\"relays\":[]}","{\"schemaVersion\":1,\"relays\":\"https://r.example/\"}","[]"})
        {
            var rejected=false;try{IndexRelays.Parse(Encoding.UTF8.GetBytes(invalid));}catch(InvalidDataException){rejected=true;}
            Check(rejected,"a malformed relay list is rejected: "+invalid);
        }

        var payload=Path.Combine(root,"relay-fallback.bin");await File.WriteAllBytesAsync(payload,RandomBytes(32*1024));
        var torrentPath=Path.Combine(root,"relay-fallback.torrent");await new TorrentCreator().CreateAsync(new TorrentFileSource(payload),torrentPath);
        var torrent=await File.ReadAllBytesAsync(torrentPath);
        var blockPage=Encoding.UTF8.GetBytes("<html>Доступ ограничен</html>");
        var release=new{id="NNM-Club:77",mediaId="movies:interstellar",title="Интерстеллар / Interstellar (2014) BDRip 1080p",source="NNM-Club",pageUrl="https://nnmclub.to/forum/viewtopic.php?t=77",torrentUrl="https://nnmclub.to/forum/download.php?id=77",size=1024L,seeds=12};
        var search=JsonSerializer.SerializeToUtf8Bytes(new{items=new[]{release},updated=true,sourceCount=1});
        var listAvailable=true;var siteRefuses=true;
        Handler Network()=>new((uri,_)=>Task.FromResult(uri.Host switch
        {
            "raw.githubusercontent.com"=>listAvailable?Bytes(List("https://relay.example.workers.dev/")):throw new HttpRequestException("offline"),
            "ka4alka-online-new.peregon.chatgpt.site"=>siteRefuses?Bytes(Encoding.UTF8.GetBytes("<title>Attention Required! | Cloudflare</title>"),HttpStatusCode.Forbidden):Bytes(search),
            "relay.example.workers.dev"=>uri.AbsolutePath=="/api/search"?Bytes(search):uri.AbsolutePath=="/api/torrent"?Bytes(torrent):Bytes([],HttpStatusCode.NotFound),
            _=>Bytes(blockPage)
        }));
        var media=new MediaItem(-1,"Интерстеллар","Фильмы","",2014,"—","—","#526B69"){OnlineId="movies:interstellar",OriginalTitle="Interstellar"};
        var cacheFile=Path.Combine(root,"relays-cache.json");

        var handler=Network();
        using(var client=new SourceClient(handler))
        {
            var relays=new IndexRelays(client,cachePath:cacheFile);var index=new OnlineIndexClient(client,relays:relays);
            var rows=await index.Releases(media,default,allowRelay:true);
            var asked=handler.Requests.Single(x=>x.Host=="relay.example.workers.dev");
            var query=System.Web.HttpUtility.ParseQueryString(asked.Query);
            Check(rows.Count==1&&rows[0].TorrentUrl==release.torrentUrl,"an opened card gets releases from the relay when the site refuses the country");
            Check(asked.AbsolutePath=="/api/search"&&query["id"]=="movies:interstellar"&&query["title"]=="Интерстеллар"&&query["original"]=="Interstellar"&&query["year"]=="2014","the relay receives the card's id, titles and year");
            var before=handler.Requests.Count;
            var failed=false;try{await index.Releases(media,default);}catch(HttpRequestException){failed=true;}
            Check(failed&&handler.Requests.Skip(before).All(x=>x.Host!="relay.example.workers.dev"),"catalog-wide quality checks never send poster-by-poster searches to the relay");
            Check(handler.Requests.Count(x=>x.Host=="raw.githubusercontent.com")==1&&File.Exists(cacheFile),"the relay list is read once and saved for later");

            client.TorrentRelay=index.RelayTorrent;
            var entry=new SourceEntry(release.id,release.title,release.source,release.pageUrl,release.torrentUrl,null);
            before=handler.Requests.Count;
            var path=await client.TorrentFile(entry,default);
            var used=handler.Requests.Skip(before).ToArray();
            Check(File.ReadAllBytes(path).SequenceEqual(torrent)&&used.Any(x=>x.Host=="relay.example.workers.dev"&&x.AbsolutePath=="/api/torrent"),"a blocked tracker's torrent file arrives through the relay when the site refuses too");
            Check(!used.Any(x=>x.Host=="ka4alka-online-new.peregon.chatgpt.site"),"a site that has just refused is not asked again during its cooldown");
        }

        handler=Network();listAvailable=false;
        using(var client=new SourceClient(handler))
        {
            var rows=await new OnlineIndexClient(client,relays:new IndexRelays(client,cachePath:cacheFile)).Releases(media,default,allowRelay:true);
            Check(rows.Count==1,"the saved relay list keeps working while GitHub is unavailable");
        }

        handler=Network();listAvailable=true;siteRefuses=false;
        using(var client=new SourceClient(handler))
        {
            var rows=await new OnlineIndexClient(client,relays:new IndexRelays(client,cachePath:cacheFile)).Releases(media,default,allowRelay:true);
            Check(rows.Count==1&&handler.Requests.All(x=>x.Host=="ka4alka-online-new.peregon.chatgpt.site"),"where the site answers, neither GitHub nor the relay is contacted");
        }
    }
    // From the October 2026 logs of a user in Russia: a RuTracker magnet carried only
    // opentrackr, which answered nowhere there; NNM-Club and MegaPeer looked empty.
    public static async Task RunRussianNetworks()
    {
        static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
        IReadOnlyList<string> pub=["https://public.example/announce"];
        var rutracker=MagnetDiscovery.Fallbacks("RuTracker",pub)!;
        Check(rutracker.Contains("http://bt2.t-ru.org/ann?magnet")&&rutracker.Contains("http://retracker.local/announce")&&rutracker.Contains(pub[0]),"RuTracker magnets gain RuTracker's own announce addresses, the local retracker and the public trackers");
        Check(MagnetDiscovery.Fallbacks("The Pirate Bay",pub)!.SequenceEqual(pub)&&MagnetDiscovery.Fallbacks(null,pub)==null&&MagnetDiscovery.Fallbacks("Мой источник",pub)==null,"other public sources keep the public list and manual links stay untouched");
        Check(MagnetDiscovery.RuTrackerTrackers.All(x=>Uri.TryCreate(x,UriKind.Absolute,out var uri)&&uri.Scheme=="http"),"RuTracker fallback trackers are valid announce addresses");
        var media=new MediaItem(-1,"Интерстеллар","Фильмы","",2014,"—","—","#526B69"){OriginalTitle="Interstellar"};
        using var blocked=new SourceClient(new Handler((_,_)=>throw new HttpRequestException("connection reset")));
        foreach(var (name,search) in new (string,Func<Task>)[]{("NNM-Club",()=>new NnmClubSource(blocked).Search(media,default)),("MegaPeer",()=>new MegaPeerSource(blocked).Search(media,default))})
        {
            var unavailable=false;try{await search();}catch(HttpRequestException){unavailable=true;}
            Check(unavailable,name+" reports a blocked tracker as unavailable instead of empty");
        }
        var empty=Encoding.UTF8.GetBytes("<html><body><table></table></body></html>");
        using var answering=new SourceClient(new Handler((_,_)=>Task.FromResult(Bytes(empty))));
        Check((await new NnmClubSource(answering).Search(media,default)).Count==0&&(await new MegaPeerSource(answering).Search(media,default)).Count==0,"a tracker that answers without matches is still empty, not unavailable");
    }
    static byte[] RandomBytes(int length)=>System.Security.Cryptography.RandomNumberGenerator.GetBytes(length);
}
