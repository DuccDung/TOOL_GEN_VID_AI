using TOOL_LOCAL.Vietsub.Domain;

namespace TOOL_LOCAL.Vietsub.Translation;

internal enum VietsubTranslationPass
{
    Translate,
    Review
}

internal static class VietsubTranslationRunModes
{
    public const string Continue = "CONTINUE";
    public const string RetryFailed = "RETRY_FAILED";
    public const string RestartUnlocked = "RESTART_UNLOCKED";

    public static string Normalize(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        RetryFailed => RetryFailed,
        RestartUnlocked => RestartUnlocked,
        _ => Continue
    };

    public static string NormalizeRequired(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        Continue => Continue,
        RetryFailed => RetryFailed,
        RestartUnlocked => RestartUnlocked,
        _ => throw new VietsubTranslationException(
            VietsubTranslationErrorCodes.ContextInvalid,
            "Chế độ chạy dịch local không hợp lệ.")
    };
}

internal static class VietsubTranslationQualityStatuses
{
    public const string Pending = "PENDING";
    public const string Valid = "VALID";
    public const string Review = "REVIEW";
    public const string Invalid = "INVALID";
    public const string ManualReviewed = "MANUAL_REVIEWED";

    public static bool IsApproved(string? value) => value?.Trim().ToUpperInvariant() is
        Valid or ManualReviewed;
}

internal static class VietsubTranslationSources
{
    public const string LocalAuto = "LOCAL_AUTO";
    public const string Manual = "MANUAL";
}

internal sealed record VietsubTranslationGlossaryEntry(
    Guid EntryId,
    string SourceText,
    string TargetText,
    string? Note = null);

