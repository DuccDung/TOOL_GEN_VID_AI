using System.Text.Json;
using System.Text.Json.Serialization;
using TOOL_LOCAL.Vietsub.Domain;

namespace TOOL_LOCAL.Vietsub.Voice;

internal static class VietsubVoiceEngines
{
    public const string Piper = "PIPER_LOCAL";
}

internal static class VietsubVoiceArtifactKinds
{
    public const string Phrase = "PHRASE";
    public const string Timeline = "TIMELINE";
}

internal static class VietsubVoiceArtifactStatuses
{
    public const string Ready = "READY";
    public const string Stale = "STALE";
    public const string ReviewRequired = "REVIEW_REQUIRED";
}

internal static class VietsubVoiceTimingStatuses
{
    public const string Natural = "NATURAL";
    public const string BorrowedGap = "BORROWED_GAP";
    public const string Compressed = "COMPRESSED";
    public const string ReviewRequired = "REVIEW_REQUIRED";
}

internal static class VietsubVoiceTranslationPolicy
{
    public static void EnsureComplete(VietsubSubtitleTrack track)
    {
        if (track.Cues.Count == 0 || track.Cues.Any(cue => string.IsNullOrWhiteSpace(cue.TranslatedText)))
        {
            throw new VietsubVoiceException(
                VietsubVoiceErrorCodes.TranslationRequired,
                "Hãy hoàn thành bản dịch tiếng Việt trước khi tạo giọng.");
        }
    }
}

internal sealed class VietsubVoiceSettings
{
    public string EngineId { get; set; } = VietsubVoiceEngines.Piper;

    public string ModelId { get; set; } = VietsubVoiceCatalog.PiperModelId;

    public string VoiceId { get; set; } = VietsubVoiceCatalog.PiperVoiceId;

    public int MaximumPhraseGapMilliseconds { get; set; } = 500;

    public int MaximumPhraseDurationMilliseconds { get; set; } = 8_000;

    public int MaximumPhraseCharacters { get; set; } = 4_500;

    public int MaximumBorrowedGapMilliseconds { get; set; } = 600;

    public double PreferredMaximumTempo { get; set; } = 1.12;

    public double MaximumTempo { get; set; } = 1.20;

    public bool TrimSilence { get; set; } = true;

    public void Normalize()
    {
        if (!string.Equals(EngineId?.Trim(), VietsubVoiceEngines.Piper, StringComparison.Ordinal)
            || !string.Equals(ModelId?.Trim(), VietsubVoiceCatalog.PiperModelId, StringComparison.Ordinal)
            || !string.Equals(VoiceId?.Trim(), VietsubVoiceCatalog.PiperVoiceId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Cấu hình giọng local chứa engine, model hoặc voice chưa được duyệt.");
        }

        EngineId = VietsubVoiceEngines.Piper;
        ModelId = VietsubVoiceCatalog.PiperModelId;
        VoiceId = VietsubVoiceCatalog.PiperVoiceId;
        MaximumPhraseGapMilliseconds = Math.Clamp(MaximumPhraseGapMilliseconds, 0, 2_000);
        MaximumPhraseDurationMilliseconds = Math.Clamp(MaximumPhraseDurationMilliseconds, 1_000, 20_000);
        MaximumPhraseCharacters = Math.Clamp(MaximumPhraseCharacters, 100, 4_500);
        MaximumBorrowedGapMilliseconds = Math.Clamp(MaximumBorrowedGapMilliseconds, 0, 2_000);
        PreferredMaximumTempo = double.IsFinite(PreferredMaximumTempo)
            ? Math.Clamp(PreferredMaximumTempo, 1, 1.20)
            : 1.12;
        MaximumTempo = double.IsFinite(MaximumTempo)
            ? Math.Clamp(MaximumTempo, PreferredMaximumTempo, 1.20)
            : 1.20;
    }
}

internal sealed record VietsubVoiceCatalogItem(
    string VoiceId,
    string EngineId,
    string ModelId,
    string DisplayName,
    string LanguageCode,
    string Gender,
    string License);

internal static class VietsubVoiceCatalog
{
    public const string PiperVoiceId = "piper:vi-vn-vais1000";
    public const string PiperModelId = "piper-vi-vais1000-medium";
    public const string PiperModelVersion = "vi_VN-vais1000-medium@ea046e8";
    public const string PiperEngineVersion = "piper-tts-1.6.0";

