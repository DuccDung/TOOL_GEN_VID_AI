using System.Diagnostics;
using System.Text;
using System.Security.Cryptography;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub.Media;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Subtitles;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Voice;
using Xunit.Abstractions;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubLongVideoTests(ITestOutputHelper output) : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vs-long-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task VoiceRenderer_TwoHours_KeepsLastPhraseAndUsesBoundedIntermediateFiles()
    {
        Directory.CreateDirectory(root);
        var phrasePath = Path.Combine(root, "phrase.wav");
        VietsubVoiceBoundaryTests.WriteWave(phrasePath, 24000, 500, 0, 500);
        var phrase = new VietsubVoicePhrase("last", [Guid.NewGuid()], "speaker_1", "Xin chào.", 7_199_000, 7_199_500);
        var paths = new VietsubAppPaths(root);
        var subtitles = new VietsubSubtitleStore(paths);
        var projects = new VietsubProjectStore(paths, subtitles);
        var project = await projects.CreateAsync(Guid.NewGuid(), "owner", "Two-hour synthetic media");
        var trackId = Guid.NewGuid();
        var runner = new RecordingRunner();
        var renderer = new VietsubVoiceTimelineRenderer(paths, new VietsubVoiceBoundaryTests.ReadyPreflight(),
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe"), runner);
        var timer = Stopwatch.StartNew();
        var result = await renderer.RenderAsync(project.ProjectId, Guid.NewGuid(), trackId, 1,
            [new(phrase, null!, phrasePath, VietsubWavInspector.Inspect(phrasePath, false))],
            VietsubVoiceBoundaryTests.Settings(), 7_200_000, CancellationToken.None);
        Assert.Equal(7_200_000, result.Metadata.DurationMilliseconds);
        Assert.InRange(new FileInfo(result.AbsolutePath).Length, 1_382_400_044, 1_382_401_024);
        Assert.True(runner.MaximumOutputBytes < 32 * 1024 * 1024, $"Intermediate WAV: {runner.MaximumOutputBytes} bytes");
        using var reader = new BinaryReader(File.OpenRead(result.AbsolutePath));
        reader.BaseStream.Position = 12;
        while (true)
        {
            var id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadUInt32();
            if (id == "data") break;
            reader.BaseStream.Position += size + (size & 1);
        }
        var start = reader.BaseStream.Position;
        reader.BaseStream.Position = start + 7_199_100L * 192;
        Assert.Contains(Enumerable.Range(0, 4800).Select(_ => reader.ReadInt16()), sample => Math.Abs((int)sample) > 1000);
        reader.BaseStream.Position = start + 7_198_000L * 192;
        Assert.All(Enumerable.Range(0, 4800).Select(_ => reader.ReadInt16()), sample => Assert.Equal(0, sample));
        output.WriteLine($"Two-hour voice render: {timer.Elapsed.TotalSeconds:F2}s; largest intermediate {runner.MaximumOutputBytes} bytes; output {new FileInfo(result.AbsolutePath).Length} bytes.");
        reader.Dispose();
        await VerifyTwoHourExportAsync(paths, projects, subtitles, project, trackId, phrase, result);
    }

    private async Task VerifyTwoHourExportAsync(VietsubAppPaths paths, VietsubProjectStore projects,
        VietsubSubtitleStore subtitles, VietsubProjectManifest project, Guid trackId,
        VietsubVoicePhrase phrase, VietsubVoiceTimelineRenderResult timeline)
    {
        var ffmpeg = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
        var ffprobe = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffprobe.exe");
        var runner = new ExternalProcessRunner();
        var sourcePath = Path.Combine(root, "two-hour-source.mp4");
        var source = await runner.RunAsync(ffmpeg,
            ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=blue:s=256x144:r=1:d=7200",
             "-an", "-c:v", "libx264", "-preset", "ultrafast", "-threads", "2", "-pix_fmt", "yuv420p", "-movflags", "+faststart", "-y", sourcePath],
            TimeSpan.FromMinutes(2));
        Assert.True(source.ExitCode == 0, source.StandardError);
        var preflight = new VietsubVoiceBoundaryTests.ReadyPreflight();
        var probe = new FfprobeService(ffprobe, runner);
        var import = new VietsubMediaImportService(paths, preflight, probe);
        project.SourceVideo = await import.ImportAsync(project, sourcePath, VietsubMediaImportMode.Link);
        var track = new VietsubSubtitleTrack { TrackId = trackId, DisplayName = "Long subtitle", Source = "PADDLE_OCR_LOCAL", LanguageCode = "en",
            Cues = [new() { CueId = phrase.CueIds[0], StartMilliseconds = phrase.StartMilliseconds, EndMilliseconds = phrase.EndMilliseconds,
                OriginalText = "Hello", TranslatedText = phrase.Text }] };
        await subtitles.SaveTrackAsync(project.ProjectId, track);
        project.ActiveSubtitleTrackId = trackId;
        var voiceStore = new VietsubVoiceStore(paths, subtitles);
        var hash = Convert.ToHexString(await HashAsync(timeline.AbsolutePath)).ToLowerInvariant();
        var settings = VietsubVoiceBoundaryTests.Settings();
        var artifact = new VietsubVoiceArtifact(Guid.NewGuid(), trackId, track.Revision, VietsubVoiceArtifactKinds.Timeline, null,
            Path.GetRelativePath(paths.GetProjectDirectory(project.ProjectId), timeline.AbsolutePath), new FileInfo(timeline.AbsolutePath).Length,
            hash, new string('b', 64), settings.EngineId, settings.EngineVersion, settings.ModelId, settings.ModelVersion,
            settings.VoiceId, 7_200_000, 48000, 2, VietsubVoiceArtifactStatuses.Ready, VietsubVoiceTimingStatuses.Natural,
            DateTime.UtcNow, DateTime.UtcNow, phrase.CueIds);
        Assert.True(await voiceStore.SaveArtifactAsync(project.ProjectId, artifact, track.Revision));
        await projects.SaveAsync(project);
        await using var session = new VietsubProjectSession(projects, project, TimeSpan.FromHours(1));
        await session.StartAsync();
        var exporter = new VietsubVideoExportService(new AllowAuthorizer(), projects, import, subtitles, voiceStore,
            paths, preflight, probe, ffmpeg, runner);
        var destination = Path.Combine(root, "two-hour-result.mp4");
        var progress = new ExportProgress(); var timer = Stopwatch.StartNew();
        var result = await exporter.ExportAsync(session, "owner", project.OrganizationId, destination, default, progress);
        var metadata = await probe.ProbeAsync(destination);
        Assert.Equal(7200, result.DurationSeconds);
        Assert.True(metadata.HasAudio && metadata.HasVideo);
        Assert.Contains(progress.Values, p => p.Stage == "RENDER" && p.Percent is > 2 and < 96);
        Assert.Contains(progress.Values, p => p.Stage == "VERIFY");
        Assert.Equal(100, progress.Values[^1].Percent);
        Assert.Equal(progress.Values.Select(p => p.Percent).Order(), progress.Values.Select(p => p.Percent));
        var decoded = Path.Combine(root, "last-output.wav");
        var decode = await runner.RunAsync(ffmpeg, ["-hide_banner", "-loglevel", "error", "-ss", "7199.1", "-i", destination,
            "-map", "0:a:0", "-t", "0.2", "-ac", "1", "-ar", "24000", "-c:a", "pcm_s16le", "-y", decoded], TimeSpan.FromSeconds(20));
        Assert.True(decode.ExitCode == 0, decode.StandardError);
        Assert.Equal(200, VietsubWavInspector.Inspect(decoded, false).DurationMilliseconds);
        var data = await File.ReadAllBytesAsync(decoded);
        Assert.Contains(Enumerable.Range(100, (data.Length - 200) / 2).Select(i => BitConverter.ToInt16(data, i * 2)), sample => Math.Abs((int)sample) > 1000);
        output.WriteLine($"Two-hour MP4 export (synthetic 256x144 / 1 fps, voice-only): {timer.Elapsed.TotalSeconds:F2}s; {result.SizeBytes} bytes; audio at 7199.1s verified.");
        Assert.Empty(Directory.GetFiles(root, "*.partial.mp4", SearchOption.AllDirectories));
    }

    private sealed class AllowAuthorizer : IVietsubLocalJobAuthorizer
    {
        public Task AuthorizeAsync(string userId, Guid organizationId, VietsubProjectManifest project, CancellationToken token) => Task.CompletedTask;
    }

    private sealed class ExportProgress : IProgress<VietsubVideoExportProgress>
    {
        public List<VietsubVideoExportProgress> Values { get; } = [];
        public void Report(VietsubVideoExportProgress value) => Values.Add(value);
    }

    [Fact]
    public async Task Segments_PreserveOverflowAcrossBoundary_ResumeAndRejectCorruptCache()
    {
        Directory.CreateDirectory(root);
        var phrasePath = Path.Combine(root, "boundary.wav");
        VietsubVoiceBoundaryTests.WriteWave(phrasePath, 22050, 2400, 0, 2400);
        var original = SHA256.HashData(await File.ReadAllBytesAsync(phrasePath));
        var phrase = new VietsubVoicePhrase("crossing", [Guid.NewGuid()], "speaker_1", "Giữ đủ tiếng ở ranh giới.", 119_000, 120_000, 120_000);
        var runner = new RecordingRunner();
        var paths = new VietsubAppPaths(root);
        var renderer = new VietsubVoiceTimelineRenderer(paths, new VietsubVoiceBoundaryTests.ReadyPreflight(),
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe"), runner);
        var projectId = Guid.NewGuid(); var trackId = Guid.NewGuid(); var jobId = Guid.NewGuid();
        VietsubVoicePhraseAudio[] inputs = [new(phrase, null!, phrasePath, VietsubWavInspector.Inspect(phrasePath, false))];
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.RenderAsync(projectId, jobId, trackId, 1,
            inputs, VietsubVoiceBoundaryTests.Settings(), 240_000, cancellation.Token,
            (done, total, token) => { Assert.Equal(2, total); cancellation.Cancel(); return Task.CompletedTask; }));
        Assert.False(Directory.Exists(paths.GetProjectPath(projectId, "temp", $"voice-{jobId:N}")));
        Assert.Equal(1, runner.Calls);
        var resumed = await renderer.RenderAsync(projectId, Guid.NewGuid(), trackId, 1, inputs,
            VietsubVoiceBoundaryTests.Settings(), 240_000, default);
        Assert.Equal(2, runner.Calls); // Only the uncommitted second segment is rendered.
        Assert.Equal(1.2, Assert.Single(resumed.Diagnostics).Tempo);
        Assert.Equal(VietsubVoiceTimingStatuses.ReviewRequired, resumed.Diagnostics[0].Status);
        var layout = VietsubWavInspector.ReadTimelineLayout(resumed.AbsolutePath, 240_000);
        using (var reader = new BinaryReader(File.OpenRead(resumed.AbsolutePath)))
        {
            foreach (var at in new[] { 119_950, 119_995, 120_000, 120_005, 120_500, 120_950 })
            {
                reader.BaseStream.Position = layout.DataOffset + at * 192L;
                Assert.Contains(Enumerable.Range(0, 480).Select(_ => reader.ReadInt16()), sample => Math.Abs((int)sample) > 1000);
            }
        }
        var cached = await renderer.RenderAsync(projectId, Guid.NewGuid(), trackId, 2, inputs,
            VietsubVoiceBoundaryTests.Settings(), 240_000, default);
        Assert.Equal(2, runner.Calls);
        Assert.Equal(await HashAsync(resumed.AbsolutePath), await HashAsync(cached.AbsolutePath));
        var segment = Directory.GetFiles(paths.GetProjectPath(projectId, "cache", "voice-segments-v2"), "*.wav")[0];
        using (var stream = new FileStream(segment, FileMode.Open, FileAccess.Write)) { stream.Position = stream.Length - 4; stream.WriteByte(123); }
        var repaired = await renderer.RenderAsync(projectId, Guid.NewGuid(), trackId, 3, inputs,
            VietsubVoiceBoundaryTests.Settings(), 240_000, default);
        Assert.Equal(3, runner.Calls);
        Assert.Equal(await HashAsync(resumed.AbsolutePath), await HashAsync(repaired.AbsolutePath));
        Assert.Equal(original, SHA256.HashData(await File.ReadAllBytesAsync(phrasePath)));
    }

    [Fact]
    public void TimelineInspector_AcceptsBoundedLargePcmWithoutReadingIt_AndRejectsMalformedData()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "large.wav");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write("RIFF"u8); writer.Write(180_000 * 192 + 36); writer.Write("WAVEfmt "u8);
            writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(192000);
            writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(180_000 * 192);
            stream.SetLength(180_000 * 192L + 44);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(180_000, VietsubWavInspector.InspectTimeline(path, 180_000).DurationMilliseconds);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - allocated, 0, 256_000);
        Assert.Throws<VietsubVoiceException>(() => VietsubWavInspector.Inspect(path, false));
        Assert.Throws<VietsubVoiceException>(() => VietsubWavInspector.InspectTimeline(path, 179_999));
        Assert.Throws<VietsubVoiceException>(() => VietsubWavInspector.InspectTimeline(path, 14_400_001));
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write)) stream.SetLength(stream.Length - 1);
        Assert.Throws<VietsubVoiceException>(() => VietsubWavInspector.InspectTimeline(path, 180_000));
    }

    [Fact]
    public void WaveformOverview_TwoHours_UsesFixedImageAndBoundedMemory()
    {
        Directory.CreateDirectory(root);
        var pcm = Path.Combine(root, "wave.pcm"); var png = Path.Combine(root, "wave.png");
        using (var stream = new FileStream(pcm, FileMode.Create, FileAccess.Write))
        {
            stream.SetLength(7200L * VietsubTimelineWaveformService.OverviewSampleRate * 2);
            stream.Position = stream.Length - 4000;
            stream.Write(Enumerable.Repeat((byte)0x7f, 4000).ToArray());
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        VietsubTimelineWaveformService.RenderOverview(pcm, png, default);
        var used = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.InRange(used, 0, 1024 * 1024);
        using var bitmap = new System.Drawing.Bitmap(png);
        Assert.Equal(VietsubTimelineWaveformService.WaveformWidth, bitmap.Width);
        Assert.Equal(64, bitmap.Height);
        Assert.True(bitmap.GetPixel(bitmap.Width - 1, 3).A > 0);
        Assert.Equal(0, bitmap.GetPixel(0, 3).A);
        output.WriteLine($"Two-hour waveform aggregation: {timer.Elapsed.TotalMilliseconds:F2}ms; managed allocation {used} bytes; overview PCM {new FileInfo(pcm).Length} bytes.");
    }

    private static async Task<byte[]> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await SHA256.HashDataAsync(stream);
    }

    private sealed class RecordingRunner : IExternalProcessRunner
    {
        public long MaximumOutputBytes { get; private set; }
        public int Calls { get; private set; }
        public async Task<ProcessExecutionResult> RunAsync(string executable, IEnumerable<string> arguments,
            TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var args = arguments.ToArray();
            Calls++;
            var result = await new ExternalProcessRunner().RunAsync(executable, args, timeout, cancellationToken);
            if (File.Exists(args[^1])) MaximumOutputBytes = Math.Max(MaximumOutputBytes, new FileInfo(args[^1]).Length);
            return result;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
