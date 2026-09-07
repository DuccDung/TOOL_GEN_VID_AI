using System.Globalization;
using System.Text;

namespace TOOL_SERVER.Generation;

internal sealed record SpeechTranscriptComparison(
    string NormalizedExpected,
    string NormalizedTranscript,
    decimal WordErrorRate,
    decimal CharacterErrorRate,
    decimal RequiredTermRecall,
    IReadOnlyList<string> RequiredTerms,
    IReadOnlyList<string> MissingRequiredTerms);

internal static class SpeechTranscriptComparer
{
    public static SpeechTranscriptComparison Compare(string expected, string transcript)
    {
        var normalizedExpected = Normalize(expected);
        var normalizedTranscript = Normalize(transcript);
        var expectedWords = SplitWords(normalizedExpected);
        var transcriptWords = SplitWords(normalizedTranscript);
        var wordErrorRate = ErrorRate(expectedWords, transcriptWords);
        var characterErrorRate = ErrorRate(
            normalizedExpected.Replace(" ", string.Empty, StringComparison.Ordinal).ToCharArray(),
            normalizedTranscript.Replace(" ", string.Empty, StringComparison.Ordinal).ToCharArray());
        var requiredTerms = expectedWords
            .Where(word => word.Length >= 4 || word.Any(char.IsDigit))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var transcriptSet = transcriptWords.ToHashSet(StringComparer.Ordinal);
        var missingTerms = requiredTerms
            .Where(term => !transcriptSet.Contains(term))
            .ToArray();
        var recall = requiredTerms.Length == 0
            ? expectedWords.Length == 0 || transcriptWords.Length > 0 ? 1m : 0m
            : requiredTerms.Count(transcriptSet.Contains) / (decimal)requiredTerms.Length;
        return new SpeechTranscriptComparison(
            normalizedExpected,
            normalizedTranscript,
            wordErrorRate,
            characterErrorRate,
            Math.Round(recall, 6, MidpointRounding.AwayFromZero),
            requiredTerms,
            missingTerms);
    }

    internal static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        var normalized = value.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        var builder = new StringBuilder(normalized.Length);
        var previousWasSpace = true;
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            var keep = char.IsLetterOrDigit(character) ||
                       category is UnicodeCategory.NonSpacingMark or
                           UnicodeCategory.SpacingCombiningMark;
            if (keep)
            {
                builder.Append(character);
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }
        return builder.ToString().Trim();
    }

    private static string[] SplitWords(string value) =>
        value.Length == 0
            ? []
            : value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static decimal ErrorRate<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
        where T : notnull
    {
        if (expected.Count == 0)
        {
            return actual.Count == 0 ? 0m : 1m;
        }
        var previous = new int[actual.Count + 1];
        var current = new int[actual.Count + 1];
        for (var index = 0; index <= actual.Count; index++)
        {
            previous[index] = index;
        }
        for (var expectedIndex = 1; expectedIndex <= expected.Count; expectedIndex++)
        {
            current[0] = expectedIndex;
            for (var actualIndex = 1; actualIndex <= actual.Count; actualIndex++)
            {
                var substitution = EqualityComparer<T>.Default.Equals(
                    expected[expectedIndex - 1],
                    actual[actualIndex - 1])
                    ? 0
                    : 1;
                current[actualIndex] = Math.Min(
                    Math.Min(current[actualIndex - 1] + 1, previous[actualIndex] + 1),
                    previous[actualIndex - 1] + substitution);
            }
            (previous, current) = (current, previous);
        }
        return Math.Min(
            1m,
            Math.Round(previous[actual.Count] / (decimal)expected.Count, 6, MidpointRounding.AwayFromZero));
    }
}
