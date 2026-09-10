using System.Diagnostics;
using TOOL_LOCAL.LocalVoice;
using Xunit.Abstractions;

namespace TOOL_TESTS.LocalVoice;

public sealed class LocalVoiceModelTests(ITestOutputHelper output)
{
    [LocalVoiceModelFact]
    public async Task RealModels_AnchorConversionAndRemux_AreAudibleAndPreserveVideo()
    {
        using var f = new LocalVoiceMediaTests.Fixture();
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("VM_LOCAL_VOICE_COMPONENT_ROOT")!);
        var source = Path.GetFullPath(Environment.GetEnvironmentVariable("VM_LOCAL_VOICE_SMOKE_SOURCE")!);
        var reference = Path.GetFullPath(Environment.GetEnvironmentVariable("VM_LOCAL_VOICE_SMOKE_ANCHOR")!);
        var runtime = new LocalVoiceRuntime(f.Root, true, root);
        var timer = Stopwatch.StartNew();
        await runtime.ProbeInstalledAsync(default);
        Assert.Equal("READY", runtime.GetStatus().Status);
        output.WriteLine("Verified runtime/probe: {0:0.0}s", timer.Elapsed.TotalSeconds);
        Assert.True(runtime.LastProcessPeakBytes > 16 * 1048576L, "Measure the model worker, not the venv launcher.");
        output.WriteLine("Probe worker peak working set: {0:0.0} MiB; process CPU: {1:0.0}s", runtime.LastProcessPeakBytes / 1048576d, runtime.LastProcessCpuSeconds);
        var native = await f.Runner.RunAsync(f.Ffmpeg, ["-y", "-f", "lavfi", "-i", "testsrc2=size=320x180:rate=25:duration=8",
            "-i", source, "-map", "0:v:0", "-map", "1:a:0", "-t", "8", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", f.Source], TimeSpan.FromMinutes(1));
        Assert.Equal(0, native.ExitCode);
        var anchorDirectory = Path.Combine(f.Root, "anchor"); Directory.CreateDirectory(anchorDirectory);
        var conversionDirectory = Path.Combine(f.Root, "conversion"); Directory.CreateDirectory(conversionDirectory);
        var anchorAudio = await f.Runner.RunAsync(f.Ffmpeg, ["-y", "-i", reference, "-t", "8", "-vn", "-ar", "48000", "-ac", "2", "-c:a", "pcm_s16le", Path.Combine(anchorDirectory, "source.wav")], TimeSpan.FromMinutes(1));
        Assert.Equal(0, anchorAudio.ExitCode);
        await runtime.RunAsync("anchor", anchorDirectory, stage => output.WriteLine("Anchor: " + stage), default);
        output.WriteLine("Anchor worker peak working set: {0:0.0} MiB; process CPU: {1:0.0}s", runtime.LastProcessPeakBytes / 1048576d, runtime.LastProcessCpuSeconds);
        Assert.True(File.Exists(Path.Combine(anchorDirectory, "anchor.wav")));
        await f.Media.PrepareAsync(f.Source, Path.Combine(conversionDirectory, "source.wav"), 8000, default);
        File.Copy(Path.Combine(anchorDirectory, "anchor.wav"), Path.Combine(conversionDirectory, "anchor.wav"));
        await runtime.RunAsync("convert", conversionDirectory, stage => output.WriteLine("Conversion: " + stage), default);
        output.WriteLine("Conversion worker peak working set: {0:0.0} MiB; process CPU: {1:0.0}s", runtime.LastProcessPeakBytes / 1048576d, runtime.LastProcessCpuSeconds);
        var convertedWav = Path.Combine(conversionDirectory, "converted.wav");
        var convertedVideo = Path.Combine(conversionDirectory, "converted.mp4");
        Assert.NotEqual(await LocalVoiceStore.FileHashAsync(Path.Combine(conversionDirectory, "source.wav"), default),
            await LocalVoiceStore.FileHashAsync(convertedWav, default));
        var inspection = await f.Media.RemuxAsync(f.Source, convertedWav, convertedVideo, 8000, default);
        Assert.True(inspection.HasAudio);
        Assert.InRange(inspection.DurationSeconds, 7.9m, 8.15m);
        Assert.Equal(await f.VideoHashAsync(f.Source), await f.VideoHashAsync(convertedVideo));
        var hash = await LocalVoiceStore.FileHashAsync(convertedWav, default);
        await runtime.RunAsync("convert", conversionDirectory, null, default);
        Assert.Equal(hash, await LocalVoiceStore.FileHashAsync(convertedWav, default));
        output.WriteLine("Total probe/anchor/conversion/remux/cache-retry: {0:0.0}s. Technical smoke only; not Vietnamese/lip-sync acceptance.", timer.Elapsed.TotalSeconds);
    }
}

public sealed class LocalVoiceModelFactAttribute : FactAttribute
{
    public LocalVoiceModelFactAttribute()
    {
        if (new[] { "VM_LOCAL_VOICE_COMPONENT_ROOT", "VM_LOCAL_VOICE_SMOKE_SOURCE", "VM_LOCAL_VOICE_SMOKE_ANCHOR" }
            .Any(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
            Skip = "Opt-in: supply pinned component root and two authorized speech samples; no model/provider download during this test.";
    }
}
