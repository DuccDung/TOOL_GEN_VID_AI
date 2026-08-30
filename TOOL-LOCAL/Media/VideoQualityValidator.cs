using TOOL_LOCAL.AI.Contracts;

namespace TOOL_LOCAL.Media;

public sealed class VideoQualityValidator
{
    public QualityReportContract Validate(
        MediaProbeResult media,
        decimal expectedDurationSeconds,
        int expectedWidth,
        int expectedHeight,
        decimal expectedFramesPerSecond,
        decimal durationToleranceSeconds = 0.5m)
    {
        var issues = new List<QualityIssueContract>();
        if (!media.HasVideo)
        {
            issues.Add(Error("VIDEO_STREAM_MISSING", "Không tìm thấy video stream."));
        }

        if (Math.Abs(media.DurationSeconds - expectedDurationSeconds) > durationToleranceSeconds)
        {
            issues.Add(Error("DURATION_MISMATCH", $"Duration {media.DurationSeconds:F2}s không khớp {expectedDurationSeconds:F2}s."));
        }

        if (media.Width != expectedWidth || media.Height != expectedHeight)
        {
            issues.Add(Error("RESOLUTION_MISMATCH", $"Resolution {media.Width}x{media.Height} không khớp {expectedWidth}x{expectedHeight}."));
        }

        if (media.FramesPerSecond is null || Math.Abs(media.FramesPerSecond.Value - expectedFramesPerSecond) > 0.1m)
        {
            issues.Add(Error("FPS_MISMATCH", $"FPS {media.FramesPerSecond?.ToString("F2") ?? "unknown"} không hợp lệ."));
        }

        if (!string.Equals(media.VideoCodec, "h264", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new QualityIssueContract("CODEC_NON_STANDARD", "Warning", "Video chưa dùng H.264.", "Normalize bằng FFmpeg trước render."));
        }

        var errorCount = issues.Count(x => x.Severity == "Error");
        var warningCount = issues.Count - errorCount;
        var score = Math.Max(0, 100 - errorCount * 25 - warningCount * 5);
        return new QualityReportContract(score, errorCount == 0 && score >= 70, issues);
    }

    private static QualityIssueContract Error(string code, string message) =>
        new(code, "Error", message, "Regenerate hoặc normalize riêng scene bị lỗi.");
}
