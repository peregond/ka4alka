using Kachalka;
using System.Text.Json;
static class ReleaseQualityTests
{
    public static void Run()
    {
        static SourceEntry Row(string title)=>new(title,title,"Test","","magnet:?xt=urn:btih:"+new string('1',40),null);
        static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS: "+name);}
        foreach(var title in new[]{"Film HDCAM 1080p","Film HDTS 2160p","Film CAMRip 1080p","Film DVDSCR 1080p","The Weight (2026) Screener [H.264/1080p]","Фильм экранка 4K","Film WEB-DL 480p","Film 640x360","Film DVDRip"})
            Check(ReleaseQuality.Poor(Row(title)),"poor video source recognized: "+title);
        foreach(var title in new[]{"Film WEB-DL 720p","Film 1080p BluRay DTS","Film 4K","Film 1920x1080","Cambridge (2026) WEB-DL","Веб-кам / Cam (2018) WEBRip 1080p","Film HDTV","Film WEBRip"})
            Check(!ReleaseQuality.Poor(Row(title)),"HD or unknown source is not falsely marked: "+title);
        Check(ReleaseQuality.Poor(Row("Film 720p"),1080)&&!ReleaseQuality.Poor(Row("Film 1080p"),1080),"Full HD minimum hides 720p and keeps 1080p");
        Check(!ReleaseQuality.OnlyPoor([])&&!ReleaseQuality.OnlyPoor([Row("Film HDCAM"),Row("Film 1080p")])&&!ReleaseQuality.OnlyPoor([Row("Film HDCAM"),Row("Film WEB-DL")])&&ReleaseQuality.OnlyPoor([Row("Film HDCAM"),Row("Film 480p")]),"catalog only marks titles with exclusively known poor releases");
        var item=new MediaItem(1,"Фильм","Фильмы","",2026,"—","—","#526B69");var changed=0;item.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(MediaItem.OnlyPoorQuality))changed++;};
        item.SetReleaseQuality([Row("Film HDCAM")]);item.SetReleaseQuality([Row("Film HDCAM"),Row("Film 1080p")]);
        Check(!item.OnlyPoorQuality&&changed==2,"catalog quality updates existing bound cards when a good release arrives");
        Check(item.BestQuality=="Full HD"&&item.HasQuality,"catalog quality chip uses an evidenced downloadable HD release");
        item.SetReleaseQuality([Row("Film HDCAM 2160p"),Row("Film WEBRip")]);
        Check(!item.HasQuality,"screen recordings and unknown resolution cannot create a 4K quality chip");
        item.SetReleaseQuality([Row("Film 2160p") with{TorrentUrl=null}]);
        Check(!item.HasQuality,"a metadata-only record cannot create a downloadable quality chip");
        item.SetReleaseQuality([Row("Film 720p"),Row("Film 2160p")]);
        Check(item.BestQuality=="4K","catalog selects the highest evidenced downloadable resolution");
        Check(item.HasReleaseResolution(720)&&item.HasReleaseResolution(2160)&&!item.HasReleaseResolution(1080),"one film can match HD Ready and 4K independently without inventing FullHD");
        Check(ReleaseQuality.HasResolution([Row("Film 1080p")],1080)&&!ReleaseQuality.HasResolution([Row("Film 2160p")],1080),"catalog resolution choices select available variants rather than a minimum height");
        Check(!ReleaseQuality.HasResolution([Row("Film HDCAM 2160p"),Row("Film HDTS 720p")],2160)&&!ReleaseQuality.HasResolution([Row("Film HDCAM 2160p"),Row("Film HDTS 720p")],720),"screen recordings never satisfy HD Ready or 4K selections");
        foreach(var unavailable in new[]{(string?)null,""," ","not-a-torrent","file:///tmp/video.torrent","magnet:?xt=urn:btih:invalid"})
            Check(!ReleaseQuality.HasResolution([Row("Film 2160p") with{TorrentUrl=unavailable}],2160),"unavailable torrent URL cannot satisfy 4K: "+(unavailable??"null"));
        Check(!ReleaseQuality.HasResolution([Row("Film WEBRip")],720)&&!ReleaseQuality.HasResolution([Row("Film 1440p")],1080),"unknown and other resolutions cannot be advertised as the selected resolution");
        Check(ReleaseQuality.Downloadable(Row("Film 4K") with{TorrentUrl="https://downloads.example/film.torrent"})&&!ReleaseQuality.Downloadable(Row("Film 4K") with{TorrentUrl="https://user:password@downloads.example/film.torrent"}),"quality evidence accepts downloadable web links without URL credentials");
        var archive=Row("Archive film 1080p") with{Source="Internet Archive",TorrentUrl=null,PageUrl="https://archive.org/details/quality-film"};
        Check(ReleaseQuality.HasResolution([archive],1080)&&!ReleaseQuality.HasResolution([archive with{PageUrl="https://archive.org.attacker.example/details/quality-film"}],1080)&&!ReleaseQuality.HasResolution([archive with{PageUrl="https://archive.org/details/"}],1080),"resolvable Internet Archive entries retain quality evidence only for a valid archive item URL");
        var copied=item with{Title="Копия"};copied.SetReleaseQuality([Row("Film 1080p")]);
        Check(item.HasReleaseResolution(720)&&item.HasReleaseResolution(2160)&&!item.HasReleaseResolution(1080)&&copied.HasReleaseResolution(1080),"copying metadata does not let quality updates mutate another movie card");
        item.SetReleaseQuality([Row("Film HDCAM 2160p")]);
        Check(item.PosterQuality=="Экранка"&&item.HasPosterQuality&&!item.HasQuality,"screen capture uses a text poster badge instead of an advertised 4K resolution");
        item.SetReleaseQuality([Row("Film WEB-DL 480p")]);
        Check(item.PosterQuality=="SD","low-resolution clean video is not falsely labelled as a screen capture");
        item.SetReleaseQuality([Row("Film HDCAM"),Row("Film WEB-DL")]);
        Check(!item.HasPosterQuality,"unknown clean video does not invent a quality badge");
        var download=new DownloadItem{MediaTitle="Фильм",Name="file.mkv",ReleaseTitle="Фильм HDCAM 1080p"};
        Check(download.PosterQuality=="Экранка"&&download.PoorQuality,"queue quality uses release title rather than friendly film title or filename");
        var originalData=Environment.GetEnvironmentVariable("KACHALKA_DATA");
        var settingsFolder=Path.Combine(Preferences.DataDir,"quality-settings");
        try
        {
            Environment.SetEnvironmentVariable("KACHALKA_DATA",settingsFolder);
            Check(!new Preferences().Light&&!Preferences.Load().Light&&new Preferences().HidePoorQuality&&Preferences.Load().HidePoorQuality,"new installations hide poor quality by default");
            Directory.CreateDirectory(settingsFolder);
            var settings=Path.Combine(settingsFolder,"settings.json");
            File.WriteAllText(settings,JsonSerializer.Serialize(new{Light=true,MinimumReleaseHeight=1080}));
            Check(Preferences.Load().Light,"an explicitly selected light theme survives upgrades");
            var missing=Preferences.Load();
            Check(missing.HidePoorQuality&&missing.MinimumReleaseHeight==1080,"older settings without a quality preference use the enabled default and retain the quality minimum");
            Check(missing.CatalogQualityHeight==0,"older minimum-quality settings do not silently narrow the catalog to one resolution");
            File.WriteAllText(settings,JsonSerializer.Serialize(new{HidePoorQuality=false,MinimumReleaseHeight=720}));
            var disabled=Preferences.Load();
            Check(disabled.HidePoorQuality&&disabled.QualityFilterConfigured,"upgrading old serialized defaults enables quality filtering once");
            disabled.HidePoorQuality=false;
            disabled.Save();Check(!Preferences.Load().HidePoorQuality,"disabled quality choice survives saving and reloading");
            Check(Preferences.Load().QualityFilterConfigured,"saved quality choice records that the one-time default migration is complete");
            disabled.HidePoorQuality=true;disabled.MinimumReleaseHeight=1080;disabled.Save();
            var enabled=Preferences.Load();
            Check(enabled.HidePoorQuality&&enabled.MinimumReleaseHeight==1080,"enabled quality choice and minimum survive saving and reloading");
            enabled.CatalogQualityHeight=2160;enabled.Save();
            Check(Preferences.Load() is {CatalogQualityHeight:2160,MinimumReleaseHeight:1080,HidePoorQuality:true},"4K catalog choice persists independently of the existing release minimum and poor-quality flag");
            enabled.CatalogQualityHeight=720;enabled.HidePoorQuality=false;enabled.Save();
            Check(Preferences.Load() is {CatalogQualityHeight:720,MinimumReleaseHeight:1080,HidePoorQuality:false},"HD Ready catalog selection and disabled poor-quality option survive reload");
            enabled.CatalogQualityHeight=0;enabled.Save();
            Check(Preferences.Load().CatalogQualityHeight==0,"resetting a resolution selection persists the unrestricted catalog");
            File.WriteAllText(settings,JsonSerializer.Serialize(new{CatalogQualityHeight=9000,MinimumReleaseHeight=1080,QualityFilterConfigured=true}));
            Check(Preferences.Load() is {CatalogQualityHeight:0,MinimumReleaseHeight:1080},"unsupported saved catalog resolution returns to an unrestricted catalog without losing old preferences");
            var cachedItem=item with{PageUrl=LiveCatalog.Base+"/movies/quality-tests"};
            var index=new CatalogIndex(Path.Combine(settingsFolder,"cache"));
            index.CacheReleasesAsync(cachedItem,[Row("Film 720p"),Row("Film 2160p")],[new("Fixture",SourceState.Ready,2,DateTime.UtcNow,DateTime.UtcNow)]).GetAwaiter().GetResult();
            var reloaded=new CatalogIndex(Path.Combine(settingsFolder,"cache")).CachedReleaseSnapshot(cachedItem);
            Check(reloaded is {Items.Length:2}&&ReleaseQuality.HasResolution(reloaded.Items,720)&&ReleaseQuality.HasResolution(reloaded.Items,2160)&&!ReleaseQuality.HasResolution(reloaded.Items,1080),"cached releases retain every evidenced resolution after reopening the app");
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();var cancelledItem=cachedItem with{Id=2,PageUrl=LiveCatalog.Base+"/movies/quality-cancelled"};
            var interrupted=false;try{index.CacheReleasesAsync(cancelledItem,[Row("Film 1080p")],ct:cancelled.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){interrupted=true;}
            Check(interrupted&&index.CachedReleaseSnapshot(cancelledItem)==null,"cancelled quality checks cannot repopulate a cleared cache");
        }
        finally{Environment.SetEnvironmentVariable("KACHALKA_DATA",originalData);}
    }
}
