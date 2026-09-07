using System.Text.RegularExpressions;

namespace TOOL_LOCAL.Vietsub.Translation;

internal sealed record VietsubTranslationQualityAssessment(
    bool IsValid,
    string? FailureCode,
    IReadOnlyList<string> Warnings,
    double CharactersPerSecond);

internal static partial class VietsubTranslationQualityValidator
{
    public static VietsubTranslationQualityAssessment Assess(
        string source,
        string translation,
        long durationMilliseconds,
        IReadOnlyList<VietsubTranslationGlossaryEntry>? glossary = null,
        double maximumCharactersPerSecond = VietsubTranslationLimits.DefaultMaximumCharactersPerSecond,
        double? providerConfidence = null,
        IReadOnlyList<string>? providerWarnings = null,
        string? sourceLanguageCode = null,
        int? suggestedMaximumCharacters = null)
    {
        var safeSource = source ?? string.Empty;
        var safeTranslation = translation ?? string.Empty;
        var normalized = WhitespaceRegex().Replace(safeTranslation, " ").Trim();
        var durationSeconds = Math.Max(0.25, durationMilliseconds / 1_000d);
        var charactersPerSecond = normalized.Count(character => !char.IsWhiteSpace(character)) / durationSeconds;
        var fatal = ValidateText(safeSource, normalized);
        if (fatal is not null)
        {
            return new VietsubTranslationQualityAssessment(false, fatal, [], charactersPerSecond);
        }

        var warnings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var warning in providerWarnings ?? [])
        {
            if (!string.IsNullOrWhiteSpace(warning))
            {
                warnings.Add(LimitCode(warning));
            }
        }

        if (providerConfidence is < 0.70)
        {
            warnings.Add("LOW_CONFIDENCE");
        }

        var sourceNumbers = NumberRegex().Matches(safeSource).Select(match => match.Value).ToArray();
        var translatedNumbers = NumberRegex().Matches(normalized).Select(match => match.Value).ToArray();
        if (!sourceNumbers.SequenceEqual(translatedNumbers, StringComparer.Ordinal))
        {
            warnings.Add("NUMBER_MISMATCH");
        }

        foreach (var entry in glossary ?? [])
        {
            if (!string.IsNullOrWhiteSpace(entry.SourceText)
                && !string.IsNullOrWhiteSpace(entry.TargetText)
                && ContainsTerm(safeSource, entry.SourceText)
                && !ContainsTerm(normalized, entry.TargetText))
            {
                warnings.Add($"GLOSSARY_MISSING:{LimitCode(entry.SourceText)}");
            }
        }

        var normalizedMaximum = double.IsFinite(maximumCharactersPerSecond)
            ? Math.Clamp(maximumCharactersPerSecond, 8, 30)
            : VietsubTranslationLimits.DefaultMaximumCharactersPerSecond;
        if (charactersPerSecond > normalizedMaximum)
        {
            warnings.Add("READING_SPEED_HIGH");
        }

        if (suggestedMaximumCharacters is > 0
            && normalized.Length > Math.Max(suggestedMaximumCharacters.Value + 8, suggestedMaximumCharacters.Value * 1.35))
        {
            warnings.Add("SUGGESTED_LENGTH_EXCEEDED");
        }

        if (VietsubTranslationLimits.NormalizeSourceLanguage(sourceLanguageCode ?? "en") == "zh")
        {
            var hanCharacters = normalized.Count(IsHanCharacter);
            if (hanCharacters >= 4 && hanCharacters * 5 >= Math.Max(1, normalized.Length))
            {
                warnings.Add("SOURCE_LANGUAGE_LEAKAGE");
            }
        }

