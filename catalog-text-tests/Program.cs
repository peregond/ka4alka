using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kachalka;

var savedLanguage=AppLanguage.Current;
try
{
    AppLanguage.Set("en");
    var localized=new MediaItem(-1,"Дюна","Фильмы","",2021,"—","—","#526B69"){OriginalTitle="Dune",English=new("Dune")};
    if(MediaIdentity.Queries(localized).First()!="Dune"||SearchLanguage.TitleRank(localized,"en")!=1)throw new Exception("English title identity/ranking fixture is inconsistent.");
    if(SearchLanguage.EnglishAudioRank(new("Dune ENGLISH 1080p","Fixture",10))!=3)throw new Exception("Real audio ranking is not wired into the text harness.");
    Console.WriteLine("PASS: real localized identity, title ranking and English audio helpers compile in the Linux catalog harness");
}
finally{AppLanguage.Set(savedLanguage);}
await BundledCatalogTests.Run();
await TransportTests.Run();
SeasonalCatalogTests.Run();
await UnifiedSearchTests.Run();
await PerformanceResourcesTests.Run();
var previousDataDir = Environment.GetEnvironmentVariable("KACHALKA_DATA");
var testDataDir = Path.Combine(Path.GetTempPath(), "kachalka-catalog-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testDataDir);
try
{
    Environment.SetEnvironmentVariable("KACHALKA_DATA", testDataDir);
    await SharedCatalogTests.Run();
}
finally
{
    Environment.SetEnvironmentVariable("KACHALKA_DATA", previousDataDir);
    Directory.Delete(testDataDir, recursive: true);
}

static class TransportTests
{
    static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }

    static byte[] Gzip(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) gzip.Write(raw);
        return output.ToArray();
    }

    static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    static Dictionary<string, object> Manifest(byte[] raw, byte[] compressed) => new()
    {
        ["schemaVersion"] = 1,
        ["revision"] = Hash(raw),
        ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
        ["downloadUrl"] = "https://github.com/peregond/ka4alka/releases/download/catalog-data/catalog-" + Hash(raw) + ".json.gz",
        ["sha256"] = Hash(compressed),
        ["bytes"] = compressed.Length,
        ["uncompressedBytes"] = raw.Length,
    };

    static async Task Reject(Dictionary<string, object> manifest, byte[] compressed, string reason, byte[]? manifestBytes = null)
    {
        var calls = new List<Uri>();
        Task<byte[]> Read(Uri uri, int limit, CancellationToken ct)
        {
            calls.Add(uri);
            return Task.FromResult(uri == TextCatalogDownload.ManifestUri ? manifestBytes ?? JsonSerializer.SerializeToUtf8Bytes(manifest) : compressed);
        }
        var rejected = false;
        try { await TextCatalogDownload.DownloadAsync(Read, default); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected && !calls.Contains(TextCatalogDownload.LegacyFeedUri), reason + "; no legacy fallback");
    }

    public static async Task Run()
    {
        var raw = Encoding.UTF8.GetBytes("{\"description\":\"Текстовый каталог\"}\n");
        var compressed = Gzip(raw);
        var manifest = Manifest(raw, compressed);
        var calls = new List<(Uri Uri, int Limit)>();
        Task<byte[]> Read(Uri uri, int limit, CancellationToken ct)
        {
            calls.Add((uri, limit));
            return Task.FromResult(uri == TextCatalogDownload.ManifestUri ? JsonSerializer.SerializeToUtf8Bytes(manifest) : compressed);
        }
        var result = await TextCatalogDownload.DownloadAsync(Read, default);
        Check(result.SequenceEqual(raw) && calls.Count == 2 && calls[0].Limit == 64 * 1024 && calls[1].Limit == compressed.Length,
            "manifest and immutable gzip load exact verified raw bytes with bounded reads");

        await Reject(manifest, compressed[..^1], "truncated download is rejected");
        var altered = compressed.ToArray(); altered[10] ^= 1;
        await Reject(manifest, altered, "compressed SHA-256 mismatch is rejected");
        var truncated = compressed[..^8];
        await Reject(Manifest(raw, truncated), truncated, "gzip missing trailer is rejected even when compressed hash matches");
        var shortRaw = raw[..^1];
        var wrongRevision = Manifest(shortRaw, compressed); wrongRevision["uncompressedBytes"] = raw.Length;
        await Reject(wrongRevision, compressed, "raw revision mismatch is rejected");
        var wrongSize = Manifest(raw, compressed); wrongSize["uncompressedBytes"] = raw.Length + 1;
        await Reject(wrongSize, compressed, "declared uncompressed size mismatch is rejected");
        var bomb = Gzip(new byte[4 * 1024 * 1024]);
        var bombManifest = Manifest(raw, bomb); bombManifest["uncompressedBytes"] = 1024;
        await Reject(bombManifest, bomb, "gzip expansion stops at declared output limit");
        var tooLarge = Manifest(raw, compressed); tooLarge["uncompressedBytes"] = TextCatalogDownload.MaxRawBytes + 1;
        await Reject(tooLarge, compressed, "raw snapshots above 32 MiB are rejected before archive request");
        var tooLargeCompressed = Manifest(raw, compressed); tooLargeCompressed["bytes"] = TextCatalogDownload.MaxCompressedBytes + 1;
        await Reject(tooLargeCompressed, compressed, "compressed snapshots above 32 MiB are rejected before archive request");
        foreach (var url in new[]
        {
            "https://evil.invalid/catalog.json.gz",
            (string)manifest["downloadUrl"] + "?download=1",
            ((string)manifest["downloadUrl"]).Replace("https://", "http://"),
            ((string)manifest["downloadUrl"]).Replace("github.com/", "github.com:443/"),
            ((string)manifest["downloadUrl"]).Replace("catalog-", "other-"),
            ((string)manifest["downloadUrl"]).Replace("github.com/", "evil.invalid@github.com/"),
        })
        {
            var unsafeManifest = Manifest(raw, compressed); unsafeManifest["downloadUrl"] = url;
            await Reject(unsafeManifest, compressed, "untrusted or noncanonical archive URL is rejected");
        }
        var unsupported = Manifest(raw, compressed); unsupported["schemaVersion"] = 2;
        await Reject(unsupported, compressed, "unsupported manifest schema is rejected");
        var hashCase = Manifest(raw, compressed); hashCase["revision"] = Hash(raw).ToUpperInvariant();
        await Reject(hashCase, compressed, "noncanonical revision is rejected");
        await Reject(manifest, compressed, "malformed manifest is rejected", Encoding.UTF8.GetBytes("{"));
        await Reject(manifest, compressed, "oversized manifest is rejected", new byte[TextCatalogDownload.MaxManifestBytes + 1]);
        var duplicate = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(manifest)).Replace("{", "{\"schemaVersion\":1,", StringComparison.Ordinal));
        await Reject(manifest, compressed, "duplicate manifest fields are rejected", duplicate);

        foreach (var status in new HttpStatusCode?[] { null, HttpStatusCode.Forbidden, HttpStatusCode.InternalServerError, HttpStatusCode.NotFound })
        {
            calls.Clear();
            Task<byte[]> Missing(Uri uri, int limit, CancellationToken ct)
            {
                calls.Add((uri, limit));
                if (uri == TextCatalogDownload.ManifestUri) throw new HttpRequestException("Unavailable manifest", null, status);
                return Task.FromResult(raw);
            }
            var failed = false;
            try { result = await TextCatalogDownload.DownloadAsync(Missing, default); }
            catch (HttpRequestException) { failed = true; }
            if (status == HttpStatusCode.NotFound)
                Check(!failed && result.SequenceEqual(raw) && calls.Count == 2 && calls[1] == (TextCatalogDownload.LegacyFeedUri, TextCatalogDownload.MaxRawBytes),
                    "legacy feed is read only when manifest returns HTTP 404");
            else
                Check(failed && calls.Count == 1, "manifest network/auth/server failure cannot trigger legacy fallback");
        }

        calls.Clear();
        Task<byte[]> MissingArchive(Uri uri, int limit, CancellationToken ct)
        {
            calls.Add((uri, limit));
            if (uri == TextCatalogDownload.ManifestUri) return Task.FromResult(JsonSerializer.SerializeToUtf8Bytes(manifest));
            throw new HttpRequestException("Unavailable archive", null, HttpStatusCode.NotFound);
        }
        var archive404 = false;
        try { await TextCatalogDownload.DownloadAsync(MissingArchive, default); }
        catch (HttpRequestException) { archive404 = true; }
        Check(archive404 && calls.Count == 2 && !calls.Any(call => call.Uri == TextCatalogDownload.LegacyFeedUri), "archive HTTP 404 cannot trigger legacy fallback");

        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        calls.Clear();
        var cancellation = false;
        try { await TextCatalogDownload.DownloadAsync(Read, cancelled.Token); }
        catch (OperationCanceledException) { cancellation = true; }
        Check(cancellation && calls.Count == 0, "cancelled refresh makes no download request");
    }
}
