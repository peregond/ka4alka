using Kachalka;
static class ReleaseQualityTests
{
    public static void Run()
    {
        static SourceEntry Row(string title)=>new(title,title,"Test","","magnet:?xt=urn:btih:"+new string('1',40),null);
        static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS: "+name);}
        foreach(var title in new[]{"Film HDCAM 1080p","Film HDTS 2160p","Film CAMRip 1080p","Film DVDSCR 1080p","The Weight (2026) Screener [H.264/1080p]","Фильм экранка 4K","Film WEB-DL 480p","Film 640x360","Film DVDRip"})
            Check(ReleaseQuality.Poor(Row(title)),"poor video source recognized: "+title);
        foreach(var title in new[]{"Film WEB-DL 720p","Film 1080p BluRay DTS","Film 4K","Film 1920x1080","Cambridge (2026) WEB-DL","Film HDTV","Film WEBRip"})
            Check(!ReleaseQuality.Poor(Row(title)),"HD or unknown source is not falsely marked: "+title);
        Check(ReleaseQuality.Poor(Row("Film 720p"),1080)&&!ReleaseQuality.Poor(Row("Film 1080p"),1080),"Full HD minimum hides 720p and keeps 1080p");
        Check(!ReleaseQuality.OnlyPoor([])&&!ReleaseQuality.OnlyPoor([Row("Film HDCAM"),Row("Film 1080p")])&&!ReleaseQuality.OnlyPoor([Row("Film HDCAM"),Row("Film WEB-DL")])&&ReleaseQuality.OnlyPoor([Row("Film HDCAM"),Row("Film 480p")]),"catalog only marks titles with exclusively known poor releases");
        var item=new MediaItem(1,"Фильм","Фильмы","",2026,"—","—","#526B69");var changed=0;item.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(MediaItem.OnlyPoorQuality))changed++;};
        item.SetReleaseQuality([Row("Film HDCAM")]);item.SetReleaseQuality([Row("Film HDCAM"),Row("Film 1080p")]);
        Check(!item.OnlyPoorQuality&&changed==2,"catalog quality updates existing bound cards when a good release arrives");
        var download=new DownloadItem{MediaTitle="Фильм",Name="file.mkv",ReleaseTitle="Фильм HDCAM 1080p"};
        Check(download.PoorQuality,"queue quality uses release title rather than friendly film title or filename");
    }
}
