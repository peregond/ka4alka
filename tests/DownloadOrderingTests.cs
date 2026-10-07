using System.Text.Json;
using Kachalka;

static class DownloadOrderingTests
{
    public static async Task Run()
    {
        void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
        var start=new DateTime(2026,10,7,12,0,0,DateTimeKind.Utc);
        var old=new DownloadItem{Id="old",Name="Zulu.release.mkv",MediaTitle="Альфа",AddedUtc=start,DownloadRate=100,UploadRate=400,TotalBytes=200,Progress=80};
        var latest=new DownloadItem{Id="latest",Name="Alpha.release.mkv",MediaTitle="Бета",AddedUtc=start.AddMinutes(1),DownloadRate=900,TotalBytes=100,Progress=20};
        var unknown=new DownloadItem{Id="unknown",Name="Чарли",AddedUtc=start.AddMinutes(-1),DownloadRate=-100,UploadRate=-20,TotalBytes=null,Progress=double.NaN};
        DownloadItem[] items=[old,latest,unknown];
        string[] Sorted(DownloadSort order)=>DownloadOrdering.Sort(items,order).Select(x=>x.Id).ToArray();
        Check(Sorted(DownloadSort.Newest).SequenceEqual(["latest","old","unknown"]),"newest-added download is first without changing insertion order");
        Check(items[0]==old&&items[1]==latest,"sorting leaves the persisted queue and object identities untouched");
        Check(Sorted(DownloadSort.Name).SequenceEqual(["old","latest","unknown"]),"alphabetical sorting uses the associated media title");
        Check(Sorted(DownloadSort.Speed).SequenceEqual(["latest","old","unknown"]),"speed sorting includes active downloads and uploads and clamps negative rates");
        Check(Sorted(DownloadSort.Size).SequenceEqual(["old","latest","unknown"]),"size sorting puts larger downloads first and unknown sizes last");
        Check(Sorted(DownloadSort.Progress).SequenceEqual(["old","latest","unknown"]),"progress sorting handles an invalid progress value safely");
        var tieA=new DownloadItem{Id="a",AddedUtc=start,Name="Same",DownloadRate=20,UploadRate=30,TotalBytes=100,Progress=25};
        var tieB=new DownloadItem{Id="b",AddedUtc=start,Name="Same",DownloadRate=20,UploadRate=30,TotalBytes=100,Progress=25};
        foreach(var order in Enum.GetValues<DownloadSort>())Check(DownloadOrdering.Sort([tieB,tieA],order).Select(x=>x.Id).SequenceEqual(["a","b"]),"equal "+order+" keys have a stable identity order");
        var high=new DownloadItem{Id="high",AddedUtc=start,DownloadRate=long.MaxValue,UploadRate=long.MaxValue};
        Check(DownloadOrdering.Sort([latest,high],DownloadSort.Speed).First()==high,"large combined rates do not overflow into negative speed");
        old.DownloadRate=1500;old.UploadRate=0;
        Check(Sorted(DownloadSort.Speed).First()==old.Id,"speed ordering follows refreshed telemetry");

        var legacyOld=new DownloadItem{Id="legacy-old"};var legacyNew=new DownloadItem{Id="legacy-new"};
        var known=new DownloadItem{Id="known",AddedUtc=start};DownloadItem[] legacy=[legacyOld,known,legacyNew];
        Check(DownloadOrdering.AssignMissingAddedUtc(legacy,start.AddDays(1)),"legacy downloads receive missing addition times");
        Check(legacyOld.AddedUtc!=default&&legacyOld.AddedUtc<legacyNew.AddedUtc&&legacyNew.AddedUtc<known.AddedUtc&&known.AddedUtc==start,"legacy times preserve insertion order before known downloads");
        var dates=legacy.Select(x=>x.AddedUtc).ToArray();
        Check(!DownloadOrdering.AssignMissingAddedUtc(legacy,start.AddDays(2))&&legacy.Select(x=>x.AddedUtc).SequenceEqual(dates),"legacy migration is stable across application restarts");
        Check(DownloadOrdering.Sort(legacy,DownloadSort.Newest).Select(x=>x.Id).SequenceEqual(["known","legacy-new","legacy-old"]),"newest-first order also works for migrated legacy queues");

        var media=new MediaItem(42,"Северное сияние","Сериалы","драма",2025,"8.1","8.0","#526B69")
            {OriginalTitle="Northern Lights",ImageUrl="https://example.test/northern-lights.jpg",PageUrl="https://example.test/series/northern-lights"};
        var matched=new DownloadItem{Name="northern lights"};
        Check(DownloadOrdering.AssociateMedia(matched,[media])&&matched.MediaTitle==media.Title&&matched.MediaSection==media.Section&&matched.ImageUrl==media.ImageUrl,"legacy download can recover a poster through an exact original-title match");
        var shortMatch=new DownloadItem{Name="Northern"};
        Check(!DownloadOrdering.AssociateMedia(shortMatch,[media])&&shortMatch.ImageUrl==null,"partial torrent names cannot acquire an unrelated poster");
        var ambiguous=new DownloadItem{Name=media.Title};
        Check(!DownloadOrdering.AssociateMedia(ambiguous,[media,media with{Id=43,Section="Фильмы"}])&&ambiguous.ImageUrl==null,"ambiguous film and series names keep their neutral poster placeholder");
        var existing=new DownloadItem{Name=media.Title,MediaTitle="Собственная карточка",MediaSection="Фильмы",ImageUrl="https://example.test/preserved.jpg"};
        DownloadOrdering.AssociateMedia(existing,[media]);
        Check(existing.ImageUrl=="https://example.test/preserved.jpg"&&existing.MediaTitle=="Собственная карточка","a legacy lookup preserves explicit download metadata");
        var releaseName="Northern.Lights.2025.S01E02.1080p.WEB-DL.mkv";
        var tagged=new DownloadItem{Name=releaseName};
        Check(DownloadMetadata.Associate(tagged,[media])&&tagged.MediaTitle==media.Title&&tagged.MediaSection==media.Section&&tagged.MediaYear==media.Year&&tagged.ImageUrl==media.ImageUrl,"legacy torrent title and year recover Russian card metadata despite episode and quality tags");
        Check(tagged.Name==releaseName,"metadata repair retains the original torrent release filename");
        var wrongYear=new DownloadItem{Name="Northern.Lights.2024.S01E02.1080p.WEB-DL.mkv"};
        Check(!DownloadMetadata.Associate(wrongYear,[media])&&wrongYear.ImageUrl==null,"a mismatching release year cannot acquire another work's poster");
        var remake=media with{Id=44,Year=2020,PageUrl="https://example.test/series/northern-lights-2020"};
        var undated=new DownloadItem{Name="Northern.Lights.1080p.WEB-DL.mkv"};
        Check(!DownloadMetadata.Associate(undated,[media,remake])&&undated.MediaTitle==null,"an undated release keeps its placeholder when remakes share a title");
        var dated=new DownloadItem{Name=releaseName};
        Check(DownloadMetadata.Associate(dated,[remake,media])&&dated.MediaYear==2025,"the release year disambiguates identically named works");
        var solaris=new MediaItem(50,"Солярис","Фильмы","фантастика",1972,"8.0","8.0","#526B69"){OriginalTitle="Solaris",ImageUrl="https://example.test/solaris-1972.jpg"};
        var solarisRemake=solaris with{Id=51,Year=2002,ImageUrl="https://example.test/solaris-2002.jpg"};
        var bilingual=new DownloadItem{Name="Solaris / Солярис (2002) 1080p"};
        Check(!DownloadMetadata.Associate(bilingual,[solaris])&&bilingual.ImageUrl==null,"a bare original-title alias cannot bypass the remake year in a bilingual release name");
        Check(DownloadMetadata.Associate(bilingual,[solaris,solarisRemake])&&bilingual.MediaYear==2002&&bilingual.ImageUrl==solarisRemake.ImageUrl,"bilingual release names select the correct remake using their stated year");
        var numbered=new MediaItem(52,"Бегущий по лезвию 2049","Фильмы","фантастика",2017,"8.0","8.0","#526B69"){OriginalTitle="Blade Runner 2049",ImageUrl="https://example.test/blade-runner.jpg"};
        Check(DownloadMetadata.Associate(new DownloadItem{Name="Blade.Runner.2049.2017.1080p.WEB-DL.mkv"},[numbered]),"a year-like number inside the actual title is preserved during release matching");
        var partial=media with{Title="Northern Lights",ImageUrl="",OriginalTitle=null};var enriched=DownloadMetadata.EnrichMedia(partial,[partial,media]);
        Check(enriched.Title==media.Title&&enriched.ImageUrl==media.ImageUrl,"selected card identity enriches an English title and missing poster from its Russian catalog entry");
        var byPage=new DownloadItem{Name="Unrelated.Tracker.Release.1080p",MediaPageUrl=media.PageUrl};
        Check(DownloadMetadata.Associate(byPage,[media,remake])&&byPage.MediaTitle==media.Title&&byPage.ImageUrl==media.ImageUrl,"persisted card identity restores metadata even when torrent naming is unrelated");
        var hash=new string('e',40);var cachedRelease=new SourceEntry("cache-release","Northern Lights (2025) 1080p","Fixture","https://example.test/source/release","magnet:?xt=urn:btih:"+hash,null);
        var byHash=new DownloadItem{Name="Unrelated.Tracker.Release.1080p",InfoHash=hash};
        Check(DownloadMetadata.Associate(byHash,[media,remake],candidate=>candidate.Id==media.Id?[cachedRelease]:[])&&byHash.MediaTitle==media.Title,"cached release hash restores the correct card for an unrecognizable legacy torrent name");
        var conflictingHash=new DownloadItem{Name="Unrelated.Tracker.Release.1080p",InfoHash=hash};
        Check(!DownloadMetadata.Associate(conflictingHash,[media,remake],_=>[cachedRelease])&&conflictingHash.ImageUrl==null,"conflicting cached release identities cannot silently choose an unrelated cover");
        var localRelease=cachedRelease with{TorrentUrl="http://localhost:9696/download?id=1"};
        var otherIndexer=new DownloadItem{Name="Unrecognizable.Tracker.Release",Source="http://localhost:9117/download?id=1"};
        Check(!DownloadMetadata.Associate(otherIndexer,[media],_=>[localRelease]),"different Torznab ports cannot associate an unrelated release poster");
        var sameIndexer=new DownloadItem{Name=otherIndexer.Name,Source=localRelease.TorrentUrl!};
        Check(DownloadMetadata.Associate(sameIndexer,[media],_=>[localRelease])&&sameIndexer.MediaTitle==media.Title,"an exact Torznab URL still restores its own card metadata");
        var persisted=JsonSerializer.Deserialize<DownloadItem>(JsonSerializer.Serialize(old))!;
        Check(persisted.AddedUtc==old.AddedUtc&&persisted.MediaTitle==old.MediaTitle,"addition dates and associated media titles survive queue serialization");
        Check(persisted.DownloadRate==0&&persisted.UploadRate==0,"live rates are not restored as stale network activity");

        var originalData=Environment.GetEnvironmentVariable("KACHALKA_DATA");
        try
        {
            var legacyData=Path.Combine(Preferences.DataDir,"legacy-queue-tests");Directory.CreateDirectory(legacyData);Environment.SetEnvironmentVariable("KACHALKA_DATA",legacyData);
            var cacheIndex=new CatalogIndex(legacyData);var cacheMedia=media with{PageUrl="https://zona.plus/tvseries/northern-lights-fixture"};var revision=cacheIndex.Revision;
            await cacheIndex.CacheReleasesAsync(cacheMedia,[cachedRelease]);
            Check(cacheIndex.Revision>revision&&cacheIndex.CachedReleases(cacheMedia).Count==1,"saving new release identities signals metadata repair even when the catalog card is unchanged");
            File.WriteAllText(Path.Combine(legacyData,"queue.json"),JsonSerializer.Serialize(new[]{new DownloadItem{Id="first",Name="First"},new DownloadItem{Id="second",Name="Second"}}));
            var migrated=new DownloadService();var migratedDates=migrated.Items.Select(x=>x.AddedUtc).ToArray();
            Check(migratedDates.All(x=>x!=default)&&migratedDates[0]<migratedDates[1]&&!migrated.EngineCreated,"restoring an old queue assigns addition dates without starting the torrent engine");
            var reopened=new DownloadService();
            Check(reopened.Items.Select(x=>x.AddedUtc).SequenceEqual(migratedDates),"legacy queue addition dates are persisted once and retained after restart");
            Check(DownloadOrdering.Sort(reopened.Items,DownloadSort.Newest).First().Id=="second","restored legacy queue presents its latest addition first");
            var releaseIndex=Path.Combine(legacyData,"release-index");Directory.CreateDirectory(releaseIndex);
            var cacheKey=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(media.Section+"|"+media.PageUrl)));
            File.WriteAllText(Path.Combine(releaseIndex,cacheKey+".json"),JsonSerializer.Serialize(new ReleaseCache(DateTime.UtcNow.AddDays(-90),[cachedRelease])));
            File.WriteAllText(Path.Combine(legacyData,"queue.json"),JsonSerializer.Serialize(new[]{new DownloadItem{Id="restored-release",Name=releaseName,Paused=true},new DownloadItem{Id="restored-hash",Name="Unrecognizable.Tracker.Release.1080p",InfoHash=hash,Paused=true}}));
            var legacyQueue=new DownloadService();var legacyAdded=legacyQueue.Items.ToDictionary(x=>x.Id,x=>x.AddedUtc);
            Check(await DownloadMetadata.RepairAsync(legacyQueue,[media])==2&&legacyQueue.Items.All(x=>x.MediaTitle==media.Title&&x.ImageUrl==media.ImageUrl)&&!legacyQueue.EngineCreated,"background metadata repair updates an actual restored queue using release titles and historical cached torrent identity");
            var repairedQueue=new DownloadService();
            Check(repairedQueue.Items.All(x=>x.MediaTitle==media.Title&&x.ImageUrl==media.ImageUrl&&x.AddedUtc==legacyAdded[x.Id])&&repairedQueue.Items.Single(x=>x.Id=="restored-release").Name==releaseName,"restored queue metadata repair persists its cover and Russian title while keeping release name and order");
            Check(await DownloadMetadata.RepairAsync(repairedQueue,[media])==0,"metadata repair is stable after a repaired queue is reopened");
        }
        finally{Environment.SetEnvironmentVariable("KACHALKA_DATA",originalData);}
    }
}
