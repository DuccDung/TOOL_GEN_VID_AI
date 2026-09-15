namespace TOOL_LOCAL.Bilibili;

internal sealed record BilibiliEntry(
    string Id, string Url, string Title, string? ThumbnailUrl, double? DurationSeconds, string? Uploader);

internal sealed record BilibiliScan(string Id, string Status, int Count, bool Complete, string? Message);

internal sealed record BilibiliDownloadJob(
    string Id, BilibiliEntry Video, string Quality, string Status, double Percent,
    long DownloadedBytes = 0, double? BytesPerSecond = null, string? FileName = null, string? Message = null);

internal sealed record BilibiliState(
    long Revision, string Operation, bool RuntimeReady, string RuntimeVersion, string FolderLabel,
    BilibiliScan? Scan, IReadOnlyList<BilibiliEntry> Entries, IReadOnlyList<BilibiliDownloadJob> Jobs);

internal sealed record BilibiliScanUpdate(long Revision, BilibiliScan Scan, IReadOnlyList<BilibiliEntry> Entries);
internal sealed record BilibiliJobUpdate(long Revision, BilibiliDownloadJob Job);
internal sealed record BilibiliNotification(string Type, object Payload);

internal sealed class BilibiliException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
