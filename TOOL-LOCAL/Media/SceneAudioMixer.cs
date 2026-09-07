using System.Globalization;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_LOCAL.Media;

public sealed record SceneAudioMixResult(
    bool PreservedNativeAudio,
    AudioQualityResult OutputAudioQuality,
    MediaProbeResult OutputProbe,
    string MixStrategy = SpeechMixStrategies.ReplaceAllNativeAudio,
    decimal VoiceDurationSeconds = 0m,
    decimal TargetDurationSeconds = 0m,
    decimal AppliedTempo = 1m,
    decimal TrailingPaddingSeconds = 0m,
    long? SourceSpeechStartMs = null,
    long? SourceSpeechEndMs = null,
    long TargetSpeechStartMs = 0,
    decimal EffectiveVoiceDurationSeconds = 0m);

public sealed class SceneAudioMixerOptions
{
    public decimal MaximumTempoAdjustmentRatio { get; init; } = 0.05m;

    public decimal MinimumVoiceDurationRatio { get; init; } = 0.20m;

    public decimal TargetLoudnessLufs { get; init; } = -16m;

    public int TargetSpeechLeadInMs { get; init; } = 150;

    public int SpeechBoundaryPaddingMs { get; init; } = 80;
}

public sealed class SpeechDurationOutOfRangeException(
    decimal voiceDurationSeconds,
    decimal targetDurationSeconds,
    decimal maximumTempoAdjustmentRatio,
    decimal? minimumVoiceDurationRatio = null)
    : Exception(
        minimumVoiceDurationRatio.HasValue
            ? $"Lời nói chỉ dài {voiceDurationSeconds:0.##} giây trong cảnh {targetDurationSeconds:0.##} giây; " +
              $"thấp hơn tỷ lệ tối thiểu {minimumVoiceDurationRatio.Value:P0}. Hãy kiểm tra lời thoại, tách cảnh hoặc chọn thời lượng video phù hợp hơn."
            : $"Lời nói dài {voiceDurationSeconds:0.##} giây nhưng cảnh chỉ có {targetDurationSeconds:0.##} giây; " +
              $"vượt ngưỡng điều chỉnh tempo {maximumTempoAdjustmentRatio:P0}. Hãy rút gọn lời, tách cảnh hoặc chọn thời lượng video dài hơn.")
{
    public const string ErrorCode = "speech_duration_out_of_range";

    public decimal VoiceDurationSeconds { get; } = voiceDurationSeconds;

    public decimal TargetDurationSeconds { get; } = targetDurationSeconds;

    public decimal? MinimumVoiceDurationRatio { get; } = minimumVoiceDurationRatio;
}

