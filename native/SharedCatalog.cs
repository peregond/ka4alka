using System.IO;
using System.Text.Json;

namespace Kachalka;

// Public metadata is refreshed independently of application releases.
public sealed class SharedCatalog(SourceClient client)
{
    public static readonly Uri FeedUri=new("https://github.com/peregond/ka4alka/releases/download/catalog-data/catalog.json");
    readonly SemaphoreSlim reader=new(1,1);
    readonly string path=Path.Combine(Preferences.DataDir,"catalog-shared.json");
    MediaItem[]? items;
    DateTime generatedUtc,retryAfterUtc;
    bool restored;
    public bool RefreshDue=>generatedUtc<BoundaryUtc(DateTime.UtcNow)&&DateTime.UtcNow>=retryAfterUtc;
    public static DateTime BoundaryUtc(DateTime now)
    {
        now=now.Kind==DateTimeKind.Local?now.ToUniversalTime():DateTime.SpecifyKind(now,DateTimeKind.Utc);
        var boundary=now.Date.AddHours(5);return now<boundary?boundary.AddDays(-1):boundary;
    }
    public static (DateTime GeneratedUtc,MediaItem[] Items) Parse(byte[] bytes,DateTime now)
    {
        using var json=JsonDocument.Parse(bytes);var root=json.RootElement;
        if(root.GetProperty("schemaVersion").GetInt32()!=1||!root.GetProperty("generatedAtUtc").TryGetDateTime(out var generated)||generated.Kind!=DateTimeKind.Utc||generated>now.AddMinutes(15)||generated<new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc))throw new InvalidDataException("Неверная дата общего каталога.");
        var rows=root.GetProperty("items");if(rows.ValueKind!=JsonValueKind.Array||rows.GetArrayLength()>15000)throw new InvalidDataException("Общий каталог слишком большой.");
        var parsed=rows.EnumerateArray().Where(row=>row.ValueKind==JsonValueKind.Object).Select(row=>OnlineIndexClient.Media(row,row.TryGetProperty("section",out var section)&&section.GetString()=="movies"?"Фильмы":"Сериалы")).OfType<MediaItem>()
            .Where(item=>item.Year is >=0 and <=2100).DistinctBy(item=>(item.Section,item.Id)).ToArray();
        if(parsed.Count(item=>item.Section=="Фильмы")<2000||parsed.Count(item=>item.Section=="Сериалы")<300)throw new InvalidDataException("Общий каталог неполный.");
        return (generated,parsed.OrderByDescending(item=>Math.Min(item.Year,now.Year)).ToArray());
    }
    public IReadOnlyList<MediaItem> Search(string section,string query)
    {
        static string Normalize(string value)=>System.Text.RegularExpressions.Regex.Replace(value.Replace('ё','е').Replace('Ё','Е').ToLowerInvariant(),@"[^\p{L}\p{N}]+"," ").Trim();
        var words=Normalize(query).Split(' ',StringSplitOptions.RemoveEmptyEntries);
        return (items??[]).Where(item=>item.Section==section&&words.All(word=>Normalize(item.Title+" "+item.OriginalTitle+" "+item.Year).Split(' ').Any(part=>part.StartsWith(word,StringComparison.Ordinal)))).Take(80).ToArray();
    }
    public async Task<CatalogPage?> PageAsync(string section,int page,CancellationToken ct,bool force=false)
    {
        await reader.WaitAsync(ct);
        try
        {
            var now=DateTime.UtcNow;
            if(!restored)
            {
                restored=true;
                try{if(File.Exists(path)&&new FileInfo(path).Length<=4*1024*1024){var saved=Parse(await File.ReadAllBytesAsync(path,ct),now);items=saved.Items;generatedUtc=saved.GeneratedUtc;}}
                catch(Exception error)when(error is IOException or JsonException or InvalidOperationException or KeyNotFoundException or UnauthorizedAccessException){}
            }
            if(force||generatedUtc<BoundaryUtc(now)&&now>=retryAfterUtc)
            {
                retryAfterUtc=now.AddMinutes(30);
                try
                {
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    var bytes=await client.Read(FeedUri,4*1024*1024,timeout.Token);var fresh=Parse(bytes,now);
                    if(fresh.GeneratedUtc>=generatedUtc)
                    {
                        items=fresh.Items;generatedUtc=fresh.GeneratedUtc;
                        try{Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temp=path+".tmp";await File.WriteAllBytesAsync(temp,bytes,ct);File.Move(temp,path,true);}catch(IOException){}catch(UnauthorizedAccessException){}
                    }
                }
                catch(Exception)when(!ct.IsCancellationRequested){}
            }
            ct.ThrowIfCancellationRequested();if(items==null)return null;
            var rows=items.Where(item=>item.Section==section).ToArray();var offset=(Math.Clamp(page,1,CatalogPaging.Limit)-1)*CatalogPaging.Size;
            return new(rows.Skip(offset).Take(CatalogPaging.Size).ToArray(),offset+CatalogPaging.Size<rows.Length,[],[]);
        }
        finally{reader.Release();}
    }
}
