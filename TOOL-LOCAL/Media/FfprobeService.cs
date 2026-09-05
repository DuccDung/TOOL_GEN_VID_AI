using System.Globalization;
using System.Text.Json;

namespace TOOL_LOCAL.Media;

public sealed record MediaProbeResult(
    decimal DurationSeconds,
    int? Width,
    int? Height,
    decimal? FramesPerSecond,
    string? VideoCodec,
    string? AudioCodec,
    int? AudioSampleRate,
    bool HasVideo,
    bool HasAudio,
    int RotationDegrees = 0);

public sealed class FfprobeService(string ffprobePath, IExternalProcessRunner processRunner)
{
    public async Task<MediaProbeResult> ProbeAsync(string mediaPath, CancellationToken cancellationToken = default)
    {
        var absolutePath = Path.GetFullPath(mediaPath);
        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException("Không tìm thấy media cần kiểm tra.", absolutePath);
        }

        ProcessExecutionResult result;
        try
        {
            result = await processRunner.RunAsync(
                ffprobePath,
                ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", absolutePath],
                TimeSpan.FromMinutes(1),
                cancellationToken);
        }
        catch (FileNotFoundException exception)
        {
            throw new MediaToolUnavailableException(
                "ffprobe_not_found",
                "Chưa tìm thấy FFprobe. Hãy cài hoặc cấu hình bộ FFmpeg rồi kiểm tra lại.",
                exception);
        }
        if (result.ExitCode != 0)
        {
            throw new InvalidDataException($"ffprobe không đọc được media: {TrimError(result.StandardError)}");
        }

        return ParseOutput(result.StandardOutput);
    }

    internal static MediaProbeResult ParseOutput(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var streams = root.TryGetProperty("streams", out var streamArray)
            ? streamArray.EnumerateArray().ToArray()
            : [];
        var video = streams.FirstOrDefault(x => GetString(x, "codec_type") == "video");
        var audio = streams.FirstOrDefault(x => GetString(x, "codec_type") == "audio");
        var duration = root.TryGetProperty("format", out var format)
            ? ParseDecimal(GetString(format, "duration")) ?? 0
            : 0;

        return new MediaProbeResult(
            duration,
            GetInt32(video, "width"),
            GetInt32(video, "height"),
            ParseFrameRate(GetString(video, "avg_frame_rate")),
            GetString(video, "codec_name"),
            GetString(audio, "codec_name"),
            ParseInt32(GetString(audio, "sample_rate")),
            video.ValueKind != JsonValueKind.Undefined,
            audio.ValueKind != JsonValueKind.Undefined,
            GetRotationDegrees(video));
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind != JsonValueKind.Undefined && element.TryGetProperty(property, out var value)
            ? value.GetString()
            : null;

    private static int? GetInt32(JsonElement element, string property) =>
        element.ValueKind != JsonValueKind.Undefined && element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result)
            ? result
            : null;

    private static int GetRotationDegrees(JsonElement video)
    {
        if (video.ValueKind == JsonValueKind.Undefined)
        {
            return 0;
        }

        double? rotation = null;
        if (video.TryGetProperty("side_data_list", out var sideData)
            && sideData.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in sideData.EnumerateArray())
            {
                if (item.TryGetProperty("rotation", out var value))
                {
                    rotation = value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
                        ? number
                        : double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
                            ? number
                            : null;
                    if (rotation is not null)
                    {
                        break;
                    }
                }
            }
        }
        if (rotation is null
            && video.TryGetProperty("tags", out var tags)
            && tags.TryGetProperty("rotate", out var tagValue))
        {
            rotation = double.TryParse(
                tagValue.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var number)
                    ? number
                    : null;
        }

        var rounded = (int)Math.Round(rotation ?? 0, MidpointRounding.AwayFromZero);
        var normalized = ((rounded % 360) + 360) % 360;
        return normalized switch
        {
            < 45 => 0,
            < 135 => 90,
            < 225 => 180,
            < 315 => 270,
            _ => 0
        };
    }

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static int? ParseInt32(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;

    private static decimal? ParseFrameRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split('/');
        if (parts.Length == 2 &&
            ParseDecimal(parts[0]) is { } numerator &&
            ParseDecimal(parts[1]) is { } denominator && denominator != 0)
        {
            return numerator / denominator;
        }

        return ParseDecimal(value);
    }

    private static string TrimError(string error) =>
        error.Length <= 2000 ? error.Trim() : error[..2000].Trim();
}
