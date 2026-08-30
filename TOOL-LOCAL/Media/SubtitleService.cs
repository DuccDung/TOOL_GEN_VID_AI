using System.Text;

namespace TOOL_LOCAL.Media;

public sealed record SubtitleCue(int Sequence, TimeSpan Start, TimeSpan End, string Text);

public sealed class SubtitleService
{
    public string CreateSrt(IEnumerable<SubtitleCue> cues)
    {
        var ordered = cues.OrderBy(x => x.Sequence).ToArray();
        Validate(ordered);
        var builder = new StringBuilder();
        foreach (var cue in ordered)
        {
            builder.AppendLine(cue.Sequence.ToString());
            builder.Append(FormatSrtTime(cue.Start));
            builder.Append(" --> ");
            builder.AppendLine(FormatSrtTime(cue.End));
            builder.AppendLine(cue.Text.Trim());
            builder.AppendLine();
        }

        return builder.ToString();
    }

    public string CreateVtt(IEnumerable<SubtitleCue> cues)
    {
        var ordered = cues.OrderBy(x => x.Sequence).ToArray();
        Validate(ordered);
        var builder = new StringBuilder("WEBVTT\n\n");
        foreach (var cue in ordered)
        {
            builder.Append(FormatVttTime(cue.Start));
            builder.Append(" --> ");
            builder.AppendLine(FormatVttTime(cue.End));
            builder.AppendLine(cue.Text.Trim());
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void Validate(IReadOnlyList<SubtitleCue> cues)
    {
        TimeSpan? previousEnd = null;
        foreach (var cue in cues)
        {
            if (cue.Sequence <= 0 || cue.Start < TimeSpan.Zero || cue.End <= cue.Start || string.IsNullOrWhiteSpace(cue.Text))
            {
                throw new ArgumentException($"Subtitle cue {cue.Sequence} không hợp lệ.", nameof(cues));
            }

            if (previousEnd.HasValue && cue.Start < previousEnd.Value)
            {
                throw new ArgumentException($"Subtitle cue {cue.Sequence} bị chồng thời gian.", nameof(cues));
            }

            previousEnd = cue.End;
        }
    }

    private static string FormatSrtTime(TimeSpan value) =>
        $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00},{value.Milliseconds:000}";

    private static string FormatVttTime(TimeSpan value) =>
        $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
}
