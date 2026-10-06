using System.Text.Json;
using Kachalka;

static class DownloadOrderingTests
{
    public static void Run()
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
            {OriginalTitle="Northern Lights",ImageUrl="https://example.test/northern-lights.jpg"};
        var matched=new DownloadItem{Name="northern lights"};
        Check(DownloadOrdering.AssociateMedia(matched,[media])&&matched.MediaTitle==media.Title&&matched.MediaSection==media.Section&&matched.ImageUrl==media.ImageUrl,"legacy download can recover a poster through an exact original-title match");
        var shortMatch=new DownloadItem{Name="Northern"};
        Check(!DownloadOrdering.AssociateMedia(shortMatch,[media])&&shortMatch.ImageUrl==null,"partial torrent names cannot acquire an unrelated poster");
        var ambiguous=new DownloadItem{Name=media.Title};
        Check(!DownloadOrdering.AssociateMedia(ambiguous,[media,media with{Id=43,Section="Фильмы"}])&&ambiguous.ImageUrl==null,"ambiguous film and series names keep their neutral poster placeholder");
        var existing=new DownloadItem{Name=media.Title,MediaTitle="Собственная карточка",MediaSection="Фильмы",ImageUrl="https://example.test/preserved.jpg"};
        DownloadOrdering.AssociateMedia(existing,[media]);
        Check(existing.ImageUrl=="https://example.test/preserved.jpg"&&existing.MediaTitle=="Собственная карточка","a legacy lookup preserves explicit download metadata");
        var persisted=JsonSerializer.Deserialize<DownloadItem>(JsonSerializer.Serialize(old))!;
        Check(persisted.AddedUtc==old.AddedUtc&&persisted.MediaTitle==old.MediaTitle,"addition dates and associated media titles survive queue serialization");
        Check(persisted.DownloadRate==0&&persisted.UploadRate==0,"live rates are not restored as stale network activity");

        var originalData=Environment.GetEnvironmentVariable("KACHALKA_DATA");
        try
        {
            var legacyData=Path.Combine(Preferences.DataDir,"legacy-queue-tests");Directory.CreateDirectory(legacyData);Environment.SetEnvironmentVariable("KACHALKA_DATA",legacyData);
            File.WriteAllText(Path.Combine(legacyData,"queue.json"),JsonSerializer.Serialize(new[]{new DownloadItem{Id="first",Name="First"},new DownloadItem{Id="second",Name="Second"}}));
            var migrated=new DownloadService();var migratedDates=migrated.Items.Select(x=>x.AddedUtc).ToArray();
            Check(migratedDates.All(x=>x!=default)&&migratedDates[0]<migratedDates[1]&&!migrated.EngineCreated,"restoring an old queue assigns addition dates without starting the torrent engine");
            var reopened=new DownloadService();
            Check(reopened.Items.Select(x=>x.AddedUtc).SequenceEqual(migratedDates),"legacy queue addition dates are persisted once and retained after restart");
            Check(DownloadOrdering.Sort(reopened.Items,DownloadSort.Newest).First().Id=="second","restored legacy queue presents its latest addition first");
        }
        finally{Environment.SetEnvironmentVariable("KACHALKA_DATA",originalData);}
    }
}
