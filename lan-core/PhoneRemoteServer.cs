using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Reflection;

namespace Kachalka.Lan;

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
    Task? accept;
    string? code;
    DateTimeOffset expires;
    int totalAttempts, disposed;
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public string[] Urls => LanNetwork.Interfaces().Select(x => $"http://{x.Address}:{Port}/").Distinct().ToArray();
    public int Connected { get { lock (sync) { Prune(); return sessions.Count; } } }

    public PhoneRemoteServer(Func<string, JsonElement, CancellationToken, Task<object>> handler, int port = 45834,
        bool allowLoopbackForTests = false, TimeProvider? timeProvider = null)
    {
        handle = handler; testLoopback = allowLoopbackForTests; clock = timeProvider ?? TimeProvider.System;
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
    public object State() { lock (sync) return new { urls = Urls, code = expires > clock.GetUtcNow() ? code : null, expires, connected = Connected }; }
    public void Revoke() { lock (sync) { sessions.Clear(); code = null; expires = default; } }
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
                    lock (sync)
                    {
                        Prune(); var tries = attempts.GetValueOrDefault(remote);
                        if (expires > clock.GetUtcNow() && totalAttempts++ < 20 && tries < 5 && sessions.Count < 8 && attempts.Count < 128)
                        {
                            attempts[remote] = tries + 1;
                            if (document.RootElement.TryGetProperty("code", out var supplied) && supplied.ValueKind == JsonValueKind.String && code != null &&
                                CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(supplied.GetString()!)))
                            { token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); sessions[token] = clock.GetUtcNow().AddHours(8); code = null; }
                        }
                    }
                    await Respond(stream, token == null ? 401 : 200, "application/json", JsonSerializer.Serialize(token == null ? (object)new { error = "Код неверен или истёк. Получи новый код на компьютере. / Get a new code on your computer." } : new { token }), timeout.Token); return;
                }
                var bearer = headers.GetValueOrDefault("Authorization", "");
                lock (sync) { Prune(); if (!bearer.StartsWith("Bearer ", StringComparison.Ordinal) || !sessions.ContainsKey(bearer[7..])) bearer = ""; }
                if (bearer.Length == 0) { await Respond(stream, 401, "application/json", "{\"error\":\"Подключись заново / Pair again\"}", timeout.Token); return; }
                if(request[1]=="/api/logout")
                {
                    lock(sync) sessions.Remove(bearer[7..]);
                    await Respond(stream,200,"application/json","{}",timeout.Token);return;
                }
                var method = request[1] switch { "/api/catalog" => "catalog", "/api/releases" => "releases", "/api/add" => "add", "/api/downloads" => "downloads", "/api/control" => "control", _ => "" };
                if (method.Length == 0) { await Respond(stream, 404, "text/plain", "Not found", timeout.Token); return; }
                timeout.CancelAfter(TimeSpan.FromSeconds(90));
                // One browser operation at a time. Never accumulate expensive searches on the desktop UI.
                if (!await operations.WaitAsync(0, timeout.Token)) { await Respond(stream, 429, "application/json", "{\"error\":\"Дождись завершения запроса / Request in progress\"}", timeout.Token); return; }
                try
                {
                    // Recheck after the body has arrived so revocation takes effect before dispatch.
                    lock (sync) if (!sessions.ContainsKey(bearer[7..])) throw new UnauthorizedAccessException();
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
