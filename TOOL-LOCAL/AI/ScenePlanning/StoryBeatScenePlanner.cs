using System.Text.RegularExpressions;
using TOOL_LOCAL.AI.Contracts;
using TOOL_LOCAL.Providers;

namespace TOOL_LOCAL.AI.ScenePlanning;

public sealed partial class StoryBeatScenePlanner
{
    public ScenePlanContract Plan(
        ScriptContract script,
        int targetDurationSeconds,
        VideoProviderCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (script.Beats.Count == 0)
        {
            throw new ArgumentException("Script phải có ít nhất một story beat.", nameof(script));
        }

        if (targetDurationSeconds is < 5 or > 3600)
        {
            throw new ArgumentOutOfRangeException(nameof(targetDurationSeconds));
        }

        var supportedDurations = capabilities.SupportedDurationsSeconds
            .Where(x => x > 0)
            .Distinct()
            .Order()
            .ToArray();
        if (supportedDurations.Length == 0)
        {
            throw new ArgumentException("Video provider phải khai báo thời lượng clip được hỗ trợ.", nameof(capabilities));
        }

        var maximumClipSeconds = supportedDurations[^1];
        var weightedBeats = script.Beats
            .Select(beat => new WeightedBeat(beat, CalculateWeight(beat)))
            .ToArray();
        var totalWeight = weightedBeats.Sum(x => x.Weight);

        var units = new List<SceneUnit>();
        foreach (var weightedBeat in weightedBeats)
        {
            var allocatedSeconds = targetDurationSeconds * weightedBeat.Weight / totalWeight;
            var partCount = Math.Max(1, (int)Math.Ceiling(allocatedSeconds / maximumClipSeconds));
            var narrationParts = SplitNarration(weightedBeat.Beat.Narration, partCount);

            for (var partIndex = 0; partIndex < partCount; partIndex++)
            {
                units.Add(new SceneUnit(
                    weightedBeat.Beat,
                    narrationParts[partIndex],
                    allocatedSeconds / partCount,
                    partIndex + 1,
                    partCount));
            }
        }

        NormalizeDurations(units, targetDurationSeconds, maximumClipSeconds);

        var scenes = new List<PlannedSceneContract>(units.Count);
        decimal cursor = 0;
        ContinuityStateContract? previousEndState = null;
        for (var index = 0; index < units.Count; index++)
        {
            var unit = units[index];
            var sceneKey = $"scene_{index + 1:000}";
            var contentDuration = decimal.Round((decimal)unit.DurationSeconds, 1, MidpointRounding.AwayFromZero);
            var end = index == units.Count - 1
                ? targetDurationSeconds
                : decimal.Round(cursor + contentDuration, 1, MidpointRounding.AwayFromZero);
            contentDuration = end - cursor;
            var startState = previousEndState ?? CreateInitialState(unit.Beat);
            var endState = CreateEndState(startState, unit.Beat);

            scenes.Add(new PlannedSceneContract(
                index + 1,
                sceneKey,
                unit.Beat.BeatType,
                unit.Beat.StoryPurpose,
                cursor,
                end,
                contentDuration,
                ChooseGenerationDuration(contentDuration, supportedDurations),
                unit.Narration,
                unit.PartNumber == 1 ? unit.Beat.Dialogue : null,
                unit.PartCount == 1
                    ? unit.Beat.VisualIntent
                    : $"{unit.Beat.VisualIntent} — continuous part {unit.PartNumber} of {unit.PartCount}",
                ChooseCamera(unit.Beat.BeatType),
                ChooseMotion(unit.Beat.BeatType),
                unit.Beat.Emotion,
                index == 0 ? "cut from black" : "continuity cut",
                startState,
                endState,
                index == 0 ? null : $"scene_{index:000}",
                index == units.Count - 1 ? null : $"scene_{index + 2:000}"));

            previousEndState = endState;
            cursor = end;
        }

        return new ScenePlanContract(targetDurationSeconds, scenes);
    }

