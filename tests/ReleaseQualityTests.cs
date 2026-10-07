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
        var download=new DownloadItem{MediaTitle="Фильм",Name="file.mkv",ReleaseTitle="Фильм HDCAM 1080p"};
        Check(download.PoorQuality,"queue quality uses release title rather than friendly film title or filename");
        var originalData=Environment.GetEnvironmentVariable("KACHALKA_DATA");
        var settingsFolder=Path.Combine(Preferences.DataDir,"quality-settings");
        try
        {
            Environment.SetEnvironmentVariable("KACHALKA_DATA",settingsFolder);
            Check(new Preferences().HidePoorQuality&&Preferences.Load().HidePoorQuality,"new installations hide poor quality by default");
            Directory.CreateDirectory(settingsFolder);
            var settings=Path.Combine(settingsFolder,"settings.json");
            File.WriteAllText(settings,JsonSerializer.Serialize(new{MinimumReleaseHeight=1080}));
            var missing=Preferences.Load();
            Check(missing.HidePoorQuality&&missing.MinimumReleaseHeight==1080,"older settings without a quality preference use the enabled default and retain the quality minimum");
            File.WriteAllText(settings,JsonSerializer.Serialize(new{HidePoorQuality=false,MinimumReleaseHeight=720}));
            var disabled=Preferences.Load();
            Check(disabled.HidePoorQuality&&disabled.QualityFilterConfigured,"upgrading old serialized defaults enables quality filtering once");
            disabled.HidePoorQuality=false;
            disabled.Save();Check(!Preferences.Load().HidePoorQuality,"disabled quality choice survives saving and reloading");
            Check(Preferences.Load().QualityFilterConfigured,"saved quality choice records that the one-time default migration is complete");
            disabled.HidePoorQuality=true;disabled.MinimumReleaseHeight=1080;disabled.Save();
            var enabled=Preferences.Load();
            Check(enabled.HidePoorQuality&&enabled.MinimumReleaseHeight==1080,"enabled quality choice and minimum survive saving and reloading");
        }
        finally{Environment.SetEnvironmentVariable("KACHALKA_DATA",originalData);}
    }
}
