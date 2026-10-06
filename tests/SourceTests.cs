using System.Text;
using System.Text.Json;
using Kachalka;
public static class SourceTests
{
    sealed class FixtureHandler(byte[] data):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new ByteArrayContent(data)});
    }
    sealed class OfflineHandler:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromException<HttpResponseMessage>(new HttpRequestException("offline"));
    }
    sealed class OnlineFixtureHandler:HttpMessageHandler
    {
        public List<Uri> Requests {get;}=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var url=request.RequestUri??throw new InvalidOperationException("Missing request URI");
            Requests.Add(url);
            var json=url.AbsolutePath switch
            {
                "/api/catalog" when url.Query.Contains("section=movies",StringComparison.Ordinal)=>"""{"items":[{"id":"movies:moonrise","section":"movies","title":"Лунный берег","originalTitle":"Moonrise","year":2026,"poster":"https://images.example.test/moonrise.jpg","pageUrl":"https://w6.zona.plus/movies/moonrise","description":null,"kinopoisk":"7,8","imdb":null}],"sourceStatus":"indexed","indexCount":1}""",
                "/api/catalog" when url.Query.Contains("section=series",StringComparison.Ordinal)=>"""{"items":[{"id":"series:northern-path","section":"series","title":"Северный путь","year":2024,"poster":null,"pageUrl":"https://w6.zona.plus/tvseries/northern-path"}],"sourceStatus":"indexed","indexCount":1}""",
                "/api/media"=>"""{"item":{"id":"movies:moonrise","section":"movies","title":"Лунный берег","originalTitle":"Moonrise","year":2026,"poster":"https://images.example.test/moonrise.jpg","pageUrl":"https://w6.zona.plus/movies/moonrise","description":"Описание из онлайн-индекса.","kinopoisk":"7,8","imdb":"8.2"}}""",
                "/api/releases"=>"""{"items":[{"id":"RuTracker:abc","mediaId":"movies:moonrise","title":"Moonrise (2026) 1080p WEB-DL","source":"RuTracker","via":"Knaben","pageUrl":"https://rutracker.org/forum/viewtopic.php?t=123","torrentUrl":"magnet:?xt=urn:btih:0123456789012345678901234567890123456789","size":4294967296,"seeds":37,"quality":"1080p","season":null,"episode":null},{"id":"Internet Archive:Archive_01","mediaId":"movies:moonrise","title":"Moonrise (2026)","source":"Internet Archive","via":null,"pageUrl":"https://archive.org/details/Archive_01","torrentUrl":null,"size":null,"seeds":null,"quality":null,"season":null,"episode":null},{"id":"The Pirate Bay:no-download","mediaId":"movies:moonrise","title":"Moonrise unavailable","source":"The Pirate Bay","pageUrl":"https://thepiratebay.org/description.php?id=2","torrentUrl":null,"size":null,"seeds":0},{"id":"Bad:script","mediaId":"movies:moonrise","title":"Moonrise unsafe","source":"Bad","pageUrl":null,"torrentUrl":"javascript:alert(1)","size":null,"seeds":0},{"id":"Bad:webpage","mediaId":"movies:moonrise","title":"Moonrise webpage","source":"Bad","pageUrl":null,"torrentUrl":"https://example.test/watch/1","size":null,"seeds":0}],"updated":false,"sourceCount":4}""",
                _=>throw new InvalidOperationException("Unexpected online index request: "+url)
            };
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")});
        }
    }
    public static async Task Run(bool live,bool allSections=false)
    {
        static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS: "+name);}
        var coverPath=Path.GetFullPath(Path.Combine("test-output","cover-recovery-"+Guid.NewGuid().ToString("N")+".img"));
        Directory.CreateDirectory(Path.GetDirectoryName(coverPath)!);
        static string DecodeCover(byte[] bytes)=>Encoding.ASCII.GetString(bytes)=="valid-image"?"decoded":throw new InvalidDataException("Invalid image");
        try
        {
            await File.WriteAllBytesAsync(coverPath,Encoding.ASCII.GetBytes("broken"));
            var fetched=0;
            var (recovered,downloaded)=await CoverCache.Load(coverPath,128,_=>{fetched++;return Task.FromResult(Encoding.ASCII.GetBytes("valid-image"));},DecodeCover,CancellationToken.None);
            Check(recovered=="decoded"&&downloaded&&fetched==1&&DecodeCover(await File.ReadAllBytesAsync(coverPath))=="decoded",
                "broken cover cache is replaced by a valid downloaded image");
            var (_,fromNetwork)=await CoverCache.Load(coverPath,128,_=>Task.FromException<byte[]>(new Exception("Should use cache")),DecodeCover,CancellationToken.None);
            Check(!fromNetwork&&fetched==1,"valid cover cache avoids another request");
            await File.WriteAllBytesAsync(coverPath,Encoding.ASCII.GetBytes("broken"));
            var networkFailed=false;
            try{await CoverCache.Load(coverPath,128,_=>Task.FromException<byte[]>(new HttpRequestException("offline")),DecodeCover,CancellationToken.None);}
            catch(HttpRequestException){networkFailed=true;}
            Check(networkFailed&&!File.Exists(coverPath),"temporary image request failure does not preserve broken cache");
        }
        finally{if(File.Exists(coverPath))File.Delete(coverPath);}
        var sample="<rss><channel><item><title>Example 1080p</title><enclosure url='magnet:?xt=urn:btih:0123456789012345678901234567890123456789' length='42'/><x:attr xmlns:x='http://torznab.com/schemas/2015/feed' name='seeders' value='7'/></item></channel></rss>";
        var parsed=SourceClient.ParseTorznab(Encoding.UTF8.GetBytes(sample),"Test");Check(parsed.Count==1&&parsed[0].Seeds==7&&parsed[0].Size==42,"Torznab result parsing");
        var movie=new MediaItem(-1,"Мангуст","Фильмы","",2026,"—","—","#526B69");
        SourceEntry Result(string title)=>new(title,title,"RuTor","https://rutor.info/torrent/1","magnet:?xt=urn:btih:0123456789012345678901234567890123456789",null);
        Check(Result("Film 720p").Quality=="HD Ready"&&Result("Film 1080p").Quality=="Full HD"&&Result("Film 2160p").Quality=="4K"&&Result("Film Full HD").Quality=="Full HD","release quality uses HD Ready, Full HD and 4K labels");
        Check(Result("Film HDTV").Quality=="Не указано","HDTV alone does not invent a resolution");
        Check(LiveCatalog.Matches(movie,Result("Мангуст / Mongoose (2026) WEB-DL 1080p"))&&!LiveCatalog.Matches(movie,Result("Владимир Малыгин - Прыжок Мангуста (2026) MP3"))&&!LiveCatalog.Matches(movie,Result("Мангуст [01-12] (2003) DVDRip"))&&!LiveCatalog.Matches(movie,Result("Мангуст 2 (2026)")),"movie releases match title and year");
        const string knabenHash="0123456789abcdef0123456789abcdef01234567";
        var knabenHits=new{
            hits=new[]{
                new{tracker="RuTracker.org",title="Мангуст / Mongoose (2026) WEB-DL 1080p",category="Movies",hash=knabenHash,details="https://rutracker.org/forum/viewtopic.php?t=123",bytes=4294967296L,seeders=27},
                new{tracker="The Pirate Bay",title="Мангуст / Mongoose (2026) WEB-DL 1080p",category="Movies",hash=knabenHash,details="https://rutracker.org/forum/viewtopic.php?t=123",bytes=1L,seeders=1},
                new{tracker="RuTracker",title="Мангуст / Mongoose (2025) WEB-DL 1080p",category="Movies",hash="1111111111111111111111111111111111111111",details="https://rutracker.org/forum/viewtopic.php?t=124",bytes=1L,seeders=1},
                new{tracker="RuTracker",title="Другой фильм (2026) WEB-DL 1080p",category="Movies",hash="2222222222222222222222222222222222222222",details="https://rutracker.org/forum/viewtopic.php?t=125",bytes=1L,seeders=1},
                new{tracker="RuTracker",title="Мангуст / Mongoose (2026) WEB-DL 1080p",category="Music",hash="3333333333333333333333333333333333333333",details="https://rutracker.org/forum/viewtopic.php?t=126",bytes=1L,seeders=1}
            }
        };
        var knabenRows=KnabenSource.Parse(JsonSerializer.SerializeToUtf8Bytes(knabenHits),movie);
        Check(knabenRows.Count==1&&knabenRows[0].Source=="RuTracker"&&knabenRows[0].Via=="Knaben"&&knabenRows[0].Id=="RuTracker:"+knabenHash.ToUpperInvariant()&&
              knabenRows[0].TorrentUrl!.StartsWith("magnet:?xt=urn:btih:"+knabenHash.ToUpperInvariant(),StringComparison.Ordinal)&&
              knabenRows[0].PageUrl=="https://rutracker.org/forum/viewtopic.php?t=123"&&knabenRows[0].Size==4294967296&&knabenRows[0].Seeds==27,
              "Knaben accepts RuTracker video with a valid hash and rejects other trackers, years, titles, and categories");
        static string NnmRow(string category,string title,string topic,string download,string size,string seeds)=>
            $"<tr class='prow1'><td></td><td><a href='tracker.php?f=1'>{category}</a></td><td><a class='genmed topictitle' href='{topic}'><b>{title}</b></a></td><td></td><td><a href='{download}'>DL</a></td><td><u>{size}</u> 4 GB</td><td title='Seeders'><b>{seeds}</b></td></tr>";
        var nnmHtml="<table>"+
            NnmRow("Зарубежное кино","Мангуст / Mongoose (2026) WEB-DL 1080p","viewtopic.php?t=123","download.php?id=456","4294967296","21")+
            NnmRow("Зарубежное кино","Мангуст / Mongoose (2025) WEB-DL 1080p","viewtopic.php?t=124","download.php?id=457","100","5")+
            NnmRow("Книги","Мангуст / Mongoose (2026) PDF","viewtopic.php?t=125","download.php?id=458","100","5")+
            NnmRow("Зарубежное кино","Мангуст / Mongoose (2026) WEB-DL 1080p","viewtopic.php?t=126","download.php?id=459&amp;other=1","100","5")+
            NnmRow("Зарубежные сериалы","Разделение / Severance (2025) WEB-DL 1080p S02E10","viewtopic.php?t=127","download.php?id=460","15989627250","6")+
            "</table>";
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var nnmBytes=Encoding.GetEncoding(1251).GetBytes(nnmHtml);
        var nnmMovie=NnmClubSource.Parse(nnmBytes,movie);
        Check(nnmMovie.Count==1&&nnmMovie[0].Source=="NNM-Club"&&nnmMovie[0].Id=="NNM-Club:456"&&
              nnmMovie[0].PageUrl=="https://nnmclub.to/forum/viewtopic.php?t=123"&&
              nnmMovie[0].TorrentUrl=="https://nnmclub.to/forum/download.php?id=456"&&
              nnmMovie[0].Size==4294967296&&nnmMovie[0].Seeds==21&&
              Uri.UnescapeDataString(NnmClubSource.QueryUri("Мангуст").Query).Contains("Мангуст",StringComparison.Ordinal),
              "NNM-Club movie results keep torrent URL, exact size and seeders, and reject wrong year/category/URL");
        var series=new MediaItem(-3,"Разделение","Сериалы","",2022,"—","—","#526B69"){OriginalTitle="Severance"};
        var nnmSeries=NnmClubSource.Parse(nnmBytes,series);
        Check(nnmSeries.Count==1&&nnmSeries[0].Id=="NNM-Club:460"&&nnmSeries[0].Series.Season==2&&nnmSeries[0].Series.Episode==10,
            "NNM-Club keeps matching series seasons despite a later release year");
        static string MegaRow(string title,string topic,string download,string size,string seeds)=>
            $"<tr class='table_fon'><td>Сегодня</td><td colspan='2'><a href='{download}'>DL</a><a class='url' href='{topic}'>{title}</a></td><td>{size}</td><td><img alt='S' src='/pic/seed.gif'><font>{seeds}</font></td></tr>";
        var megaHtml="<table>"+
            MegaRow("Мангуст / Mongoose (2026) WEB-DL 1080p","/torrent/123/mongoose-2026","/download/123","3.91 GB","14")+
            MegaRow("Мангуст / Mongoose (2025) WEB-DL 1080p","/torrent/124/mongoose-2025","/download/124","3.91 GB","14")+
            MegaRow("Мангуст / Mongoose (2026) WEB-DL 1080p","/torrent/125/mongoose-2026","/download/126","3.91 GB","14")+
            MegaRow("Разделение / Severance [2 сезон] (2025) WEB-DL 720p","/torrent/127/severance-2025","/download/127","2.30 GB","6")+
            "</table>";
        var megaBytes=Encoding.GetEncoding(1251).GetBytes(megaHtml);
        var megaMovie=MegaPeerSource.Parse(megaBytes,movie);
        var megaUri=MegaPeerSource.QueryUri("Разделение");
        Check(megaMovie.Count==1&&megaMovie[0].Source=="MegaPeer"&&megaMovie[0].Id=="MegaPeer:123"&&
              megaMovie[0].TorrentUrl=="https://megapeer.vip/download/123"&&megaMovie[0].PageUrl=="https://megapeer.vip/torrent/123/mongoose-2026"&&
              megaMovie[0].Size==4198330531&&megaMovie[0].Seeds==14&&megaUri.Query.Contains("stype=0",StringComparison.Ordinal)&&megaUri.Query.Contains("%D0%E0",StringComparison.OrdinalIgnoreCase),
              "MegaPeer movie search uses CP1251 title mode and rejects wrong year or mismatched download ID");
        var megaSeries=MegaPeerSource.Parse(megaBytes,series);
        Check(megaSeries.Count==1&&megaSeries[0].Id=="MegaPeer:127","MegaPeer keeps matching series results");
        Check(LiveCatalog.Matches(series,Result("Severance.S02E10.1080p.WEB-DL"))&&LiveCatalog.Matches(series,Result("[Release] Разделение / Severance (2022) 2 сезон"))&&!LiveCatalog.Matches(series,Result("Severance 2: The Movie"))&&!LiveCatalog.Matches(series,Result("Severance Pay 2022")),"series search matches aliases and excludes sequels");
        var parsedSeason=SeriesReleaseInfo.Parse("Severance S02E10 1080p");var parsedPack=SeriesReleaseInfo.Parse("Severance S01-S03 WEB-DL");
        Check(parsedSeason.Season==2&&parsedSeason.Episode==10&&parsedPack.MultipleSeasons&&parsedPack.SeasonLabel=="Несколько сезонов","season and episode labels are extracted");
        var maze="[{\"show\":{\"name\":\"Severance\",\"premiered\":\"2022-02-18\",\"externals\":{\"imdb\":\"tt11280740\"}}},{\"show\":{\"name\":\"Severance\",\"premiered\":\"1988-01-01\",\"externals\":{\"imdb\":\"tt1234567\"}}}]";
        Check(SourceClient.ParseTvMaze(Encoding.UTF8.GetBytes(maze),"Severance",2022)?.ImdbId=="tt11280740","TVMaze identity uses exact title and year");
        var eztv="{\"torrents\":[{\"title\":\"Severance S02E10 1080p\",\"magnet_url\":\"magnet:?xt=urn:btih:0123456789012345678901234567890123456789\",\"size_bytes\":1234,\"seeds\":7,\"hash\":\"0123456789012345678901234567890123456789\"},{\"title\":\"Severance Pay S02E10\",\"magnet_url\":\"magnet:?xt=urn:btih:0123456789012345678901234567890123456789\"}]}";
        Check(SourceClient.ParseEztv(Encoding.UTF8.GetBytes(eztv),"Severance").Count==1,"EZTV filters unrelated titles");
        var nyaa="<rss xmlns:nyaa='https://nyaa.si/xmlns/nyaa'><channel><item><title>[Group] One Piece - 1180 [1080p]</title><link>https://nyaa.si/download/123.torrent</link><guid>https://nyaa.si/view/123</guid><nyaa:infoHash>0123456789012345678901234567890123456789</nyaa:infoHash><nyaa:seeders>14</nyaa:seeders><nyaa:size>430.0 MiB</nyaa:size></item></channel></rss>";
        var nyaaRows=SourceClient.ParseNyaa(Encoding.UTF8.GetBytes(nyaa));Check(nyaaRows.Count==1&&nyaaRows[0].Seeds==14&&nyaaRows[0].Size>400*1024*1024,"Nyaa RSS parses torrent and seeder count");
        var onlineHandler=new OnlineFixtureHandler();
        using(var fixture=new SourceClient(onlineHandler))
        {
            var online=new OnlineIndexClient(fixture,new Uri("https://index.example.test/"));
            var movies=await online.Browse("Фильмы","Лунный берег & путь",1,CancellationToken.None);
            var nextPage=await online.Browse("Фильмы","",2,CancellationToken.None);
            var onlineShows=await online.Browse("Сериалы","",1,CancellationToken.None);
            var expected=LiveCatalog.Parse(Encoding.UTF8.GetBytes("<li class='results-item-wrap'><a itemprop='url' href='/movies/moonrise'><span itemprop='name'>Лунный берег</span></a></li>"),"Фильмы").Single();
            Check(movies.Count==1&&movies[0].Id==expected.Id&&movies[0].OnlineId=="movies:moonrise"&&movies[0].ImageUrl=="https://images.example.test/moonrise.jpg"&&movies[0].Kinopoisk=="7,8"&&nextPage.Count==1,
                "online catalog maps posters and ratings while keeping existing favorite IDs");
            Check(onlineShows.Count==1&&onlineShows[0].Section=="Сериалы"&&onlineShows[0].OnlineId=="series:northern-path"&&onlineShows[0].PageUrl=="https://w6.zona.plus/tvseries/northern-path",
                "online series catalog maps section and identity");
            var detailed=await online.Detail(movies[0],CancellationToken.None);
            Check(detailed.Description=="Описание из онлайн-индекса."&&detailed.OriginalTitle=="Moonrise"&&detailed.Kinopoisk=="7,8"&&detailed.Imdb=="8.2"&&detailed.Id==expected.Id,
                "online detail fills original title, description, and both ratings");
            var releases=await online.Releases(detailed,CancellationToken.None);
            Check(releases.Count==2&&releases[0].Source=="RuTracker"&&releases[0].Via=="Knaben"&&releases[0].Seeds==37&&releases[0].Size==4294967296&&releases[0].TorrentUrl!.StartsWith("magnet:?xt=urn:btih:",StringComparison.Ordinal),
                "online RuTracker release keeps source attribution and download metadata");
            Check(releases[1].Source=="Internet Archive"&&releases[1].Id=="Archive_01"&&releases[1].TorrentUrl==null&&releases[1].PageUrl=="https://archive.org/details/Archive_01"&&releases.All(x=>!x.Title.Contains("unsafe")&&!x.Title.Contains("webpage")&&!x.Title.Contains("unavailable")),
                "online releases keep resolvable Archive entries and reject unusable URLs");
            var calls=onlineHandler.Requests;
            var movieCalls=calls.Where(x=>x.AbsolutePath=="/api/catalog"&&x.Query.Contains("section=movies",StringComparison.Ordinal)).ToArray();
            Check(calls.Count==5&&calls.All(x=>x.Host=="index.example.test")&&movieCalls.Length==2&&
                movieCalls.Any(x=>x.Query.Contains("page=2",StringComparison.Ordinal))&&
                movieCalls.Any(x=>x.Query.Contains("%26",StringComparison.OrdinalIgnoreCase)&&Uri.UnescapeDataString(x.Query).Contains("Лунный берег & путь",StringComparison.Ordinal))&&
                calls.Any(x=>x.AbsolutePath=="/api/media"&&Uri.UnescapeDataString(x.Query).Contains("id=movies:moonrise",StringComparison.Ordinal))&&
                calls.Any(x=>x.AbsolutePath=="/api/releases"&&Uri.UnescapeDataString(x.Query).Contains("id=movies:moonrise",StringComparison.Ordinal)),
                "online requests use the configured API and encode search and media IDs");
        }
        var nnmApiJson="""{"items":[{"id":"NNM-Club:456","mediaId":"movies:moonrise","title":"Moonrise (2026) 1080p","source":"NNM-Club","pageUrl":"https://nnmclub.to/forum/viewtopic.php?t=123","torrentUrl":"https://nnmclub.to/forum/download.php?id=456","size":4294967296,"seeds":21},{"id":"NNM-Club:457","mediaId":"movies:moonrise","title":"Moonrise invalid query","source":"NNM-Club","pageUrl":"https://nnmclub.to/forum/viewtopic.php?t=124","torrentUrl":"https://nnmclub.to/forum/download.php?id=457&other=1"},{"id":"NNM-Club:458","mediaId":"movies:moonrise","title":"Moonrise invalid host","source":"NNM-Club","pageUrl":"https://nnmclub.to/forum/viewtopic.php?t=125","torrentUrl":"https://nnmclub.to.evil.test/forum/download.php?id=458"}]}""";
        using(var fixture=new SourceClient(new FixtureHandler(Encoding.UTF8.GetBytes(nnmApiJson))))
        {
            var online=new OnlineIndexClient(fixture,new Uri("https://index.example.test/"));
            var item=new MediaItem(-9,"Moonrise","Фильмы","",2026,"—","—","#526B69"){OnlineId="movies:moonrise"};
            var releases=await online.Releases(item,CancellationToken.None);
            Check(releases.Count==1&&releases[0].Source=="NNM-Club"&&releases[0].TorrentUrl=="https://nnmclub.to/forum/download.php?id=456"&&releases[0].Seeds==21,
                "online NNM-Club .torrent endpoint is accepted while altered query and host are rejected");
        }
        var megaApiJson="""{"items":[{"id":"MegaPeer:123","mediaId":"movies:moonrise","title":"Moonrise (2026) 1080p","source":"MegaPeer","pageUrl":"https://megapeer.vip/torrent/123/moonrise-2026","torrentUrl":"https://megapeer.vip/download/123","size":4198330531,"seeds":14},{"id":"MegaPeer:124","mediaId":"movies:moonrise","title":"Moonrise invalid query","source":"MegaPeer","pageUrl":"https://megapeer.vip/torrent/124/moonrise-2026","torrentUrl":"https://megapeer.vip/download/124?token=other"},{"id":"MegaPeer:125","mediaId":"movies:moonrise","title":"Moonrise invalid host","source":"MegaPeer","pageUrl":"https://megapeer.vip/torrent/125/moonrise-2026","torrentUrl":"https://megapeer.vip.evil.test/download/125"}]}""";
        using(var fixture=new SourceClient(new FixtureHandler(Encoding.UTF8.GetBytes(megaApiJson))))
        {
            var online=new OnlineIndexClient(fixture,new Uri("https://index.example.test/"));
            var item=new MediaItem(-9,"Moonrise","Фильмы","",2026,"—","—","#526B69"){OnlineId="movies:moonrise"};
            var releases=await online.Releases(item,CancellationToken.None);
            Check(releases.Count==1&&releases[0].Source=="MegaPeer"&&releases[0].TorrentUrl=="https://megapeer.vip/download/123"&&releases[0].Seeds==14,
                "online MegaPeer .torrent endpoint is accepted while altered query and host are rejected");
        }
        var indexDir=Path.Combine("test-output","index-"+Guid.NewGuid().ToString("N"));
        var index=new CatalogIndex(indexDir);
        var indexedSeries=series with{PageUrl="https://w6.zona.plus/tvseries/severance",ImageUrl="https://example.org/poster.jpg"};
        await index.AddAsync([indexedSeries]);
        await index.AddAsync([indexedSeries with{OriginalTitle=null,Imdb="8,5"}]);
        var restoredIndex=new CatalogIndex(indexDir);
        Check(restoredIndex.Search("Сериалы","severance").Single().OriginalTitle=="Severance"&&
              restoredIndex.Search("Сериалы","разделение 2022").Count==1&&
              restoredIndex.Search("Фильмы","severance").Count==0&&
              restoredIndex.Search("Сериалы","Severance Pay").Count==0,
              "local catalog index persists aliases and ranks exact matches");
        await index.CacheReleasesAsync(indexedSeries,[Result("Severance.S02E10.1080p.WEB-DL")]);
        Check(new CatalogIndex(indexDir).CachedReleases(indexedSeries).Single().Series.Episode==10,"release index survives restart");
        var wide=WindowSizing.Fit(1920,1040);var scaled=WindowSizing.Fit(683,350);
        Check(wide.Width==1760&&wide.Height==950&&scaled.Width<=scaled.MaxWidth&&scaled.Height<=scaled.MaxHeight&&scaled.MinWidth<=scaled.Width&&scaled.MinHeight<=scaled.Height,"window fits available desktop at normal and 200% scale");
        Check(WindowSizing.PosterColumns(1200)==7&&WindowSizing.PosterColumns(1060)==6&&WindowSizing.PosterColumns(980)==6&&WindowSizing.PosterColumns(450)==2,"poster columns follow available width");
        var saved=new Preferences();
        Check(!saved.FolderConfigured,"new settings require one-time download folder choice");
        saved.Save();
        Check(!Preferences.Load().FolderConfigured,"unconfigured folder state survives restart");
        var selectedFolder=Path.GetFullPath(Path.Combine("test-output","selected-download-folder"));
        File.WriteAllText(Path.Combine(Preferences.DataDir,"settings.json"),JsonSerializer.Serialize(new{Folder=selectedFolder}));
        Check(!Preferences.Load().FolderConfigured,"older settings without folder choice require one-time prompt");
        saved.Folder=selectedFolder;saved.FolderConfigured=true;
        saved.Favorites.Add(movie.Id);saved.LiveFavorites.Add(movie with{PageUrl="https://w6.zona.plus/movies/mongoose"});saved.Save();
        var restored=Preferences.Load();
        Check(restored.FolderConfigured&&restored.Folder==selectedFolder,"chosen download folder survives restart without prompting again");
        Check(restored.LiveFavorites.Count==1&&restored.LiveFavorites[0].Title==movie.Title&&restored.Favorites.Contains(movie.Id),"favorites survive settings save and restart");
        var changedFolder=Path.GetFullPath(Path.Combine("test-output","changed-download-folder"));
        restored.Folder=changedFolder;restored.Save();
        Check(Preferences.Load().FolderConfigured&&Preferences.Load().Folder==changedFolder,"folder changed in settings survives restart");
        try{SourceClient.ParseTorznab(Encoding.UTF8.GetBytes("<!DOCTYPE rss [<!ENTITY a SYSTEM 'file:///secret'>]><rss>&a;</rss>"),"Test");throw new Exception("DTD accepted");}catch(System.Xml.XmlException){Console.WriteLine("PASS: XML external entities rejected");}
        var root=Path.GetFullPath("test-output/research");if(File.Exists(Path.Combine(root,"zona.txt"))){var movies=LiveCatalog.Parse(await File.ReadAllBytesAsync(Path.Combine(root,"zona.txt")),"Фильмы");Check(movies.Count>0&&movies[0].ImageUrl?.StartsWith("https://")==true&&movies[0].Kinopoisk=="—","catalog parser preserves real title/poster without invented scores");var releases=LiveCatalog.ParseRutor(await File.ReadAllBytesAsync(Path.Combine(root,"rutor.txt")));Check(releases.Count==5&&releases.All(x=>x.TorrentUrl!.StartsWith("magnet:")),"RuTor parses title, magnet and seed count");if(File.Exists(Path.Combine(root,"detail.txt"))){using var fixture=new SourceClient(new FixtureHandler(await File.ReadAllBytesAsync(Path.Combine(root,"detail.txt"))));var snapshot=await new LiveCatalog(fixture).Detail(movies[0],CancellationToken.None);using var offline=new SourceClient(new OfflineHandler());var savedDetail=await new LiveCatalog(offline).Detail(movies[0],CancellationToken.None);Check(snapshot.Description?.Length>10&&savedDetail.Description==snapshot.Description&&savedDetail.Kinopoisk==snapshot.Kinopoisk,"description and ratings survive offline restart");}}
        var nnmSnapshot=Path.GetFullPath("test-output/nnm-severance.html");
        if(File.Exists(nnmSnapshot))
        {
            var actual=NnmClubSource.Parse(await File.ReadAllBytesAsync(nnmSnapshot),series);
            Check(actual.Count>0&&actual.All(x=>x.Source=="NNM-Club"&&x.TorrentUrl!.StartsWith("https://nnmclub.to/forum/download.php?id=",StringComparison.Ordinal))&&actual.Any(x=>x.Seeds>0),
                "real NNM-Club HTML snapshot yields matching downloadable series releases");
        }
        var megaSnapshot=Path.GetFullPath("test-output/megapeer-avatar.html");
        if(File.Exists(megaSnapshot))
        {
            var avatar=new MediaItem(-10,"Аватар","Фильмы","",2009,"—","—","#526B69"){OriginalTitle="Avatar"};
            var actual=MegaPeerSource.Parse(await File.ReadAllBytesAsync(megaSnapshot),avatar);
            Check(actual.Count>0&&actual.All(x=>x.Source=="MegaPeer"&&x.TorrentUrl!.StartsWith("https://megapeer.vip/download/",StringComparison.Ordinal))&&actual.Any(x=>x.Seeds>0&&x.Size>0),
                "real MegaPeer HTML snapshot yields matching downloadable movie releases");
        }
        if(!live)return;
        using var http=new SourceClient();var api=new LiveCatalog(http);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var real=await api.Browse("Фильмы","",1,timeout.Token);Check(real.Count>10,"automatic live movie catalog without configuration");
        var shows=await api.Browse("Сериалы","",1,timeout.Token);Check(shows.Count>10,"automatic live series catalog without configuration");
        var seriesResults=await api.Browse("Сериалы","Разделение",1,timeout.Token);
        var severance=seriesResults.FirstOrDefault(x=>x.Title=="Разделение");Check(severance!=null,"series search finds exact catalog title");
        var severanceDetail=await api.Detail(severance!,timeout.Token);Check(severanceDetail.OriginalTitle=="Severance","series detail provides original title");
        var identity=await http.FindSeriesIdentity(severanceDetail.OriginalTitle!,severanceDetail.Year,timeout.Token);Check(identity?.ImdbId=="tt11280740","TVMaze resolves exact series identity");
        var episodes=await http.SearchEztv(identity!,timeout.Token);Check(episodes.Count>10&&episodes.Any(x=>x.Seeds>0&&LiveCatalog.Matches(severanceDetail,x)),"live EZTV series episodes with active seeders");
        var anime=await http.SearchNyaa("One Piece",timeout.Token);Check(anime.Count>10&&anime.Any(x=>x.Seeds>0&&x.TorrentUrl!.EndsWith(".torrent")),"live Nyaa anime source");
        var detail=await api.Detail(real[0],timeout.Token);Check(!string.IsNullOrEmpty(detail.Description),"live movie description");
        using(var offline=new SourceClient(new OfflineHandler())){var cached=await new LiveCatalog(offline).Detail(real[0],timeout.Token);Check(cached.Description==detail.Description&&cached.Kinopoisk==detail.Kinopoisk,"ratings and description remain available offline");}
        var results=await api.Releases("Big Buck Bunny",timeout.Token);Check(results.Count>0&&results.All(x=>x.TorrentUrl!.StartsWith("magnet:")),"live built-in RuTor lookup without account");
        Check(results.Any(x=>LiveCatalog.Matches(new MediaItem(-2,"Big Buck Bunny","Фильмы","",2008,"—","—","#526B69"),x)),"matching movie release remains available");
        var archive=await http.SearchArchive("Big Buck Bunny","Фильмы",1,timeout.Token);Check(archive.Count>0,"live Internet Archive search");
        var verified=await http.ResolveArchive(new SourceEntry("BigBuckBunny_328","Big Buck Bunny","Internet Archive","https://archive.org/details/BigBuckBunny_328",null,null),timeout.Token);Check(verified.TorrentUrl?.EndsWith(".torrent")==true,"archive record resolves to a real torrent file");
        if(!allSections){Console.WriteLine("Live results: "+real.Count+" movies; "+shows.Count+" series; "+episodes.Count+" EZTV releases; "+anime.Count+" Nyaa releases");return;}
        var music=await http.SearchArchive("","Музыка",1,timeout.Token);Check(music.Count>10,"automatic music catalog without configuration");
        var software=await http.SearchArchive("","Программы",1,timeout.Token);Check(software.Count>10,"automatic software catalog without configuration");
        var broadcast=new BroadcastCatalog(http);var radio=await broadcast.Radio(timeout.Token);Check(radio.Count>10,"radio stations appear without setup");
        var tv=await broadcast.Television(false,timeout.Token);Check(tv.Count>10,"TV channels appear without setup");
        var sports=await broadcast.Television(true,timeout.Token);Check(sports.Count>10,"sports streams appear without setup");
        Console.WriteLine("Live results: "+real.Count+" movies; "+results.Count+" releases; "+archive.Count+" archive records");
    }
}