    public static readonly IReadOnlyList<VietsubVoiceCatalogItem> Items =
    [
        new(
            PiperVoiceId,
            VietsubVoiceEngines.Piper,
            PiperModelId,
            "Piper · Nữ tiếng Việt",
            "vi-VN",
            "FEMALE",
            "VAIS-1000 CC BY 4.0; Piper runtime GPL-3.0")
    ];
}

internal sealed record VietsubVoiceRuntimeStatus(
    string Status,
    bool Ready,
    string EngineId,
    string EngineVersion,
    string ModelId,
    string ModelVersion,
    string VoiceId,
    long InstalledBytes,
    long RequiredBytes,
    string Message,
    string? ErrorCode = null);

internal sealed record VietsubVoiceRuntimeInstallProgress(
    string Stage,
    double Percent,
    string Message,
    long BytesProcessed,
    long TotalBytes);

internal sealed record VietsubStartVoiceInput(Guid ExpectedTrackId, int ExpectedTrackRevision);

internal sealed record VietsubVoiceSettingsSnapshot(
    string EngineId,
    string EngineVersion,
    string ModelId,
    string ModelVersion,
    string VoiceId,
    int MaximumPhraseGapMilliseconds,
    int MaximumPhraseDurationMilliseconds,
    int MaximumPhraseCharacters,
    int MaximumBorrowedGapMilliseconds,
    double PreferredMaximumTempo,
    double MaximumTempo,
    bool TrimSilence);

internal sealed record VietsubVoiceJobParameters(
    int StrategyVersion,
    Guid InputTrackId,
    int InputRevision,
    string ConfigurationFingerprint,
    VietsubVoiceSettingsSnapshot Settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static VietsubVoiceJobParameters Parse(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<VietsubVoiceJobParameters>(json, JsonOptions)
                ?? throw new JsonException("Voice job parameters rỗng.");
            if (value.StrategyVersion != 1
                || value.InputTrackId == Guid.Empty
                || value.InputRevision < 1
                || value.Settings is null
                || value.ConfigurationFingerprint.Length != 64
                || !value.ConfigurationFingerprint.All(Uri.IsHexDigit)
                || value.Settings.EngineId != VietsubVoiceEngines.Piper
                || value.Settings.ModelId != VietsubVoiceCatalog.PiperModelId
                || value.Settings.VoiceId != VietsubVoiceCatalog.PiperVoiceId)
            {
                throw new JsonException("Voice job parameters không hợp lệ.");
            }
            return value;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new VietsubVoiceException(
                VietsubVoiceErrorCodes.JobNotResumable,
                "Metadata voice job không thể phục hồi.",
                innerException: exception);
        }
    }
}

internal sealed record VietsubVoicePhrase(
    string PhraseId,
    IReadOnlyList<Guid> CueIds,
    string Speaker,
    string Text,
    long StartMilliseconds,
    long EndMilliseconds);

internal sealed record VietsubVoiceSynthesisItem(
    int Index,
    string PhraseId,
    string Text,
    string OutputPath);

internal sealed record VietsubVoiceArtifact(
    Guid ArtifactId,
    Guid TrackId,
    int TrackRevision,
    string ArtifactKind,
    string? PhraseId,
    [property: JsonIgnore] string RelativePath,
    long SizeBytes,
    string Sha256,
    [property: JsonIgnore] string ContentFingerprint,
    string EngineId,
    string EngineVersion,
    string ModelId,
    string ModelVersion,
    string VoiceId,
    long DurationMilliseconds,
    int SampleRate,
    int Channels,
    string Status,
    string? TimingStatus,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    [property: JsonIgnore] IReadOnlyList<Guid> CueIds);

internal sealed record VietsubVoiceTimingDiagnostic(
    string PhraseId,
    long NaturalDurationMilliseconds,
    long TargetDurationMilliseconds,
    long BorrowedGapMilliseconds,
    double Tempo,
    string Status,
    int SuggestedMaximumCharacters);

internal sealed record VietsubVoiceWorkspaceSummary(
    VietsubVoiceSettings Settings,
    IReadOnlyList<VietsubVoiceCatalogItem> Voices,
    VietsubVoiceArtifact? Timeline,
    string? TimelinePlaybackUrl,
    IReadOnlyList<VietsubVoiceTimingDiagnostic> TimingDiagnostics);

internal interface IVietsubVoiceSynthesizer
{
    Task SynthesizeIncrementallyAsync(
        IReadOnlyList<VietsubVoiceSynthesisItem> items,
        Func<VietsubVoiceSynthesisItem, ValueTask> onCompleted,
        CancellationToken cancellationToken);
}

internal sealed class VietsubVoiceException(
    string code,
    string message,
    bool retryable = false,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;

    public bool Retryable { get; } = retryable;
}

internal static class VietsubVoiceErrorCodes
{
    public const string FeatureDisabled = "VOICE_FEATURE_DISABLED";
    public const string AccessDenied = "VOICE_ACCESS_DENIED";
    public const string LicenseRequired = "VOICE_LICENSE_REQUIRED";
    public const string TrackRequired = "VOICE_TRACK_REQUIRED";
    public const string TrackChanged = "VOICE_TRACK_CHANGED";
    public const string TranslationRequired = "VOICE_TRANSLATION_REQUIRED";
    public const string TranslationReviewRequired = "VOICE_TRANSLATION_REVIEW_REQUIRED";
    public const string RuntimeNotInstalled = "VOICE_RUNTIME_NOT_INSTALLED";
    public const string RuntimeInvalid = "VOICE_RUNTIME_INVALID";
    public const string RuntimeInstallFailed = "VOICE_RUNTIME_INSTALL_FAILED";
    public const string WorkerFailed = "VOICE_WORKER_FAILED";
    public const string WorkerTimeout = "VOICE_WORKER_TIMEOUT";
    public const string WorkerProtocolInvalid = "VOICE_WORKER_PROTOCOL_INVALID";
    public const string ResultInvalid = "VOICE_RESULT_INVALID";
    public const string TimelineFailed = "VOICE_TIMELINE_FAILED";
    public const string TimingReviewRequired = "VOICE_TIMING_REVIEW_REQUIRED";
    public const string JobConflict = "VOICE_JOB_CONFLICT";
    public const string JobNotResumable = "VOICE_JOB_NOT_RESUMABLE";
}
