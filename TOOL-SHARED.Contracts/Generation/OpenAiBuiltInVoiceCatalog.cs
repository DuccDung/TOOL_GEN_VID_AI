namespace TOOL_SHARED.Contracts.Generation;

public sealed record OpenAiVoiceOption(
    string VoiceCode,
    string DisplayName);

public static class OpenAiBuiltInVoiceCatalog
{
    private static readonly IReadOnlyList<OpenAiVoiceOption> BuiltInVoiceOptions =
    [
        new("alloy", "Alloy"),
        new("ash", "Ash"),
        new("ballad", "Ballad"),
        new("coral", "Coral"),
        new("echo", "Echo"),
        new("fable", "Fable"),
        new("onyx", "Onyx"),
        new("nova", "Nova"),
        new("sage", "Sage"),
        new("shimmer", "Shimmer"),
        new("verse", "Verse"),
        new("marin", "Marin"),
        new("cedar", "Cedar")
    ];

    private static readonly IReadOnlyDictionary<string, string> LegacyAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["female-sweet"] = "shimmer",
            ["male-warm"] = "onyx"
        };

    public static IReadOnlyList<OpenAiVoiceOption> Voices => BuiltInVoiceOptions;

    public static bool IsSupported(string? voiceCode) =>
        TryResolveProviderVoice(voiceCode, out _);

    public static bool TryResolveProviderVoice(string? voiceCode, out string providerVoiceCode)
    {
        providerVoiceCode = string.Empty;
        if (string.IsNullOrWhiteSpace(voiceCode))
        {
            return false;
        }

        var normalized = voiceCode.Trim();
        var builtIn = BuiltInVoiceOptions.FirstOrDefault(
            voice => string.Equals(voice.VoiceCode, normalized, StringComparison.OrdinalIgnoreCase));
        if (builtIn is not null)
        {
            providerVoiceCode = builtIn.VoiceCode;
            return true;
        }

        if (LegacyAliases.TryGetValue(normalized, out var legacyProviderVoice))
        {
            providerVoiceCode = legacyProviderVoice;
            return true;
        }

        return false;
    }

    public static string NormalizeSelection(string? voiceCode, string fallback = "shimmer") =>
        TryResolveProviderVoice(voiceCode, out var providerVoiceCode)
            ? providerVoiceCode
            : fallback;
}
