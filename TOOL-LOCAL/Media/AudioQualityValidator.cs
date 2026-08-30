using System.Globalization;
using System.Text.RegularExpressions;

namespace TOOL_LOCAL.Media;

public sealed record AudioQualityResult(
    bool HasAudioStream,
    bool IsAudible,
    decimal? MeanVolumeDb,
    decimal? MaxVolumeDb,
    decimal SilentRatio,
    string? FailureCode,
    string? FailureMessage);

public sealed partial class AudioQualityValidator(
    string ffmpegPath,
    IExternalProcessRunner processRunner,
    FfprobeService mediaProbe)
{
    internal const decimal MinimumMeanVolumeDb = -55m;
    internal const decimal MinimumMaxVolumeDb = -45m;
    internal const decimal MaximumSilentRatio = 0.98m;

    public async Task<AudioQualityResult> AnalyzeAsync(
        string mediaPath,
        CancellationToken cancellationToken = default)
    {
        var absolutePath = Path.GetFullPath(mediaPath);
        var probe = await mediaProbe.ProbeAsync(absolutePath, cancellationToken);
        if (!probe.HasAudio)
        {
            return Invalid("audio_stream_missing", "Media không có audio stream.");
        }
        if (probe.DurationSeconds <= 0)
        {
            return Invalid("audio_duration_invalid", "Không xác định được thời lượng audio.");
        }

        ProcessExecutionResult result;
        try
        {
            result = await processRunner.RunAsync(
                ffmpegPath,
                [
                    "-hide_banner", "-nostats", "-i", absolutePath,
                    "-map", "0:a:0", "-af", "silencedetect=noise=-50dB:d=0.5,volumedetect",
                    "-f", "null", "NUL"
                ],
                TimeSpan.FromMinutes(3),
                cancellationToken);
        }
        catch (FileNotFoundException exception)
        {
            throw new MediaToolUnavailableException(
                "ffmpeg_not_found",
                "Chưa tìm thấy FFmpeg để kiểm tra chất lượng âm thanh.",
                exception);
        }
        if (result.ExitCode != 0)
        {
            throw new InvalidDataException($"FFmpeg không phân tích được âm thanh: {TrimError(result.StandardError)}");
        }

        return ParseDiagnostics(result.StandardError, probe.DurationSeconds);
    }

    public async Task<AudioQualityResult> RequireAudibleAsync(
        string mediaPath,
        string failureMessage,
        CancellationToken cancellationToken = default)
    {
        var quality = await AnalyzeAsync(mediaPath, cancellationToken);
        if (!quality.IsAudible)
        {
            throw new InvalidDataException($"{failureMessage} ({quality.FailureCode}: {quality.FailureMessage})");
        }
        return quality;
    }

    internal static AudioQualityResult ParseDiagnostics(string diagnostics, decimal durationSeconds)
    {
        var meanVolume = ParseLastVolume(MeanVolumeRegex(), diagnostics);
        var maxVolume = ParseLastVolume(MaxVolumeRegex(), diagnostics);
        var silentSeconds = SilenceDurationRegex().Matches(diagnostics)
            .Select(match => ParseDecimal(match.Groups[1].Value) ?? 0)
            .Sum();
        var silentRatio = durationSeconds <= 0
            ? 1m
            : Math.Clamp(silentSeconds / durationSeconds, 0m, 1m);

        if (meanVolume is null || maxVolume is null)
        {
            return Invalid("audio_levels_unavailable", "Không đọc được mức âm lượng từ media.", true, meanVolume, maxVolume, silentRatio);
        }
        if (maxVolume <= MinimumMaxVolumeDb || meanVolume <= MinimumMeanVolumeDb || silentRatio >= MaximumSilentRatio)
        {
            return Invalid(
                "audio_effectively_silent",
                "Audio stream tồn tại nhưng âm lượng gần như im lặng.",
                true,
                meanVolume,
                maxVolume,
                silentRatio);
        }
        return new AudioQualityResult(true, true, meanVolume, maxVolume, silentRatio, null, null);
    }

    private static AudioQualityResult Invalid(
        string code,
        string message,
        bool hasAudioStream = false,
        decimal? meanVolume = null,
        decimal? maxVolume = null,
        decimal silentRatio = 1m) =>
        new(hasAudioStream, false, meanVolume, maxVolume, silentRatio, code, message);

    private static decimal? ParseLastVolume(Regex regex, string value)
    {
        var matches = regex.Matches(value);
        if (matches.Count == 0)
        {
            return null;
        }
        var raw = matches[^1].Groups[1].Value;
        return string.Equals(raw, "-inf", StringComparison.OrdinalIgnoreCase)
            ? decimal.MinValue
            : ParseDecimal(raw);
    }

    private static decimal? ParseDecimal(string value) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static string TrimError(string error) =>
        error.Length <= 2000 ? error.Trim() : error[..2000].Trim();

    [GeneratedRegex(@"mean_volume:\s*(-inf|-?\d+(?:\.\d+)?)\s*dB", RegexOptions.IgnoreCase)]
    private static partial Regex MeanVolumeRegex();

    [GeneratedRegex(@"max_volume:\s*(-inf|-?\d+(?:\.\d+)?)\s*dB", RegexOptions.IgnoreCase)]
    private static partial Regex MaxVolumeRegex();

    [GeneratedRegex(@"silence_duration:\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex SilenceDurationRegex();
}
