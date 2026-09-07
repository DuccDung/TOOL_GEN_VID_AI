using System.Text;
using TOOL_LOCAL.Vietsub.Domain;

namespace TOOL_LOCAL.Vietsub.Translation;

internal sealed record VietsubPlannedTranslationScene(
    int SceneNumber,
    int ChapterNumber,
    long ChapterStartMilliseconds,
    long ChapterEndMilliseconds,
    IReadOnlyList<VietsubTranslationCueInput> Cues)
{
    public IReadOnlyList<Guid> TargetCueIds => Cues
        .Where(cue => cue.IsTarget)
        .Select(cue => cue.CueId)
        .ToArray();

    public IReadOnlyList<string> TargetCueAliases => Cues
        .Where(cue => cue.IsTarget)
        .Select(cue => cue.CueAlias)
        .ToArray();
}

internal static class VietsubTranslationScenePlanner
{
    public static IReadOnlyList<VietsubPlannedTranslationScene> Plan(
        IReadOnlyList<VietsubSubtitleCue> cues,
        IReadOnlySet<Guid> targetCueIds,
        int maximumTargetCues,
        int contextCueCount,
        int sceneGapMilliseconds,
        double maximumCharactersPerSecond,
        int maximumChapterDurationMilliseconds = VietsubTranslationLimits.DefaultMaximumChapterDurationMilliseconds,
        int maximumSceneSourceCharacters = VietsubTranslationLimits.DefaultMaximumSceneSourceCharacters)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(targetCueIds);
        if (cues.Count == 0 || targetCueIds.Count == 0)
        {
            return [];
        }

        var ordered = cues
            .Select((cue, originalIndex) => new IndexedCue(cue, originalIndex))
            .OrderBy(item => item.Cue.StartMilliseconds)
            .ThenBy(item => item.Cue.EndMilliseconds)
            .ThenBy(item => item.OriginalIndex)
            .ToArray();
        EnsureValidTimeline(ordered);

        maximumTargetCues = Math.Clamp(maximumTargetCues, 1, 30);
        contextCueCount = Math.Clamp(contextCueCount, 0, 10);
        sceneGapMilliseconds = Math.Clamp(sceneGapMilliseconds, 1_000, 60_000);
        maximumCharactersPerSecond = double.IsFinite(maximumCharactersPerSecond)
            ? Math.Clamp(maximumCharactersPerSecond, 8, 30)
            : VietsubTranslationLimits.DefaultMaximumCharactersPerSecond;
        maximumChapterDurationMilliseconds = Math.Clamp(
            maximumChapterDurationMilliseconds,
            60_000,
            30 * 60 * 1_000);
        maximumSceneSourceCharacters = Math.Clamp(maximumSceneSourceCharacters, 1_000, 20_000);

        var ranges = FindChapterRanges(ordered, sceneGapMilliseconds, maximumChapterDurationMilliseconds);
        var plans = new List<VietsubPlannedTranslationScene>();
        for (var chapterIndex = 0; chapterIndex < ranges.Count; chapterIndex++)
        {
            var (chapterStart, chapterEnd) = ranges[chapterIndex];
            var targets = Enumerable.Range(chapterStart, chapterEnd - chapterStart + 1)
                .Where(index => targetCueIds.Contains(ordered[index].Cue.CueId))
                .ToArray();
            foreach (var targetChunk in PackTargets(
                         ordered,
                         targets,
                         maximumTargetCues,
                         maximumSceneSourceCharacters))
            {
                var first = Math.Max(chapterStart, targetChunk[0] - contextCueCount);
                var last = Math.Min(chapterEnd, targetChunk[^1] + contextCueCount);
                var chunkIds = targetChunk.Select(index => ordered[index].Cue.CueId).ToHashSet();
                var inputs = Enumerable.Range(first, last - first + 1)
                    .Select((index, sceneCueIndex) => ToInput(
                        ordered[index].Cue,
                        ordered[index].OriginalIndex,
                        $"c{sceneCueIndex + 1:D3}",
                        chunkIds.Contains(ordered[index].Cue.CueId),
                        maximumCharactersPerSecond))
                    .ToArray();
                plans.Add(new VietsubPlannedTranslationScene(
                    plans.Count + 1,
                    chapterIndex + 1,
                    ordered[chapterStart].Cue.StartMilliseconds,
                    ordered[chapterEnd].Cue.EndMilliseconds,
                    inputs));
            }
        }

