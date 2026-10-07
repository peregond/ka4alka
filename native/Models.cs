using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using System.Windows.Media;
namespace Kachalka;
public record MediaItem(int Id, string Title, string Section, string Genre, int Year, string Kinopoisk, string Imdb, string Color) : INotifyPropertyChanged
{
    public string? PageUrl {get;init;}
    public string? OnlineId {get;init;}
    public string? ImageUrl {get;init;}
    public string? Description {get;init;}
    public string? OriginalTitle {get;init;}
    public string[] GenreKeys {get;init;}=[];
    public string[] CountryKeys {get;init;}=[];
    public string Country {get;init;}="";
    [JsonIgnore] public bool IsLive => PageUrl!=null;
    [JsonIgnore] public bool Cinema => Section is "Фильмы" or "Сериалы";
    string? liveScores,liveKp,liveImdb,liveGenre;
    [JsonIgnore] public string KpDisplay => "КП  "+(liveKp??Kinopoisk);
    [JsonIgnore] public string ImdbDisplay => "IMDb  "+(liveImdb??Imdb);
    [JsonIgnore] public bool KpAvailable => !string.IsNullOrWhiteSpace(liveKp??Kinopoisk)&&(liveKp??Kinopoisk)!="—";
    [JsonIgnore] public bool ImdbAvailable => !string.IsNullOrWhiteSpace(liveImdb??Imdb)&&(liveImdb??Imdb)!="—";
    [JsonIgnore] public string CardRating => KpAvailable?(liveKp??Kinopoisk):ImdbAvailable?(liveImdb??Imdb):"—";
    [JsonIgnore] public string CardRatingSource => KpAvailable?"Кинопоиск":ImdbAvailable?"IMDb":"Оценка пока недоступна";
    [JsonIgnore] public string CardGenre => string.IsNullOrWhiteSpace(liveGenre??Genre)?Section=="Сериалы"?"Сериал":"Фильм":liveGenre??Genre;
    string bestQuality="";
    [JsonIgnore] public string BestQuality=>bestQuality;
    [JsonIgnore] public bool HasQuality=>bestQuality.Length>0;
    [JsonIgnore] public string Scores => liveScores ?? (Cinema ? $"КП {Kinopoisk}   IMDb {Imdb}" : Section);
    public event PropertyChangedEventHandler? PropertyChanged;
    [JsonIgnore] public bool OnlyPoorQuality {get;private set;}
    public void SetReleaseQuality(IEnumerable<SourceEntry> entries,int minimum=720)
    {
        var rows=entries.ToArray();
        var value=ReleaseQuality.OnlyPoor(rows,minimum);
        var height=rows.Where(x=>(x.TorrentUrl!=null||x.Source=="Internet Archive")&&!ReleaseQuality.Poor(x,720)).Select(x=>ReleaseQuality.Height(x)??0).DefaultIfEmpty(0).Max();
        var quality=height>=2160?"4K":height>=1080?"Full HD":height>=720?"HD Ready":"";
        if(value!=OnlyPoorQuality){OnlyPoorQuality=value;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(OnlyPoorQuality)));}
        if(quality!=bestQuality){bestQuality=quality;PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(BestQuality)));PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(nameof(HasQuality)));}
    }
    public void SetScores(string kp,string imdb,string? genre=null){if(!string.IsNullOrWhiteSpace(genre))liveGenre=genre;liveKp=kp;liveImdb=imdb;liveScores=$"КП {kp}   IMDb {imdb}";PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(null));}
    [JsonIgnore] public string Subtitle => Cinema ? string.Join(" · ",new[]{Section=="Сериалы"?"Сериал":"Фильм",Year>0?Year.ToString():null,Genre}.Where(x=>!string.IsNullOrWhiteSpace(x))) : "Демонстрационный каталог";
    [JsonIgnore] public Brush Cover { get { var b = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(Color), (Color)ColorConverter.ConvertFromString("#20262E"), 75); b.Freeze(); return b; } }
}
public record CatalogRow(MediaItem[] Items,int Columns);
public record Release(int Id,string Quality,string Type,string Voice,string Subs,string Format,string Codec,string Hdr,double Size,int Seeds,string Source);
public static class Catalog
{
    public static readonly string[] Sections=["Фильмы","Сериалы","Игры","Музыка","ТВ-каналы","Радио","Спорт","Программы"];
    static readonly string[][] Titles=[
        ["За пределами тишины","Последний рейс","Тёплый сентябрь","Город без сна","Глубина","Письма к морю","Орбита времени","На краю света"],
        ["Северная линия","Обратная сторона","Наш двор","Точка отсчёта","Тихая гавань","Нулевой день"],
        ["Outer Fields","Neon Drift","Forest Tales","Dust Runner"],["Midnight Echoes","Тёплый воздух","Beyond the Waves","Soft Signals"],
        ["Кино 24","Мир природы","Наука рядом","Путешествия"],["Тихая волна","Джаз вечером","Ритм города","Фоновая станция"],
        ["Футбол","Теннис","Баскетбол","Автоспорт"],["Pixel Studio","Orbit Player","Write Space","File Nest"]];
    static readonly string[] Colors=["#5C8179","#A4684C","#AC965F","#796296","#3F7785","#9A747A","#5E7697","#927B5E"];
    public static readonly MediaItem[] Items=Titles.SelectMany((titles,s)=>titles.Select((name,i)=>new MediaItem(s*10+i,name,Sections[s],new[]{"Фантастика","Триллер","Драма","Приключения"}[i%4],2025-i%3,(8.7-i*.2).ToString("F1"),(8.4-i*.2).ToString("F1"),Colors[i%8]))).ToArray();
    public static readonly Release[] Releases=[
        new(0,"1080p","WEB-DL","Дубляж","RU + EN","MKV","H.264","SDR",8.4,342,"Open Collection"),
        new(1,"2160p","BluRay","Дубляж","RU + EN","MKV","H.265","HDR10",24.1,128,"Media Archive"),
        new(2,"720p","WEB-DL","Многоголосая","RU","MP4","H.264","SDR",2.6,87,"Community Library"),
        new(3,"1080p","BluRay","Оригинал","EN","MKV","H.265","SDR",10.2,211,"Media Archive"),
        new(4,"1080p","WEB-DL","Многоголосая","Нет","MP4","H.264","SDR",5.8,165,"Community Library"),
        new(5,"2160p","WEB-DL","Оригинал","RU + EN","MKV","H.265","Dolby Vision",18.6,294,"Open Collection"),
        new(6,"720p","BluRay","Дубляж","Нет","MP4","H.264","SDR",3.2,69,"Media Archive"),
        new(7,"2160p","BluRay","Многоголосая","RU","MKV","H.265","HDR10",31.5,76,"Community Library"),
        new(8,"1080p","BluRay","Дубляж","RU + EN","MKV","H.264","SDR",12.8,184,"Open Collection")];
}
public class Preferences
{
    public string Folder { get;set; }=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads","Ka4alka");
    public bool FolderConfigured {get;set;}
    public bool Light {get;set;}
    public int MaxDownloadKbps {get;set;}
    public int MaxUploadKbps {get;set;}
    public string DownloadSort {get;set;}="newest";
    public bool Economy {get;set;}=true;
    public bool AutoResumeDownloads {get;set;}=true;
    public bool CheckForUpdates {get;set;}=true;
    public bool AutoUpdate {get;set;}=true;
    public bool HidePoorQuality {get;set;}=true;
    public bool QualityFilterConfigured {get;set;}
    public int MinimumReleaseHeight {get;set;}=720;
    public HashSet<int> Favorites {get;set;}=[];
    public List<MediaItem> LiveFavorites {get;set;}=[];
    public static string DataDir=>Environment.GetEnvironmentVariable("KACHALKA_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Kachalka");
    public static Preferences Load()
    {
        try
        {
            var prefs=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(DataDir,"settings.json")))??new();
            if(!prefs.QualityFilterConfigured){prefs.HidePoorQuality=true;prefs.QualityFilterConfigured=true;}
            return prefs;
        }
        catch{return new(){QualityFilterConfigured=true};}
    }
    public void Save(){QualityFilterConfigured=true;Directory.CreateDirectory(DataDir);var p=Path.Combine(DataDir,"settings.json");File.WriteAllText(p+".tmp",JsonSerializer.Serialize(this));File.Move(p+".tmp",p,true);}
}
public record DownloadFile(string Name,string FullPath,string IncompletePath,long Size,double Progress)
{
    [JsonIgnore] public string Summary=>$"{Math.Clamp(Progress,0,100):F1}% · {DownloadService.FormatBytes(Size)}";
}
public class DownloadItem : INotifyPropertyChanged
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Source {get;set;}="";
    public string? InfoHash {get;set;}
    public string Folder {get;set;}="";
    public string Name {get;set;}="Получение метаданных…";
    public DateTime AddedUtc {get;set;}
    public string? ImageUrl {get;set;}
    public string? MediaTitle {get;set;}
    public string? MediaSection {get;set;}
    public string? MediaPageUrl {get;set;}
    public int MediaYear {get;set;}
    public string? ReleaseTitle {get;set;}
    public string? ReleaseSource {get;set;}
    public string? ReleaseId {get;set;}
    public string? ReleasePageUrl {get;set;}
    public string? ReleaseUrl {get;set;}
    [JsonIgnore] public string DisplayName=>string.IsNullOrWhiteSpace(MediaTitle)?Name:MediaTitle;
    [JsonIgnore] public bool HasMediaCard=>DownloadMetadata.Card(this)!=null;
    [JsonIgnore] public int MinimumQualityHeight {get;set;}=720;
    [JsonIgnore] public bool PoorQuality=>ReleaseQuality.Poor(new SourceEntry("",string.IsNullOrWhiteSpace(ReleaseTitle)?Name:ReleaseTitle,"","",null,null),MinimumQualityHeight);
    [JsonIgnore] public long DownloadRate {get;set;}
    [JsonIgnore] public long UploadRate {get;set;}
    [JsonIgnore] public long? TotalBytes {get;set;}
    [JsonIgnore] public string RateSummary=>$"↓ {DownloadService.FormatBytes(Math.Max(0,DownloadRate))}/с · ↑ {DownloadService.FormatBytes(Math.Max(0,UploadRate))}/с";
    [JsonIgnore] public string SizeSummary=>TotalBytes.HasValue&&TotalBytes.Value>=0?DownloadService.FormatBytes(TotalBytes.Value):"Размер уточняется";
    [JsonIgnore] public string CompletionLabel=>$"{(double.IsFinite(Progress)?Math.Clamp(Progress,0,100):0):F1}%";
    public string Status {get;set;}="На паузе";
    public double Progress {get;set;}
    public List<DownloadFile> Files {get;set;}=[];
    public string Stats {get;set;}="";
    public string Hint {get;set;}="";
    [JsonIgnore] public string PeersText {get;set;}="";
    [JsonIgnore] public string Remaining {get;set;}="";
    [JsonIgnore] public bool Indeterminate {get;set;}
    public DateTime LastStartedUtc {get;set;}
    public bool Busy {get;set;}
    public bool Paused {get;set;}=true;
    [JsonIgnore] public bool Completed=>Progress>=100;
    public string Action => Paused?"Продолжить":"Пауза";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh()=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(null));
}
