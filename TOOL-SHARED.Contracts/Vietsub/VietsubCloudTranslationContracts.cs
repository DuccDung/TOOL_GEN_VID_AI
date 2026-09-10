using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TOOL_SHARED.Contracts.Vietsub;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VietsubCloudCue(Guid CueId, int CueIndex, long StartMilliseconds,
    long EndMilliseconds, string Speaker, string OriginalText, bool IsTarget,
    string InputFingerprint, string? ApprovedTranslation = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VietsubCloudStartRequest(Guid OrganizationId, Guid ClientOperationId,
    Guid TrackId, int TrackRevision, string SourceLanguageCode, string TargetLanguageCode,
    IReadOnlyList<VietsubCloudCue> Cues, string Context = "", int SnapshotVersion = 1);

public sealed record VietsubCloudAvailability(bool Available, string? ErrorCode, string? Message);
public sealed record VietsubCloudJobResponse(Guid JobId, Guid ClientOperationId, Guid TrackId,
    string SnapshotHash, string Status, int TotalCues, int CompletedCues, int FailedCues,
    string? ErrorCode, string? Message, DateTime? ResultExpiresAtUtc, bool CanRetry);
public sealed record VietsubCloudCueResult(Guid CueId, string InputFingerprint, string TranslatedText,
    IReadOnlyList<string> Warnings);
public sealed record VietsubCloudResultPage(Guid JobId, string SnapshotHash, int NextCursor,
    bool HasMore, IReadOnlyList<VietsubCloudCueResult> Items);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VietsubCloudReconcileRequest(Guid RequestId, string Decision, string EvidenceReference,
    long? InputTokens = null, long? OutputTokens = null, string? ResponseId = null);
public sealed record VietsubCloudReconcileResponse(Guid JobId, Guid RequestId, string Status, decimal ActualCost, bool Settled);

public static class VietsubCloudStates
{
    public const string Queued = "QUEUED", Running = "RUNNING", Paused = "PAUSED",
        Blocked = "BLOCKED", Unknown = "UNKNOWN", Completed = "COMPLETED",
        Failed = "FAILED", Cancelled = "CANCELLED";
    public static bool IsTerminal(string status) => status is Completed or Failed or Cancelled;
}

public static class VietsubCloudSnapshot
{
    public const int MaximumBytes = 5 * 1024 * 1024;
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static string Serialize(VietsubCloudStartRequest request) => JsonSerializer.Serialize(request, JsonOptions);
    public static string Hash(VietsubCloudStartRequest request) => HashText(Serialize(request));
    public static string HashText(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static string CueFingerprint(Guid trackId, Guid cueId, string text, string speaker, long start, long end) =>
        HashText(JsonSerializer.Serialize(new { trackId, cueId, text, speaker, start, end }, JsonOptions));

    public static void Validate(VietsubCloudStartRequest request)
    {
        if (request.OrganizationId == Guid.Empty || request.ClientOperationId == Guid.Empty
            || request.TrackId == Guid.Empty || request.TrackRevision < 1 || request.SnapshotVersion != 1
            || request.SourceLanguageCode is not ("en" or "zh") || request.TargetLanguageCode != "vi"
            || request.Cues is null || request.Cues.Count is < 1 or > 20_000
            || request.Context is null || request.Context.Length > 12_000
            || Encoding.UTF8.GetByteCount(Serialize(request)) > MaximumBytes)
            throw new ArgumentException("Snapshot dịch Cloud không hợp lệ hoặc vượt giới hạn.");
        var ids = new HashSet<Guid>();
        var indexes = new HashSet<int>();
        foreach (var cue in request.Cues)
        {
            if (cue is null || cue.CueId == Guid.Empty || !ids.Add(cue.CueId) || !indexes.Add(cue.CueIndex)
                || cue.CueIndex < 0 || cue.StartMilliseconds < 0 || cue.EndMilliseconds <= cue.StartMilliseconds
                || cue.Speaker is null || cue.Speaker.Length > 200 || cue.OriginalText is null
                || cue.OriginalText.Length > 10_000 || (cue.IsTarget && string.IsNullOrWhiteSpace(cue.OriginalText))
                || cue.ApprovedTranslation?.Length > 8_000
                || cue.InputFingerprint != CueFingerprint(request.TrackId, cue.CueId, cue.OriginalText,
                    cue.Speaker, cue.StartMilliseconds, cue.EndMilliseconds))
                throw new ArgumentException("Câu phụ đề hoặc fingerprint không hợp lệ.");
        }
        if (!request.Cues.Any(cue => cue.IsTarget)) throw new ArgumentException("Không có câu cần dịch.");
    }
}
