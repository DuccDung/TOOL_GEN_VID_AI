using System.Text;
using TOOL_LOCAL.Vietsub.Domain;

namespace TOOL_LOCAL.Vietsub.Ocr;

internal sealed record VietsubOcrAccumulatorSnapshot(
    long LastTimestampMilliseconds,
    string? PendingText,
    long? PendingStartMilliseconds,
    long? PendingEndMilliseconds,
    float PendingConfidenceTotal,
    int PendingSamples);

internal sealed class VietsubOcrCueAccumulator
{
    private readonly int _interval;
    private readonly List<VietsubSubtitleCue> _completed = [];
    private PendingCue? _pending;
    private long _ignoreSamplesThroughMilliseconds = -1;

    public VietsubOcrCueAccumulator(int sampleIntervalMilliseconds)
    {
        _interval = Math.Clamp(sampleIntervalMilliseconds, 100, 5_000);
    }

    public long LastTimestampMilliseconds { get; private set; }

    public void Add(long timestampMilliseconds, string? text, float confidence)
    {
        if (timestampMilliseconds <= _ignoreSamplesThroughMilliseconds)
        {
            return;
        }
        LastTimestampMilliseconds = Math.Max(LastTimestampMilliseconds, timestampMilliseconds);
        var cleanText = NormalizeWhitespace(text);
        var normalized = NormalizeForComparison(cleanText);
        if (normalized.Length == 0 || confidence < 0.45f)
        {
            Flush();
            return;
        }
        if (_pending is not null && IsSimilar(_pending.NormalizedText, normalized))
        {
            _pending.EndMilliseconds = Math.Max(
                _pending.EndMilliseconds,
                timestampMilliseconds + _interval);
            _pending.Text = cleanText;
            _pending.NormalizedText = normalized;
            _pending.ConfidenceTotal += confidence;
            _pending.Samples++;
            return;
        }

        Flush();
        _pending = new PendingCue
        {
            StartMilliseconds = timestampMilliseconds,
            EndMilliseconds = timestampMilliseconds + _interval,
            Text = cleanText,
            NormalizedText = normalized,
            ConfidenceTotal = confidence,
            Samples = 1
        };
    }

    public IReadOnlyList<VietsubSubtitleCue> DrainCompleted()
    {
        var result = _completed.ToArray();
        _completed.Clear();
        return result;
    }

    public IReadOnlyList<VietsubSubtitleCue> Complete()
    {
        Flush();
        return DrainCompleted();
    }

    public VietsubOcrAccumulatorSnapshot Snapshot() => new(
        LastTimestampMilliseconds,
        _pending?.Text,
        _pending?.StartMilliseconds,
        _pending?.EndMilliseconds,
        _pending?.ConfidenceTotal ?? 0,
        _pending?.Samples ?? 0);

    public static VietsubOcrCueAccumulator Restore(
        int sampleIntervalMilliseconds,
        VietsubOcrAccumulatorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.LastTimestampMilliseconds < 0
            || !float.IsFinite(snapshot.PendingConfidenceTotal)
            || snapshot.PendingConfidenceTotal < 0
            || snapshot.PendingSamples < 0)
        {
            throw new ArgumentException("Checkpoint accumulator OCR không hợp lệ.", nameof(snapshot));
        }

        var accumulator = new VietsubOcrCueAccumulator(sampleIntervalMilliseconds)
        {
            LastTimestampMilliseconds = snapshot.LastTimestampMilliseconds,
            _ignoreSamplesThroughMilliseconds = snapshot.LastTimestampMilliseconds
        };
        var hasPendingValue = snapshot.PendingText is not null
            || snapshot.PendingStartMilliseconds.HasValue
            || snapshot.PendingEndMilliseconds.HasValue
            || snapshot.PendingConfidenceTotal > 0
            || snapshot.PendingSamples > 0;
        if (!hasPendingValue)
        {
            return accumulator;
        }

        var cleanText = NormalizeWhitespace(snapshot.PendingText);
        var normalized = NormalizeForComparison(cleanText);
        if (normalized.Length == 0
            || snapshot.PendingStartMilliseconds is not long start
            || snapshot.PendingEndMilliseconds is not long end
            || start < 0
            || end <= start
            || snapshot.PendingSamples < 1
            || snapshot.PendingConfidenceTotal <= 0
            || snapshot.PendingConfidenceTotal > snapshot.PendingSamples)
        {
            throw new ArgumentException("Checkpoint cue OCR đang chờ không hợp lệ.", nameof(snapshot));
        }

        accumulator._pending = new PendingCue
        {
            StartMilliseconds = start,
            EndMilliseconds = end,
            Text = cleanText,
            NormalizedText = normalized,
            ConfidenceTotal = snapshot.PendingConfidenceTotal,
            Samples = snapshot.PendingSamples
        };
        return accumulator;
    }

    internal static bool IsSimilar(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }
        var longest = Math.Max(left.Length, right.Length);
        return longest == 0 || Levenshtein(left, right) <= Math.Max(1, (int)Math.Ceiling(longest * 0.16));
    }

    internal static string NormalizeWhitespace(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    internal static string NormalizeForComparison(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }
        return builder.ToString().Trim();
    }

    private void Flush()
    {
        if (_pending is null)
        {
            return;
        }
        var averageConfidence = _pending.ConfidenceTotal / _pending.Samples;
        if (_pending.Samples >= 2 || averageConfidence >= 0.72f)
        {
            _completed.Add(new VietsubSubtitleCue
            {
                StartMilliseconds = _pending.StartMilliseconds,
                EndMilliseconds = _pending.EndMilliseconds,
                OriginalText = _pending.Text,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
        _pending = null;
    }

    private static int Levenshtein(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        var current = new int[right.Length + 1];
        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var cost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }

    private sealed class PendingCue
    {
        public long StartMilliseconds { get; set; }
        public long EndMilliseconds { get; set; }
        public string Text { get; set; } = string.Empty;
        public string NormalizedText { get; set; } = string.Empty;
        public float ConfidenceTotal { get; set; }
        public int Samples { get; set; }
    }
}
