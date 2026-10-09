using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Kachalka.Lan;

// These checks use real TCP/TLS sessions and independent persisted identities.
// They intentionally enable loopback only through the explicit test option.
var directory = Path.Combine(Path.GetTempPath(), "kachalka-lan-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var evidence = Path.GetFullPath(Path.Combine("test-output", "lan-network"));
Directory.CreateDirectory(evidence);
var checkLog = Path.Combine(evidence, "checks.txt");
File.WriteAllText(checkLog, "Real mutual-TLS LAN integration checks" + Environment.NewLine);
var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
    Console.WriteLine("PASS: " + description);
    File.AppendAllText(checkLog, "PASS: " + description + Environment.NewLine);
}
async Task Reject(Func<Task> action, string description)
{
    Exception? failure = null;
    try { await action(); }
    catch (Exception error) when (error is LanException or IOException or AuthenticationException or OperationCanceledException or ArgumentException or InvalidDataException or SocketException)
    { failure = error; }
    Check(failure is not null, description);
}

const string Magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=LAN%20fixture";
const string OtherMagnet = "magnet:?xt=urn:btih:89abcdef0123456789abcdef0123456789abcdef&dn=Other";
var received = new ConcurrentQueue<LanDownloadRequest>();
var approvals = new ConcurrentQueue<LanPairingRequest>();
var allowSender = true;
var allowReceiver = true;
var callbackMode = "accept";
var callbackCalls = 0;
LanServiceOptions Options(string key, string name) => new()
{
    StateDirectory = Path.Combine(directory, key), DeviceName = name,
    TcpPort = 0, DiscoveryPort = 0, AllowLoopbackForTests = true,
    RequestTimeout = TimeSpan.FromSeconds(2), PairingTimeout = TimeSpan.FromSeconds(8)
};
var aOptions = Options("sender", "Ноутбук");
var bOptions = Options("receiver", "Гостиная");
LanService Create(LanServiceOptions options, bool sender)
{
    var service = new LanService(options);
    service.ConfirmPairingAsync = (request, _) =>
    {
        approvals.Enqueue(request);
        return Task.FromResult(sender ? allowSender : allowReceiver);
    };
    if (!sender) service.ReceiveDownloadAsync = async (request, token) =>
    {
        Interlocked.Increment(ref callbackCalls);
        if (callbackMode == "throw") throw new IOException("Fixture queue unavailable");
        if (callbackMode == "reject") return new() { RequestId = request.RequestId, Accepted = false, Message = "Fixture refused" };
        if (callbackMode == "wait") await Task.Delay(TimeSpan.FromSeconds(30), token);
        received.Enqueue(request);
        return new() { RequestId = request.RequestId, Accepted = true, DownloadId = "download-" + request.RequestId.ToString("N"), Message = "Added" };
    };
    return service;
}

