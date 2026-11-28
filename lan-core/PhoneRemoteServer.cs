using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Reflection;

namespace Kachalka.Lan;

/// <summary>A device the user chose to remember: only a hash of its token is kept, on disk, until it expires or is forgotten.</summary>
public sealed record RememberedDevice(string Id, string Name, string Hash, DateTimeOffset Created, DateTimeOffset Expires, DateTimeOffset LastSeen);

/// <summary>A file the server may stream to a paired device; the resolver decides which files exist, never the request.</summary>
public sealed record MediaFile(string Path, string ContentType);

/// <summary>Opt-in, local-subnet-only browser access. Credentials exist only in memory.</summary>
public sealed class PhoneRemoteServer : IAsyncDisposable
{
    readonly TcpListener listener;
    readonly bool testLoopback;
    readonly CancellationTokenSource lifetime = new();
    readonly SemaphoreSlim clients = new(8, 8), operations = new(1, 1);
    readonly object sync = new();
    readonly Dictionary<string, DateTimeOffset> sessions = [];
    readonly Dictionary<string, int> attempts = [];
    readonly HashSet<Task> active = [];
    readonly Func<string, JsonElement, CancellationToken, Task<object>> handle;
    readonly TimeProvider clock;
    readonly string? devicesFile;
    readonly Func<string, int, MediaFile?>? media;
    readonly List<RememberedDevice> devices = [];
    public static readonly TimeSpan RememberFor = TimeSpan.FromDays(90);
    public const int MaxRememberedDevices = 16;
    Task? accept;
    string? code;
    DateTimeOffset expires;
    int totalAttempts, disposed;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public string[] Urls => LanNetwork.Interfaces().Select(x => $"http://{x.Address}:{Port}/").Distinct().ToArray();
    public int Connected { get { lock (sync) { Prune(); return sessions.Count; } } }

    public PhoneRemoteServer(Func<string, JsonElement, CancellationToken, Task<object>> handler, int port = 45834,
        bool allowLoopbackForTests = false, TimeProvider? timeProvider = null, string? devicesFile = null, Func<string, int, MediaFile?>? mediaResolver = null)
    {
        handle = handler; testLoopback = allowLoopbackForTests; clock = timeProvider ?? TimeProvider.System;
        this.devicesFile = devicesFile; media = mediaResolver;
        devices.AddRange(LoadDevices(devicesFile));
        listener = new(allowLoopbackForTests ? IPAddress.Loopback : IPAddress.Any, port);
    }
    public void Start()
    {
        if (accept != null) throw new InvalidOperationException("Already started.");
        if(OperatingSystem.IsWindows())listener.ExclusiveAddressUse=true;
        else listener.Server.SetSocketOption(SocketOptionLevel.Socket,SocketOptionName.ReuseAddress,true);
        listener.Start(8); accept = Accept();
    }
    public object OpenPairing()
    {
        lock (sync)
        {
            code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            expires = clock.GetUtcNow().AddMinutes(2); attempts.Clear(); totalAttempts = 0;
            return new { urls = Urls, code, expires, connected = Connected };
        }
    }
    public object State() { lock (sync) { PruneDevices(); return new { urls = Urls, code = expires > clock.GetUtcNow() ? code : null, expires, connected = Connected, devices = devices.Select(x => new { x.Id, x.Name, x.Created, x.LastSeen, x.Expires }).ToArray() }; } }
    public void Revoke() { lock (sync) { sessions.Clear(); code = null; expires = default; } }
    // ---- remembered devices

    static readonly JsonSerializerOptions FileJson = new(JsonSerializerDefaults.Web);
    sealed record DevicesFile(List<RememberedDevice>? Devices);

    static List<RememberedDevice> LoadDevices(string? file)
    {
        try
        {
            if (file == null || !File.Exists(file) || new FileInfo(file).Length > 256 * 1024) return [];
            var rows = JsonSerializer.Deserialize<DevicesFile>(File.ReadAllText(file), FileJson)?.Devices ?? [];
            return rows.Where(x => x.Id?.Length is > 0 and <= 64 && x.Hash?.Length == 64).Take(MaxRememberedDevices).ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return []; }
    }

    /// <summary>True when the file holds at least one device that has not expired: the host then starts the server without a pairing code.</summary>
    public static bool HasRememberedDevices(string? file, TimeProvider? timeProvider = null) =>
        LoadDevices(file).Any(x => x.Expires > (timeProvider ?? TimeProvider.System).GetUtcNow());

