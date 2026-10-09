using System.Buffers.Binary;
using System.Text.Json;

namespace Kachalka.Lan;

public static class LanProtocol
{
    public const string Name = "kachalka-lan/1";
    public const int MaxFrameBytes = 14 * 1024 * 1024;
    public const int MaxTorrentBytes = 10 * 1024 * 1024;
    public const int MaxMagnetLength = 32 * 1024;
    internal static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, MaxDepth = 12 };

    internal static async Task WriteAsync(Stream stream, WireMessage message, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (bytes.Length > MaxFrameBytes) throw new LanException("Слишком большой запрос локальной сети.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length);
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    internal static async Task<WireMessage> ReadAsync(Stream stream, CancellationToken token, int maxBytes = MaxFrameBytes)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 2 || length > maxBytes) throw new LanException("Недопустимый размер запроса локальной сети.");
        var bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        var message = JsonSerializer.Deserialize<WireMessage>(bytes, JsonOptions) ?? throw new LanException("Пустой запрос локальной сети.");
        if (message.Protocol != Name) throw new LanException("Версия протокола локальной сети не поддерживается.");
        return message;
    }
}

internal sealed record WireMessage
{
    public string Protocol { get; init; } = LanProtocol.Name;
    public string Kind { get; init; } = "";
    public LanDevice? Identity { get; init; }
    public LanDownloadRequest? Request { get; init; }
    public LanDownloadReceipt? Receipt { get; init; }
    public bool Approved { get; init; }
    public string? Message { get; init; }
}