LanService? a = null, b = null, attacker = null;
try
{
    a = Create(aOptions, true); b = Create(bOptions, false);
    await a.StartAsync(); await b.StartAsync();
    Check(a.Port > 0 && b.Port > 0 && a.Port != b.Port, "separate real TCP listeners bind ephemeral loopback test ports");
    var peer = await a.FindManualAsync($"127.0.0.1:{b.Port}");
    Check(peer.Id == b.Id && peer.Name == "Гостиная" && peer.Fingerprint == b.Fingerprint && !peer.Paired, "manual discovery reports actual authenticated certificate identity and friendly name");
    await Reject(() => a.PairAsync(peer), "closed pairing window rejects pairing");
    Check(!a.Devices.Any(x => x.Paired) && !b.Devices.Any(x => x.Paired), "closed window leaves neither endpoint trusted");

    b.AllowPairingFor(TimeSpan.FromMinutes(2));
    allowReceiver = false;
    await Reject(() => a.PairAsync(peer), "receiver declining confirmation rejects pairing");
    Check(!a.Devices.Any(x => x.Paired) && !b.Devices.Any(x => x.Paired), "receiver denial persists no trust on either side");
    allowReceiver = true; allowSender = false;
    await Reject(() => a.PairAsync(peer), "sender declining certificate comparison rejects pairing");
    Check(!a.Devices.Any(x => x.Paired) && !b.Devices.Any(x => x.Paired), "sender denial persists no trust on either side");
    allowSender = true;
    using (var cancelled = new CancellationTokenSource())
    {
        cancelled.Cancel();
        await Reject(() => a.PairAsync(peer, cancelled.Token), "cancelled pairing exits without trusting an unknown certificate");
    }
    Check(!a.Devices.Any(x => x.Paired) && !b.Devices.Any(x => x.Paired), "cancelled pairing does not leave a one-sided trusted peer");
    var senderConfirmation = a.ConfirmPairingAsync;
    var senderPrompt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    a.ConfirmPairingAsync = async (_, token) => { senderPrompt.TrySetResult(); await Task.Delay(TimeSpan.FromSeconds(30), token); return true; };
    try
    {
        using var pendingCancellation = new CancellationTokenSource();
        var pendingPair = a.PairAsync(peer, pendingCancellation.Token);
        await senderPrompt.Task.WaitAsync(TimeSpan.FromSeconds(2)); pendingCancellation.Cancel();
        await Reject(() => pendingPair, "cancelling an already displayed pairing confirmation stops the transaction");
        Check(!a.Devices.Any(x => x.Paired) && !b.Devices.Any(x => x.Paired), "cancellation during certificate comparison leaves no trusted endpoint");
    }
    finally { a.ConfirmPairingAsync = senderConfirmation; }
    await using (var timedSender = new LanService(Options("timed-sender", "Timeout sender") with { PairingTimeout = TimeSpan.FromSeconds(1) }))
    await using (var timedReceiver = new LanService(Options("timed-receiver", "Timeout receiver") with { PairingTimeout = TimeSpan.FromSeconds(1) }))
    {
        var prompts = 0;
        Task<bool> BlockConfirmation(LanPairingRequest _, CancellationToken token)
        { Interlocked.Increment(ref prompts); return WaitForCancellation(token); }
        timedSender.ConfirmPairingAsync = BlockConfirmation; timedReceiver.ConfirmPairingAsync = BlockConfirmation;
        await timedSender.StartAsync(); await timedReceiver.StartAsync(); timedReceiver.AllowPairingFor(TimeSpan.FromMinutes(2));
        var timedPeer = await timedSender.FindManualAsync($"127.0.0.1:{timedReceiver.Port}");
        var pairingElapsed = Stopwatch.StartNew();
        await Reject(() => timedSender.PairAsync(timedPeer), "pairing expires when neither device confirms its visible code");
        Check(prompts == 2 && pairingElapsed.Elapsed < TimeSpan.FromSeconds(4) &&
            !timedSender.Devices.Any(x => x.Paired) && !timedReceiver.Devices.Any(x => x.Paired),
            "two stalled confirmation prompts time out with no persisted one-sided trust");
    }
    while (approvals.TryDequeue(out _)) { }
    await a.PairAsync(peer);
    var approved = approvals.ToArray();
    Check(approved.Length == 2 && approved[0].VerificationCode == approved[1].VerificationCode &&
        approved[0].VerificationCode.Count(Uri.IsHexDigit) == 12 && approved.Count(x => x.Incoming) == 1,
        "both people confirm the same 48-bit certificate verification code over mutual TLS");
    Check(approved.Any(x => x.Device.Id == a.Id && x.Device.Name == "Ноутбук" && x.Device.Fingerprint == a.Fingerprint) &&
        approved.Any(x => x.Device.Id == b.Id && x.Device.Name == "Гостиная" && x.Device.Fingerprint == b.Fingerprint),
        "pairing comparison binds both device names and real certificate identities");
    Check(a.Devices.Single(x => x.Id == b.Id).Paired && b.Devices.Single(x => x.Id == a.Id).Paired,
        "successful mutual confirmation creates both durable trusted peers");
    peer = a.Devices.Single(x => x.Id == b.Id);

    attacker = Create(Options("attacker", "Неизвестный компьютер"), true); await attacker.StartAsync();
    var publicPeer = await attacker.FindManualAsync($"127.0.0.1:{b.Port}");
    await Reject(() => attacker.SendDownloadAsync(publicPeer, new() { Magnet = Magnet }), "an unpaired installation cannot enqueue a download");
    Check(received.IsEmpty && callbackCalls == 0, "unpaired request never reaches the receiver queue callback");

    var request = new LanDownloadRequest
    {
        Magnet = Magnet, Title = "Проверка локальной сети",
        SenderId = Guid.NewGuid(), SenderFingerprint = new string('F', 64),
        Media = new() { Id = "movies:lan-fixture", Category = "Фильмы", Title = "Тестовый фильм", Year = "2026",
            PageUrl = "https://w6.zona.plus/movies/lan-fixture", ImageUrl = "https://example.test/poster.jpg", Rating = "8.2", Genres = ["драма"] },
        Release = new() { Title = "LAN fixture 1080p", Source = "Fixture", PageUrl = "https://example.test/release", Size = "2 ГБ", Seeds = 3 }
    };
    var receipt = await a.SendDownloadAsync(peer, request);
    Check(receipt.Accepted && receipt.RequestId == request.RequestId && receipt.DownloadId == "download-" + request.RequestId.ToString("N"),
        "sender receives the receiver's actual accepted queue receipt");
    Check(received.Count == 1 && received.TryPeek(out var first) && first.Magnet == Magnet && first.Title == request.Title &&
        first.Media?.Title == "Тестовый фильм" && first.Media.Year == "2026" && first.Release?.Title == "LAN fixture 1080p" &&
        first.SenderId == a.Id && first.SenderFingerprint == a.Fingerprint,
        "real encrypted transport preserves the selected torrent and catalog metadata");
    var retried = await a.SendDownloadAsync(peer, request);
    Check(retried.Accepted && retried.DownloadId == receipt.DownloadId && received.Count == 1 && callbackCalls == 1,
        "retry after an acknowledgement returns the durable receipt without enqueuing twice");
    await Task.WhenAll(a.SendDownloadAsync(peer, request), a.SendDownloadAsync(peer, request));
    Check(received.Count == 1 && callbackCalls == 1, "concurrent retries of one request do not duplicate receiver queue work");
    var conflictingReceipt = await a.SendDownloadAsync(peer, request with { Magnet = OtherMagnet });
    Check(!conflictingReceipt.Accepted, "reusing a request ID with a different payload cannot claim the old accepted receipt");
    Check(received.Count == 1 && callbackCalls == 1, "conflicting duplicate payload never reaches the receiver queue");

    foreach (var invalid in new[] { "https://example.test/movie.torrent", "magnet:?dn=MissingHash", "magnet:?xt=urn:btih:wrong" })
        await Reject(() => a.SendDownloadAsync(peer, new() { Magnet = invalid }), "invalid download input is rejected: " + invalid);
    await Reject(() => a.SendDownloadAsync(peer, new() { Magnet = Magnet, TorrentBytes = [1, 2, 3] }), "ambiguous magnet-plus-file payload is rejected");
    await Reject(() => a.SendDownloadAsync(peer, new() { TorrentBytes = new byte[LanProtocol.MaxTorrentBytes + 1] }), "torrent bytes over the documented limit are rejected");
    Check(callbackCalls == 1, "invalid and oversized requests cannot enqueue receiver work");

    // A complete single-file bencoded torrent: SHA1 piece length is 20 bytes.
    var torrent = Encoding.ASCII.GetBytes("d4:infod6:lengthi1e4:name11:fixture.bin12:piece lengthi16384e6:pieces20:01234567890123456789ee");
    var torrentRequest = new LanDownloadRequest { TorrentBytes = torrent, Title = "Торрент-файл" };
    var torrentReceipt = await a.SendDownloadAsync(peer, torrentRequest);
    Check(torrentReceipt.Accepted && received.Last().TorrentBytes!.SequenceEqual(torrent) && received.Last().Magnet is null,
        "selected .torrent bytes arrive unchanged without sending a sender filesystem path");

    callbackMode = "reject";
    var rejectedRequest = new LanDownloadRequest { Magnet = OtherMagnet };
    var declinedReceipt = await a.SendDownloadAsync(peer, rejectedRequest);
    Check(!declinedReceipt.Accepted && received.Count == 2, "receiver queue refusal is returned accurately and does not report an accepted download");
    callbackMode = "throw";
    var transientRequest = new LanDownloadRequest { Magnet = OtherMagnet };
    var failedReceipt = await a.SendDownloadAsync(peer, transientRequest);
    Check(!failedReceipt.Accepted && received.Count == 2, "receiver callback exception returns failure without a success receipt");
    callbackMode = "accept";
    var recoveredReceipt = await a.SendDownloadAsync(peer, transientRequest);
    Check(recoveredReceipt.Accepted && received.Count == 3, "same request can be retried after a temporary receiver queue error");

    a.Name = "Рабочий ноутбук"; b.Name = "Кино в гостиной";
    var aId = a.Id; var aFingerprint = a.Fingerprint; var bId = b.Id; var bFingerprint = b.Fingerprint;
    var savedAName = a.Name; var savedBName = b.Name;
    await a.StopAsync(); await b.StopAsync(); await a.DisposeAsync(); await b.DisposeAsync();
    a = Create(aOptions, true); b = Create(bOptions, false); await a.StartAsync(); await b.StartAsync();
    Check(a.Id == aId && a.Fingerprint == aFingerprint && a.Name == savedAName && b.Id == bId && b.Fingerprint == bFingerprint && b.Name == savedBName,
        "device names, install identities and certificate private keys survive service restart");
    Check(a.Devices.Single(x => x.Id == bId).Paired && b.Devices.Single(x => x.Id == aId).Paired,
        "both certificate pins survive restart");
    peer = await a.FindManualAsync($"127.0.0.1:{b.Port}");
    var afterRestart = await a.SendDownloadAsync(peer, request);
    Check(afterRestart.Accepted && afterRestart.DownloadId == receipt.DownloadId && received.Count == 3,
        "persisted receipt prevents the original request from being enqueued again after receiver restart");

    using (var senderCertificate = LoadIdentity(aOptions.StateDirectory))
    using (var unknownCertificate = CreateCertificate())
    {
        var senderIdentity = new LanDevice { Id = a.Id, Name = a.Name, Port = a.Port, Fingerprint = a.Fingerprint };
        var unknownIdentity = new LanDevice { Id = Guid.NewGuid(), Name = "Raw unknown", Port = 45832,
            Fingerprint = Convert.ToHexString(SHA256.HashData(unknownCertificate.RawData)) };
        var beforeRaw = received.Count;
        var unknownResponse = await RawExchange(unknownCertificate, peer,
            new { Protocol = LanProtocol.Name, Kind = "download", Identity = unknownIdentity, Request = new LanDownloadRequest { Magnet = Magnet } });
        Check(unknownResponse?.GetProperty("Kind").GetString() == "error", "server rejects a raw unpaired download even when the client bypasses its trust guard");
        await Reject(() => RawExchange(unknownCertificate, peer,
            new { Protocol = LanProtocol.Name, Kind = "download", Identity = senderIdentity, Request = new LanDownloadRequest { Magnet = Magnet } }),
            "raw client cannot claim a paired sender ID using an unrelated TLS certificate");
        var missingHashResponse = await RawExchange(senderCertificate, peer,
            new { Protocol = LanProtocol.Name, Kind = "download", Identity = senderIdentity, Request = new LanDownloadRequest { Magnet = "magnet:?dn=NoHash" } });
        Check(missingHashResponse?.GetProperty("Kind").GetString() == "error", "receiver independently rejects a malformed magnet after trusted mutual TLS");
        var oversizedResponse = await RawExchange(senderCertificate, peer,
            new { Protocol = LanProtocol.Name, Kind = "download", Identity = senderIdentity,
                Request = new LanDownloadRequest { TorrentBytes = new byte[LanProtocol.MaxTorrentBytes + 1] } });
        Check(oversizedResponse?.GetProperty("Kind").GetString() == "error", "receiver independently rejects a torrent payload above 10 MiB");
        await Reject(() => RawExchange(senderCertificate, peer, null, LanProtocol.MaxFrameBytes + 1),
            "receiver rejects an oversized trusted frame from its header before allocating or reading its body");
        await Reject(() => RawExchange(unknownCertificate, peer, null, 4097),
            "unknown certificate cannot allocate a large command frame before pairing");
        await Reject(() => RawExchange(senderCertificate, peer, null, -1), "negative wire length is rejected without queue activity");
        Check(received.Count == beforeRaw, "none of the raw authentication, malformed or oversized requests enqueue files");
    }

    var previousReceiver = b.ReceiveDownloadAsync;
    var firstQueued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var secondArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var queueCallbacks = 0;
    b.ReceiveDownloadAsync = async (incoming, token) =>
    {
        Interlocked.Increment(ref queueCallbacks); firstQueued.TrySetResult();
        await releaseFirst.Task.WaitAsync(token);
        return new() { RequestId = incoming.RequestId, Accepted = false, Message = "Fixture did not enqueue" };
    };
    try
    {
        var firstSend = a.SendDownloadAsync(peer, new() { Magnet = Magnet });
        await firstQueued.Task.WaitAsync(TimeSpan.FromSeconds(2));
        void ObserveArrival(object? _, EventArgs __) => secondArrived.TrySetResult();
        b.Changed += ObserveArrival;
        var secondSend = a.SendDownloadAsync(peer, new() { Magnet = OtherMagnet });
        await secondArrived.Task.WaitAsync(TimeSpan.FromSeconds(2));
        b.Changed -= ObserveArrival;
        await b.ForgetAsync(a.Id); releaseFirst.TrySetResult();
        await firstSend;
        try { var queuedReceipt = await secondSend; Check(!queuedReceipt.Accepted, "request queued before revocation cannot report acceptance afterwards"); }
        catch (LanException) { Check(true, "request queued before revocation is refused afterwards"); }
        Check(queueCallbacks == 1 && received.Count == 3, "receiver rechecks trust after its queue lock before invoking a formerly paired sender");
    }
    finally { releaseFirst.TrySetResult(); b.ReceiveDownloadAsync = previousReceiver; }
    b.AllowPairingFor(TimeSpan.FromMinutes(2)); await a.PairAsync(peer);

    // Real UDP input must survive malformed announcements, and an announced
    // WAN address must never override the packet's local source endpoint.
    using (var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
    {
        var destination = new IPEndPoint(IPAddress.Loopback, b.BoundDiscoveryPort);
        var advertisedId = Guid.NewGuid();
        foreach (var packet in new[]
        {
            Encoding.UTF8.GetBytes("{"), new byte[4097],
            JsonSerializer.SerializeToUtf8Bytes(new { Protocol = LanProtocol.Name, Kind = "announce", Identity = new { Id = Guid.NewGuid(), Name = "Bad", Port = 45832, Fingerprint = (string?)null } }),
            JsonSerializer.SerializeToUtf8Bytes(new { Protocol = "foreign/1", Kind = "announce", Identity = new { Id = Guid.NewGuid(), Name = "Bad", Port = 45832, Fingerprint = new string('A', 64) } })
        }) await udp.SendAsync(packet, destination);
        var validPacket = JsonSerializer.SerializeToUtf8Bytes(new { Protocol = LanProtocol.Name, Kind = "announce", Identity = new
        { Id = advertisedId, Name = "Новый компьютер", Port = 45832, Fingerprint = new string('A', 64), Address = "8.8.8.8", Paired = true } });
        await udp.SendAsync(validPacket, destination);
        var seen = Stopwatch.StartNew();
        while (!b.Devices.Any(x => x.Id == advertisedId) && seen.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(20);
        var advertised = b.Devices.Single(x => x.Id == advertisedId);
        Check(advertised.Address == "127.0.0.1" && !advertised.Paired, "malformed UDP does not stop discovery and unverified WAN redirects or paired claims are ignored");
        Check(!LanNetwork.IsLocal(IPAddress.Parse("8.8.8.8"), true) && !LanNetwork.IsLocal(IPAddress.Loopback, false),
            "production local-network validation excludes public addresses and test-only loopback");
        await Reject(() => a.FindManualAsync("8.8.8.8:45832"), "manual device selection cannot connect to a public WAN address");
    }

    await b.ForgetAsync(a.Id);
    await Reject(() => a.SendDownloadAsync(peer, new() { Magnet = Magnet }), "receiver revocation prevents a formerly paired sender from enqueuing");
    Check(received.Count == 3, "revoked peer never reaches the queue callback");
    b.AllowPairingFor(TimeSpan.FromMinutes(2)); await a.PairAsync(peer);

    await b.StopAsync(); await b.DisposeAsync(); b = null;
    // Keep the receiver's installation ID but replace its private identity with
    // another installation's key. A trusted sender must reject this impostor.
    var receiverKey = Path.Combine(bOptions.StateDirectory, "identity.key");
    var originalKey = await File.ReadAllBytesAsync(receiverKey);
    await File.WriteAllBytesAsync(receiverKey, await File.ReadAllBytesAsync(Path.Combine(directory, "attacker", "identity.key")));
    b = Create(bOptions, false); await b.StartAsync();
    await Reject(() => a.FindManualAsync($"127.0.0.1:{b.Port}"), "a trusted device ID presenting a different certificate is rejected");
    await Reject(() => a.SendDownloadAsync(peer with { Port = b.Port }, new() { Magnet = Magnet }), "pinned TLS certificate mismatch prevents download transmission");
    Check(received.Count == 3, "certificate impostor cannot enqueue work using the trusted device identity");
    await b.StopAsync(); await b.DisposeAsync(); b = null;
    await File.WriteAllBytesAsync(receiverKey, originalKey);
    b = Create(bOptions, false); await b.StartAsync();
    peer = await a.FindManualAsync($"127.0.0.1:{b.Port}");

    var elapsed = Stopwatch.StartNew();
    await b.StopAsync();
    await Reject(() => a.SendDownloadAsync(peer, new() { Magnet = Magnet }), "offline receiver returns a bounded connection failure");
    Check(elapsed.Elapsed < TimeSpan.FromSeconds(5), "offline send does not hang the application");
    await b.DisposeAsync(); b = Create(bOptions, false); await b.StartAsync();
    peer = await a.FindManualAsync($"127.0.0.1:{b.Port}");
    callbackMode = "wait";
    var pendingSend = a.SendDownloadAsync(peer, new() { Magnet = OtherMagnet });
    await Task.Delay(150);
    elapsed.Restart(); await b.StopAsync();
    Check(elapsed.Elapsed < TimeSpan.FromSeconds(3), "service shutdown remains bounded with a receiver request in flight");
    try { await pendingSend.WaitAsync(TimeSpan.FromSeconds(4)); }
    catch (Exception error) when (error is LanException or IOException or AuthenticationException or OperationCanceledException or SocketException) { }
    Check(received.Count == 3, "shutdown cancels unfinished queue reception without claiming a new accepted item");

    Console.WriteLine($"All {checks} LAN checks passed.");
    File.WriteAllText(Path.Combine(evidence, "checks.json"), JsonSerializer.Serialize(new
    {
        Passed = true, Checks = checks, Utc = DateTimeOffset.UtcNow,
        Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription
    }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

catch (Exception error)
{
    Console.Error.WriteLine(error);
    File.AppendAllText(checkLog, "FAIL: " + error + Environment.NewLine);
    File.WriteAllText(Path.Combine(evidence, "checks.json"), JsonSerializer.Serialize(new
    { Passed = false, Checks = checks, Utc = DateTimeOffset.UtcNow, Error = error.Message }, new JsonSerializerOptions { WriteIndented = true }));
    if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        Console.Error.WriteLine("::error title=LAN integration check::" + error.Message.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A"));
    return 1;
}
finally
{
    if (a is not null) await a.DisposeAsync();
    if (b is not null) await b.DisposeAsync();
    if (attacker is not null) await attacker.DisposeAsync();
    Directory.Delete(directory, true);
}

static X509Certificate2 CreateCertificate()
{
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var request = new CertificateRequest("CN=Raw-LAN-test", key, HashAlgorithmName.SHA256);
    request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
    request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection
    { new("1.3.6.1.5.5.7.3.1"), new("1.3.6.1.5.5.7.3.2") }, true));
    return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
}

static async Task<bool> WaitForCancellation(CancellationToken token)
{
    await Task.Delay(TimeSpan.FromSeconds(30), token);
    return true;
}

static X509Certificate2 LoadIdentity(string stateDirectory)
{
    var bytes = File.ReadAllBytes(Path.Combine(stateDirectory, "identity.key"));
    var plain = OperatingSystem.IsWindows() ? Dpapi.Unprotect(bytes) : bytes;
    try { return X509CertificateLoader.LoadPkcs12(plain, null, X509KeyStorageFlags.EphemeralKeySet); }
    finally { CryptographicOperations.ZeroMemory(plain); }
}

static async Task<JsonElement?> RawExchange(X509Certificate2 identity, LanDevice peer, object? message, int? lengthOverride = null)
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(4));
    using var tcp = new TcpClient(AddressFamily.InterNetwork);
    await tcp.ConnectAsync(IPAddress.Loopback, peer.Port, deadline.Token);
    using var tls = new SslStream(tcp.GetStream(), false, (_, certificate, _, _) => certificate is not null &&
        Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData())) == peer.Fingerprint);
    await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
    {
        TargetHost = "Ka4alka-LAN", ClientCertificates = new X509CertificateCollection { identity },
        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
    }, deadline.Token);
    var bytes = message is null ? [] : JsonSerializer.SerializeToUtf8Bytes(message);
    var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, lengthOverride ?? bytes.Length);
    await tls.WriteAsync(header, deadline.Token);
    if (bytes.Length > 0) await tls.WriteAsync(bytes, deadline.Token);
    await tls.FlushAsync(deadline.Token);
    await tls.ReadExactlyAsync(header, deadline.Token);
    var length = BinaryPrimitives.ReadInt32BigEndian(header);
    if (length is < 2 or > 4096) throw new InvalidDataException("Unexpected raw response frame length.");
    bytes = new byte[length]; await tls.ReadExactlyAsync(bytes, deadline.Token);
    using var json = JsonDocument.Parse(bytes);
    return json.RootElement.Clone();
}
