using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Subtitles;
using TOOL_LOCAL.Vietsub.Voice;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubVoiceBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"vietsub-voice-boundary-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(16000, 3500)]
    [InlineData(22050, 3750)]
    [InlineData(48000, 4000)]
    public async Task Renderer_TrimsOnlyVerifiedTail_AndKeepsSkippedIntervalSilent(int rate, long cueEnd)
    {
        var path = Path.Combine(_root, "phrase.wav");
        WriteWave(path, rate, 1834, 62, 1793);
        var original = SHA256.HashData(File.ReadAllBytes(path));
        var cues = new[] { Cue(0, 1500, false), Cue(2250, cueEnd, true), Cue(3750, 4000, false), Cue(4000, 4200, false) };
        var phrase = Assert.Single(VietsubVoicePhrasePlanner.Plan(cues, Settings()));
        var metadata = VietsubWavInspector.Inspect(path, true);
        Assert.Equal(1832, metadata.AudibleDurationMilliseconds);
        var result = await Renderer().RenderAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            [new(phrase, null!, path, metadata)], Settings(), 4500, CancellationToken.None);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.InRange(diagnostic.Tempo, 1, 1.2);
        Assert.True(diagnostic.NaturalDurationMilliseconds < 1832);
        Assert.NotEqual(VietsubVoiceTimingStatuses.ReviewRequired, diagnostic.Status);
        Assert.Equal(original, SHA256.HashData(File.ReadAllBytes(path)));
        var samples = ReadSamples(result.AbsolutePath);
        Assert.Equal(4500, result.Metadata.DurationMilliseconds);
        Assert.True(Peak(samples, 0, 2250) == 0,
            $"First signal at {Array.FindIndex(samples, value => Math.Abs((int)value) > 100) / 96d} ms, output {result.Metadata.SampleRate} Hz/{result.Metadata.Channels} channels, phrase start={phrase.StartMilliseconds}; {_filter}");
        Assert.True(Peak(samples, 2350, 3650) > 1000);
        Assert.True(Peak(samples, 3690, 3720) > 1000); // The end of the spoken signal survives.
        Assert.Equal(0, Peak(samples, 3750, 4200));
    }

    [Theory]
    [InlineData(1834, 1834, true, 27)]
    [InlineData(1834, 1793, false, 29)]
    [InlineData(1964, 1964, true, 135)]
    public async Task Renderer_PreservesOverflowingSpeechOrUnanalyzedTail(
        int duration, int signalEnd, bool analyze, int expectedOverflow)
    {
        var path = Path.Combine(_root, "phrase.wav");
        WriteWave(path, 22050, duration, 62, signalEnd);
        var original = File.ReadAllBytes(path);
        var phrase = Assert.Single(VietsubVoicePhrasePlanner.Plan(
            [Cue(0, 1500, false), Cue(2250, 3500, true), Cue(3750, 4000, false)], Settings()));
        var result = await Renderer().RenderAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            [new(phrase, null!, path, VietsubWavInspector.Inspect(path, analyze))],
            Settings() with { TrimSilence = analyze }, 4500, CancellationToken.None);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(VietsubVoiceTimingStatuses.ReviewRequired, diagnostic.Status);
        Assert.Equal(1.2, diagnostic.Tempo);
        Assert.Equal(1500, diagnostic.TargetDurationMilliseconds);
        Assert.Equal(expectedOverflow, Math.Ceiling(diagnostic.NaturalDurationMilliseconds / diagnostic.Tempo - 1e-7) - 1500);
        Assert.Equal(4500, result.Metadata.DurationMilliseconds);
        var samples = ReadSamples(result.AbsolutePath);
        Assert.Equal(0, Peak(samples, 0, 2250));
        Assert.True(Peak(samples, 3690, 3720) > 1000);
        if (signalEnd == duration)
            Assert.True(Peak(samples, 3750, 3750 + expectedOverflow - 5) > 1000,
                "Speech that crosses the skipped cue must remain audible.");
        Assert.Equal(0, Peak(samples, 4000, 4500));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Renderer_PreservesCueStartingInsideSkippedInterval()
    {
        var path = Path.Combine(_root, "phrase.wav");
        WriteWave(path, 16000, 300, 0, 200);
        var phrase = Assert.Single(VietsubVoicePhrasePlanner.Plan(
            [Cue(0, 1500, false), Cue(1000, 2000, true)], Settings()));
        var result = await Renderer().RenderAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
            [new(phrase, null!, path, VietsubWavInspector.Inspect(path, true))], Settings(), 3000, CancellationToken.None);
        Assert.Equal(VietsubVoiceTimingStatuses.ReviewRequired, Assert.Single(result.Diagnostics).Status);
        Assert.Equal(3000, result.Metadata.DurationMilliseconds);
        var samples = ReadSamples(result.AbsolutePath);
        Assert.Equal(0, Peak(samples, 0, 1000));
        Assert.True(Peak(samples, 1000, 1150) > 1000);
    }

    [Theory]
    [InlineData(false, 3500)]
    [InlineData(false, 4000)]
    [InlineData(true, 3500)]
    public async Task Workflow_NewVoiceAndCacheRebuild_UseSameBoundaryPolicy_AndPreserveProgress(bool speechAtEnd, long cueEnd)
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        var projects = new VietsubProjectStore(paths, subtitles);
        var project = await projects.CreateAsync(Guid.NewGuid(), "owner", "Boundary regression");
        var track = new VietsubSubtitleTrack
        {
            DisplayName = "OCR", LanguageCode = "en", Source = "PADDLE_OCR_LOCAL",
            Cues = [Cue(2250, cueEnd, true), Cue(3750, 4000, true), Cue(4250, 6250, true)]
        };
        await subtitles.SaveTrackAsync(project.ProjectId, track);
        project.ActiveSubtitleTrackId = track.TrackId;
        await projects.SaveAsync(project);
        var voices = new VietsubVoiceStore(paths, subtitles);
        var jobs = new VietsubJobStore(paths, subtitles);
        var synth = new FixtureSynthesizer(speechAtEnd ? 1834 : 1793);
        var renderer = Renderer();
        var executor = new VietsubVoiceJobExecutor(projects, subtitles, voices, synth, renderer, jobs, paths);
        await using var manager = new VietsubJobManager(jobs, new VietsubJobExecutorRegistry([executor]));

        async Task<VietsubLocalJob> RunJob(bool retryPreviousFailure = false)
        {
            var current = Assert.Single(await subtitles.LoadTracksAsync(project.ProjectId));
            var settings = Settings();
            var parameters = new VietsubVoiceJobParameters(2, track.TrackId, current.Revision,
                VietsubVoiceFingerprintBuilder.BuildConfigurationFingerprint(settings), settings,
                VietsubVoiceFingerprintBuilder.BuildSelectionFingerprint(current.Cues));
            var steps = new[] { "VOICE_PREPARE", "VOICE_SYNTHESIZE", "VOICE_TIMELINE", "VOICE_PUBLISH" };
            Guid queuedId;
            if (retryPreviousFailure)
            {
                // Reproduce the persisted 72% failure from the previous renderer.
                var previous = await jobs.CreateAsync(project.ProjectId, VietsubJobTypes.SynthesizeVoiceLocal,
                    steps, parameters.ToJson(), track.TrackId, current.Revision);
                await jobs.TransitionAsync(project.ProjectId, previous.Id, VietsubJobStatus.Running, "STARTED");
                await jobs.UpdateProgressAsync(project.ProjectId, previous.Id,
                    new("VOICE_TIMELINE", 5, 72, "Đang phân tích khoảng lặng và độ dài giọng đọc."));
                await jobs.TransitionAsync(project.ProjectId, previous.Id, VietsubJobStatus.Failed, "FAILED",
                    errorCode: VietsubVoiceErrorCodes.SkippedCueOverlap, errorMessage: "Giọng tràn vào câu đã bỏ qua.");
                queuedId = (await manager.RetryAsync(project.ProjectId, previous.Id)).Id;
            }
            else
            {
                queuedId = (await manager.EnqueueAsync(project.ProjectId, VietsubJobTypes.SynthesizeVoiceLocal,
                    steps, parameters.ToJson(), track.TrackId, current.Revision)).Id;
            }
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (true)
            {
                var job = (await jobs.GetAsync(project.ProjectId, queuedId, timeout.Token))!;
                if (job.Status is VietsubJobStatus.Completed or VietsubJobStatus.Failed) return job;
                await Task.Delay(20, timeout.Token);
            }
        }

        var initial = await RunJob();
        Assert.True(initial.Status == VietsubJobStatus.Completed, $"{initial.ErrorCode}: {initial.ErrorMessage}; {_filter}");
        Assert.Equal(3, synth.Generated);
        var cached = await voices.LoadReadyPhraseArtifactsAsync(project.ProjectId, track.TrackId);
        var hashes = cached.ToDictionary(item => item.ArtifactId, item => item.Sha256);
        var revision = await subtitles.SetVoiceEnabledAsync(project.ProjectId, track.TrackId, track.Revision,
            [track.Cues[1].CueId], false);

        using var components = new VietsubVoiceComponentStore(paths, false);
        var service = new VietsubVoiceService(null!, subtitles, voices, paths, components,
            new VietsubVoicePlaybackRegistry(voices.IsTrackRevisionCurrent), renderer, null!);
        var rebuilt = await service.GetWorkspaceAsync(project, CancellationToken.None);
        Assert.NotNull(rebuilt.Timeline);
        Assert.False(rebuilt.RequiresRebuild);
        Assert.Equal(revision, rebuilt.Timeline.TrackRevision);
        Assert.Equal([track.Cues[0].CueId, track.Cues[2].CueId], rebuilt.Timeline.CueIds);
        Assert.NotNull(rebuilt.TimelinePlaybackUrl);
        var timelinePath = Path.Combine(paths.GetProjectDirectory(project.ProjectId), rebuilt.Timeline.RelativePath);
        AssertSkippedCueAudio(timelinePath, speechAtEnd);
        await VerifyExportAsync(timelinePath, speechAtEnd);

        var cachedJob = await RunJob(retryPreviousFailure: true);
        Assert.True(cachedJob.Status == VietsubJobStatus.Completed, $"{cachedJob.ErrorCode}: {cachedJob.ErrorMessage}");
        Assert.Equal(100, cachedJob.ProgressPercent);
        Assert.Null(cachedJob.ErrorCode);
        Assert.Equal(3, synth.Generated);
        {
            using var checkpoint = JsonDocument.Parse(cachedJob.CheckpointJson!);
            Assert.Equal("COMPLETED", checkpoint.RootElement.GetProperty("stage").GetString());
            Assert.Equal(2, checkpoint.RootElement.GetProperty("cacheHits").GetInt32());
        }
        {
            // A fresh synthesis with the selection already set must behave like cache recovery.
            var freshTrack = new VietsubSubtitleTrack
            {
                DisplayName = "Fresh", LanguageCode = "en", Source = "PADDLE_OCR_LOCAL",
                Cues = [Cue(2250, 3500, true), Cue(3750, 4000, false), Cue(4250, 6250, true)]
            };
            await subtitles.SaveTrackAsync(project.ProjectId, freshTrack);
            var freshParameters = new VietsubVoiceJobParameters(2, freshTrack.TrackId, freshTrack.Revision,
                VietsubVoiceFingerprintBuilder.BuildConfigurationFingerprint(Settings()), Settings(),
                VietsubVoiceFingerprintBuilder.BuildSelectionFingerprint(freshTrack.Cues));
            var freshJob = await jobs.CreateAsync(project.ProjectId, VietsubJobTypes.SynthesizeVoiceLocal,
                ["VOICE_PREPARE", "VOICE_SYNTHESIZE", "VOICE_TIMELINE", "VOICE_PUBLISH"], freshParameters.ToJson(), freshTrack.TrackId, freshTrack.Revision);
            await executor.ExecuteAsync(new(freshJob, (_, _) => ValueTask.CompletedTask, (_, _) => ValueTask.CompletedTask), CancellationToken.None);
            var fresh = await voices.LoadWorkspaceAsync(project.ProjectId, freshTrack.TrackId, freshTrack.Revision, project.VoiceSettings);
            Assert.NotNull(fresh.Timeline);
            AssertSkippedCueAudio(Path.Combine(paths.GetProjectDirectory(project.ProjectId), fresh.Timeline.RelativePath), speechAtEnd);
            Assert.Equal(5, synth.Generated);
            // Complete the fixture job before further subtitle mutations.
            await jobs.TransitionAsync(project.ProjectId, freshJob.Id, VietsubJobStatus.Running, "STARTED", "Fixture");
            await jobs.TransitionAsync(project.ProjectId, freshJob.Id, VietsubJobStatus.Completed, "COMPLETED", "Fixture");
        }

        revision = await subtitles.SetVoiceEnabledAsync(project.ProjectId, track.TrackId, revision, [track.Cues[1].CueId], true);
        var restored = await service.GetWorkspaceAsync(project, CancellationToken.None);
        Assert.True(restored.Timeline is not null, _filter);
        Assert.Equal(revision, restored.Timeline.TrackRevision);
        Assert.Equal(3, restored.Timeline.CueIds.Count);
        foreach (var artifact in cached)
            Assert.Equal(hashes[artifact.ArtifactId], Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(Path.Combine(paths.GetProjectDirectory(project.ProjectId), artifact.RelativePath)))).ToLowerInvariant());
    }

    private static void AssertSkippedCueAudio(string timelinePath, bool speechAtEnd)
    {
        var samples = ReadSamples(timelinePath);
        if (speechAtEnd)
            Assert.True(Peak(samples, 3750, 3770) > 1000);
        else
            Assert.Equal(0, Peak(samples, 3750, 4000));
        Assert.Equal(0, Peak(samples, 3820, 4000)); // The skipped cue itself contributes no speech.
    }

    private async Task VerifyExportAsync(string timelinePath, bool speechAtEnd)
    {
        var ffmpeg = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
        var runner = new ExternalProcessRunner();
        async Task Run(IEnumerable<string> arguments)
        {
            var result = await runner.RunAsync(ffmpeg, arguments, TimeSpan.FromSeconds(30));
            Assert.True(result.ExitCode == 0, result.StandardError);
        }
        var source = Path.Combine(_root, "video.mp4");
        await Run(["-v", "error", "-f", "lavfi", "-i", "color=c=blue:s=160x90:r=25:d=6.5",
            "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000:duration=6.5", "-c:v", "libx264", "-c:a", "aac", "-y", source]);
        foreach (var includeOriginal in new[] { false, true })
        {
            var output = Path.Combine(_root, $"export-{includeOriginal}.mp4");
            var mix = new VietsubAudioMixSettings { OriginalMuted = !includeOriginal, OriginalVolume = 0.25, TranslatedVoiceVolume = 1, AutoDuckOriginal = true };
            await Run(VietsubVideoExportService.BuildRenderArguments(source, timelinePath, output, "null", 6.5m, true, mix));
            var wave = Path.Combine(_root, $"export-{includeOriginal}.wav");
            await Run(["-v", "error", "-i", output, "-vn", "-ar", "48000", "-ac", "2", "-c:a", "pcm_s16le", "-y", wave]);
            var samples = ReadSamples(wave);
            Assert.True(Peak(samples, 2350, 3650) > 1000);
            if (speechAtEnd) Assert.True(Peak(samples, 3750, 3770) > 1000);
            Assert.True(includeOriginal ? Peak(samples, 3820, 3980) > 100 : Peak(samples, 3820, 3980) < 10);
        }
    }

    private sealed class FixtureSynthesizer(int signalEnd) : IVietsubVoiceSynthesizer
    {
        public int Generated { get; private set; }
        public async Task SynthesizeIncrementallyAsync(IReadOnlyList<VietsubVoiceSynthesisItem> items,
            Func<VietsubVoiceSynthesisItem, ValueTask> completed, CancellationToken token)
        {
            foreach (var item in items)
            {
                WriteWave(item.OutputPath, 22050, 1834, 62, signalEnd);
                Generated++;
                await completed(item);
            }
        }
    }

    private string? _filter;
    private VietsubVoiceTimelineRenderer Renderer()
    {
        var ffmpeg = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
        Assert.True(File.Exists(ffmpeg), "The licensed FFmpeg test bundle is required.");
        return new(new(_root), new ReadyPreflight(), ffmpeg, new RecordingRunner(filter => _filter = filter));
    }

    private sealed class RecordingRunner(Action<string> record) : IExternalProcessRunner
    {
        public async Task<ProcessExecutionResult> RunAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken token = default)
        {
            var args = arguments.ToArray();
            record(args[Array.IndexOf(args, "-filter_complex") + 1]);
            var result = await new ExternalProcessRunner().RunAsync(executable, args, timeout, token);
            if (result.ExitCode != 0) record(result.StandardError);
            return result;
        }
    }

    internal static VietsubVoiceSettingsSnapshot Settings() => new(
        VietsubVoiceEngines.Piper, VietsubVoiceCatalog.PiperEngineVersion,
        VietsubVoiceCatalog.PiperModelId, VietsubVoiceCatalog.PiperModelVersion,
        VietsubVoiceCatalog.PiperVoiceId, 500, 8000, 4500, 600, 1.12, 1.2, true);

    internal static VietsubSubtitleCue Cue(long start, long end, bool enabled) => new()
    {
        StartMilliseconds = start, EndMilliseconds = end, VoiceEnabled = enabled,
        OriginalText = "source", TranslatedText = "Xin chào.", Speaker = "speaker_1"
    };

    internal static void WriteWave(string path, int rate, int duration, int signalStart, int signalEnd)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new BinaryWriter(File.Create(path), Encoding.ASCII);
        var frames = rate * duration / 1000;
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + frames * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(frames * 2);
        for (var frame = 0; frame < frames; frame++)
            writer.Write((short)(frame >= (int)Math.Ceiling(rate * signalStart / 1000d) && frame < rate * signalEnd / 1000
                ? 4000 * Math.Cos(2 * Math.PI * 440 * frame / rate) : 0));
    }

    // Renderer output is 48 kHz stereo PCM; FFmpeg may add metadata chunks before data.
    internal static short[] ReadSamples(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.BaseStream.Position = 12;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadInt32();
            if (id == "data")
                return Enumerable.Range(0, size / 2).Select(_ => reader.ReadInt16()).ToArray();
            reader.BaseStream.Position += size + (size & 1);
        }
        throw new InvalidDataException("Missing WAV data.");
    }

    internal static int Peak(short[] samples, int startMs, int endMs) =>
        samples.Skip(startMs * 96).Take((endMs - startMs) * 96).Max(sample => Math.Abs((int)sample));

    internal sealed class ReadyPreflight : IMediaToolPreflightService
    {
        public Task<MediaToolStatusSummary> GetStatusAsync(bool force, CancellationToken token) => RequireReadyAsync(token);
        public Task<MediaToolStatusSummary> RequireReadyAsync(CancellationToken token) =>
            Task.FromResult(new MediaToolStatusSummary(true, null, "Ready", "test", "test", DateTime.UtcNow));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
