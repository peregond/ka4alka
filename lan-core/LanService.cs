using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Kachalka.Lan;

/// <summary>Opt-in local network discovery, certificate pairing and download requests. It never opens ports on a router.</summary>
public sealed class LanService : IAsyncDisposable
{
    readonly LanServiceOptions options;
    readonly LanState state;
    readonly object gate = new();
    readonly SemaphoreSlim lifecycle = new(1, 1);
    readonly SemaphoreSlim clientSlots = new(8, 8);
    readonly SemaphoreSlim outgoingSlots = new(8, 8);
    readonly SemaphoreSlim pairingSlot = new(1, 1);
    readonly SemaphoreSlim receiveSlot = new(1, 1);
    readonly Dictionary<Guid, LanDevice> devices = [];
    readonly Dictionary<string, (DateTimeOffset Since, int Count)> rates = [];
    readonly ConcurrentDictionary<int, TcpClient> activeClients = new();
    readonly ConcurrentDictionary<int, Task> clientTasks = new();
    readonly ConcurrentDictionary<Guid, byte> probes = new();
    CancellationTokenSource? lifetime;
    TcpListener? listener;
    UdpClient? discovery;
    Task? acceptTask, discoveryTask, heartbeatTask;
    DateTimeOffset pairingExpires;
    int nextClient;
    bool disposed;

    public LanService(LanServiceOptions options)
    {
        if (options.TcpPort is < 0 or > 65535 || options.DiscoveryPort is < 0 or > 65535 || options.RequestTimeout < TimeSpan.FromSeconds(1) || options.RequestTimeout > TimeSpan.FromMinutes(2) || options.PairingTimeout < TimeSpan.FromSeconds(1) || options.PairingTimeout > TimeSpan.FromMinutes(2)) throw new ArgumentException("Недопустимые параметры локальной сети.", nameof(options));
        this.options = options;
        state = new LanState(options);
        foreach (var peer in state.Data.Peers) devices[peer.Id] = peer with { LastSeenUtc = DateTimeOffset.MinValue, Paired = true };
    }

