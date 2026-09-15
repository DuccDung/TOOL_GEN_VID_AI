using System.Security.Cryptography;
using System.Text;
using TOOL_LOCAL.Vietsub.Domain;

namespace TOOL_LOCAL.Vietsub.Voice;

internal static class VietsubVoicePhrasePlanner
{
    public static IReadOnlyList<VietsubVoicePhrase> Plan(
        IReadOnlyList<VietsubSubtitleCue> cues,
        VietsubVoiceSettingsSnapshot settings)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(settings);
        var ordered = cues.OrderBy(cue => cue.StartMilliseconds).ThenBy(cue => cue.EndMilliseconds).ToArray();
        var result = new List<VietsubVoicePhrase>();
        var current = new List<VietsubSubtitleCue>();

        foreach (var cue in ordered)
        {
            if (!cue.VoiceEnabled)
            {
                if (current.Count > 0)
                {
                    result.Add(Create(current));
                    current.Clear();
                }
                continue;
            }
            if (string.IsNullOrWhiteSpace(cue.TranslatedText))
            {
                continue;
            }

            if (current.Count > 0 && !CanJoin(current, cue, settings))
            {
                result.Add(Create(current));
                current.Clear();
            }
            current.Add(cue);
        }

        if (current.Count > 0)
        {
            result.Add(Create(current));
        }
        return ApplySelectionBoundaries(result, ordered);
    }

    internal static IReadOnlyList<VietsubVoicePhrase> ApplySelectionBoundaries(
        IEnumerable<VietsubVoicePhrase> phrases, IReadOnlyList<VietsubSubtitleCue> cues)
    {
        var numbers = cues.OrderBy(cue => cue.StartMilliseconds).ThenBy(cue => cue.EndMilliseconds)
            .Select((cue, index) => (cue.CueId, Number: index + 1)).ToDictionary(item => item.CueId, item => item.Number);
        return phrases.Select(phrase => phrase with
        {
            CueNumbers = phrase.CueIds.Select(id => numbers[id]).ToArray(),
            HardEndMilliseconds = cues.Where(cue => !cue.VoiceEnabled && cue.EndMilliseconds > phrase.StartMilliseconds)
                .Select(cue => (long?)Math.Max(phrase.StartMilliseconds, cue.StartMilliseconds)).Min()
        }).ToArray();
    }

    private static bool CanJoin(
        IReadOnlyList<VietsubSubtitleCue> current,
        VietsubSubtitleCue next,
        VietsubVoiceSettingsSnapshot settings)
    {
        var previous = current[^1];
        if (!string.Equals(previous.Speaker, next.Speaker, StringComparison.Ordinal)
            || next.StartMilliseconds < previous.EndMilliseconds
            || next.StartMilliseconds - previous.EndMilliseconds > settings.MaximumPhraseGapMilliseconds
            || next.EndMilliseconds - current[0].StartMilliseconds > settings.MaximumPhraseDurationMilliseconds
            || current.Sum(cue => cue.TranslatedText.Trim().Length) + current.Count + next.TranslatedText.Trim().Length
                > settings.MaximumPhraseCharacters)
        {
            return false;
        }

        var previousText = previous.TranslatedText.TrimEnd();
        return previousText.Length == 0 || previousText[^1] is not ('.' or '!' or '?' or '…');
    }

    private static VietsubVoicePhrase Create(IReadOnlyList<VietsubSubtitleCue> cues)
    {
        var ids = cues.Select(cue => cue.CueId).ToArray();
        var stableInput = string.Join('|', ids.Select(id => id.ToString("N")));
        var phraseId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stableInput)))
            .ToLowerInvariant()[..32];
        return new(
            phraseId,
            ids,
            cues[0].Speaker,
            string.Join(' ', cues.Select(cue => cue.TranslatedText.Trim())),
            cues[0].StartMilliseconds,
            cues[^1].EndMilliseconds);
    }
}

internal static class VietsubVoiceFingerprintBuilder
{
    public static string BuildSelectionFingerprint(IEnumerable<VietsubSubtitleCue> cues) =>
        Hash(string.Join('\n', cues.OrderBy(cue => cue.CueId)
            .Select(cue => $"{cue.CueId:N}:{(cue.VoiceEnabled ? 1 : 0)}")));

    public static string BuildConfigurationFingerprint(VietsubVoiceSettingsSnapshot settings) =>
        Hash(string.Join('\n',
        [
            "voice-config-v1",
            settings.EngineId,
            settings.EngineVersion,
            settings.ModelId,
            settings.ModelVersion,
            settings.VoiceId,
            settings.MaximumPhraseGapMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            settings.MaximumPhraseDurationMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            settings.MaximumPhraseCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture),
            settings.MaximumBorrowedGapMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            settings.PreferredMaximumTempo.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            settings.MaximumTempo.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            settings.TrimSilence ? "1" : "0"
        ]));

    public static string BuildPhraseFingerprint(
        VietsubVoicePhrase phrase,
        VietsubVoiceSettingsSnapshot settings) =>
        Hash(string.Join('\n',
        [
            "voice-phrase-v1",
            BuildConfigurationFingerprint(settings),
            phrase.PhraseId,
            phrase.Speaker,
            phrase.Text.Normalize(NormalizationForm.FormC)
        ]));

    public static string BuildTimelineFingerprint(
        string configurationFingerprint,
        IEnumerable<VietsubVoiceArtifact> artifacts) =>
        Hash(string.Join('\n', new[] { "voice-timeline-v2", configurationFingerprint }
            .Concat(artifacts
                .OrderBy(item => item.PhraseId, StringComparer.Ordinal)
                .Select(item => item.ContentFingerprint))));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
