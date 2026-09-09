using TOOL_LOCAL.LocalVoice;
using TOOL_LOCAL.Media;

namespace TOOL_TESTS.LocalVoice;

public sealed class LocalVoiceMediaTests
{
    [Fact]
    public async Task PrepareAndRemux_PreserveVideoPacketsAndDuration()
    {
        using var f = new Fixture(); await f.CreateAsync();
        var originalHash = await LocalVoiceStore.FileHashAsync(f.Source, default);
        var wav = Path.Combine(f.Root, "prepared.wav"); var output = Path.Combine(f.Root, "converted.mp4");
        await f.Media.PrepareAsync(f.Source, wav, 4000, default);
        var probe = await f.Media.RemuxAsync(f.Source, wav, output, 4000, default);
        Assert.True(probe.HasVideo && probe.HasAudio); Assert.InRange(probe.DurationSeconds, 3.95m, 4.15m);
        Assert.Equal(originalHash, await LocalVoiceStore.FileHashAsync(f.Source, default));
        Assert.Equal(await f.VideoHashAsync(f.Source), await f.VideoHashAsync(output));
        Assert.False(File.Exists(wav + ".part")); Assert.False(File.Exists(output + ".part"));
    }

    [Fact]
    public async Task SilentSource_AndDurationMismatch_AreRejected()
    {
        using var f = new Fixture(); await f.CreateAsync(silent: true);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Media.PrepareAsync(f.Source, Path.Combine(f.Root, "bad.wav"), 4000, default));
        await f.CreateAsync();
        var wav = Path.Combine(f.Root, "short.wav"); await f.Media.PrepareAsync(f.Source, wav, 2000, default);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Media.RemuxAsync(f.Source, wav, Path.Combine(f.Root, "bad.mp4"), 4000, default));
    }

    internal sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "vm-voice-media-test-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "native.mp4");
        public ExternalProcessRunner Runner { get; } = new();
        public string Ffmpeg => Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
        public LocalVoiceMedia Media { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Root);
            var probe = new FfprobeService(Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffprobe.exe"), Runner);
            Media = new(Ffmpeg, Runner, probe, new(Ffmpeg, Runner, probe));
        }
        public async Task CreateAsync(bool silent = false)
        {
            var result = await Runner.RunAsync(Ffmpeg, ["-y", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25:duration=4",
                "-f", "lavfi", "-i", silent ? "anullsrc=r=48000:cl=stereo" : "sine=frequency=440:sample_rate=48000:duration=4",
                "-t", "4", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", Source], TimeSpan.FromMinutes(1));
            Assert.Equal(0, result.ExitCode);
        }
        public async Task<string> VideoHashAsync(string input)
        {
            var result = await Runner.RunAsync(Ffmpeg, ["-v", "error", "-i", input, "-map", "0:v:0", "-c:v", "copy", "-f", "hash", "-hash", "sha256", "-"], TimeSpan.FromMinutes(1));
            Assert.Equal(0, result.ExitCode); return result.StandardOutput.Trim();
        }
        public void Dispose()
        {
            var absolute = Path.GetFullPath(Root); var temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\') + "\\";
            if (absolute.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(absolute).StartsWith("vm-voice-media-test-")) Directory.Delete(absolute, true);
        }
    }
}