    public Guid Id => state.Data.Id;
    public string Fingerprint => state.Fingerprint;
    public string Name
    {
        get { lock (gate) return state.Data.Name; }
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var name = LanState.CleanName(value);
            lock (gate)
            {
                var old = state.Data.Name; state.Data.Name = name;
                try { state.Save(); } catch { state.Data.Name = old; throw; }
            }
            NotifyChanged();
        }
    }
    public int Port { get; private set; }
    internal int BoundDiscoveryPort => (discovery?.Client.LocalEndPoint as IPEndPoint)?.Port ?? 0;
    public bool IsRunning { get { lock (gate) return lifetime is { IsCancellationRequested: false }; } }
    public bool IsPairingAllowed => IsRunning && PairingExpiresUtc > DateTimeOffset.UtcNow;
    public DateTimeOffset PairingExpiresUtc { get { lock (gate) return pairingExpires; } }
    public IReadOnlyList<LanDevice> Devices { get { lock (gate) return devices.Values.OrderByDescending(d => d.Paired).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(); } }
    public event EventHandler? Changed;
    public Func<LanPairingRequest, CancellationToken, Task<bool>>? ConfirmPairingAsync { get; set; }
    public Func<LanDownloadRequest, CancellationToken, Task<LanDownloadReceipt>>? ReceiveDownloadAsync { get; set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (IsRunning) return;
            cancellationToken.ThrowIfCancellationRequested();
            var source = new CancellationTokenSource();
            var tcp = new TcpListener(options.AllowLoopbackForTests ? IPAddress.Loopback : IPAddress.Any, options.TcpPort);
            UdpClient? udp = null;
            try
            {
                tcp.Start(16);
                udp = new UdpClient(AddressFamily.InterNetwork);
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(options.AllowLoopbackForTests ? IPAddress.Loopback : IPAddress.Any, options.DiscoveryPort));
                udp.EnableBroadcast = true;
                cancellationToken.ThrowIfCancellationRequested();
                lock (gate) { lifetime = source; listener = tcp; discovery = udp; Port = ((IPEndPoint)tcp.LocalEndpoint).Port; }
                acceptTask = AcceptLoopAsync(tcp, source.Token);
                discoveryTask = DiscoveryLoopAsync(udp, source.Token);
                heartbeatTask = HeartbeatAsync(source.Token);
                NotifyChanged();
            }
            catch
            {
                source.Cancel(); source.Dispose(); tcp.Stop(); udp?.Dispose();
                throw;
            }
        }
        catch (SocketException ex) { throw new LanException("Не удалось включить локальную сеть: порт уже занят или заблокирован. Проверьте брандмауэр.", ex); }
        finally { lifecycle.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CancellationTokenSource? source;
            lock (gate) { source = lifetime; lifetime = null; pairingExpires = default; }
            if (source is null) return;
            source.Cancel(); listener?.Stop(); discovery?.Dispose();
            foreach (var client in activeClients.Values) client.Dispose();
            var tasks = clientTasks.Values.Concat(new[] { acceptTask, discoveryTask, heartbeatTask }.OfType<Task>()).ToArray();
            try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or SocketException or IOException or ObjectDisposedException) { }
            lock (gate)
            {
                foreach (var id in devices.Keys.ToArray())
                    if (devices[id].Paired) devices[id] = devices[id] with { LastSeenUtc = DateTimeOffset.MinValue };
                    else devices.Remove(id);
            }
            source.Dispose(); listener = null; discovery = null; Port = 0;
            NotifyChanged();
        }
        finally { lifecycle.Release(); }
    }

    public void AllowPairingFor(TimeSpan duration)
    {
        if (!IsRunning) throw new LanException("Сначала включите локальную сеть.");
        if (duration < TimeSpan.Zero || duration > TimeSpan.FromMinutes(2)) throw new ArgumentOutOfRangeException(nameof(duration));
        lock (gate) pairingExpires = DateTimeOffset.UtcNow.Add(duration);
        NotifyChanged();
    }

    public async Task DiscoverAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = OperationToken(cancellationToken, options.RequestTimeout);
        var udp = discovery ?? throw new LanException("Локальная сеть выключена.");
        var message = JsonSerializer.SerializeToUtf8Bytes(new WireMessage { Kind = "discover", Identity = LocalIdentity() }, LanProtocol.JsonOptions);
        foreach (var (address, mask) in LanNetwork.Interfaces())
        {
            try { await udp.SendAsync(message, new IPEndPoint(LanNetwork.Broadcast(address, mask), options.DiscoveryPort), timeout.Token).ConfigureAwait(false); }
            catch (SocketException) { }
        }
        var peers = Devices.Where(d => d.Paired).ToArray();
        await Task.WhenAll(peers.Select(p => ProbeQuietlyAsync(p, timeout.Token))).ConfigureAwait(false);
    }

    public async Task<LanDevice> FindManualAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        var pieces = endpoint.Trim().Split(':');
        if (pieces.Length != 2 || !IPAddress.TryParse(pieces[0], out var ip) || !int.TryParse(pieces[1], out var port) || port is < 1 or > 65535 || !LanNetwork.IsLocal(ip, options.AllowLoopbackForTests)) throw new LanException("Введите локальный IPv4-адрес и порт, например 192.168.1.10:45832.");
        return await ProbeAsync(new LanDevice { Address = ip.ToString(), Port = port }, cancellationToken).ConfigureAwait(false);
    }

    public async Task PairAsync(LanDevice device, CancellationToken cancellationToken = default)
    {
        using var timeout = OperationToken(cancellationToken, options.PairingTimeout);
        if (!await pairingSlot.WaitAsync(0, timeout.Token).ConfigureAwait(false)) throw new LanException("Другое сопряжение уже выполняется.");
        var added = false;
        LanDevice? verified = null;
        try
        {
            using var connection = await ConnectAsync(device, timeout.Token).ConfigureAwait(false);
            await LanProtocol.WriteAsync(connection.Stream, new WireMessage { Kind = "pair", Identity = LocalIdentity() }, timeout.Token).ConfigureAwait(false);
            var challenge = await ReadResponseAsync(connection.Stream, timeout.Token).ConfigureAwait(false);
            if (challenge.Kind != "pair-challenge") throw new LanException("Устройство не открыло окно сопряжения.");
            verified = ValidateIdentity(challenge.Identity, connection.Fingerprint, device.Address, device.Port, device.Id);
            var expires = DateTimeOffset.UtcNow.Add(options.PairingTimeout);
            var approve = await ConfirmAsync(verified, false, expires, timeout.Token).ConfigureAwait(false);
            await LanProtocol.WriteAsync(connection.Stream, new WireMessage { Kind = "pair-decision", Approved = approve }, timeout.Token).ConfigureAwait(false);
            var peerApproval = await ReadResponseAsync(connection.Stream, timeout.Token).ConfigureAwait(false);
            if (peerApproval.Kind != "pair-decision" || !approve || !peerApproval.Approved) throw new LanException("Сопряжение отклонено. Подтвердите одинаковый код на обоих устройствах.");
            var ready = await ReadResponseAsync(connection.Stream, timeout.Token).ConfigureAwait(false);
            if (ready.Kind != "pair-ready") throw new LanException("Не удалось завершить сопряжение.");
            await LanProtocol.WriteAsync(connection.Stream, new WireMessage { Kind = "pair-commit" }, timeout.Token).ConfigureAwait(false);
            if ((await ReadResponseAsync(connection.Stream, timeout.Token).ConfigureAwait(false)).Kind != "pair-complete") throw new LanException("Не удалось завершить сопряжение.");
            timeout.Token.ThrowIfCancellationRequested();
            added = Trust(verified);
            await LanProtocol.WriteAsync(connection.Stream, new WireMessage { Kind = "pair-ack" }, timeout.Token).ConfigureAwait(false);
            if ((await ReadResponseAsync(connection.Stream, timeout.Token).ConfigureAwait(false)).Kind != "pair-done") throw new LanException("Не удалось подтвердить сопряжение.");
            added = false;
            NotifyChanged();
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not LanException) { throw ConnectionFailure(ex); }
        finally
        {
            if (added && verified is not null) RemoveTrust(verified.Id);
            pairingSlot.Release();
        }
    }

    public Task ForgetAsync(Guid peerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveTrust(peerId);
        NotifyChanged();
        return Task.CompletedTask;
    }

    public async Task<LanDownloadReceipt> SendDownloadAsync(LanDevice device, LanDownloadRequest request, CancellationToken cancellationToken = default)
    {
        ValidateDownload(request);
        var trusted = Trusted(device.Id, device.Fingerprint);
        if (trusted is null) throw new LanException("Сначала выполните сопряжение с этим устройством.");
        using var timeout = OperationToken(cancellationToken, options.RequestTimeout);
        try
        {
            using var connection = await ConnectAsync(device with { Fingerprint = trusted.Fingerprint }, timeout.Token).ConfigureAwait(false);
            await LanProtocol.WriteAsync(connection.Stream, new WireMessage { Kind = "download", Identity = LocalIdentity(), Request = request with { SenderId = Id, SenderFingerprint = Fingerprint } }, timeout.Token).ConfigureAwait(false);
            var response = await ReadResponseAsync(connection.Stream, timeout.Token).ConfigureAwait(false);
            if (response.Kind != "receipt" || response.Receipt is null || response.Receipt.RequestId != request.RequestId) throw new LanException("Устройство не подтвердило добавление загрузки.");
            UpdateVerified(trusted with { Address = device.Address, Port = device.Port, LastSeenUtc = DateTimeOffset.UtcNow });
            return response.Receipt;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && IsRunning) { throw new LanException("Устройство не ответило вовремя. Проверьте, что Качалка запущена и локальная сеть разрешена брандмауэром."); }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not LanException) { throw ConnectionFailure(ex); }
    }

    async Task AcceptLoopAsync(TcpListener tcp, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await tcp.AcceptTcpClientAsync(token).ConfigureAwait(false);
                var endpoint = client.Client.RemoteEndPoint as IPEndPoint;
                if (endpoint is null || !LanNetwork.IsLocal(endpoint.Address, options.AllowLoopbackForTests) || !RateAllowed(endpoint.Address, 120) || !await clientSlots.WaitAsync(0, token).ConfigureAwait(false)) { client.Dispose(); continue; }
                var id = Interlocked.Increment(ref nextClient);
                activeClients[id] = client;
                var captured = client;
                var task = Task.Run(async () =>
                {
                    try { await HandleClientAsync(captured, endpoint.Address, token).ConfigureAwait(false); }
                    catch (Exception) { /* A failed remote session must never stop accepting other devices. */ }
                    finally { captured.Dispose(); activeClients.TryRemove(id, out _); clientSlots.Release(); }
                }, CancellationToken.None);
                clientTasks[id] = task;
                _ = task.ContinueWith(_ => clientTasks.TryRemove(id, out var ignored), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
            { client?.Dispose(); if (token.IsCancellationRequested) break; }
        }
    }

    async Task HandleClientAsync(TcpClient client, IPAddress address, CancellationToken serviceToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(serviceToken);
        timeout.CancelAfter(options.RequestTimeout);
        string? fingerprint = null;
        using var tls = new SslStream(client.GetStream(), false, (_, certificate, _, _) =>
        {
            if (!ValidCertificate(certificate)) return false;
            fingerprint = CertificateFingerprint(certificate!);
            return true;
        });
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = state.Certificate, ClientCertificateRequired = true, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, CertificateRevocationCheckMode = X509RevocationMode.NoCheck }, timeout.Token).ConfigureAwait(false);
        if (fingerprint is null) throw new LanException("Сертификат устройства отсутствует.");
        var knownPin = false;
        lock (gate) knownPin = state.Data.Peers.Any(p => p.Fingerprint == fingerprint);
        var request = await LanProtocol.ReadAsync(tls, timeout.Token, knownPin ? LanProtocol.MaxFrameBytes : 4096).ConfigureAwait(false);
        var peer = ValidateIdentity(request.Identity, fingerprint, address.ToString(), request.Identity?.Port ?? 0);
        if (request.Kind == "hello")
        {
            var trusted = Trusted(peer.Id, fingerprint);
            if (trusted is not null) UpdateVerified(peer with { Paired = true });
            await LanProtocol.WriteAsync(tls, new WireMessage { Kind = "hello", Identity = LocalIdentity() }, timeout.Token).ConfigureAwait(false);
            return;
        }
        if (request.Kind == "pair")
        {
            if (!IsPairingAllowed || !RateAllowed(address, 8, "pair") || !await pairingSlot.WaitAsync(0, timeout.Token).ConfigureAwait(false))
            { await ErrorAsync(tls, "Откройте сопряжение на принимающем устройстве. Оно доступно две минуты.", timeout.Token).ConfigureAwait(false); return; }
            try
            {
                var expiry = PairingExpiresUtc;
                var remaining = expiry - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero) return;
                timeout.CancelAfter(remaining < options.PairingTimeout ? remaining : options.PairingTimeout);
                await ReceivePairAsync(tls, peer, expiry, timeout.Token).ConfigureAwait(false);
            }
            finally { pairingSlot.Release(); }
            return;
        }
        if (request.Kind != "download" || Trusted(peer.Id, fingerprint) is null)
        { await ErrorAsync(tls, "Устройство не сопряжено. Выполните сопряжение перед отправкой загрузки.", timeout.Token).ConfigureAwait(false); return; }
        if (request.Request is null) { await ErrorAsync(tls, "Запрос загрузки отсутствует.", timeout.Token).ConfigureAwait(false); return; }
        try { ValidateDownload(request.Request); }
        catch (LanException ex) { await ErrorAsync(tls, ex.Message, timeout.Token).ConfigureAwait(false); return; }
        UpdateVerified(peer with { Paired = true });
        var receipt = await ReceiveAsync(peer, request.Request with { SenderId = peer.Id, SenderFingerprint = fingerprint }, timeout.Token).ConfigureAwait(false);
        await LanProtocol.WriteAsync(tls, new WireMessage { Kind = "receipt", Receipt = receipt }, timeout.Token).ConfigureAwait(false);
    }

    async Task ReceivePairAsync(SslStream tls, LanDevice peer, DateTimeOffset expiry, CancellationToken token)
    {
        var added = false;
        try
        {
            await LanProtocol.WriteAsync(tls, new WireMessage { Kind = "pair-challenge", Identity = LocalIdentity() }, token).ConfigureAwait(false);
            var localApproval = ConfirmAsync(peer, true, expiry, token);
            var remoteApproval = LanProtocol.ReadAsync(tls, token, 4096);
            var approved = await localApproval.ConfigureAwait(false);
            await LanProtocol.WriteAsync(tls, new WireMessage { Kind = "pair-decision", Approved = approved }, token).ConfigureAwait(false);
            var decision = await remoteApproval.ConfigureAwait(false);
            if (decision.Kind != "pair-decision" || !approved || !decision.Approved || DateTimeOffset.UtcNow >= expiry) return;
            await LanProtocol.WriteAsync(tls, new WireMessage { Kind = "pair-ready" }, token).ConfigureAwait(false);
            if ((await LanProtocol.ReadAsync(tls, token, 4096).ConfigureAwait(false)).Kind != "pair-commit") return;
            token.ThrowIfCancellationRequested();
            added = Trust(peer);
            await LanProtocol.WriteAsync(tls, new WireMessage { Kind = "pair-complete" }, token).ConfigureAwait(false);
            if ((await LanProtocol.ReadAsync(tls, token, 4096).ConfigureAwait(false)).Kind != "pair-ack") return;
            await LanProtocol.WriteAsync(tls, new WireMessage { Kind = "pair-done" }, token).ConfigureAwait(false);
            added = false;
            lock (gate) pairingExpires = default;
            NotifyChanged();
        }
        finally { if (added) RemoveTrust(peer.Id); }
    }

    async Task<bool> ConfirmAsync(LanDevice peer, bool incoming, DateTimeOffset expires, CancellationToken token)
    {
        var callback = ConfirmPairingAsync;
        if (callback is null) return false;
        var local = LocalIdentity();
        var ordered = new[] { local, peer }.OrderBy(d => d.Fingerprint, StringComparer.Ordinal).ThenBy(d => d.Id).ToArray();
        var payload = JsonSerializer.SerializeToUtf8Bytes(ordered.Select(d => new { d.Id, d.Name, d.Fingerprint }).ToArray(), LanProtocol.JsonOptions);
        var code = Convert.ToHexString(SHA256.HashData(payload).AsSpan(0, 6));
        code = $"{code[..4]} {code[4..8]} {code[8..]}";
        return await callback(new LanPairingRequest { Device = peer, VerificationCode = code, ExpiresUtc = expires, Incoming = incoming }, token).WaitAsync(token).ConfigureAwait(false);
    }

    async Task<LanDownloadReceipt> ReceiveAsync(LanDevice peer, LanDownloadRequest request, CancellationToken token)
    {
        await receiveSlot.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (Trusted(peer.Id, peer.Fingerprint) is null) return new LanDownloadReceipt { RequestId = request.RequestId, Accepted = false, Message = "Сопряжение с отправителем отменено." };
            var digest = DownloadDigest(request);
            lock (gate)
            {
                var previous = state.Data.Receipts.FirstOrDefault(r => r.PeerFingerprint == peer.Fingerprint && r.RequestId == request.RequestId);
                if (previous is not null)
                    return previous.ContentDigest == digest ? previous.Receipt : new LanDownloadReceipt { RequestId = request.RequestId, Accepted = false, Message = "Этот идентификатор уже использован для другой загрузки." };
            }
            if (ReceiveDownloadAsync is null) return new LanDownloadReceipt { RequestId = request.RequestId, Accepted = false, Message = "Приём загрузок ещё не готов. Попробуйте позже." };
            LanDownloadReceipt receipt;
            try { receipt = (await ReceiveDownloadAsync(request, token).WaitAsync(token).ConfigureAwait(false)) with { RequestId = request.RequestId }; }
            catch (OperationCanceledException) { throw; }
            catch { return new LanDownloadReceipt { RequestId = request.RequestId, Accepted = false, Message = "Не удалось добавить загрузку на принимающем устройстве. Проверьте его журнал диагностики." }; }
            token.ThrowIfCancellationRequested();
            receipt = receipt with { Message = Limit(receipt.Message, 512), DownloadId = Limit(receipt.DownloadId, 128) };
            if (receipt.Accepted)
                lock (gate)
                {
                    state.Data.Receipts.Add(new SavedReceipt(peer.Fingerprint, request.RequestId, digest, receipt, DateTimeOffset.UtcNow));
                    while (state.Data.Receipts.Count > 256) state.Data.Receipts.RemoveAt(0);
                    state.Save();
                }
            return receipt;
        }
        finally { receiveSlot.Release(); }
    }

    async Task<LanDevice> ProbeAsync(LanDevice device, CancellationToken cancellationToken)
    {
        using var timeout = OperationToken(cancellationToken, options.RequestTimeout);
        try
        {
            using var connection = await ConnectAsync(device, timeout.Token).ConfigureAwait(false);
            await LanProtocol.WriteAsync(connection.Stream, new WireMessage { Kind = "hello", Identity = LocalIdentity() }, timeout.Token).ConfigureAwait(false);
            var response = await ReadResponseAsync(connection.Stream, timeout.Token).ConfigureAwait(false);
            if (response.Kind != "hello") throw new LanException("Устройство не распознано.");
            var peer = ValidateIdentity(response.Identity, connection.Fingerprint, device.Address, device.Port, device.Id);
            UpdateVerified(peer);
            return Devices.First(d => d.Id == peer.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not LanException) { throw ConnectionFailure(ex); }
    }

    async Task ProbeQuietlyAsync(LanDevice device, CancellationToken token)
    {
        if (!probes.TryAdd(device.Id, 0)) return;
        try { await ProbeAsync(device, token).ConfigureAwait(false); }
        catch (Exception ex) when (ex is LanException or OperationCanceledException) { }
        finally { probes.TryRemove(device.Id, out _); }
    }

    async Task<Connection> ConnectAsync(LanDevice device, CancellationToken token)
    {
        if (!IPAddress.TryParse(device.Address, out var address) || !LanNetwork.IsLocal(address, options.AllowLoopbackForTests) || device.Port is < 1 or > 65535) throw new LanException("Устройство должно находиться в той же локальной сети.");
        await outgoingSlots.WaitAsync(token).ConfigureAwait(false);
        TcpClient? client = null;
        SslStream? tls = null;
        try
        {
            client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(address, device.Port, token).ConfigureAwait(false);
            string? actualFingerprint = null;
            var saved = device.Id == Guid.Empty ? null : TrustedById(device.Id);
            var expected = saved?.Fingerprint ?? device.Fingerprint;
            tls = new SslStream(client.GetStream(), false, (_, certificate, _, _) =>
            {
                if (!ValidCertificate(certificate)) return false;
                actualFingerprint = CertificateFingerprint(certificate!);
                return string.IsNullOrEmpty(expected) || FixedEqual(expected, actualFingerprint);
            });
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "Ka4alka-LAN", ClientCertificates = new X509CertificateCollection { state.Certificate }, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13, CertificateRevocationCheckMode = X509RevocationMode.NoCheck }, token).ConfigureAwait(false);
            return new Connection(client, tls, actualFingerprint ?? throw new LanException("Сертификат устройства отсутствует."), outgoingSlots);
        }
        catch { tls?.Dispose(); client?.Dispose(); outgoingSlots.Release(); throw; }
    }

    async Task DiscoveryLoopAsync(UdpClient udp, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var packet = await udp.ReceiveAsync(token).ConfigureAwait(false);
                if (packet.Buffer.Length is < 2 or > 4096 || !LanNetwork.IsLocal(packet.RemoteEndPoint.Address, options.AllowLoopbackForTests) || !RateAllowed(packet.RemoteEndPoint.Address, 120, "udp")) continue;
                WireMessage? message;
                try { message = JsonSerializer.Deserialize<WireMessage>(packet.Buffer, LanProtocol.JsonOptions); }
                catch (JsonException) { continue; }
                if (message?.Protocol != LanProtocol.Name || message.Kind is not ("discover" or "announce") || message.Identity is null) continue;
                LanDevice peer;
                try { peer = ValidateIdentity(message.Identity, message.Identity.Fingerprint, packet.RemoteEndPoint.Address.ToString(), message.Identity.Port); }
                catch (LanException) { continue; }
                var saved = TrustedById(peer.Id);
                if (saved is not null)
                {
                    if (FixedEqual(saved.Fingerprint, peer.Fingerprint)) _ = ProbeQuietlyAsync(peer with { Paired = true }, token);
                }
                else UpdateUnverified(peer);
                if (message.Kind == "discover")
                {
                    var response = JsonSerializer.SerializeToUtf8Bytes(new WireMessage { Kind = "announce", Identity = LocalIdentity() }, LanProtocol.JsonOptions);
                    await udp.SendAsync(response, packet.RemoteEndPoint, token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            { if (token.IsCancellationRequested) break; }
        }
    }

    async Task HeartbeatAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                try { await DiscoverAsync(token).ConfigureAwait(false); }
                catch (Exception ex) when (ex is LanException or OperationCanceledException or ObjectDisposedException) { if (token.IsCancellationRequested) break; }
                lock (gate)
                    foreach (var id in devices.Where(p => !p.Value.Paired && p.Value.LastSeenUtc < DateTimeOffset.UtcNow.AddMinutes(-2)).Select(p => p.Key).ToArray()) devices.Remove(id);
                NotifyChanged();
                await Task.Delay(TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    LanDevice LocalIdentity() => new() { Id = Id, Name = Name, Port = Port, Fingerprint = Fingerprint, LastSeenUtc = DateTimeOffset.UtcNow };
    LanDevice ValidateIdentity(LanDevice? device, string fingerprint, string address, int port, Guid expectedId = default)
    {
        if (device is null || device.Id == Guid.Empty || device.Id == Id || expectedId != Guid.Empty && expectedId != device.Id || port is < 1 or > 65535 || !ValidFingerprint(fingerprint) || !FixedEqual(fingerprint, device.Fingerprint)) throw new LanException("Идентификатор устройства или сертификат не совпадает.");
        var name = LanState.CleanName(device.Name);
        var saved = TrustedById(device.Id);
        if (saved is not null && !FixedEqual(saved.Fingerprint, fingerprint)) throw new LanException("Сертификат устройства изменился. Удалите старое сопряжение и сверьте новый код.");
        return device with { Name = name, Address = address, Port = port, Fingerprint = fingerprint.ToUpperInvariant(), LastSeenUtc = DateTimeOffset.UtcNow, Paired = saved is not null };
    }
    LanDevice? TrustedById(Guid id) { lock (gate) return state.Data.Peers.FirstOrDefault(p => p.Id == id); }
    LanDevice? Trusted(Guid id, string fingerprint) { var peer = TrustedById(id); return peer is not null && FixedEqual(peer.Fingerprint, fingerprint) ? peer : null; }
    bool Trust(LanDevice peer)
    {
        lock (gate)
        {
            var old = state.Data.Peers.FirstOrDefault(p => p.Id == peer.Id);
            if (old is not null)
            {
                if (!FixedEqual(old.Fingerprint, peer.Fingerprint)) throw new LanException("Сертификат сопряжённого устройства изменился.");
                devices[peer.Id] = peer with { Paired = true }; return false;
            }
            if (state.Data.Peers.Count >= 128) throw new LanException("Достигнут предел сопряжённых устройств.");
            var trusted = peer with { Paired = true };
            state.Data.Peers.Add(trusted); devices[peer.Id] = trusted;
            try { state.Save(); }
            catch { state.Data.Peers.Remove(trusted); devices[peer.Id] = peer with { Paired = false }; throw; }
            return true;
        }
    }
    void RemoveTrust(Guid id)
    {
        lock (gate)
        {
            var fingerprints = state.Data.Peers.Where(p => p.Id == id).Select(p => p.Fingerprint).ToArray();
            state.Data.Peers.RemoveAll(p => p.Id == id);
            state.Data.Receipts.RemoveAll(r => fingerprints.Contains(r.PeerFingerprint));
            devices.Remove(id); state.Save();
        }
    }
    void UpdateVerified(LanDevice peer)
    {
        lock (gate)
        {
            var index = state.Data.Peers.FindIndex(p => p.Id == peer.Id && FixedEqual(p.Fingerprint, peer.Fingerprint));
            var verified = peer with { Paired = index >= 0 };
            if (index >= 0) { state.Data.Peers[index] = verified; state.Save(); }
            if (devices.Count < 128 || devices.ContainsKey(peer.Id) || index >= 0) devices[peer.Id] = verified;
        }
        NotifyChanged();
    }
    void UpdateUnverified(LanDevice peer)
    {
        lock (gate) if (devices.Count < 128 || devices.ContainsKey(peer.Id)) devices[peer.Id] = peer with { Paired = false };
        NotifyChanged();
    }
    bool RateAllowed(IPAddress address, int limit, string suffix = "tcp")
    {
        lock (gate)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var key in rates.Where(r => r.Value.Since < now.AddMinutes(-1)).Select(r => r.Key).ToArray()) rates.Remove(key);
            var keyName = address + "/" + suffix;
            if (!rates.TryGetValue(keyName, out var value)) { if (rates.Count >= 256) return false; value = (now, 0); }
            rates[keyName] = (value.Since, value.Count + 1);
            return value.Count < limit;
        }
    }
    CancellationTokenSource OperationToken(CancellationToken external, TimeSpan limit)
    {
        CancellationToken token;
        lock (gate) token = lifetime is { IsCancellationRequested: false } current ? current.Token : throw new LanException("Локальная сеть выключена.");
        var source = CancellationTokenSource.CreateLinkedTokenSource(token, external); source.CancelAfter(limit); return source;
    }
    static async Task<WireMessage> ReadResponseAsync(Stream stream, CancellationToken token)
    {
        var response = await LanProtocol.ReadAsync(stream, token, 4096).ConfigureAwait(false);
        if (response.Kind == "error") throw new LanException(Limit(response.Message, 512) ?? "Ошибка локальной сети.");
        return response;
    }
    static Task ErrorAsync(Stream stream, string message, CancellationToken token) => LanProtocol.WriteAsync(stream, new WireMessage { Kind = "error", Message = message }, token);
    static bool ValidCertificate(X509Certificate? certificate)
    {
        if (certificate is null) return false;
        using var cert = new X509Certificate2(certificate);
        using var publicKey = cert.GetECDsaPublicKey();
        return cert.NotBefore.ToUniversalTime() <= DateTime.UtcNow && cert.NotAfter.ToUniversalTime() > DateTime.UtcNow && publicKey is not null;
    }
    static string CertificateFingerprint(X509Certificate certificate) => Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
    static bool ValidFingerprint(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    static bool FixedEqual(string? a, string? b)
    {
        if (a is null || b is null || !ValidFingerprint(a) || !ValidFingerprint(b)) return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(a), Convert.FromHexString(b));
    }
    static string? Limit(string? value, int max) => value is null ? null : new string(value.Where(c => !char.IsControl(c) || c is '\n' or '\t').Take(max).ToArray());
    static LanException ConnectionFailure(Exception ex) => ex is AuthenticationException ? new LanException("Сертификат устройства не совпадает с сопряжением. Удалите старое сопряжение и сверьте код заново.", ex) : new LanException("Не удалось связаться с устройством. Проверьте, что оно в той же сети, Качалка запущена и разрешена брандмауэром.", ex);
    void NotifyChanged() { try { Changed?.Invoke(this, EventArgs.Empty); } catch { } }
    static string DownloadDigest(LanDownloadRequest request)
    {
        // Sender identity is authenticated separately; every actual download field participates in retry identity.
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request with { SenderId = Guid.Empty, SenderFingerprint = null }, LanProtocol.JsonOptions)));
    }
    static bool ValidMagnet(string magnet)
    {
        if (!magnet.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            foreach (var item in magnet[8..].Split('&'))
            {
                var separator = item.IndexOf('=');
                if (separator < 0 || !item[..separator].Equals("xt", StringComparison.OrdinalIgnoreCase)) continue;
                var value = Uri.UnescapeDataString(item[(separator + 1)..]);
                if (value.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase))
                {
                    var hash = value[9..];
                    if (hash.Length == 40 && hash.All(Uri.IsHexDigit) || hash.Length == 32 && hash.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '2' and <= '7')) return true;
                }
                if (value.StartsWith("urn:btmh:1220", StringComparison.OrdinalIgnoreCase) && value.Length == 77 && value[13..].All(Uri.IsHexDigit)) return true;
            }
        }
        catch (UriFormatException) { }
        return false;
    }
    static void ValidateDownload(LanDownloadRequest request)
    {
        if (request.RequestId == Guid.Empty || (request.Magnet is null) == (request.TorrentBytes is null)) throw new LanException("Отправьте magnet-ссылку или торрент-файл.");
        if (request.Magnet is not null && (request.Magnet.Length > LanProtocol.MaxMagnetLength || !ValidMagnet(request.Magnet) || request.Magnet.Any(char.IsControl))) throw new LanException("Недопустимая magnet-ссылка.");
        if (request.TorrentBytes is not null && request.TorrentBytes.Length is < 1 or > LanProtocol.MaxTorrentBytes) throw new LanException("Торрент-файл должен быть меньше 10 МБ.");
        if (request.Title?.Length > 512 || request.Media?.Genres?.Length > 32) throw new LanException("Слишком длинное описание загрузки.");
        if (request.Media is { } media)
        {
            foreach (var value in new[] { media.Id, media.Category, media.Title, media.PageUrl, media.ImageUrl, media.Year, media.Rating }.Concat(media.Genres ?? []))
                if (value?.Length > 2048 || value?.Any(char.IsControl) == true) throw new LanException("Недопустимое описание фильма.");
        }
        if (request.Release is { } release)
            foreach (var value in new[] { release.Title, release.Source, release.PageUrl, release.Size })
                if (value?.Length > 2048 || value?.Any(char.IsControl) == true) throw new LanException("Недопустимое описание раздачи.");
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await StopAsync().ConfigureAwait(false);
        state.Dispose();
    }

    sealed class Connection(TcpClient client, SslStream stream, string fingerprint, SemaphoreSlim slots) : IDisposable
    {
        int disposed;
        public SslStream Stream { get; } = stream;
        public string Fingerprint { get; } = fingerprint;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            try { Stream.Dispose(); client.Dispose(); }
            finally { slots.Release(); }
        }
    }
}
