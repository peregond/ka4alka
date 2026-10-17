using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Kachalka;

// Award lists (Academy Awards, Cannes and the like) come from our own backend as one small file next to the shared
// catalog: the winners of every year with their IMDb ids. The app shows a winner like any other title; its details
// come from the site's /api/title and its releases are searched the usual way. Nothing here asks another source.
public sealed record AwardEntry(int Year,string ImdbId,string Title,string TitleEn,string OriginalTitle,int FilmYear,string? Poster);
public sealed record AwardCollection(string Id,string Title,string TitleEn,string Description,string DescriptionEn,AwardEntry[] Entries);

public sealed partial class AwardsCatalog(SourceClient client,Func<DateTime>? utcNow=null)
{
    public static readonly Uri FeedUri=new("https://github.com/peregond/ka4alka/releases/download/catalog-data/awards.json");
    public const int MaxBytes=512*1024,MaxEntries=300,MaxAwards=12;
    static readonly TimeSpan RefreshInterval=TimeSpan.FromDays(1),RetryInterval=TimeSpan.FromMinutes(30);
    readonly Func<DateTime> clock=utcNow??(()=>DateTime.UtcNow);
    readonly string path=Path.Combine(Preferences.DataDir,"awards.json");
    readonly SemaphoreSlim gate=new(1,1);
    AwardCollection[] collections=[];
    DateTime nextTryUtc;
    bool restored;
    string? contentHash;
    public IReadOnlyList<AwardCollection> All=>collections;

