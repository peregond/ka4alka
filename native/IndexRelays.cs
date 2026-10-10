using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Kachalka;

// Standalone copies of the site's release search and torrent relay (relay-worker/).
// The site's hosting refuses visitors from Russia and Belarus. The list of relays
// is published on GitHub, which answers there, and changes without an update.
public sealed class IndexRelays(SourceClient client,Uri? listUri=null,string? cachePath=null)
{
    public static readonly Uri PublishedList=new("https://raw.githubusercontent.com/peregond/ka4alka/main/distribution/relays.json");
    static readonly TimeSpan Fresh=TimeSpan.FromHours(6);
    static readonly TimeSpan RetryAfterFailure=TimeSpan.FromHours(1);
    readonly Uri list=listUri??PublishedList;
    readonly string? cache=cachePath;
    readonly SemaphoreSlim gate=new(1,1);
    IReadOnlyList<Uri>? current;
    DateTime nextCheckUtc;

    string CacheFile=>cache??Path.Combine(Preferences.DataDir,"relays.json");

    public async Task<IReadOnlyList<Uri>> ListAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(current!=null&&DateTime.UtcNow<nextCheckUtc)return current;
            try
            {
                var bytes=await client.Read(list,64*1024,ct);
                current=Parse(bytes);nextCheckUtc=DateTime.UtcNow+Fresh;
                try{Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);await File.WriteAllBytesAsync(CacheFile+".tmp",bytes,CancellationToken.None);File.Move(CacheFile+".tmp",CacheFile,true);}
                catch(Exception error) when(error is IOException or UnauthorizedAccessException){}
            }
            catch(Exception error) when(!ct.IsCancellationRequested&&error is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException)
            {
                // A saved list keeps working while GitHub is briefly unavailable.
                current??=Saved();nextCheckUtc=DateTime.UtcNow+RetryAfterFailure;
                DiagnosticLog.Write("index-relays-unavailable",new{Error=error.GetType().Name,Saved=current.Count});
            }
            return current;
        }
        finally{gate.Release();}
    }

    IReadOnlyList<Uri> Saved()
    {
        try{return File.Exists(CacheFile)?Parse(File.ReadAllBytes(CacheFile)):[];}
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException){return [];}
    }

    // {"schemaVersion":1,"relays":["https://relay.example/"]}: HTTPS base addresses only.
    public static IReadOnlyList<Uri> Parse(byte[] json)
    {
        using var document=JsonDocument.Parse(json);
        var root=document.RootElement;
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("schemaVersion",out var version)||version.ValueKind!=JsonValueKind.Number||!version.TryGetInt32(out var schema)||schema!=1||
           !root.TryGetProperty("relays",out var relays)||relays.ValueKind!=JsonValueKind.Array)
            throw new InvalidDataException("Неверный список резервных адресов онлайн-индекса.");
        return relays.EnumerateArray()
            .Select(row=>row.ValueKind==JsonValueKind.String&&Uri.TryCreate(row.GetString(),UriKind.Absolute,out var uri)?uri:null)
            .OfType<Uri>()
            .Where(uri=>uri.Scheme=="https"&&string.IsNullOrEmpty(uri.UserInfo)&&uri.IsDefaultPort&&uri.Query.Length==0&&uri.Fragment.Length==0&&uri.AbsolutePath.EndsWith('/')&&uri.Host!=OnlineIndexClient.PublishedSite.Host)
            .DistinctBy(uri=>uri.AbsoluteUri)
            .Take(5)
            .ToArray();
    }
}
