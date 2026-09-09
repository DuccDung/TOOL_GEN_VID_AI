using TOOL_LOCAL.Media;

namespace TOOL_TESTS.Media;

public sealed class LipSyncMediaPreparerIntegrationTests
{
    [Fact]
    public async Task PrepareAndFinalizeAsync_StripsProviderAudioAndRestoresAudibleCanonicalWav()
    {
        var tools = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
        var ffmpeg = Path.Combine(tools, "ffmpeg.exe");
        var ffprobe = Path.Combine(tools, "ffprobe.exe");
        Assert.True(File.Exists(ffmpeg), "Test output must include the licensed FFmpeg bundle.");
        Assert.True(File.Exists(ffprobe), "Test output must include the licensed FFprobe bundle.");

        var root = Path.Combine(Path.GetTempPath(), $"videomaker-lipsync-media-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var sourceVideo = Path.Combine(root, "source-with-provider-audio.mp4");
            var sourceVoice = Path.Combine(root, "canonical.wav");
            var preparedVideo = Path.Combine(root, "prepared-silent.mp4");
            var preparedAudio = Path.Combine(root, "prepared-canonical.wav");
            var finalOutput = Path.Combine(root, "final-lipsync.mp4");
            var runner = new ExternalProcessRunner();
            await RequireFfmpegSuccessAsync(runner, ffmpeg,
                [
                    "-y", "-f", "lavfi", "-i", "color=c=blue:s=320x180:r=25:d=2",
                    "-f", "lavfi", "-i", "sine=frequency=220:sample_rate=48000:duration=2",
                    "-map", "0:v:0", "-map", "1:a:0", "-c:v", "libx264", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-shortest", sourceVideo
                ]);
            await RequireFfmpegSuccessAsync(runner, ffmpeg,
                [
                    "-y", "-f", "lavfi", "-i", "sine=frequency=660:sample_rate=24000:duration=1.8",
                    "-af", "volume=0.5", "-c:a", "pcm_s16le", sourceVoice
                ]);

            var probe = new FfprobeService(ffprobe, runner);
            var validator = new AudioQualityValidator(ffmpeg, runner, probe);
            var mixer = new SceneAudioMixer(ffmpeg, runner, probe, validator);
            var preparer = new LipSyncMediaPreparer(ffmpeg, runner, probe, validator, mixer);

            var prepared = await preparer.PrepareAsync(
                sourceVideo,
                sourceVoice,
                preparedVideo,
                preparedAudio,
                2_000);
            var silentProbe = await probe.ProbeAsync(prepared.VideoPath);
            var wavProbe = await probe.ProbeAsync(prepared.AudioPath);

            Assert.True(silentProbe.HasVideo);
            Assert.False(silentProbe.HasAudio);
            Assert.True(wavProbe.HasAudio);
            Assert.False(wavProbe.HasVideo);
            Assert.Equal(48_000, wavProbe.AudioSampleRate);
            Assert.Equal(64, prepared.VideoSha256.Length);
            Assert.Equal(64, prepared.AudioSha256.Length);

            // The prepared silent clip stands in for Fal's returned lip-motion video.
            var finalized = await preparer.FinalizeAsync(
                prepared.VideoPath,
                prepared.AudioPath,
                finalOutput,
                2_000);

            Assert.True(finalized.Probe.HasVideo);
            Assert.True(finalized.Probe.HasAudio);
            Assert.Equal("aac", finalized.Probe.AudioCodec);
            Assert.Equal(48_000, finalized.Probe.AudioSampleRate);
            Assert.True(finalized.AudioQuality.IsAudible, finalized.AudioQuality.FailureMessage);
            Assert.InRange(finalized.DurationMs, 1_900, 2_500);
            Assert.Equal(64, finalized.Sha256.Length);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task RequireFfmpegSuccessAsync(
        IExternalProcessRunner runner,
        string ffmpeg,
        IReadOnlyList<string> arguments)
    {
        var result = await runner.RunAsync(ffmpeg, arguments, TimeSpan.FromMinutes(1), CancellationToken.None);
        Assert.True(result.ExitCode == 0, result.StandardError);
    }
}