    // Strict reading: an unknown shape, a bad id or an oversized file discards the whole answer, never part of it.
    public static AwardCollection[] Parse(byte[] bytes)
    {
        if(bytes.Length is 0 or >MaxBytes)throw new InvalidDataException("Неверный размер списка наград.");
        using var json=JsonDocument.Parse(bytes);var root=json.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("schemaVersion",out var version)||version.ValueKind!=JsonValueKind.Number||version.GetInt32()!=1)throw new InvalidDataException("Неизвестная версия списка наград.");
        if(!root.TryGetProperty("awards",out var awards)||awards.ValueKind!=JsonValueKind.Array||awards.GetArrayLength()>MaxAwards)throw new InvalidDataException("Неверный список наград.");
        var result=new List<AwardCollection>();var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var award in awards.EnumerateArray())
        {
            if(award.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Неверная награда.");
            var id=Text(award,"id",60);
            if(!AwardId().IsMatch(id)||!ids.Add(id))throw new InvalidDataException("Неверный код награды.");
            var title=Text(award,"title",120);if(title.Length==0)throw new InvalidDataException("У награды нет названия.");
            if(!award.TryGetProperty("entries",out var rows)||rows.ValueKind!=JsonValueKind.Array||rows.GetArrayLength() is 0 or >MaxEntries)throw new InvalidDataException("Неверный состав награды.");
            var entries=new List<AwardEntry>();var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var row in rows.EnumerateArray())
            {
                var entry=ReadEntry(row);
                if(seen.Add(entry.ImdbId))entries.Add(entry);
            }
            result.Add(new(id,title,Text(award,"titleEn",120).Length>0?Text(award,"titleEn",120):title,Text(award,"description",300),Text(award,"descriptionEn",300),
                entries.OrderByDescending(entry=>entry.Year).ThenBy(entry=>entry.Title,StringComparer.CurrentCulture).ToArray()));
        }
        return result.ToArray();
    }
    static AwardEntry ReadEntry(JsonElement row)
    {
        if(row.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Неверная запись награды.");
        var imdb=Text(row,"imdbId",12);
        if(!TitleId().IsMatch(imdb))throw new InvalidDataException("Неверный код IMDb.");
        var year=Number(row,"year");
        if(year is <1900 or >2100)throw new InvalidDataException("Неверный год награды.");
        var original=Text(row,"originalTitle",200);var name=Text(row,"title",200);var nameEn=Text(row,"titleEn",200);
        if((name.Length==0?(nameEn.Length==0?original:nameEn):name).Length==0)throw new InvalidDataException("У записи награды нет названия.");
        var filmYear=Number(row,"filmYear");if(filmYear is not (0 or (>=1880 and <=2100)))throw new InvalidDataException("Неверный год фильма.");
        var poster=Text(row,"poster",400);
        // Only https images; anything else is dropped, the card then keeps its plain colour.
        if(poster.Length>0&&!(Uri.TryCreate(poster,UriKind.Absolute,out var uri)&&uri.Scheme==Uri.UriSchemeHttps))poster="";
        return new(year,imdb,name,nameEn,original,filmYear,poster.Length>0?poster:null);
    }
    static string Text(JsonElement row,string name,int max)=>row.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?(value.GetString()??"").Trim() is {} text&&text.Length<=max?text:throw new InvalidDataException("Слишком длинное значение."):"";
    static int Number(JsonElement row,string name)=>row.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out var number)?number:0;

    // A card for the entry. A title the Russian catalog already has keeps its own card: its page, quality and release data.
    public static MediaItem Item(AwardEntry entry,IReadOnlyDictionary<string,MediaItem>? catalog=null)
    {
        if(catalog!=null&&catalog.TryGetValue(entry.ImdbId,out var known))return known;
        var title=AppLanguage.Current=="en"?(entry.TitleEn.Length>0?entry.TitleEn:entry.OriginalTitle.Length>0?entry.OriginalTitle:entry.Title):(entry.Title.Length>0?entry.Title:entry.TitleEn.Length>0?entry.TitleEn:entry.OriginalTitle);
        return ImdbSuggestions.Card(entry.ImdbId,title,entry.OriginalTitle.Length>0?entry.OriginalTitle:null,"Фильмы",entry.FilmYear,entry.Poster);
    }

    // The saved copy is read once; a missing or damaged one just means there are no award lists yet.
    public async Task RestoreAsync(CancellationToken ct=default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(restored)return;restored=true;
            try
            {
                if(File.Exists(path)&&new FileInfo(path).Length<=MaxBytes)
                {
                    var saved=await CacheFiles.ReadAllBytesAsync(path,CancellationToken.None);
                    collections=await Task.Run(()=>Parse(saved),CancellationToken.None);contentHash=Convert.ToHexString(SHA256.HashData(saved));
                }
            }
            catch(Exception error)when(error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or FormatException){collections=[];}
        }
        finally{gate.Release();}
    }
    // Checks the backend at most daily; true when the lists changed. A missing file (not published yet) or a broken one keeps what we have.
    public async Task<bool> RefreshAsync(CancellationToken ct=default,bool force=false)
    {
        await RestoreAsync(ct);
        await gate.WaitAsync(ct);
        try
        {
            var now=clock();
            // A saved copy that did not parse (no lists in memory) is not a reason to wait a day.
            if(!force&&(now<nextTryUtc||collections.Length>0&&File.Exists(path)&&now-File.GetLastWriteTimeUtc(path)<RefreshInterval))return false;
            nextTryUtc=now+RetryInterval;
            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var bytes=await client.Read(FeedUri,MaxBytes,timeout.Token);
                var fresh=Parse(bytes);
                var hash=Convert.ToHexString(SHA256.HashData(bytes));var changed=hash!=contentHash||!SameLists(collections,fresh);collections=fresh;contentHash=hash;
                try{Directory.CreateDirectory(Path.GetDirectoryName(path)!);await CacheFiles.WriteAllBytesAsync(path,bytes,CancellationToken.None);}catch(IOException){}catch(UnauthorizedAccessException){}
                return changed;
            }
            catch(Exception error)when(error is not OperationCanceledException||!ct.IsCancellationRequested){return false;}
        }
        finally{gate.Release();}
    }
    // ── Nominees ──
    // A second, larger file: the nominees of every year. Only the page of one award asks for it, so the first screen
    // never waits for it, and an app that does not know it keeps working with the winners alone.
    public static readonly Uri NomineesUri=new("https://github.com/peregond/ka4alka/releases/download/catalog-data/awards-nominees.json");
    public const int NomineesMaxBytes=1536*1024,NomineesMaxEntries=1500;
    readonly string nomineesPath=Path.Combine(Preferences.DataDir,"awards-nominees.json");
    Dictionary<string,AwardEntry[]> nominees=[];
    bool nomineesRestored;
    DateTime nomineesNextTryUtc;
    string? nomineesHash;
    public IReadOnlyList<AwardEntry> NomineesOf(string awardId)=>nominees.TryGetValue(awardId,out var rows)?rows:[];

    public static Dictionary<string,AwardEntry[]> ParseNominees(byte[] bytes)
    {
        if(bytes.Length is 0 or >NomineesMaxBytes)throw new InvalidDataException("Неверный размер списка номинантов.");
        using var json=JsonDocument.Parse(bytes);var root=json.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("schemaVersion",out var version)||version.ValueKind!=JsonValueKind.Number||version.GetInt32()!=1)throw new InvalidDataException("Неизвестная версия списка номинантов.");
        if(!root.TryGetProperty("awards",out var awards)||awards.ValueKind!=JsonValueKind.Array||awards.GetArrayLength()>MaxAwards)throw new InvalidDataException("Неверный список номинантов.");
        var result=new Dictionary<string,AwardEntry[]>(StringComparer.Ordinal);
        foreach(var award in awards.EnumerateArray())
        {
            if(award.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Неверная награда.");
            var id=Text(award,"id",60);
            if(!AwardId().IsMatch(id)||result.ContainsKey(id))throw new InvalidDataException("Неверный код награды.");
            if(!award.TryGetProperty("entries",out var rows)||rows.ValueKind!=JsonValueKind.Array||rows.GetArrayLength()>NomineesMaxEntries)throw new InvalidDataException("Неверный состав номинантов.");
            var seen=new HashSet<string>(StringComparer.Ordinal);var entries=new List<AwardEntry>();
            foreach(var row in rows.EnumerateArray())
            {
                var entry=ReadEntry(row);
                // A film is a nominee once a year.
                if(seen.Add(entry.ImdbId+"|"+entry.Year))entries.Add(entry);
            }
            result[id]=entries.OrderByDescending(entry=>entry.Year).ThenBy(entry=>entry.Title,StringComparer.CurrentCulture).ToArray();
        }
        return result;
    }
    // For tests and for fixtures: the lists as if they had come from the backend.
    internal void Seed(AwardCollection[] winners,Dictionary<string,AwardEntry[]>? nomineeLists=null)
    {
        collections=winners;restored=true;nextTryUtc=DateTime.MaxValue;nominees=nomineeLists??[];nomineesRestored=true;nomineesNextTryUtc=DateTime.MaxValue;
    }
    // Reads the saved copy once, then asks the backend at most daily. True when the nominees changed.
    public async Task<bool> RefreshNomineesAsync(CancellationToken ct=default,bool force=false)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(!nomineesRestored)
            {
                nomineesRestored=true;
                try
                {
                    if(File.Exists(nomineesPath)&&new FileInfo(nomineesPath).Length<=NomineesMaxBytes)
                    {
                        var saved=await CacheFiles.ReadAllBytesAsync(nomineesPath,CancellationToken.None);
                        nominees=await Task.Run(()=>ParseNominees(saved),CancellationToken.None);nomineesHash=Convert.ToHexString(SHA256.HashData(saved));
                    }
                }
                catch(Exception error)when(error is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or FormatException){nominees=[];}
            }
            var now=clock();
            if(!force&&(now<nomineesNextTryUtc||nominees.Count>0&&File.Exists(nomineesPath)&&now-File.GetLastWriteTimeUtc(nomineesPath)<RefreshInterval))return false;
            nomineesNextTryUtc=now+RetryInterval;
            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var bytes=await client.Read(NomineesUri,NomineesMaxBytes,timeout.Token);
                var fresh=ParseNominees(bytes);
                var hash=Convert.ToHexString(SHA256.HashData(bytes));var changed=hash!=nomineesHash;nominees=fresh;nomineesHash=hash;
                try{Directory.CreateDirectory(Path.GetDirectoryName(nomineesPath)!);await CacheFiles.WriteAllBytesAsync(nomineesPath,bytes,CancellationToken.None);}catch(IOException){}catch(UnauthorizedAccessException){}
                return changed;
            }
            catch(Exception error)when(error is not OperationCanceledException||!ct.IsCancellationRequested){return false;}
        }
        finally{gate.Release();}
    }
    static bool SameLists(AwardCollection[] a,AwardCollection[] b)=>a.Length==b.Length&&a.Zip(b).All(pair=>pair.First.Id==pair.Second.Id&&pair.First.Entries.Select(e=>e.ImdbId+e.Year).SequenceEqual(pair.Second.Entries.Select(e=>e.ImdbId+e.Year)));

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")] private static partial Regex AwardId();
    [GeneratedRegex("^tt\\d{5,10}$")] private static partial Regex TitleId();
}