public sealed class SceneAudioMixer(
    string ffmpegPath,
    IExternalProcessRunner processRunner,
    FfprobeService mediaProbe,
    AudioQualityValidator audioQualityValidator,
    SceneAudioMixerOptions? options = null)
{
    private readonly SceneAudioMixerOptions _options = options ?? new SceneAudioMixerOptions();

    public void ValidateVoiceDuration(
        decimal voiceDurationSeconds,
        decimal targetDurationSeconds,
        long? sourceSpeechStartMs = null,
        long? sourceSpeechEndMs = null)
    {
        if (voiceDurationSeconds <= 0 || targetDurationSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(voiceDurationSeconds));
        }
        if (_options.MaximumTempoAdjustmentRatio is < 0m or > 0.25m)
        {
            throw new InvalidOperationException("Ngưỡng điều chỉnh tempo của mixer không hợp lệ.");
        }
        if (_options.MinimumVoiceDurationRatio is < 0.05m or > 1m)
        {
            throw new InvalidOperationException("Tỷ lệ thời lượng giọng tối thiểu của mixer không hợp lệ.");
        }
        if (_options.TargetSpeechLeadInMs is < 0 or > 2000 ||
            _options.SpeechBoundaryPaddingMs is < 0 or > 500)
        {
            throw new InvalidOperationException("Cấu hình cửa sổ thời gian lời nói của mixer không hợp lệ.");
        }
        var window = ResolveSpeechWindow(
            voiceDurationSeconds,
            targetDurationSeconds,
            sourceSpeechStartMs,
            sourceSpeechEndMs);
        if (window.EffectiveDurationSeconds / window.AvailableDurationSeconds < _options.MinimumVoiceDurationRatio)
        {
            throw new SpeechDurationOutOfRangeException(
                window.EffectiveDurationSeconds,
                window.AvailableDurationSeconds,
                _options.MaximumTempoAdjustmentRatio,
                _options.MinimumVoiceDurationRatio);
        }
        var maximumTempo = 1m + _options.MaximumTempoAdjustmentRatio;
        if (window.EffectiveDurationSeconds / window.AvailableDurationSeconds > maximumTempo)
        {
            throw new SpeechDurationOutOfRangeException(
                window.EffectiveDurationSeconds,
                window.AvailableDurationSeconds,
                _options.MaximumTempoAdjustmentRatio);
        }
    }

    public async Task<SceneAudioMixResult> MixAsync(
        string videoPath,
        string voicePath,
        string outputPath,
        decimal targetDurationSeconds,
        CancellationToken cancellationToken = default,
        string mixStrategy = SpeechMixStrategies.ReplaceAllNativeAudio,
        bool nativeAmbienceVerifiedNoSpeech = false,
        long? sourceSpeechStartMs = null,
        long? sourceSpeechEndMs = null)
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

        var window = ResolveSpeechWindow(
            voice.DurationSeconds,
            targetDurationSeconds,
            sourceSpeechStartMs,
            sourceSpeechEndMs);
        var tempo = window.EffectiveDurationSeconds > window.AvailableDurationSeconds
            ? window.EffectiveDurationSeconds / window.AvailableDurationSeconds
            : 1m;
        var renderedVoiceDuration = window.EffectiveDurationSeconds / tempo;
        var trailingPaddingSeconds = Math.Max(
            0m,
            targetDurationSeconds - window.DelaySeconds - renderedVoiceDuration);
        ValidateVoiceDuration(
            voice.DurationSeconds,
            targetDurationSeconds,
            sourceSpeechStartMs,
            sourceSpeechEndMs);

        if (mixStrategy is not (SpeechMixStrategies.ReplaceAllNativeAudio or SpeechMixStrategies.MixWithVerifiedAmbience))
        {
            throw new ArgumentException("Chiến lược ghép audio không được hỗ trợ.", nameof(mixStrategy));
        }
        if (mixStrategy == SpeechMixStrategies.MixWithVerifiedAmbience && !nativeAmbienceVerifiedNoSpeech)
        {
            throw new InvalidOperationException("Chỉ được giữ ambience sau khi đã xác minh native track không có lời người.");
        }
        var nativeQuality = mixStrategy == SpeechMixStrategies.MixWithVerifiedAmbience && video.HasAudio
            ? await audioQualityValidator.AnalyzeAsync(videoPath, cancellationToken)
            : new AudioQualityResult(false, false, null, null, 1m, "native_audio_replaced", "Native audio được thay hoàn toàn.");
        var preserveNative = mixStrategy == SpeechMixStrategies.MixWithVerifiedAmbience &&
                             nativeAmbienceVerifiedNoSpeech &&
                             nativeQuality.IsAudible;
        var target = Invariant(targetDurationSeconds);
        var voiceChain = window.HasDetectedSpeechWindow
            ? $"atrim=start={Invariant(window.TrimStartSeconds)}:end={Invariant(window.TrimEndSeconds)},asetpts=PTS-STARTPTS,"
            : string.Empty;
        voiceChain += tempo > 1.001m
            ? $"atempo={Invariant(tempo)},"
            : string.Empty;
        voiceChain += $"aformat=sample_rates=48000:channel_layouts=stereo,loudnorm=I={Invariant(_options.TargetLoudnessLufs)}:LRA=11:TP=-1.5,";
        if (window.DelayMilliseconds > 0)
        {
            voiceChain += $"adelay=delays={window.DelayMilliseconds}:all=1,";
        }
        voiceChain += $"apad,atrim=duration={target}";
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
        return new SceneAudioMixResult(
            preserveNative,
            outputQuality,
            outputProbe,
            mixStrategy,
            voice.DurationSeconds,
            targetDurationSeconds,
            tempo,
            trailingPaddingSeconds,
            sourceSpeechStartMs,
            sourceSpeechEndMs,
            window.TargetSpeechStartMs,
            window.EffectiveDurationSeconds);
    }

    private SpeechWindow ResolveSpeechWindow(
        decimal voiceDurationSeconds,
        decimal targetDurationSeconds,
        long? sourceSpeechStartMs,
        long? sourceSpeechEndMs)
    {
        if (sourceSpeechStartMs is null && sourceSpeechEndMs is null)
        {
            return new SpeechWindow(
                false,
                0m,
                voiceDurationSeconds,
                voiceDurationSeconds,
                targetDurationSeconds,
                0,
                0,
                0m);
        }
        if (sourceSpeechStartMs is not { } speechStartMs ||
            sourceSpeechEndMs is not { } speechEndMs ||
            speechStartMs < 0 ||
            speechEndMs <= speechStartMs ||
            speechEndMs > checked((long)Math.Ceiling(voiceDurationSeconds * 1000m)) + 750)
        {
            throw new InvalidDataException("Mốc thời gian ASR của giọng đọc không hợp lệ.");
        }

        var voiceDurationMs = checked((long)Math.Round(voiceDurationSeconds * 1000m));
        var trimStartMs = Math.Max(0, speechStartMs - _options.SpeechBoundaryPaddingMs);
        var trimEndMs = Math.Min(voiceDurationMs, speechEndMs + _options.SpeechBoundaryPaddingMs);
        if (trimEndMs <= trimStartMs)
        {
            throw new InvalidDataException("Cửa sổ thời gian lời nói sau khi thêm biên an toàn không hợp lệ.");
        }
        var preSpeechMs = speechStartMs - trimStartMs;
        var effectiveDurationSeconds = (trimEndMs - trimStartMs) / 1000m;
        var targetDurationMs = targetDurationSeconds * 1000m;
        decimal exactDelayMs = Math.Max(0, _options.TargetSpeechLeadInMs - preSpeechMs);
        var initialAvailableDurationSeconds = targetDurationSeconds - exactDelayMs / 1000m;
        if (effectiveDurationSeconds > initialAvailableDurationSeconds && trimEndMs - trimStartMs > preSpeechMs)
        {
            // atempo also compresses the preserved pre-speech boundary. Solve the delay so the
            // detected first word still lands on TargetSpeechLeadInMs after tempo adjustment.
            exactDelayMs = Math.Max(
                0m,
                (_options.TargetSpeechLeadInMs * (decimal)(trimEndMs - trimStartMs) -
                 preSpeechMs * targetDurationMs) /
                (trimEndMs - trimStartMs - preSpeechMs));
        }
        var delayMs = checked((int)Math.Round(exactDelayMs, MidpointRounding.AwayFromZero));
        var availableDurationSeconds = targetDurationSeconds - delayMs / 1000m;
        if (availableDurationSeconds <= 0)
        {
            throw new SpeechDurationOutOfRangeException(
                effectiveDurationSeconds,
                targetDurationSeconds,
                _options.MaximumTempoAdjustmentRatio);
        }
        var calculatedTempo = effectiveDurationSeconds > availableDurationSeconds
            ? effectiveDurationSeconds / availableDurationSeconds
            : 1m;
        var targetSpeechStartMs = delayMs + checked((long)Math.Round(
            preSpeechMs / calculatedTempo,
            MidpointRounding.AwayFromZero));
        return new SpeechWindow(
            true,
            trimStartMs / 1000m,
            trimEndMs / 1000m,
            effectiveDurationSeconds,
            availableDurationSeconds,
            delayMs,
            targetSpeechStartMs,
            delayMs / 1000m);
    }

    private sealed record SpeechWindow(
        bool HasDetectedSpeechWindow,
        decimal TrimStartSeconds,
        decimal TrimEndSeconds,
        decimal EffectiveDurationSeconds,
        decimal AvailableDurationSeconds,
        int DelayMilliseconds,
        long TargetSpeechStartMs,
        decimal DelaySeconds);

    private static string Invariant(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