        return new VietsubTranslationQualityAssessment(
            true,
            null,
            warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            charactersPerSecond);
    }

    internal static string? ValidateText(string source, string translation)
    {
        var normalized = WhitespaceRegex().Replace(translation ?? string.Empty, " ").Trim();
        if (normalized.Length == 0)
        {
            return "EMPTY_TRANSLATION";
        }

        var sourceLength = Math.Max(1, (source ?? string.Empty).Count(character => !char.IsWhiteSpace(character)));
        if (normalized.Length > Math.Max(200, sourceLength * 14))
        {
            return "EXCESSIVE_LENGTH";
        }

        var tokens = WordRegex()
            .Matches(normalized.ToLowerInvariant())
            .Select(match => match.Value)
            .ToArray();
        if (tokens.Length < 4)
        {
            return null;
        }

        var sourceRepeatRun = FindMaximumIntentionalRepeatRun(source ?? string.Empty);
        var maximumAllowedConsecutive = sourceRepeatRun >= 3 ? sourceRepeatRun + 1 : 3;
        var consecutive = 1;
        for (var index = 1; index < tokens.Length; index++)
        {
            consecutive = tokens[index] == tokens[index - 1] ? consecutive + 1 : 1;
            if (consecutive > maximumAllowedConsecutive)
            {
                return "REPEATED_TOKEN_RUN";
            }
        }

        var dominantCount = tokens.GroupBy(token => token, StringComparer.Ordinal).Max(group => group.Count());
        if (dominantCount >= 6
            && dominantCount * 100 >= tokens.Length * 35
            && (sourceRepeatRun < 3 || dominantCount > sourceRepeatRun + 1))
        {
            return "DOMINANT_REPEATED_TOKEN";
        }

        for (var size = 2; size <= Math.Min(5, tokens.Length / 3); size++)
        {
            var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var index = 0; index <= tokens.Length - size; index++)
            {
                var key = string.Join('\u001f', tokens.AsSpan(index, size).ToArray());
                frequencies[key] = frequencies.GetValueOrDefault(key) + 1;
            }

            if (frequencies.Values.Any(count => count >= 4
                && count * size * 100 >= tokens.Length * 55
                && (sourceRepeatRun < 3 || count > sourceRepeatRun + 1)))
            {
                return "REPEATED_PHRASE";
            }
        }

        return null;
    }

    private static bool ContainsTerm(string text, string term)
    {
        var escaped = Regex.Escape(term.Trim()).Replace("\\ ", @"\s+");
        if (escaped.Length == 0)
        {
            return false;
        }

        var startsWord = char.IsLetterOrDigit(term.Trim()[0]);
        var endsWord = char.IsLetterOrDigit(term.Trim()[^1]);
        var pattern = $"{(startsWord ? @"(?<![\p{L}\p{M}\p{N}])" : string.Empty)}{escaped}{(endsWord ? @"(?![\p{L}\p{M}\p{N}])" : string.Empty)}";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static int FindMaximumIntentionalRepeatRun(string source)
    {
        var normalized = WhitespaceRegex().Replace(source, " ").Trim().ToLowerInvariant();
        var sourceTokens = WordRegex().Matches(normalized).Select(match => match.Value).ToArray();
        var maximum = 1;
        var current = 1;
        for (var index = 1; index < sourceTokens.Length; index++)
        {
            current = sourceTokens[index] == sourceTokens[index - 1] ? current + 1 : 1;
            maximum = Math.Max(maximum, current);
        }

        var maximumHanRun = 1;
        var currentHanRun = 1;
        char? previousHan = null;
        foreach (var character in normalized.Where(IsHanCharacter))
        {
            currentHanRun = previousHan == character ? currentHanRun + 1 : 1;
            previousHan = character;
            maximumHanRun = Math.Max(maximumHanRun, currentHanRun);
        }

        return Math.Max(maximum, maximumHanRun);
    }

    private static bool IsHanCharacter(char character) => character is
        >= '\u3400' and <= '\u4DBF'
        or >= '\u4E00' and <= '\u9FFF'
        or >= '\uF900' and <= '\uFAFF';

    private static string LimitCode(string value)
    {
        var normalized = WhitespaceRegex().Replace(value.Trim(), "_");
        return normalized.Length <= 64 ? normalized : normalized[..64];
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[\p{L}\p{M}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"\d+(?:[.,]\d+)*", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();
}