        return plans;
    }

    public static string BuildChapterContext(
        VietsubPlannedTranslationScene scene,
        IReadOnlyList<VietsubSubtitleCue> allCues,
        int maximumCharacters = 2_200)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(allCues);
        maximumCharacters = Math.Clamp(maximumCharacters, 400, 4_000);
        var chapterCues = allCues
            .Where(cue => cue.StartMilliseconds >= scene.ChapterStartMilliseconds
                && cue.EndMilliseconds <= scene.ChapterEndMilliseconds)
            .OrderBy(cue => cue.StartMilliseconds)
            .ThenBy(cue => cue.EndMilliseconds)
            .ToArray();
        if (chapterCues.Length == 0)
        {
            return string.Empty;
        }

        var targetIds = scene.TargetCueIds.ToHashSet();
        var firstTargetTime = scene.Cues
            .Where(cue => cue.IsTarget)
            .Select(cue => cue.StartMilliseconds)
            .DefaultIfEmpty(scene.ChapterStartMilliseconds)
            .Min();
        var previousApproved = chapterCues
            .Where(cue => cue.EndMilliseconds <= firstTargetTime
                && !targetIds.Contains(cue.CueId)
                && VietsubTranslationResultValidator.IsApprovedContext(cue))
            .TakeLast(6)
            .ToArray();
        var sampleIndices = BuildSampleIndices(chapterCues.Length, 8);
        var builder = new StringBuilder();
        AppendLimited(
            builder,
            $"Chapter {scene.ChapterNumber}: {FormatTime(scene.ChapterStartMilliseconds)}-{FormatTime(scene.ChapterEndMilliseconds)}.\n",
            maximumCharacters);
        AppendLimited(builder, "Representative source dialogue:\n", maximumCharacters);
        foreach (var index in sampleIndices)
        {
            var cue = chapterCues[index];
            AppendLimited(builder, $"- {NormalizeSpeaker(cue.Speaker)}: {cue.OriginalText.Trim()}\n", maximumCharacters);
        }

        if (previousApproved.Length > 0)
        {
            AppendLimited(builder, "Recent approved Vietnamese continuity:\n", maximumCharacters);
            foreach (var cue in previousApproved)
            {
                AppendLimited(
                    builder,
                    $"- {NormalizeSpeaker(cue.Speaker)}: {cue.OriginalText.Trim()} => {cue.TranslatedText.Trim()}\n",
                    maximumCharacters);
            }
        }

        return builder.ToString().Trim();
    }

    private static void EnsureValidTimeline(IReadOnlyList<IndexedCue> cues)
    {
        var duplicateIds = cues
            .GroupBy(item => item.Cue.CueId)
            .FirstOrDefault(group => group.Key == Guid.Empty || group.Count() > 1);
        if (duplicateIds is not null
            || cues.Any(item => item.Cue.StartMilliseconds < 0
                || item.Cue.EndMilliseconds <= item.Cue.StartMilliseconds
                || string.IsNullOrWhiteSpace(item.Cue.OriginalText)))
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.ContextInvalid,
                "Track nguồn chứa cue trùng, rỗng hoặc timeline không hợp lệ.");
        }
    }

    private static IReadOnlyList<(int Start, int End)> FindChapterRanges(
        IReadOnlyList<IndexedCue> cues,
        int gapMilliseconds,
        int maximumDurationMilliseconds)
    {
        var ranges = new List<(int Start, int End)>();
        var start = 0;
        for (var index = 1; index < cues.Count; index++)
        {
            var gap = cues[index].Cue.StartMilliseconds - cues[index - 1].Cue.EndMilliseconds;
            var chapterDuration = cues[index].Cue.EndMilliseconds - cues[start].Cue.StartMilliseconds;
            if (gap <= gapMilliseconds && chapterDuration <= maximumDurationMilliseconds)
            {
                continue;
            }

            ranges.Add((start, index - 1));
            start = index;
        }

        ranges.Add((start, cues.Count - 1));
        return ranges;
    }

    private static IReadOnlyList<int[]> PackTargets(
        IReadOnlyList<IndexedCue> cues,
        IReadOnlyList<int> targets,
        int maximumTargetCues,
        int maximumSourceCharacters)
    {
        var chunks = new List<int[]>();
        var current = new List<int>();
        var characters = 0;
        foreach (var index in targets)
        {
            var cueCharacters = Math.Max(1, cues[index].Cue.OriginalText.Length);
            if (current.Count > 0
                && (current.Count >= maximumTargetCues
                    || characters + cueCharacters > maximumSourceCharacters))
            {
                chunks.Add(current.ToArray());
                current.Clear();
                characters = 0;
            }

            current.Add(index);
            characters += cueCharacters;
        }

        if (current.Count > 0)
        {
            chunks.Add(current.ToArray());
        }

        return chunks;
    }

    private static VietsubTranslationCueInput ToInput(
        VietsubSubtitleCue cue,
        int originalIndex,
        string cueAlias,
        bool isTarget,
        double maximumCharactersPerSecond)
    {
        var durationSeconds = Math.Max(0.25, (cue.EndMilliseconds - cue.StartMilliseconds) / 1_000d);
        var suggestedMaximum = Math.Max(8, (int)Math.Ceiling(durationSeconds * maximumCharactersPerSecond));
        return new VietsubTranslationCueInput(
            cueAlias,
            cue.CueId,
            originalIndex,
            cue.StartMilliseconds,
            cue.EndMilliseconds,
            NormalizeSpeaker(cue.Speaker),
            cue.OriginalText.Trim(),
            isTarget,
            suggestedMaximum,
            !isTarget && VietsubTranslationResultValidator.IsApprovedContext(cue)
                ? cue.TranslatedText.Trim()
                : null);
    }

    private static IReadOnlyList<int> BuildSampleIndices(int count, int maximum)
    {
        if (count <= maximum)
        {
            return Enumerable.Range(0, count).ToArray();
        }

        return Enumerable.Range(0, maximum)
            .Select(index => (int)Math.Round(index * (count - 1d) / (maximum - 1d)))
            .Distinct()
            .ToArray();
    }

    private static void AppendLimited(StringBuilder builder, string value, int maximum)
    {
        if (builder.Length >= maximum)
        {
            return;
        }

        var remaining = maximum - builder.Length;
        builder.Append(value.Length <= remaining ? value : value[..remaining]);
    }

    private static string NormalizeSpeaker(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "speaker_unknown" : value.Trim();

    private static string FormatTime(long milliseconds) =>
        TimeSpan.FromMilliseconds(Math.Max(0, milliseconds)).ToString(@"hh\:mm\:ss");

    private sealed record IndexedCue(VietsubSubtitleCue Cue, int OriginalIndex);
}
