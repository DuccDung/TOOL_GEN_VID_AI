namespace TOOL_SHARED.Contracts.Generation;

public static class LocalVoicePolicies
{
    public const string None = "None";
    public const string VeoLocalVoiceConsistency = "veo-local-voice-v1";
    public const string AssetType = "SceneVideoVoiceConverted";
}

public static class LocalVoiceStatuses
{
    public const string Preparing = "Preparing";
    public const string DetectingSpeech = "DetectingSpeech";
    public const string SeparatingAudio = "SeparatingAudio";
    public const string ConvertingVoice = "ConvertingVoice";
    public const string Mixing = "Mixing";
    public const string Validating = "Validating";
    public const string ReviewRequired = "ReviewRequired";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Stale = "Stale";
    public static bool IsRunning(string value) => value is Preparing or DetectingSpeech or SeparatingAudio or ConvertingVoice or Mixing or Validating;
}

public sealed record LocalVoiceAccessRequest(Guid OrganizationId, Guid ProjectId);
public sealed record LocalVoiceAccessResponse(Guid OrganizationId, Guid ProjectId);
public sealed record LocalVoiceRuntimeSummary(string Status, string Message, string? Fingerprint = null);
public sealed record LocalVoiceAnchorSummary(Guid Id, Guid CharacterId, Guid SceneId, string Status, string? PreviewUrl, string? Message = null);
public sealed record LocalVoiceJobSummary(Guid Id, Guid SceneId, Guid CharacterId, string Status, string? ErrorCode,
    string? Message, string? PreviewUrl, Guid? AnchorId, string SourceFingerprint, string? NativePreviewUrl = null, bool NativeException = false);
public sealed record LocalVoiceProjectSummary(Guid ProjectId, bool Enabled, LocalVoiceRuntimeSummary Runtime,
    IReadOnlyList<LocalVoiceAnchorSummary> Anchors, IReadOnlyList<LocalVoiceJobSummary> Jobs, bool Running = false);
public sealed record LocalVoiceActionRequest(Guid ProjectId, Guid? SceneId = null, Guid? JobId = null,
    Guid? AnchorId = null, IReadOnlyList<Guid>? SceneIds = null, bool Confirmed = false, string? Reason = null);
