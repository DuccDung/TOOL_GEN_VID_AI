namespace TOOL_LOCAL.Media;

public sealed class SpeechAudioExtractor(
    string ffmpegPath,
    IExternalProcessRunner processRunner,
    FfprobeService mediaProbe)
{
    public async Task<MediaProbeResult> ExtractAsync(
        string sourcePath,
        string outputWavPath,
        CancellationToken cancellationToken = default)
    {
        var source = Path.GetFullPath(sourcePath);
        var output = Path.GetFullPath(outputWavPath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Không tìm thấy media nguồn để tách lời nói.", source);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var result = await processRunner.RunAsync(
            ffmpegPath,
            [
                "-y", "-i", source,
                "-map", "0:a:0", "-vn",
                "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le",
                "-f", "wav", output
            ],
            TimeSpan.FromMinutes(5),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            var error = result.StandardError.Length <= 2000
                ? result.StandardError
                : result.StandardError[..2000];
            throw new InvalidDataException($"FFmpeg không tách được lời nói từ media: {error.Trim()}");
        }

        var probe = await mediaProbe.ProbeAsync(output, cancellationToken);
        if (!probe.HasAudio || probe.DurationSeconds <= 0)
        {
            throw new InvalidDataException("Audio dùng để kiểm tra lời nói không hợp lệ.");
        }
        return probe;
    }
}