    void SaveDevices()
    {
        if (devicesFile == null) return;
        var directory = Path.GetDirectoryName(Path.GetFullPath(devicesFile));
        if (directory != null) Directory.CreateDirectory(directory);
        var temporary = devicesFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new DevicesFile(devices), FileJson));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, devicesFile, true);
    }

    static string HashOf(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public IReadOnlyList<RememberedDevice> Devices() { lock (sync) { PruneDevices(); return devices.ToArray(); } }

    /// <summary>Makes the device forget this computer's trust: its token stops working at once.</summary>
    public bool Forget(string id)
    {
        lock (sync)
        {
            var removed = devices.RemoveAll(x => x.Id == id) > 0;
            if (removed) SaveDevices();
            return removed;
        }
    }

    public void ForgetAllDevices() { lock (sync) { devices.Clear(); SaveDevices(); } }

    void PruneDevices()
    {
        if (devices.RemoveAll(x => x.Expires <= clock.GetUtcNow()) > 0) SaveDevices();
    }

    bool IsAuthorized(string token)
    {
        lock (sync)
        {
            Prune();
            if (sessions.ContainsKey(token)) return true;
            PruneDevices();
            var hash = HashOf(token);
            var index = devices.FindIndex(x => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(x.Hash), Encoding.ASCII.GetBytes(hash)));
            if (index < 0) return false;
            // The last-seen time is kept in memory and written with the next change, so reads never touch the disk.
            devices[index] = devices[index] with { LastSeen = clock.GetUtcNow() };
            return true;
        }
    }

    string RememberDevice(string name)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var clean = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length == 0) clean = "Устройство"; else if (clean.Length > 40) clean = clean[..40];
        lock (sync)
        {
            PruneDevices();
            while (devices.Count >= MaxRememberedDevices) devices.RemoveAt(0); // the oldest makes room
            var now = clock.GetUtcNow();
            devices.Add(new RememberedDevice(Guid.NewGuid().ToString("N"), clean, HashOf(token), now, now + RememberFor, now));
            SaveDevices();
        }
        return token;
    }

    void Prune() { foreach (var key in sessions.Where(x => x.Value <= clock.GetUtcNow()).Select(x => x.Key).ToArray()) sessions.Remove(key); }
    async Task Accept()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(lifetime.Token);
                if (!LanNetwork.IsLocal(((IPEndPoint)client.Client.RemoteEndPoint!).Address, testLoopback) || !clients.Wait(0)) { client.Dispose(); continue; }
                var task = Serve(client);
                lock (sync) active.Add(task);
                _ = task.ContinueWith(done => { lock (sync) active.Remove(done); clients.Release(); }, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (SocketException) when (lifetime.IsCancellationRequested) { }
    }
    async Task Serve(TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var stream = client.GetStream();
            try
            {
                var header = new List<byte>(); var single = new byte[1];
                while (header.Count < 8192)
                {
                    if (await stream.ReadAsync(single, timeout.Token) == 0) return;
                    header.Add(single[0]);
                    if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10) break;
                }
                if (header.Count >= 8192) { await Respond(stream, 431, "text/plain", "Headers too large", timeout.Token); return; }
                var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
                var request = lines[0].Split(' ');
                if (request.Length != 3 || request[2] != "HTTP/1.1" || request[0] is not ("GET" or "POST")) { await Respond(stream, 400, "text/plain", "Invalid request", timeout.Token); return; }
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines.Skip(1).Where(x => x.Length > 0))
                {
                    var colon = line.IndexOf(':');
                    if (colon < 1 || char.IsWhiteSpace(line[0]) || !headers.TryAdd(line[..colon], line[(colon + 1)..].Trim())) throw new InvalidDataException();
                }
                var host = headers.GetValueOrDefault("Host") ?? "";
                var endpoint = (IPEndPoint)client.Client.LocalEndPoint!;
                // Only literal interface addresses: reject DNS rebinding, proxy absolute URLs and foreign Origins.
                if (host != $"{endpoint.Address}:{Port}" || headers.ContainsKey("Transfer-Encoding") ||
                    headers.TryGetValue("Origin", out var origin) && origin != "http://" + host)
                { await Respond(stream, 403, "text/plain", "Local origin required", timeout.Token); return; }
                if (request[0] == "GET" && request[1].StartsWith("/media/", StringComparison.Ordinal))
                {
                    await ServeMedia(stream, request[1], headers.GetValueOrDefault("Authorization", ""), headers.GetValueOrDefault("Range"), timeout);
                    return;
                }
                if (request[0] == "GET")
                {
                    var resource = request[1] switch { "/" => ("text/html; charset=utf-8", "phone.html"), "/app.js" => ("text/javascript; charset=utf-8", "phone.js"), "/style.css" => ("text/css; charset=utf-8", "phone.css"), _ => default };
                    if (resource.Item2 == null) { await Respond(stream, 404, "text/plain", "Not found", timeout.Token); return; }
                    using var asset = Assembly.GetExecutingAssembly().GetManifestResourceStream("Kachalka.Phone." + resource.Item2)!;
                    using var reader = new StreamReader(asset);
                    await Respond(stream, 200, resource.Item1, await reader.ReadToEndAsync(timeout.Token), timeout.Token); return;
                }
                if (!headers.TryGetValue("Content-Length", out var length) || !int.TryParse(length, out var count) || count is < 2 or > 65536 ||
                    !headers.GetValueOrDefault("Content-Type", "").Equals("application/json", StringComparison.OrdinalIgnoreCase))
                { await Respond(stream, 400, "text/plain", "JSON body required", timeout.Token); return; }
                var body = new byte[count]; await stream.ReadExactlyAsync(body, timeout.Token);
                using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 12 });
                if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
                if (request[1] == "/pair")
                {
                    string? token = null; var remote = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
                    // "remember" is only honoured when the host gave the server a place to keep remembered devices.
                    var remember = devicesFile != null && document.RootElement.TryGetProperty("remember", out var asked) && asked.ValueKind == JsonValueKind.True;
                    var deviceName = document.RootElement.TryGetProperty("name", out var named) && named.ValueKind == JsonValueKind.String ? named.GetString() ?? "" : "";
                    lock (sync)
                    {
                        Prune(); var tries = attempts.GetValueOrDefault(remote);
                        if (expires > clock.GetUtcNow() && totalAttempts++ < 20 && tries < 5 && sessions.Count < 8 && attempts.Count < 128)
                        {
                            attempts[remote] = tries + 1;
                            if (document.RootElement.TryGetProperty("code", out var supplied) && supplied.ValueKind == JsonValueKind.String && code != null &&
                                CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(supplied.GetString()!)))
                            {
                                if (remember) token = RememberDevice(deviceName);
                                else { token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); sessions[token] = clock.GetUtcNow().AddHours(8); }
                                code = null;
                            }
                        }
                    }
                    await Respond(stream, token == null ? 401 : 200, "application/json", JsonSerializer.Serialize(token == null ? (object)new { error = "Код неверен или истёк. Получи новый код на компьютере. / Get a new code on your computer." } : new { token, remembered = remember, expires = remember ? clock.GetUtcNow() + RememberFor : (DateTimeOffset?)null }), timeout.Token); return;
                }
                var bearer = headers.GetValueOrDefault("Authorization", "");
                if (!bearer.StartsWith("Bearer ", StringComparison.Ordinal) || !IsAuthorized(bearer[7..])) bearer = "";
                if (bearer.Length == 0) { await Respond(stream, 401, "application/json", "{\"error\":\"Подключись заново / Pair again\"}", timeout.Token); return; }
                if(request[1]=="/api/logout")
                {
                    lock(sync)
                    {
                        sessions.Remove(bearer[7..]);
                        var hash = HashOf(bearer[7..]);
                        if (devices.RemoveAll(x => x.Hash == hash) > 0) SaveDevices();
                    }
                    await Respond(stream,200,"application/json","{}",timeout.Token);return;
                }
                var method = request[1] switch { "/api/catalog" => "catalog", "/api/releases" => "releases", "/api/add" => "add", "/api/downloads" => "downloads", "/api/control" => "control", "/api/saved" => "saved", "/api/files" => "files", _ => "" };
                if (method.Length == 0) { await Respond(stream, 404, "text/plain", "Not found", timeout.Token); return; }
                timeout.CancelAfter(TimeSpan.FromSeconds(90));
                // One browser operation at a time. Never accumulate expensive searches on the desktop UI.
                if (!await operations.WaitAsync(0, timeout.Token)) { await Respond(stream, 429, "application/json", "{\"error\":\"Дождись завершения запроса / Request in progress\"}", timeout.Token); return; }
                try
                {
                    // Recheck after the body has arrived so revocation takes effect before dispatch.
                    if (!IsAuthorized(bearer[7..])) throw new UnauthorizedAccessException();
                    var result = await handle(method, document.RootElement.Clone(), timeout.Token);
                    await Respond(stream, 200, "application/json", JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)), timeout.Token);
                }
                finally { operations.Release(); }
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // Never return local paths, exception text, credentials or raw tracker responses.
                try { await Respond(stream, error is UnauthorizedAccessException ? 401 : 400, "application/json", "{\"error\":\"Запрос не выполнен. Проверь папку загрузок и повтори. / Check the download folder and try again.\"}", timeout.Token); } catch (Exception) { }
            }
            finally
            {
                // Windows resets a TCP connection closed with unread request bytes. Send a FIN first
                // and drain a bounded amount so rejected requests still receive their complete response.
                try
                {
                    client.Client.Shutdown(SocketShutdown.Send);
                    using var drain = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                    var buffer = new byte[4096]; var remaining = 128 * 1024;
                    while (remaining > 0)
                    {
                        var count = await stream.ReadAsync(buffer.AsMemory(0,Math.Min(buffer.Length,remaining)),drain.Token);
                        if(count==0)break;remaining-=count;
                    }
                }
                catch(Exception error) when(error is SocketException or IOException or OperationCanceledException or ObjectDisposedException) { }
            }
        }
    }
    /// <summary>
    /// Streams one file to a paired device with HTTP Range support (so the player can seek). Needs the same Bearer token as the API;
    /// which file it is comes only from the resolver the host supplied, never from a path in the request.
    /// </summary>
    async Task ServeMedia(NetworkStream stream, string path, string authorization, string? range, CancellationTokenSource timeout)
    {
        if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal) || !IsAuthorized(authorization[7..]))
        { await Respond(stream, 401, "application/json", "{\"error\":\"Pair again\"}", timeout.Token); return; }
        var parts = path.Split('/');
        if (media == null || parts.Length != 4 || parts[2].Length is 0 or > 64 || !parts[2].All(char.IsAsciiLetterOrDigit) || !int.TryParse(parts[3], out var index) || index is < 0 or > 9999)
        { await Respond(stream, 404, "text/plain", "Not found", timeout.Token); return; }
        MediaFile? file;
        try { file = media(parts[2], index); } catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException) { file = null; }
        if (file == null || !File.Exists(file.Path)) { await Respond(stream, 404, "text/plain", "Not found", timeout.Token); return; }
        FileStream input;
        try { input = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.Asynchronous | FileOptions.SequentialScan); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { await Respond(stream, 404, "text/plain", "Not found", timeout.Token); return; }
        await using (input)
        {
            var size = input.Length;
            long start = 0, end = size - 1; var partial = false;
            if (!string.IsNullOrEmpty(range))
            {
                var match = System.Text.RegularExpressions.Regex.Match(range, @"^\s*bytes\s*=\s*(\d*)\s*-\s*(\d*)\s*$");
                if (!match.Success || match.Groups[1].Value.Length == 0 && match.Groups[2].Value.Length == 0)
                { await Respond(stream, 416, "text/plain", "Range not satisfiable", timeout.Token); return; }
                if (match.Groups[1].Value.Length == 0)
                {
                    if (!long.TryParse(match.Groups[2].Value, out var tail) || tail == 0) { await Respond(stream, 416, "text/plain", "Range not satisfiable", timeout.Token); return; }
                    start = Math.Max(0, size - tail);
                }
                else
                {
                    if (!long.TryParse(match.Groups[1].Value, out start)) { await Respond(stream, 416, "text/plain", "Range not satisfiable", timeout.Token); return; }
                    if (match.Groups[2].Value.Length > 0) { if (!long.TryParse(match.Groups[2].Value, out end)) { await Respond(stream, 416, "text/plain", "Range not satisfiable", timeout.Token); return; } end = Math.Min(end, size - 1); }
                }
                if (size == 0 || start >= size || end < start) { await Respond(stream, 416, "text/plain", "Range not satisfiable", timeout.Token); return; }
                partial = true;
            }
            var length = size == 0 ? 0 : end - start + 1;
            var head = $"HTTP/1.1 {(partial ? "206 Partial Content" : "200 OK")}\r\nContent-Type: {file.ContentType}\r\nContent-Length: {length}\r\nAccept-Ranges: bytes\r\n" +
                       (partial ? $"Content-Range: bytes {start}-{end}/{size}\r\n" : "") +
                       "Connection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n\r\n";
            // Playback can last hours: only the writes are time-limited, so a player that stops reading cannot hold the connection forever.
            timeout.CancelAfter(Timeout.InfiniteTimeSpan);
            using (var first = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token)) { first.CancelAfter(TimeSpan.FromSeconds(30)); await stream.WriteAsync(Encoding.ASCII.GetBytes(head), first.Token); }
            if (length == 0) return;
            input.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[256 * 1024]; var remaining = length;
            while (remaining > 0)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), lifetime.Token);
                if (read == 0) break;
                using var write = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); write.CancelAfter(TimeSpan.FromSeconds(30));
                await stream.WriteAsync(buffer.AsMemory(0, read), write.Token);
                remaining -= read;
            }
        }
    }
    static async Task Respond(NetworkStream stream, int status, string type, string body, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Response\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self'; img-src https:; connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'\r\n\r\n");
        await stream.WriteAsync(head, ct); await stream.WriteAsync(bytes, ct);
    }
    public async ValueTask DisposeAsync()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        Revoke(); lifetime.Cancel(); listener.Stop();
        if (accept != null) await accept;
        Task[] remaining; lock (sync) remaining = active.ToArray();
        await Task.WhenAll(remaining); lifetime.Dispose();
    }
}
