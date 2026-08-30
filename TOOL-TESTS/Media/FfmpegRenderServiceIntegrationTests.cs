using TOOL_LOCAL.Media;

namespace TOOL_TESTS.Media;

public sealed class FfmpegRenderServiceIntegrationTests
{
    [Fact]
    public async Task RenderAsync_PreservesKlingNativeAudioWithoutGlobalAudioInputs()
    {
        var tools = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
        var ffmpeg = Path.Combine(tools, "ffmpeg.exe");
        var ffprobe = Path.Combine(tools, "ffprobe.exe");
        Assert.True(File.Exists(ffmpeg), "Test output must include the licensed FFmpeg bundle.");
        Assert.True(File.Exists(ffprobe), "Test output must include the licensed FFprobe bundle.");

        var root = Path.Combine(Path.GetTempPath(), $"videomaker-final-render-audio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var firstScene = Path.Combine(root, "scene-001-native-audio.mp4");
            var secondScene = Path.Combine(root, "scene-002-native-audio.mp4");
            var output = Path.Combine(root, "final.mp4");
            var working = Path.Combine(root, "working");
            var runner = new ExternalProcessRunner();

            await CreateNativeAudioSceneAsync(runner, ffmpeg, firstScene, "blue", 440);
            await CreateNativeAudioSceneAsync(runner, ffmpeg, secondScene, "green", 660);

            var renderer = new FfmpegRenderService(ffmpeg, runner);
            await renderer.RenderAsync(new FinalRenderManifest(
                [firstScene, secondScene],
                output,
                working,
                320,
                180,
                25m));

            var probe = new FfprobeService(ffprobe, runner);
            var result = await probe.ProbeAsync(output);
            Assert.True(result.HasVideo);
            Assert.True(result.HasAudio);
            Assert.Equal("h264", result.VideoCodec);
            Assert.Equal("aac", result.AudioCodec);
            Assert.InRange(result.DurationSeconds, 1.9m, 2.1m);

            var validator = new AudioQualityValidator(ffmpeg, runner, probe);
            var quality = await validator.AnalyzeAsync(output);
            Assert.True(quality.IsAudible, quality.FailureMessage);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RenderAsync_PreservesKlingNativeAudioWhenAddingQuietBackgroundMusic()
    {
        var tools = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
        var ffmpeg = Path.Combine(tools, "ffmpeg.exe");
        var ffprobe = Path.Combine(tools, "ffprobe.exe");
        var root = Path.Combine(Path.GetTempPath(), $"videomaker-final-render-music-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var scene = Path.Combine(root, "scene-native-audio.mp4");
            var music = Path.Combine(root, "music.wav");
            var output = Path.Combine(root, "final-with-music.mp4");
            var working = Path.Combine(root, "working");
            var runner = new ExternalProcessRunner();

            await CreateNativeAudioSceneAsync(runner, ffmpeg, scene, "purple", 440);
            var musicResult = await runner.RunAsync(
                ffmpeg,
                [
                    "-y", "-f", "lavfi", "-i", "sine=frequency=180:sample_rate=48000:duration=1",
                    "-c:a", "pcm_s16le", music
                ],
                TimeSpan.FromMinutes(2));
            Assert.True(musicResult.ExitCode == 0, musicResult.StandardError);

            var renderer = new FfmpegRenderService(ffmpeg, runner);
            await renderer.RenderAsync(new FinalRenderManifest(
                [scene],
                output,
                working,
                320,
                180,
                25m,
                MusicPath: music,
                MusicVolume: 0.001m));

            var probe = new FfprobeService(ffprobe, runner);
            var validator = new AudioQualityValidator(ffmpeg, runner, probe);
            var quality = await validator.AnalyzeAsync(output);

            Assert.True(quality.IsAudible, "Quiet background music must not replace the audible Kling Native Audio track.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task CreateNativeAudioSceneAsync(
        IExternalProcessRunner runner,
        string ffmpeg,
        string output,
        string color,
        int frequency)
    {
        var result = await runner.RunAsync(
            ffmpeg,
            [
                "-y",
                "-f", "lavfi", "-i", $"color=c={color}:s=320x180:r=25:d=1",
                "-f", "lavfi", "-i", $"sine=frequency={frequency}:sample_rate=48000:duration=1",
                "-map", "0:v:0", "-map", "1:a:0",
                "-c:v", "libx264", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-b:a", "192k", "-shortest", output
            ],
            TimeSpan.FromMinutes(2));

        Assert.True(result.ExitCode == 0, result.StandardError);
    }
}
