using System.Text.RegularExpressions;

namespace TOOL_SHARED.Contracts.Generation;

public static class SpeechPacingStatuses
{
    public const string TooShort = "TooShort";
    public const string Short = "Short";
    public const string OnTarget = "OnTarget";
    public const string Long = "Long";
    public const string TooLong = "TooLong";
}

public static class ContentPlanViolationReasons
{
    public const string SpeechTooShort = "speech_too_short";

    public const string SpeechTooLong = "speech_too_long";
}

public static class ContentPlanErrorCodes
{
    public const string SpeechPacingInvalid = "content_speech_pacing_invalid";
}

public sealed record SpeechPacingGuidance(
    decimal TargetMinimumSeconds,
    decimal TargetMaximumSeconds,
    int SuggestedMinimumSpeechUnits,
    int SuggestedMaximumSpeechUnits);

public sealed record SpeechPacingAssessment(
    int SpeechUnitCount,
    decimal EstimatedDurationSeconds,
    decimal DurationRatio,
    decimal TargetMinimumSeconds,
    decimal TargetMaximumSeconds,
    string Status,
    bool PassesGenerationGuard);

public static partial class SpeechPacingPolicy
{
    public const decimal TargetMinimumRatio = 0.85m;

    public const decimal TargetMaximumRatio = 0.95m;

    public const decimal GenerationMinimumRatio = 0.80m;

    public const decimal GenerationMaximumRatio = 1.05m;

    public const decimal VietnameseSpeechUnitsPerSecond = 2.8m;

    public static SpeechPacingGuidance CreateGuidance(
        decimal sceneDurationSeconds,
        decimal speakingRate = 1m)
    {
        RequireDuration(sceneDurationSeconds);
        var normalizedRate = NormalizeSpeakingRate(speakingRate);
        var targetMinimum = decimal.Round(
            sceneDurationSeconds * TargetMinimumRatio,
            2,
            MidpointRounding.AwayFromZero);
        var targetMaximum = decimal.Round(
            sceneDurationSeconds * TargetMaximumRatio,
            2,
            MidpointRounding.AwayFromZero);
        var minimumUnits = Math.Max(
            1,
            decimal.ToInt32(decimal.Floor(
                targetMinimum * VietnameseSpeechUnitsPerSecond * normalizedRate)));
        var maximumUnits = Math.Max(
            minimumUnits,
            decimal.ToInt32(decimal.Ceiling(
                targetMaximum * VietnameseSpeechUnitsPerSecond * normalizedRate)));

        return new SpeechPacingGuidance(
            targetMinimum,
            targetMaximum,
            minimumUnits,
            maximumUnits);
    }

    public static SpeechPacingAssessment Assess(
        string? spokenText,
        decimal sceneDurationSeconds,
        decimal speakingRate = 1m)
    {
        RequireDuration(sceneDurationSeconds);
        var guidance = CreateGuidance(sceneDurationSeconds, speakingRate);
        var speechUnits = CountSpeechUnits(spokenText);
        var estimatedDuration = EstimateDurationSeconds(spokenText, speakingRate);
        var ratio = decimal.Round(
            estimatedDuration / sceneDurationSeconds,
            3,
            MidpointRounding.AwayFromZero);
        var status = ratio < GenerationMinimumRatio
            ? SpeechPacingStatuses.TooShort
            : ratio < TargetMinimumRatio
                ? SpeechPacingStatuses.Short
                : ratio <= TargetMaximumRatio
                    ? SpeechPacingStatuses.OnTarget
                    : ratio <= GenerationMaximumRatio
                        ? SpeechPacingStatuses.Long
                        : SpeechPacingStatuses.TooLong;

        return new SpeechPacingAssessment(
            speechUnits,
            estimatedDuration,
            ratio,
            guidance.TargetMinimumSeconds,
            guidance.TargetMaximumSeconds,
            status,
            ratio >= GenerationMinimumRatio && ratio <= GenerationMaximumRatio);
    }

    public static decimal EstimateDurationSeconds(
        string? spokenText,
        decimal speakingRate = 1m)
    {
        if (string.IsNullOrWhiteSpace(spokenText))
        {
            return 0m;
        }

        var normalizedRate = NormalizeSpeakingRate(speakingRate);
        var speechSeconds = CountSpeechUnits(spokenText) /
                            (VietnameseSpeechUnitsPerSecond * normalizedRate);
        var shortPauses = ShortPauseRegex().Matches(spokenText).Count * 0.12m;
        var sentencePauses = SentencePauseRegex().Matches(spokenText).Count * 0.22m;
        var ellipsisPauses = EllipsisRegex().Matches(spokenText).Count * 0.30m;

        return decimal.Round(
            speechSeconds + shortPauses + sentencePauses + ellipsisPauses,
            2,
            MidpointRounding.AwayFromZero);
    }

    public static int CountSpeechUnits(string? spokenText)
    {
        if (string.IsNullOrWhiteSpace(spokenText))
        {
            return 0;
        }

        var count = 0;
        foreach (Match match in SpeechTokenRegex().Matches(spokenText))
        {
            var token = match.Value;
            if (token.All(char.IsDigit))
            {
                count += Math.Clamp(token.Length, 1, 4);
                continue;
            }

            if (token.Length is >= 2 and <= 6 &&
                token.All(character => char.IsAsciiLetter(character) && char.IsUpper(character)))
            {
                count += token.Length;
                continue;
            }

            count++;
        }

        return count;
    }

    public static string ClassifyActualDuration(
        decimal actualDurationSeconds,
        decimal sceneDurationSeconds)
    {
        RequireDuration(sceneDurationSeconds);
        if (actualDurationSeconds < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(actualDurationSeconds));
        }

        var ratio = actualDurationSeconds / sceneDurationSeconds;
        return ratio < GenerationMinimumRatio
            ? SpeechPacingStatuses.TooShort
            : ratio < TargetMinimumRatio
                ? SpeechPacingStatuses.Short
                : ratio <= TargetMaximumRatio
                    ? SpeechPacingStatuses.OnTarget
                    : ratio <= GenerationMaximumRatio
                        ? SpeechPacingStatuses.Long
                        : SpeechPacingStatuses.TooLong;
    }

    private static decimal NormalizeSpeakingRate(decimal speakingRate) =>
        Math.Clamp(speakingRate <= 0m ? 1m : speakingRate, 0.5m, 2m);

    private static void RequireDuration(decimal sceneDurationSeconds)
    {
        if (sceneDurationSeconds <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(sceneDurationSeconds));
        }
    }

    [GeneratedRegex(@"[\p{L}\p{M}\p{N}]+(?:['\u2019\-][\p{L}\p{M}\p{N}]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex SpeechTokenRegex();

    [GeneratedRegex(@"[,;:]", RegexOptions.CultureInvariant)]
    private static partial Regex ShortPauseRegex();

    [GeneratedRegex(@"[.!?]+", RegexOptions.CultureInvariant)]
    private static partial Regex SentencePauseRegex();

    [GeneratedRegex(@"\u2026+", RegexOptions.CultureInvariant)]
    private static partial Regex EllipsisRegex();
}
