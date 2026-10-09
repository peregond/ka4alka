namespace Kachalka.Lan;

public sealed record LanServiceOptions
{
    public required string StateDirectory { get; init; }
    public string DeviceName { get; init; } = Environment.MachineName;
    public int TcpPort { get; init; } = 45832;
    public int DiscoveryPort { get; init; } = 45833;
    public bool AllowLoopbackForTests { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan PairingTimeout { get; init; } = TimeSpan.FromMinutes(2);
}

public sealed record LanDevice
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public string Address { get; init; } = "";
    public int Port { get; init; }
    public string Fingerprint { get; init; } = "";
    public DateTimeOffset LastSeenUtc { get; init; }
    public bool Paired { get; init; }
}

public sealed record LanPairingRequest
{
    public required LanDevice Device { get; init; }
    public required string VerificationCode { get; init; }
    public DateTimeOffset ExpiresUtc { get; init; }
    public bool Incoming { get; init; }
}

public sealed record LanDownloadRequest
{
    public Guid RequestId { get; init; } = Guid.NewGuid();
    public Guid SenderId { get; init; }
    public string? SenderFingerprint { get; init; }
    public string? Magnet { get; init; }
    public byte[]? TorrentBytes { get; init; }
    public string? Title { get; init; }
    public LanMedia? Media { get; init; }
    public LanRelease? Release { get; init; }
}

public sealed record LanDownloadReceipt
{
    public Guid RequestId { get; init; }
    public bool Accepted { get; init; }
    public string? Message { get; init; }
    public string? DownloadId { get; init; }
}

public sealed record LanMedia
{
    public string? Id { get; init; }
    public string? Category { get; init; }
    public string? Title { get; init; }
    public string? PageUrl { get; init; }
    public string? ImageUrl { get; init; }
    public string? Year { get; init; }
    public string? Rating { get; init; }
    public string[]? Genres { get; init; }
}

public sealed record LanRelease
{
    public string? Title { get; init; }
    public string? Source { get; init; }
    public string? PageUrl { get; init; }
    public string? Size { get; init; }
    public int? Seeds { get; init; }
}

public sealed class LanException(string message, Exception? inner = null) : Exception(message, inner);
