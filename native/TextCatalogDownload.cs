using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Kachalka;

// The manifest points to immutable bytes. Never use a failed new snapshot as a
// reason to overwrite a complete local catalog with the mutable legacy feed.
public static class TextCatalogDownload
{
    const string ReleaseBase = "https://github.com/peregond/ka4alka/releases/download/catalog-data/";
    public static readonly Uri ManifestUri = new(ReleaseBase + "manifest.json");
    public static readonly Uri LegacyFeedUri = new(ReleaseBase + "catalog.json");
    public const int MaxManifestBytes = 64 * 1024;
    public const int MaxRawBytes = 32 * 1024 * 1024;
    public const int MaxCompressedBytes = 32 * 1024 * 1024;
    static readonly uint[] CrcTable = MakeCrcTable();

    sealed record Manifest(string Revision, Uri DownloadUri, string Sha256, int Bytes, int UncompressedBytes);

    public static async Task<byte[]> DownloadAsync(Func<Uri, int, CancellationToken, Task<byte[]>> read, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        byte[] manifestBytes;
        try
        {
            manifestBytes = await read(ManifestUri, MaxManifestBytes, ct);
        }
        catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.NotFound && !ct.IsCancellationRequested)
        {
            var legacy = await read(LegacyFeedUri, MaxRawBytes, ct);
            ct.ThrowIfCancellationRequested();
            if (legacy.Length == 0 || legacy.Length > MaxRawBytes)
                throw new InvalidDataException("Неверный размер общего каталога.");
            return legacy;
        }

        ct.ThrowIfCancellationRequested();
        var manifest = ParseManifest(manifestBytes);
        var compressed = await read(manifest.DownloadUri, manifest.Bytes, ct);
        ct.ThrowIfCancellationRequested();
        if (compressed.Length != manifest.Bytes || !MatchesHash(compressed, manifest.Sha256))
            throw new InvalidDataException("Повреждён архив общего каталога.");
        var raw = await DecompressAsync(compressed, manifest.UncompressedBytes, ct);
        if (!MatchesHash(raw, manifest.Revision))
            throw new InvalidDataException("Ревизия общего каталога не совпадает с содержимым.");
        return raw;
    }

    static Manifest ParseManifest(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxManifestBytes)
            throw new InvalidDataException("Неверный размер манифеста общего каталога.");
        try
        {
            using var json = JsonDocument.Parse(bytes);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Неверный манифест общего каталога.");
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in root.EnumerateObject())
                if (!fields.Add(field.Name))
                    throw new InvalidDataException("Повторяющееся поле манифеста общего каталога.");
            if (root.GetProperty("schemaVersion").GetInt32() != 1)
                throw new InvalidDataException("Неподдерживаемый манифест общего каталога.");
            var revision = root.GetProperty("revision").GetString();
            var hash = root.GetProperty("sha256").GetString();
            if (!IsHash(revision) || !IsHash(hash))
                throw new InvalidDataException("Неверная контрольная сумма общего каталога.");
            var generatedField = root.GetProperty("generatedAtUtc");
            if (!generatedField.TryGetDateTime(out var generated) || generated.Kind != DateTimeKind.Utc ||
                generated < new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) || generated > DateTime.UtcNow.AddMinutes(15))
                throw new InvalidDataException("Неверная дата манифеста общего каталога.");
            var download = root.GetProperty("downloadUrl").GetString();
            var expected = ReleaseBase + "catalog-" + revision + ".json.gz";
            if (!string.Equals(download, expected, StringComparison.Ordinal))
                throw new InvalidDataException("Ненадёжный адрес общего каталога.");
            var compressedBytes = root.GetProperty("bytes").GetInt32();
            var rawBytes = root.GetProperty("uncompressedBytes").GetInt32();
            if (compressedBytes < 18 || compressedBytes > MaxCompressedBytes || rawBytes <= 0 || rawBytes > MaxRawBytes)
                throw new InvalidDataException("Общий каталог слишком большой.");
            return new(revision!, new Uri(expected), hash!, compressedBytes, rawBytes);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw new InvalidDataException("Неверный манифест общего каталога.", error);
        }
    }

    static bool IsHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    static bool MatchesHash(byte[] bytes, string expected) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(expected));

    static async Task<byte[]> DecompressAsync(byte[] compressed, int expectedBytes, CancellationToken ct)
    {
        // GZipStream may finish without reporting a missing trailer. Checking
        // CRC32 and ISIZE explicitly rejects truncated headers/trailers too.
        if (compressed.Length < 18 || compressed[0] != 0x1f || compressed[1] != 0x8b || compressed[2] != 8 || (compressed[3] & 0xe0) != 0)
            throw new InvalidDataException("Неверный gzip-архив общего каталога.");
        using var input = new MemoryStream(compressed, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(Math.Min(expectedBytes, 64 * 1024));
        var buffer = new byte[64 * 1024];
        uint crc = 0xffffffff;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var count = await gzip.ReadAsync(buffer, ct);
            if (count == 0) break;
            if (output.Length + count > expectedBytes || output.Length + count > MaxRawBytes)
                throw new InvalidDataException("Распакованный общий каталог слишком большой.");
            for (var i = 0; i < count; i++) crc = CrcTable[(crc ^ buffer[i]) & 0xff] ^ (crc >> 8);
            output.Write(buffer, 0, count);
        }
        var trailer = compressed.AsSpan(compressed.Length - 8);
        var expectedCrc = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(trailer);
        var expectedSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(trailer[4..]);
        if (output.Length != expectedBytes || expectedSize != expectedBytes || expectedCrc != ~crc)
            throw new InvalidDataException("Неполный gzip-архив общего каталога.");
        ct.ThrowIfCancellationRequested();
        return output.ToArray();
    }

    static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xedb88320);
            table[i] = value;
        }
        return table;
    }
}