    private static double CalculateWeight(StoryBeatContract beat)
    {
        var wordCount = WordBoundaryRegex().Split(beat.Narration.Trim()).Count(x => x.Length > 0);
        var estimatedSpeechSeconds = wordCount / 2.8d;
        var visualSeconds = 2.5d + Math.Clamp(beat.VisualComplexity, 1, 5) * 0.8d;
        var hookMultiplier = beat.BeatType.Equals("HOOK", StringComparison.OrdinalIgnoreCase) ? 0.75d : 1d;
        return Math.Max(1.5d, Math.Max(estimatedSpeechSeconds, visualSeconds) * hookMultiplier);
    }

    private static string[] SplitNarration(string narration, int partCount)
    {
        if (partCount == 1)
        {
            return [narration.Trim()];
        }

        var words = WordBoundaryRegex().Split(narration.Trim()).Where(x => x.Length > 0).ToArray();
        if (words.Length == 0)
        {
            return Enumerable.Repeat(string.Empty, partCount).ToArray();
        }

        var result = new string[partCount];
        for (var index = 0; index < partCount; index++)
        {
            var start = index * words.Length / partCount;
            var end = (index + 1) * words.Length / partCount;
            result[index] = string.Join(' ', words[start..end]);
        }

        return result;
    }

    private static void NormalizeDurations(List<SceneUnit> units, int targetSeconds, int maximumClipSeconds)
    {
        var minimum = Math.Min(1.5d, targetSeconds / (double)units.Count);
        foreach (var unit in units)
        {
            unit.DurationSeconds = Math.Clamp(unit.DurationSeconds, minimum, maximumClipSeconds);
        }

        for (var iteration = 0; iteration < 20; iteration++)
        {
            var difference = targetSeconds - units.Sum(x => x.DurationSeconds);
            if (Math.Abs(difference) < 0.01d)
            {
                return;
            }

            var candidates = difference > 0
                ? units.Where(x => x.DurationSeconds < maximumClipSeconds - 0.01d).ToArray()
                : units.Where(x => x.DurationSeconds > minimum + 0.01d).ToArray();
            if (candidates.Length == 0)
            {
                throw new InvalidOperationException("Không thể phân bổ thời lượng theo giới hạn của provider.");
            }

            var delta = difference / candidates.Length;
            foreach (var candidate in candidates)
            {
                candidate.DurationSeconds = Math.Clamp(candidate.DurationSeconds + delta, minimum, maximumClipSeconds);
            }
        }
    }

    private static int ChooseGenerationDuration(decimal contentDuration, IReadOnlyList<int> supportedDurations) =>
        supportedDurations.FirstOrDefault(x => x >= Math.Ceiling(contentDuration), supportedDurations[^1]);

    private static ContinuityStateContract CreateInitialState(StoryBeatContract beat) =>
        new(
            "standing naturally",
            "center frame",
            "toward the main action",
            "locked character-profile clothing",
            [],
            "locked project environment",
            "continuous story time",
            "locked style-profile lighting",
            beat.Emotion);

    private static ContinuityStateContract CreateEndState(
        ContinuityStateContract start,
        StoryBeatContract beat) =>
        start with { Emotion = beat.Emotion };

    private static string ChooseCamera(string beatType) => beatType.ToUpperInvariant() switch
    {
        "HOOK" => "tight close-up, immediate visual focus",
        "CLIMAX" => "dynamic cinematic wide-to-close movement",
        "CTA" => "stable medium close-up",
        _ => "cinematic medium shot"
    };

    private static string ChooseMotion(string beatType) => beatType.ToUpperInvariant() switch
    {
        "HOOK" => "fast controlled push-in",
        "CLIMAX" => "energetic but coherent subject and camera motion",
        "CTA" => "subtle slow dolly-in",
        _ => "slow deliberate camera movement"
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex WordBoundaryRegex();

    private sealed record WeightedBeat(StoryBeatContract Beat, double Weight);

    private sealed class SceneUnit(
        StoryBeatContract beat,
        string narration,
        double durationSeconds,
        int partNumber,
        int partCount)
    {
        public StoryBeatContract Beat { get; } = beat;
        public string Narration { get; } = narration;
        public double DurationSeconds { get; set; } = durationSeconds;
        public int PartNumber { get; } = partNumber;
        public int PartCount { get; } = partCount;
    }
}
