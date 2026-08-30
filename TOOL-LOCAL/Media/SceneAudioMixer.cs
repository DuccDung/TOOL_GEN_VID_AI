using System.Globalization;

namespace TOOL_LOCAL.Media;

public sealed record SceneAudioMixResult(
    bool PreservedNativeAudio,
    AudioQualityResult OutputAudioQuality,
    MediaProbeResult OutputProbe);

public sealed class SceneAudioMixer(
    string ffmpegPath,
    IExternalProcessRunner processRunner,
    FfprobeService mediaProbe,
    AudioQualityValidator audioQualityValidator)
{
    public async Task<SceneAudioMixResult> MixAsync(
        string videoPath,
        string voicePath,
        string outputPath,
        decimal targetDurationSeconds,
        CancellationToken cancellationToken = default)
    {
        if (targetDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetDurationSeconds));
        }
        var video = await mediaProbe.ProbeAsync(videoPath, cancellationToken);
        if (!video.HasVideo)
        {
            throw new InvalidDataException("Clip Kling không có video stream hợp lệ.");
        }
        var voice = await mediaProbe.ProbeAsync(voicePath, cancellationToken);
        await audioQualityValidator.RequireAudibleAsync(
            voicePath,
            "Giọng đọc tạo ra không nghe được",
            cancellationToken);
        if (!voice.HasAudio || voice.DurationSeconds <= 0)
        {
            throw new InvalidDataException("Giọng đọc không có audio stream hoặc thời lượng hợp lệ.");
        }

        var tempo = voice.DurationSeconds > targetDurationSeconds
            ? voice.DurationSeconds / targetDurationSeconds
            : 1m;
        if (tempo > 2m)
        {
            throw new InvalidDataException("Lời đọc dài hơn quá nhiều so với thời lượng cảnh. Hãy rút gọn lời đọc.");
        }

        var nativeQuality = video.HasAudio
            ? await audioQualityValidator.AnalyzeAsync(videoPath, cancellationToken)
            : new AudioQualityResult(false, false, null, null, 1m, "audio_stream_missing", "Clip Kling không có audio stream.");
        var preserveNative = nativeQuality.IsAudible;
        var target = Invariant(targetDurationSeconds);
        var voiceChain = tempo > 1.001m
            ? $"atempo={Invariant(tempo)},"
            : string.Empty;
        voiceChain += $"aformat=sample_rates=48000:channel_layouts=stereo,apad,atrim=duration={target}";
        var filter = preserveNative
            ? $"[1:a:0]{voiceChain},volume=1.0,asplit=2[voice_mix][voice_sidechain];" +
              $"[0:a:0]aformat=sample_rates=48000:channel_layouts=stereo,volume=0.65,apad,atrim=duration={target}[native];" +
              "[native][voice_sidechain]sidechaincompress=threshold=0.02:ratio=8:attack=20:release=300[ducked];" +
              $"[ducked][voice_mix]amix=inputs=2:duration=longest:dropout_transition=0,alimiter=limit=0.95,atrim=duration={target}[aout]"
            : $"[1:a:0]{voiceChain},volume=1.0,alimiter=limit=0.95[aout]";

        var absoluteOutput = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput)!);
        var result = await processRunner.RunAsync(
            ffmpegPath,
            [
                "-y", "-i", Path.GetFullPath(videoPath), "-i", Path.GetFullPath(voicePath),
                "-filter_complex", filter,
                "-map", "0:v:0", "-map", "[aout]",
                "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2",
                "-t", target, "-movflags", "+faststart", "-f", "mp4", absoluteOutput
            ],
            TimeSpan.FromMinutes(20),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            var error = result.StandardError.Length <= 4000 ? result.StandardError : result.StandardError[..4000];
            throw new InvalidDataException($"FFmpeg không ghép được giọng đọc vào clip: {error.Trim()}");
        }

        var outputProbe = await mediaProbe.ProbeAsync(absoluteOutput, cancellationToken);
        if (!outputProbe.HasVideo || !outputProbe.HasAudio)
        {
            throw new InvalidDataException("Clip sau khi ghép không có đủ video và audio stream.");
        }
        var outputQuality = await audioQualityValidator.RequireAudibleAsync(
            absoluteOutput,
            "Clip sau khi ghép vẫn không nghe được",
            cancellationToken);
        return new SceneAudioMixResult(preserveNative, outputQuality, outputProbe);
    }

    private static string Invariant(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
