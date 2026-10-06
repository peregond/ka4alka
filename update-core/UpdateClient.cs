using System.Net;
using System.Security.Cryptography;

namespace Kachalka.Updates;

public sealed record UpdateOffer(UpdateManifest Manifest, byte[] ManifestBytes, byte[] Signature);

public sealed partial class UpdateClient : IDisposable
{
    readonly HttpClient client;
    readonly string publicKey;
    public UpdateClient(string publicKey, HttpMessageHandler? handler = null)
    {
        this.publicKey = publicKey;
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Kachalka-Updater/0.19");
    }
    public static bool AllowedUri(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        (uri.Host == "release-assets.githubusercontent.com" ||
         (uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/" + UpdateManifest.Repository + "/releases/", StringComparison.Ordinal)));

    async Task<HttpResponseMessage> OpenAsync(string url, CancellationToken cancellation, System.Net.Http.Headers.RangeHeaderValue? range=null)
    {
        var uri = new Uri(url);
        for (int redirects = 0; redirects <= 5; redirects++)
        {
            if (!AllowedUri(uri)) throw new InvalidDataException("Недопустимый адрес обновления.");
            using var request=new HttpRequestMessage(HttpMethod.Get,uri);
            request.Headers.Range=range;
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            // Also reject a handler that followed a redirect without our validation.
            if (response.RequestMessage?.RequestUri is { } final && !AllowedUri(final))
            { response.Dispose(); throw new InvalidDataException("Недопустимое перенаправление обновления."); }
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location;
                response.Dispose();
                if (location is null) throw new InvalidDataException("Пустое перенаправление обновления.");
                uri = new Uri(uri, location);
                continue;
            }
            try { response.EnsureSuccessStatusCode(); return response; }
            catch { response.Dispose(); throw; }
        }
        throw new InvalidDataException("Слишком много перенаправлений обновления.");
    }
    async Task<byte[]> ReadSmallAsync(string url, int limit, CancellationToken cancellation)
    {
        using var response = await OpenAsync(url, cancellation);
        using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var output = new MemoryStream();
        var buffer = new byte[4096]; int count;
        while ((count = await stream.ReadAsync(buffer, cancellation)) > 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("Описание обновления слишком большое.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }
    public async Task<UpdateOffer?> CheckAsync(Version current, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var bytes = await ReadSmallAsync(UpdateManifest.ManifestUrl, 64 * 1024, timeout.Token);
        var signature = await ReadSmallAsync(UpdateManifest.ManifestUrl.Replace("latest.json", "latest.sig", StringComparison.Ordinal), 4096, timeout.Token);
        var manifest = UpdateManifest.Verify(bytes, signature, publicKey);
        return manifest.IsNewerThan(current) ? new(manifest, bytes, signature) : null;
    }
    public async Task DownloadAsync(UpdateOffer offer, string destination, IProgress<int>? progress, CancellationToken cancellation)
    {
        // Revalidate offers even when created by another caller.
        var manifest = UpdateManifest.Verify(offer.ManifestBytes, offer.Signature, publicKey);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        var temporary = destination + ".partial";
        try
        {
            using var response = await OpenAsync(manifest.Package.Url, timeout.Token);
            if (response.Content.Headers.ContentLength is { } size && size != manifest.Package.Size)
                throw new InvalidDataException("Размер обновления не совпадает.");
            using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0; var buffer = new byte[64 * 1024];
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                int count;
                while ((count = await stream.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += count;
                    if (total > manifest.Package.Size) throw new InvalidDataException("Обновление превышает заявленный размер.");
                    hash.AppendData(buffer, 0, count);
                    await file.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    progress?.Report((int)(100 * total / manifest.Package.Size));
                }
            }
            if (total != manifest.Package.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(manifest.Package.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Архив обновления повреждён.");
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public void Dispose() => client.Dispose();
}
