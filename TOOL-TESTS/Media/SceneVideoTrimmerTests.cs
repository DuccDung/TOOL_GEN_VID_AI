using TOOL_LOCAL.Media;

namespace TOOL_TESTS.Media;

public sealed class SceneVideoTrimmerTests
{
    [Fact]
    public async Task TrimAsync_ProducesExactOneSecondClipWithNativeAudio()
    {
        var tools = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
        var ffmpeg = Path.Combine(tools, "ffmpeg.exe");
        var ffprobe = Path.Combine(tools, "ffprobe.exe");
        Assert.True(File.Exists(ffmpeg), "Test output must include the licensed FFmpeg bundle.");
        Assert.True(File.Exists(ffprobe), "Test output must include the licensed FFprobe bundle.");
        var root = Path.Combine(Path.GetTempPath(), $"videomaker-trimmer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var input = Path.Combine(root, "provider-three-seconds.mp4");
            var output = Path.Combine(root, "selected-one-second.mp4.trimmed.part");
            var runner = new ExternalProcessRunner();
            var createResult = await runner.RunAsync(
                ffmpeg,
                [
                    "-y",
                    "-f", "lavfi", "-i", "color=c=blue:s=320x180:r=25:d=3",
                    "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=3",
                    "-map", "0:v:0", "-map", "1:a:0",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-b:a", "192k", "-shortest", input
                ],
                TimeSpan.FromMinutes(2));
            Assert.Equal(0, createResult.ExitCode);

            var trimmer = new SceneVideoTrimmer(ffmpeg, runner);
            await trimmer.TrimAsync(input, output, 1, CancellationToken.None);

            var probe = await new FfprobeService(ffprobe, runner).ProbeAsync(output);
            Assert.True(probe.HasVideo);
            Assert.True(probe.HasAudio);
            Assert.InRange(probe.DurationSeconds, 0.9m, 1.1m);
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
    public async Task TrimAsync_WhenAudioDisabled_ProducesExactSilentClip()
    {
        var tools = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg");
        var ffmpeg = Path.Combine(tools, "ffmpeg.exe");
        var ffprobe = Path.Combine(tools, "ffprobe.exe");
        Assert.True(File.Exists(ffmpeg), "Test output must include the licensed FFmpeg bundle.");
        Assert.True(File.Exists(ffprobe), "Test output must include the licensed FFprobe bundle.");
        var root = Path.Combine(Path.GetTempPath(), $"videomaker-trimmer-silent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var input = Path.Combine(root, "provider-three-seconds.mp4");
            var output = Path.Combine(root, "selected-one-second-silent.mp4.trimmed.part");
            var runner = new ExternalProcessRunner();
            var createResult = await runner.RunAsync(
                ffmpeg,
                [
                    "-y",
                    "-f", "lavfi", "-i", "color=c=blue:s=320x180:r=25:d=3",
                    "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=3",
                    "-map", "0:v:0", "-map", "1:a:0",
                    "-c:v", "libx264", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-b:a", "192k", "-shortest", input
                ],
                TimeSpan.FromMinutes(2));
            Assert.Equal(0, createResult.ExitCode);

            var trimmer = new SceneVideoTrimmer(ffmpeg, runner);
            await trimmer.TrimAsync(input, output, 1, CancellationToken.None, includeAudio: false);

            var probe = await new FfprobeService(ffprobe, runner).ProbeAsync(output);
            Assert.True(probe.HasVideo);
            Assert.False(probe.HasAudio);
            Assert.InRange(probe.DurationSeconds, 0.9m, 1.1m);
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
    public async Task TrimAsync_UsesSelectedDurationAndPreservesVideoAndAudio()
    {
        var input = Path.Combine(Path.GetTempPath(), $"trim-input-{Guid.NewGuid():N}.mp4");
        var output = Path.Combine(Path.GetTempPath(), $"trim-output-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(input, [0x00]);
        var runner = new RecordingProcessRunner();
        var trimmer = new SceneVideoTrimmer("ffmpeg", runner);

        try
        {
            await trimmer.TrimAsync(input, output, 2, CancellationToken.None);

            Assert.Equal("ffmpeg", runner.Executable);
            Assert.Contains("-t", runner.Arguments);
            Assert.Equal("2", runner.Arguments[runner.Arguments.IndexOf("-t") + 1]);
            Assert.Contains("0:v:0", runner.Arguments);
            Assert.Contains("0:a:0?", runner.Arguments);
            Assert.Contains("libx264", runner.Arguments);
            Assert.Contains("aac", runner.Arguments);
            Assert.Equal(Path.GetFullPath(output), runner.Arguments[^1]);
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    [Fact]
    public async Task StripAudioAsync_RemovesAudioWithoutReencodingVideo()
    {
        var input = Path.Combine(Path.GetTempPath(), $"strip-audio-input-{Guid.NewGuid():N}.mp4");
        var output = Path.Combine(Path.GetTempPath(), $"strip-audio-output-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(input, [0x00]);
        var runner = new RecordingProcessRunner();
        var trimmer = new SceneVideoTrimmer("ffmpeg", runner);

        try
        {
            await trimmer.StripAudioAsync(input, output, CancellationToken.None);

            Assert.Contains("-an", runner.Arguments);
            Assert.Contains("copy", runner.Arguments);
            Assert.DoesNotContain("aac", runner.Arguments);
            Assert.Equal(Path.GetFullPath(output), runner.Arguments[^1]);
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public async Task TrimAsync_RejectsDurationOutsideOneToFifteenSeconds(int durationSeconds)
    {
        var runner = new RecordingProcessRunner();
        var trimmer = new SceneVideoTrimmer("ffmpeg", runner);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            trimmer.TrimAsync("missing.mp4", "output.mp4", durationSeconds, CancellationToken.None));
        Assert.Null(runner.Executable);
    }

    private sealed class RecordingProcessRunner : IExternalProcessRunner
    {
        public string? Executable { get; private set; }

        public List<string> Arguments { get; private set; } = [];

        public Task<ProcessExecutionResult> RunAsync(
            string executable,
            IEnumerable<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Executable = executable;
            Arguments = arguments.ToList();
            return Task.FromResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
        }
    }
}
