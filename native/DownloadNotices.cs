namespace Kachalka;

public enum DownloadNoticeKind { Completed, Error, LowSpace }

public sealed record DownloadNotice(DownloadNoticeKind Kind, string DownloadId, string Name, string Message);
