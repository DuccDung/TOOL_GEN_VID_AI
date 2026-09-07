using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.Vietsub.Domain;

namespace TOOL_LOCAL.Vietsub.Translation;

internal sealed record VietsubTranslationConfigurationSnapshot(
    string SourceLanguageCode,
    string TargetLanguageCode,
    string EngineId,
    string EngineVersion,
    double MaximumCharactersPerSecond,
    string ProjectSummary,
    string CharacterInstructions,
    string StyleInstructions,
    IReadOnlyList<VietsubTranslationGlossaryEntry> Glossary,
    string TranslationMemoryVersion,
    int PlannerVersion = 1,
    string? RuntimeProfileId = null);

internal static class VietsubTranslationFingerprintBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string BuildConfigurationFingerprint(VietsubTranslationConfigurationSnapshot configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var glossary = configuration.Glossary.Select(entry => new
        {
            entry.EntryId,
            sourceText = Normalize(entry.SourceText),
            targetText = Normalize(entry.TargetText),
            note = Normalize(entry.Note)
        }).OrderBy(entry => entry.EntryId).ToArray();
        var runtimeProfileId = Normalize(configuration.RuntimeProfileId);
        var payload = runtimeProfileId.Length == 0
            ? JsonSerializer.Serialize(new
            {
                version = 1,
                sourceLanguageCode = VietsubTranslationLimits.NormalizeSourceLanguage(configuration.SourceLanguageCode),
                targetLanguageCode = VietsubTranslationLimits.NormalizeTargetLanguage(configuration.TargetLanguageCode),
                engineId = Normalize(configuration.EngineId),
                engineVersion = Normalize(configuration.EngineVersion),
                maximumCharactersPerSecond = NormalizeMaximumCharactersPerSecond(configuration.MaximumCharactersPerSecond),
                projectSummary = Normalize(configuration.ProjectSummary),
                characterInstructions = Normalize(configuration.CharacterInstructions),
                styleInstructions = Normalize(configuration.StyleInstructions),
                glossary,
                translationMemoryVersion = Normalize(configuration.TranslationMemoryVersion),
                configuration.PlannerVersion
            }, JsonOptions)
            : JsonSerializer.Serialize(new
            {
                version = 2,
                sourceLanguageCode = VietsubTranslationLimits.NormalizeSourceLanguage(configuration.SourceLanguageCode),
                targetLanguageCode = VietsubTranslationLimits.NormalizeTargetLanguage(configuration.TargetLanguageCode),
                engineId = Normalize(configuration.EngineId),
                engineVersion = Normalize(configuration.EngineVersion),
                runtimeProfileId,
                maximumCharactersPerSecond = NormalizeMaximumCharactersPerSecond(configuration.MaximumCharactersPerSecond),
                projectSummary = Normalize(configuration.ProjectSummary),
                characterInstructions = Normalize(configuration.CharacterInstructions),
                styleInstructions = Normalize(configuration.StyleInstructions),
                glossary,
                translationMemoryVersion = Normalize(configuration.TranslationMemoryVersion),
                configuration.PlannerVersion
            }, JsonOptions);
        return Hash(payload);
    }

    public static string BuildCueFingerprint(
        VietsubSubtitleCue cue,
        int cueIndex,
        IReadOnlyList<VietsubSubtitleCue> orderedCues,
        int contextCueCount,
        string configurationFingerprint)
    {
        ArgumentNullException.ThrowIfNull(cue);
        ArgumentNullException.ThrowIfNull(orderedCues);
        if (cueIndex < 0
            || cueIndex >= orderedCues.Count
            || orderedCues[cueIndex].CueId != cue.CueId
            || string.IsNullOrWhiteSpace(configurationFingerprint))
        {
            throw new ArgumentException("Không thể tạo fingerprint vì vị trí cue hoặc configuration không hợp lệ.");
        }

        contextCueCount = Math.Clamp(contextCueCount, 0, 10);
        var first = Math.Max(0, cueIndex - contextCueCount);
        var last = Math.Min(orderedCues.Count - 1, cueIndex + contextCueCount);
        var payload = JsonSerializer.Serialize(new
        {
            version = 1,
            configurationFingerprint,
            targetCueId = cue.CueId,
            cues = orderedCues.Skip(first).Take(last - first + 1).Select(item => new
            {
                item.CueId,
                item.StartMilliseconds,
                item.EndMilliseconds,
                speaker = Normalize(item.Speaker),
                originalText = Normalize(item.OriginalText),
                approvedTranslation = item.CueId != cue.CueId
                    && VietsubTranslationResultValidator.IsApprovedContext(item)
                    ? Normalize(item.TranslatedText)
                    : null,
                translationLocked = item.CueId == cue.CueId ? false : item.TranslationLocked
            }).ToArray()
        }, JsonOptions);
        return Hash(payload);
    }

    private static double NormalizeMaximumCharactersPerSecond(double value) =>
        double.IsFinite(value)
            ? Math.Round(Math.Clamp(value, 8, 30), 4, MidpointRounding.AwayFromZero)
            : VietsubTranslationLimits.DefaultMaximumCharactersPerSecond;

    private static string Normalize(string? value) =>
        (value ?? string.Empty)
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n')
        .Trim();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