internal sealed record VietsubTranslationMemoryEntry(
    Guid EntryId,
    string SourceLanguageCode,
    string TargetLanguageCode,
    string SourceText,
    string TranslatedText,
    string? ContextFingerprint,
    string SourceKind,
    int UseCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

internal sealed record VietsubTranslationCueInput(
    string CueAlias,
    Guid CueId,
    int CueIndex,
    long StartMilliseconds,
    long EndMilliseconds,
    string Speaker,
    string OriginalText,
    bool IsTarget,
    int SuggestedMaximumCharacters,
    string? ApprovedVietnameseContext = null);

internal sealed record VietsubTranslationSceneRequest(
    string ProjectName,
    string SourceLanguageCode,
    string TargetLanguageCode,
    string ProjectSummary,
    string CharacterInstructions,
    string StyleInstructions,
    IReadOnlyList<VietsubTranslationGlossaryEntry> Glossary,
    IReadOnlyList<VietsubTranslationMemoryEntry> TranslationMemory,
    IReadOnlyList<VietsubTranslationCueInput> Cues,
    VietsubTranslationPass Pass,
    string ChapterContext,
    string ConfigurationFingerprint,
    bool ResourceWarningAccepted = false)
{
    public IReadOnlyList<Guid> TargetCueIds => Cues
        .Where(cue => cue.IsTarget)
        .Select(cue => cue.CueId)
        .ToArray();
}

internal sealed record VietsubTranslationItemResult(
    string CueAlias,
    string TranslatedText,
    double? Confidence,
    IReadOnlyList<string> Warnings,
    bool WasReviewed = false);

internal sealed record VietsubTranslationSceneResult(
    string EngineId,
    string EngineVersion,
    IReadOnlyList<VietsubTranslationItemResult> Items);

internal sealed record VietsubLocalTranslationCapabilities(
    string EngineId,
    string EngineVersion,
    IReadOnlyList<string> SupportedSourceLanguages,
    string TargetLanguageCode,
    bool SupportsSceneContext,
    bool SupportsReviewPass,
    int MaximumTargetCues,
    int MaximumContextCues,
    int MaximumSourceCharacters,
    int MaximumOutputCharacters);

internal interface IVietsubLocalTranslationProvider
{
    VietsubLocalTranslationCapabilities Capabilities { get; }

    Task<VietsubTranslationSceneResult> TranslateAsync(
        VietsubTranslationSceneRequest request,
        CancellationToken cancellationToken);
}

internal sealed class VietsubTranslationException(
    string code,
    string message,
    bool retryable = false,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;

    public bool Retryable { get; } = retryable;
}

internal static class VietsubTranslationErrorCodes
{
    public const string FeatureDisabled = "TRANSLATION_FEATURE_DISABLED";
    public const string AccessDenied = "TRANSLATION_ACCESS_DENIED";
    public const string LicenseRequired = "TRANSLATION_LICENSE_REQUIRED";
    public const string SourceTrackRequired = "TRANSLATION_SOURCE_TRACK_REQUIRED";
    public const string SourceLanguageRequired = "TRANSLATION_SOURCE_LANGUAGE_REQUIRED";
    public const string LanguageUnsupported = "TRANSLATION_LANGUAGE_UNSUPPORTED";
    public const string RuntimeNotInstalled = "TRANSLATION_RUNTIME_NOT_INSTALLED";
    public const string RuntimeInvalid = "TRANSLATION_RUNTIME_INVALID";
    public const string RuntimeDownloadFailed = "TRANSLATION_RUNTIME_DOWNLOAD_FAILED";
    public const string ResourceConfirmationRequired = "TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED";
    public const string RuntimeUnsupportedPlatform = "TRANSLATION_RUNTIME_UNSUPPORTED_PLATFORM";
    public const string RuntimeInsufficientDisk = "TRANSLATION_RUNTIME_INSUFFICIENT_DISK";
    public const string RuntimeOutOfMemory = "TRANSLATION_RUNTIME_OUT_OF_MEMORY";
    public const string BackendLoadFailed = "TRANSLATION_BACKEND_LOAD_FAILED";
    public const string ProcessCrashed = "TRANSLATION_PROCESS_CRASHED";
    public const string WorkerProtocolInvalid = "TRANSLATION_WORKER_PROTOCOL_INVALID";
    public const string RuntimeProbeFailed = "TRANSLATION_RUNTIME_PROBE_FAILED";
    public const string EnglishProbeFailed = "TRANSLATION_EN_PROBE_FAILED";
    public const string ChineseProbeFailed = "TRANSLATION_ZH_PROBE_FAILED";
    public const string ModelNotReady = "TRANSLATION_MODEL_NOT_READY";
    public const string ContextInvalid = "TRANSLATION_CONTEXT_INVALID";
    public const string GlossaryInvalid = "TRANSLATION_GLOSSARY_INVALID";
    public const string ResultInvalid = "TRANSLATION_RESULT_INVALID";
    public const string TrackChanged = "TRANSLATION_TRACK_CHANGED";
    public const string JobConflict = "TRANSLATION_JOB_CONFLICT";
    public const string JobNotResumable = "TRANSLATION_JOB_NOT_RESUMABLE";
    public const string ProcessFailed = "TRANSLATION_PROCESS_FAILED";
    public const string ProcessTimeout = "TRANSLATION_PROCESS_TIMEOUT";
}

internal static class VietsubTranslationLimits
{
    public const int DefaultMaximumChapterDurationMilliseconds = 10 * 60 * 1000;
    public const int DefaultMaximumSceneSourceCharacters = 6_000;
    public const int DefaultMaximumSceneOutputCharacters = 8_000;
    public const int DefaultMaximumTargetCues = 12;
    public const int DefaultContextCueCount = 3;
    public const int DefaultSceneGapMilliseconds = 8_000;
    public const double DefaultMaximumCharactersPerSecond = 18;

    public static string NormalizeSourceLanguage(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "en" => "en",
        "zh" or "zh-cn" or "zh-hans" => "zh",
        _ => throw new VietsubTranslationException(
            VietsubTranslationErrorCodes.LanguageUnsupported,
            "Dịch local hiện chỉ hỗ trợ nguồn tiếng Anh hoặc tiếng Trung.")
    };

    public static string NormalizeTargetLanguage(string? value) =>
        string.Equals(value?.Trim(), "vi", StringComparison.OrdinalIgnoreCase)
            ? "vi"
            : throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.LanguageUnsupported,
                "Dịch local hiện chỉ hỗ trợ ngôn ngữ đích tiếng Việt.");

    public static int ResolveMaximumTargetCues(
        int configured,
        VietsubLocalTranslationCapabilities? capabilities = null) =>
        Math.Min(
            Math.Clamp(configured, 1, 30),
            Math.Clamp(capabilities?.MaximumTargetCues ?? 30, 1, 30));

    public static int ResolveContextCueCount(
        int configured,
        VietsubLocalTranslationCapabilities? capabilities = null) =>
        Math.Min(
            Math.Clamp(configured, 0, 10),
            Math.Clamp(capabilities?.MaximumContextCues ?? 10, 0, 10));

    public static int ResolveMaximumSourceCharacters(
        int configured,
        VietsubLocalTranslationCapabilities? capabilities = null) =>
        Math.Min(
            Math.Clamp(configured, 1_000, 20_000),
            Math.Clamp(capabilities?.MaximumSourceCharacters ?? 20_000, 1_000, 20_000));

    public static int ResolveMaximumOutputCharacters(
        int configured,
        VietsubLocalTranslationCapabilities? capabilities = null) =>
        Math.Min(
            Math.Clamp(configured, 1_000, 30_000),
            Math.Clamp(capabilities?.MaximumOutputCharacters ?? 30_000, 1_000, 30_000));
}

internal static class VietsubTranslationResultValidator
{
    public static void EnsureValid(
        VietsubTranslationSceneResult result,
        IReadOnlyList<string> expectedCueAliases)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(expectedCueAliases);
        if (result.Items.Count != expectedCueAliases.Count
            || !result.Items.Select(item => item.CueAlias).SequenceEqual(expectedCueAliases, StringComparer.Ordinal)
            || result.Items.Select(item => item.CueAlias).Distinct(StringComparer.Ordinal).Count() != result.Items.Count
            || result.Items.Any(item => string.IsNullOrWhiteSpace(item.CueAlias))
            || result.Items.Any(item => string.IsNullOrWhiteSpace(item.TranslatedText)))
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.ResultInvalid,
                "Engine dịch trả về thiếu, thừa, trùng, sai thứ tự cue hoặc bản dịch rỗng.");
        }
    }

    public static bool IsApprovedContext(VietsubSubtitleCue cue) =>
        !string.IsNullOrWhiteSpace(cue.TranslatedText)
        && (cue.TranslationLocked
            || cue.TranslationSource == VietsubTranslationSources.Manual
            || string.Equals(
                cue.QualityStatus,
                VietsubTranslationQualityStatuses.ManualReviewed,
                StringComparison.Ordinal));
}
