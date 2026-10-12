using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kachalka;

// Small platform adapters for running the actual SharedCatalog state machine on
// Linux. These are fixtures, not replacements for the application's parsers or
// cache implementation; see README.md for the exact scope of this harness.
public record MediaItem(int Id, string Title, string Section, string Genre, int Year, string Kinopoisk, string Imdb, string Color)
{
    public string? PageUrl { get; init; }
    public string? OnlineId { get; init; }
    public string? Description { get; init; }
    public string? OriginalTitle { get; init; }
    public string? ImdbId { get; init; }
    public string[] TitleAliases { get; init; }=[];
}

public static class Preferences
{
    public static string DataDir => Environment.GetEnvironmentVariable("KACHALKA_DATA") ?? Path.GetTempPath();
}

public static class CacheFiles
{
    public static void Touch(string path) { }
    public static Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct) => File.ReadAllBytesAsync(path, ct);
    public static Task<string> ReadAllTextAsync(string path) => File.ReadAllTextAsync(path);
    public static async Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, ct);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static Task WriteAllTextAsync(string path, string value) => WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(value), default);
}

public sealed class SourceClient(HttpMessageHandler handler) : IDisposable
{
    readonly HttpClient http = new(handler);
    public async Task<byte[]> Read(Uri uri, int max, CancellationToken ct)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > max) throw new InvalidDataException("Oversized fixture response");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[16384];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) != 0)
        {
            if (output.Length + count > max) throw new InvalidDataException("Oversized fixture response");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    public void Dispose() => http.Dispose();
}

public static class OnlineIndexClient
{
    public static Action? BeforeMedia;
    public static MediaItem? Media(JsonElement row, string section)
    {
        BeforeMedia?.Invoke();
        var key = section == "Фильмы" ? "movies" : "series";
        if (row.GetProperty("section").GetString() != key) return null;
        var url = row.GetProperty("pageUrl").GetString()!;
        var id = -(BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(url)), 0) & int.MaxValue);
        return new(id, row.GetProperty("title").GetString()!, section, "", row.GetProperty("year").GetInt32(), "—", "—", "#526B69")
        {
            OnlineId = row.GetProperty("id").GetString(),
            PageUrl = url,
            Description = row.TryGetProperty("description", out var description) ? description.GetString() : null,
            OriginalTitle = row.TryGetProperty("originalTitle", out var original) ? original.GetString() : null,
        };
    }
}

public static class ErrorLog
{
    public static void Write(Exception error)=>Console.Error.WriteLine(error);
}
public static class CinemaPeople
{
    public record Work(string Title,int Year,string? OriginalTitle=null);
    public static bool MatchesWork(MediaItem item,Work work)=>item.Year==work.Year&&(item.Title==work.Title||item.OriginalTitle==work.Title||work.OriginalTitle!=null&&item.OriginalTitle==work.OriginalTitle);
}

public record CatalogChoice(string Key, string Label);
public record CatalogPage(MediaItem[] Items, bool HasNext, CatalogChoice[] Genres, CatalogChoice[] Countries);
public record CatalogSelection
{
    public bool Matches(MediaItem item) => true;
}
public static class CatalogPaging
{
    public const int Size = 40, Limit = 250;
}

// UnifiedSearch regressions use empty IMDb supplements. External suggestion
// matching remains covered by the native application tests.
public static class ImdbSuggestions
{
    public static bool IsImdbOnly(MediaItem item)=>item.PageUrl?.StartsWith("https://www.imdb.com/title/")==true;
}
public static class MediaMetadata
{
    // The harness has only catalog fields. Full native metadata merging is exercised on Windows.
    public static MediaItem Merge(MediaItem prior,MediaItem fresh)=>prior with{ImdbId=fresh.ImdbId??prior.ImdbId,OriginalTitle=fresh.OriginalTitle??prior.OriginalTitle,TitleAliases=prior.TitleAliases.Concat(fresh.TitleAliases).Distinct().ToArray(),Description=fresh.Description??prior.Description};
}
