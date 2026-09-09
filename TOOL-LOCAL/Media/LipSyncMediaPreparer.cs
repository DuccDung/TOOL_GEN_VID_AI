using System.Globalization;
using System.Security.Cryptography;

namespace TOOL_LOCAL.Media;

public sealed record LipSyncPreparedInputs(
    string VideoPath,
    string AudioPath,
    string VideoSha256,
    string AudioSha256,
    long VideoSizeBytes,
    long AudioSizeBytes,
    long DurationMs);

public sealed record LipSyncFinalizedOutput(
    MediaProbeResult Probe,
    AudioQualityResult AudioQuality,
    string Sha256,
    long SizeBytes,
    long DurationMs);

public sealed class LipSyncMediaPreparer(
    string ffmpegPath,
    IExternalProcessRunner processRunner,
    FfprobeService mediaProbe,
    AudioQualityValidator audioQualityValidator,
    SceneAudioMixer sceneAudioMixer)
{
    public async Task<LipSyncPreparedInputs> PrepareAsync(
        string sourceVideoPath,
        string sourceVoicePath,
        string preparedVideoPath,
        string preparedAudioPath,
        long targetDurationMs,
        CancellationToken cancellationToken = default)
    {
        if (targetDurationMs is < 1000 or > 120_000)
        {
            throw new ArgumentOutOfRangeException(nameof(targetDurationMs));
        }
        var sourceVideo = await mediaProbe.ProbeAsync(sourceVideoPath, cancellationToken);
        var sourceVoice = await mediaProbe.ProbeAsync(sourceVoicePath, cancellationToken);
        if (!sourceVideo.HasVideo)
        {
            throw new InvalidDataException("The approved base clip has no video stream.");
        }
        if (!sourceVoice.HasAudio || sourceVoice.DurationSeconds <= 0)
        {
            throw new InvalidDataException("The approved canonical WAV has no audio stream.");
        }
        var targetSeconds = targetDurationMs / 1000m;
        sceneAudioMixer.ValidateVoiceDuration(sourceVoice.DurationSeconds, targetSeconds);

        var absoluteVideo = Path.GetFullPath(preparedVideoPath);
        var absoluteAudio = Path.GetFullPath(preparedAudioPath);
        Directory.CreateDirectory(Path.GetDirectoryName(absoluteVideo)!);
        Directory.CreateDirectory(Path.GetDirectoryName(absoluteAudio)!);
        var videoPartial = absoluteVideo + ".part";
        var audioPartial = absoluteAudio + ".part";
        DeleteIfExists(videoPartial);
        DeleteIfExists(audioPartial);
        try
        {
            await RunFfmpegAsync(
                [
                    "-y", "-i", Path.GetFullPath(sourceVideoPath),
                    "-map", "0:v:0", "-an", "-c:v", "copy",
                    "-t", Invariant(targetSeconds), "-movflags", "+faststart", "-f", "mp4", videoPartial
                ],
                "Could not prepare the silent lip-sync input video.",
                cancellationToken);

            var tempo = sourceVoice.DurationSeconds > targetSeconds
                ? sourceVoice.DurationSeconds / targetSeconds
                : 1m;
            var audioFilter = tempo > 1.001m
                ? $"atempo={Invariant(tempo)},"
                : string.Empty;
            audioFilter += $"aformat=sample_rates=48000:channel_layouts=stereo,loudnorm=I=-16:LRA=11:TP=-1.5,apad,atrim=duration={Invariant(targetSeconds)}";
            await RunFfmpegAsync(
                [
                    "-y", "-i", Path.GetFullPath(sourceVoicePath),
                    "-map", "0:a:0", "-af", audioFilter,
                    "-c:a", "pcm_s16le", "-ar", "48000", "-ac", "2", "-f", "wav", audioPartial
                ],
                "Could not prepare the canonical WAV for lip-sync.",
                cancellationToken);

            await ValidatePreparedAsync(videoPartial, audioPartial, targetSeconds, cancellationToken);
            File.Move(videoPartial, absoluteVideo, true);
            File.Move(audioPartial, absoluteAudio, true);
            return new LipSyncPreparedInputs(
                absoluteVideo,
                absoluteAudio,
                await ComputeSha256Async(absoluteVideo, cancellationToken),
                await ComputeSha256Async(absoluteAudio, cancellationToken),
                new FileInfo(absoluteVideo).Length,
                new FileInfo(absoluteAudio).Length,
                targetDurationMs);
        }
        catch
        {
            DeleteIfExists(videoPartial);
            DeleteIfExists(audioPartial);
            throw;
        }
    }

    public async Task<LipSyncFinalizedOutput> FinalizeAsync(
        string providerVideoPath,
        string preparedAudioPath,
        string outputPath,
        long targetDurationMs,
        CancellationToken cancellationToken = default)
    {
        var targetSeconds = targetDurationMs / 1000m;
        if (!HasIsoBaseMediaSignature(providerVideoPath) || !HasWaveSignature(preparedAudioPath))
        {
            throw new InvalidDataException("The downloaded lip-sync media has an invalid MP4 or WAV signature.");
        }
        var providerProbe = await mediaProbe.ProbeAsync(providerVideoPath, cancellationToken);
        var audioProbe = await mediaProbe.ProbeAsync(preparedAudioPath, cancellationToken);
        if (!providerProbe.HasVideo || !audioProbe.HasAudio)
        {
            throw new InvalidDataException("The provider video or prepared WAV is missing its required stream.");
        }
        var absoluteOutput = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput)!);
        var partialPath = absoluteOutput + ".part";
        DeleteIfExists(partialPath);
        try
        {
            await RunFfmpegAsync(
                [
                    "-y", "-i", Path.GetFullPath(providerVideoPath), "-i", Path.GetFullPath(preparedAudioPath),
                    "-map", "0:v:0", "-map", "1:a:0", "-c:v", "copy",
                    "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2",
                    "-t", Invariant(targetSeconds), "-movflags", "+faststart", "-f", "mp4", partialPath
                ],
                "Could not replace provider audio with the canonical WAV.",
                cancellationToken);
            var probe = await mediaProbe.ProbeAsync(partialPath, cancellationToken);
            if (!HasIsoBaseMediaSignature(partialPath) || !probe.HasVideo || !probe.HasAudio ||
                probe.DurationSeconds < targetSeconds - 0.25m ||
                probe.DurationSeconds > targetSeconds + 0.50m)
            {
                throw new InvalidDataException("The finalized lip-sync clip does not match the required streams or duration.");
            }
            var quality = await audioQualityValidator.RequireAudibleAsync(
                partialPath,
                "The finalized lip-sync clip has no audible canonical voice",
                cancellationToken);
            File.Move(partialPath, absoluteOutput, true);
            return new LipSyncFinalizedOutput(
                probe,
                quality,
                await ComputeSha256Async(absoluteOutput, cancellationToken),
                new FileInfo(absoluteOutput).Length,
                checked((long)Math.Round(probe.DurationSeconds * 1000m, MidpointRounding.AwayFromZero)));
        }
        catch
        {
            DeleteIfExists(partialPath);
            throw;
        }
    }

    private async Task ValidatePreparedAsync(
        string videoPath,
        string audioPath,
        decimal targetSeconds,
        CancellationToken cancellationToken)
    {
        var video = await mediaProbe.ProbeAsync(videoPath, cancellationToken);
        var audio = await mediaProbe.ProbeAsync(audioPath, cancellationToken);
        if (!HasIsoBaseMediaSignature(videoPath) || !HasWaveSignature(audioPath) ||
            !video.HasVideo || video.HasAudio || !audio.HasAudio || audio.HasVideo ||
            video.DurationSeconds < targetSeconds - 0.25m || video.DurationSeconds > targetSeconds + 0.50m ||
            audio.DurationSeconds < targetSeconds - 0.10m || audio.DurationSeconds > targetSeconds + 0.10m ||
            audio.AudioSampleRate != 48_000)
        {
            throw new InvalidDataException("The prepared lip-sync MP4/WAV does not match the required streams, sample rate, or duration.");
        }
        await audioQualityValidator.RequireAudibleAsync(
            audioPath,
            "The prepared lip-sync WAV has no audible voice",
            cancellationToken);
    }

    private async Task RunFfmpegAsync(
        IReadOnlyList<string> arguments,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(ffmpegPath, arguments, TimeSpan.FromMinutes(20), cancellationToken);
        if (result.ExitCode != 0)
        {
            var detail = result.StandardError.Length <= 4000 ? result.StandardError : result.StandardError[..4000];
            throw new InvalidDataException($"{errorMessage} {detail.Trim()}");
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static string Invariant(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool HasIsoBaseMediaSignature(string path)
    {
        Span<byte> header = stackalloc byte[12];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return stream.Read(header) == header.Length &&
               header[4] == (byte)'f' && header[5] == (byte)'t' &&
               header[6] == (byte)'y' && header[7] == (byte)'p';
    }

    private static bool HasWaveSignature(string path)
    {
        Span<byte> header = stackalloc byte[12];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return stream.Read(header) == header.Length &&
               header[..4].SequenceEqual("RIFF"u8) &&
               header[8..12].SequenceEqual("WAVE"u8);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
