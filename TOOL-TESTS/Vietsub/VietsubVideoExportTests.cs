using System.Security.Cryptography;
using System.Text.Json;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Media;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Subtitles;
using TOOL_LOCAL.Vietsub.Voice;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubVideoExportTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"videomaker-vietsub-export-{Guid.NewGuid():N}");

    [Fact]
    public void BuildRenderArguments_MapsVoiceOnlyAndOmitsOriginalAudio()
    {
        var settings = new VietsubAudioMixSettings
        {
            OriginalMuted = true,
            TranslatedVoiceVolume = 1.25,
            AutoDuckOriginal = true
        };

        var arguments = VietsubVideoExportService.BuildRenderArguments(
            "source.mp4",
            "voice.wav",
            "output.mp4",
            "subtitles=test.ass",
            9,
            sourceHasAudio: true,
            settings);

        Assert.Contains("1:a:0", arguments);
        Assert.Contains(arguments, argument => argument.Contains("volume=1.25", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, argument => argument.Contains("sidechaincompress", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildRenderArguments_WhenBothChannelsMuted_ExportsWithoutAudio()
    {
        var settings = new VietsubAudioMixSettings
        {
            OriginalMuted = true,
            TranslatedVoiceMuted = true
        };

        var arguments = VietsubVideoExportService.BuildRenderArguments(
            "source.mp4",
            "voice.wav",
            "output.mp4",
            "subtitles=test.ass",
            9,
            sourceHasAudio: true,
            settings);

        Assert.Contains("-an", arguments);
        Assert.DoesNotContain("-c:a", arguments);
        Assert.Equal(1, arguments.Count(argument => argument == "-i"));
    }

    [Fact]
    public async Task ExportAsync_RendersToPartial_ValidatesAndPublishesMp4()
    {
        Directory.CreateDirectory(_root);
        var paths = new VietsubAppPaths(_root);
        var subtitleStore = new VietsubSubtitleStore(paths);
        var voiceStore = new VietsubVoiceStore(paths, subtitleStore);
        var projectStore = new VietsubProjectStore(paths, subtitleStore);
        var project = await projectStore.CreateAsync(Guid.NewGuid(), "owner", "Export project");
        var sourcePath = Path.Combine(_root, "input.mp4");
        var sourceBytes = "stable-test-video"u8.ToArray();
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);
        var sourceInfo = new FileInfo(sourcePath);
        project.SourceVideo = new VietsubMediaReference
        {
            ImportMode = VietsubMediaImportModes.Link,
            OriginalPath = sourcePath,
            FileName = sourceInfo.Name,
            SizeBytes = sourceInfo.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant(),
            SourceLastWriteAtUtc = sourceInfo.LastWriteTimeUtc,
            Metadata = new VietsubMediaMetadata
            {
                DurationSeconds = 9,
                Width = 1920,
                Height = 1080,
                HasVideo = true,
                HasAudio = true
            }
        };
        var track = new VietsubSubtitleTrack
        {
            DisplayName = "Translated",
            LanguageCode = "en",
            Source = "IMPORTED_SRT",
            Cues =
            [
                new VietsubSubtitleCue
                {
                    StartMilliseconds = 100,
                    EndMilliseconds = 2_000,
                    OriginalText = "Hello",
                    TranslatedText = "Xin chào"
                }
            ]
        };
        await subtitleStore.SaveTrackAsync(project.ProjectId, track);
        project.ActiveSubtitleTrackId = track.TrackId;
        project.AudioMixSettings = new VietsubAudioMixSettings
        {
            OriginalVolume = 0.25,
            TranslatedVoiceVolume = 1.15,
            AutoDuckOriginal = true
        };
        var voicePath = paths.GetProjectPath(project.ProjectId, "voice", "timeline.wav");
        var voiceBytes = Enumerable.Range(0, 128).Select(index => (byte)index).ToArray();
        await File.WriteAllBytesAsync(voicePath, voiceBytes);
        var voiceHash = Convert.ToHexString(SHA256.HashData(voiceBytes)).ToLowerInvariant();
        var voiceArtifact = new VietsubVoiceArtifact(
            Guid.NewGuid(),
            track.TrackId,
            track.Revision,
            VietsubVoiceArtifactKinds.Timeline,
            null,
            Path.GetRelativePath(paths.GetProjectDirectory(project.ProjectId), voicePath),
            voiceBytes.Length,
            voiceHash,
            new string('b', 64),
            VietsubVoiceEngines.Piper,
            VietsubVoiceCatalog.PiperEngineVersion,
            VietsubVoiceCatalog.PiperModelId,
            VietsubVoiceCatalog.PiperModelVersion,
            VietsubVoiceCatalog.PiperVoiceId,
            9_000,
            22_050,
            1,
            VietsubVoiceArtifactStatuses.Ready,
            VietsubVoiceTimingStatuses.Natural,
            DateTime.UtcNow,
            DateTime.UtcNow,
            [track.Cues[0].CueId]);
        Assert.True(await voiceStore.SaveArtifactAsync(project.ProjectId, voiceArtifact, track.Revision));
        await projectStore.SaveAsync(project);
        await using var session = new VietsubProjectSession(projectStore, project, TimeSpan.FromHours(1));
        await session.StartAsync();

        var runner = new ExportProcessRunner();
        var preflight = new ReadyMediaPreflight();
        var mediaProbe = new FfprobeService("ffprobe-test", runner);
        var mediaImport = new VietsubMediaImportService(paths, preflight, mediaProbe);
        var service = new VietsubVideoExportService(
            new AllowLocalJobAuthorizer(),
            projectStore,
            mediaImport,
            subtitleStore,
            voiceStore,
            paths,
            preflight,
            mediaProbe,
            "ffmpeg-test",
            runner);
        var destination = Path.Combine(_root, "out", "result.mp4");

        var result = await service.ExportAsync(
            session,
            "owner",
            project.OrganizationId,
            destination,
            CancellationToken.None);

        Assert.True(File.Exists(destination));
        Assert.Equal("result.mp4", result.FileName);
        Assert.Equal(9, result.DurationSeconds);
        Assert.NotNull(runner.FfmpegArguments);
        Assert.Contains("-vf", runner.FfmpegArguments!);
        Assert.Equal(2, runner.FfmpegArguments!.Count(argument => argument == "-i"));
        Assert.Contains("-filter_complex", runner.FfmpegArguments!);
        Assert.Contains("libx264", runner.FfmpegArguments!);
        Assert.Contains(runner.FfmpegArguments!, argument => argument.Contains("subtitles=filename=", StringComparison.Ordinal));
        Assert.Contains(runner.FfmpegArguments!, argument => argument.Contains("volume=0.25", StringComparison.Ordinal));
        Assert.Contains(runner.FfmpegArguments!, argument => argument.Contains("volume=1.15", StringComparison.Ordinal));
        Assert.Contains(runner.FfmpegArguments!, argument => argument.Contains("sidechaincompress", StringComparison.Ordinal));
        Assert.Contains(runner.FfmpegArguments!, argument => argument.Contains("alimiter=limit=0.95", StringComparison.Ordinal));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(Path.GetDirectoryName(destination)!),
            path => path.Contains(".partial.mp4", StringComparison.OrdinalIgnoreCase));

        // Selecting no voice permits original audio; restoring a selection requires a current timeline.
        var revision = await subtitleStore.SetVoiceEnabledAsync(project.ProjectId, track.TrackId,
            track.Revision, [track.Cues[0].CueId], false);
        var originalDestination = Path.Combine(_root, "out", "original-only.mp4");
        await service.ExportAsync(session, "owner", project.OrganizationId, originalDestination, CancellationToken.None);
        Assert.True(File.Exists(originalDestination));
        Assert.Equal(1, runner.FfmpegArguments!.Count(argument => argument == "-i"));
        await subtitleStore.SetVoiceEnabledAsync(project.ProjectId, track.TrackId, revision, [track.Cues[0].CueId], true);
        var staleDestination = Path.Combine(_root, "out", "stale.mp4");
        var error = await Assert.ThrowsAsync<VietsubVideoExportException>(() => service.ExportAsync(
            session, "owner", project.OrganizationId, staleDestination, CancellationToken.None));
        Assert.Equal(VietsubVideoExportErrorCodes.VoiceChanged, error.Code);
        Assert.False(File.Exists(staleDestination));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("error")]
    public async Task BridgeExport_ReportsOutcomeAndReleasesAwaitingUi(string outcome)
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        var projects = new VietsubProjectStore(paths, subtitles);
        var project = await projects.CreateAsync(Guid.NewGuid(), "owner", "Export completion");
        var sourcePath = Path.Combine(_root, "input.mp4");
        var sourceBytes = "stable-test-video"u8.ToArray();
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);
        project.SourceVideo = new VietsubMediaReference
        {
            ImportMode = VietsubMediaImportModes.Link,
            OriginalPath = sourcePath,
            FileName = "input.mp4",
            SizeBytes = sourceBytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant(),
            SourceLastWriteAtUtc = File.GetLastWriteTimeUtc(sourcePath),
            Metadata = new VietsubMediaMetadata
            {
                DurationSeconds = 9, Width = 1920, Height = 1080, HasVideo = true, HasAudio = true
            }
        };
        var track = new VietsubSubtitleTrack
        {
            DisplayName = "Translated", LanguageCode = "en", Source = "IMPORTED_SRT",
            Cues = [new VietsubSubtitleCue
            {
                StartMilliseconds = 100, EndMilliseconds = 2_000,
                OriginalText = "Hello", TranslatedText = "Xin chào"
            }]
        };
        await subtitles.SaveTrackAsync(project.ProjectId, track);
        project.ActiveSubtitleTrackId = track.TrackId;
        await projects.SaveAsync(project);
        var runner = new ExportProcessRunner { FailRender = outcome == "error" };
        var preflight = new ReadyMediaPreflight();
        var probe = new FfprobeService("ffprobe-test", runner);
        var mediaImport = new VietsubMediaImportService(paths, preflight, probe);
        var service = new VietsubVideoExportService(
            new AllowLocalJobAuthorizer(), projects, mediaImport, subtitles,
            new VietsubVoiceStore(paths, subtitles), paths, preflight, probe, "ffmpeg-test", runner);
        var destination = Path.Combine(_root, "out", "finished.mp4");
        var cancel = outcome == "cancel";
        var responses = new List<string>();
        using var bridge = new VietsubWebBridge(true, responses.Add, projects,
            () => new VietsubUserContext("owner", project.OrganizationId),
            subtitleService: new VietsubSubtitleService(paths, subtitles),
            videoExportSelector: () => cancel ? null : destination, videoExportService: service);
        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.project.open", requestId = "open", payload = new { projectId = project.ProjectId }
        }));
        responses.Clear();

        await bridge.TryHandleAsync("""{"type":"vietsub.video.export","requestId":"export"}""");

        var messages = responses.Select(json => JsonSerializer.Deserialize<JsonElement>(json)).ToArray();
        Assert.All(messages, message => Assert.Equal("export", message.GetProperty("requestId").GetString()));
        var states = messages.Where(message => message.GetProperty("type").GetString() == "vietsub.state").ToArray();
        Assert.True(states.First().GetProperty("payload").GetProperty("busy").GetBoolean());
        Assert.False(states.Last().GetProperty("payload").GetProperty("busy").GetBoolean());
        var types = messages.Select(message => message.GetProperty("type").GetString()).ToArray();
        Assert.Equal(outcome == "success", File.Exists(destination));
        if (outcome == "error")
        {
            Assert.Equal("vietsub.error", types.Last());
            Assert.Equal(VietsubVideoExportErrorCodes.RenderFailed,
                messages.Last().GetProperty("error").GetProperty("code").GetString());
            Assert.DoesNotContain("vietsub.operation.completed", types);
            Assert.DoesNotContain("vietsub.video.export.completed", types);
        }
        else
        {
            var resultType = cancel ? "vietsub.video.export.cancelled" : "vietsub.video.export.completed";
            Assert.Single(messages, message => message.GetProperty("type").GetString() == resultType);
            Assert.Equal("vietsub.state", types[^2]);
            Assert.Equal("vietsub.operation.completed", types.Last());
            Assert.Single(types, type => type == "vietsub.operation.completed");
            Assert.True(Array.IndexOf(types, resultType) < types.Length - 2);
            if (!cancel)
            {
                var result = messages.Single(message => message.GetProperty("type").GetString() == resultType);
                Assert.Equal("finished.mp4", result.GetProperty("payload").GetProperty("fileName").GetString());
            }
        }

        // A subsequent export must work after every terminal outcome.
        cancel = false;
        runner.FailRender = false;
        responses.Clear();
        await bridge.TryHandleAsync("""{"type":"vietsub.video.export","requestId":"export-again"}""");
        var last = JsonSerializer.Deserialize<JsonElement>(responses.Last());
        Assert.Equal("vietsub.operation.completed", last.GetProperty("type").GetString());
        Assert.Equal("export-again", last.GetProperty("requestId").GetString());
    }

    private sealed class AllowLocalJobAuthorizer : IVietsubLocalJobAuthorizer
    {
        public Task AuthorizeAsync(
            string userId,
            Guid organizationId,
            VietsubProjectManifest project,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ReadyMediaPreflight : IMediaToolPreflightService
    {
        private static readonly MediaToolStatusSummary Ready = new(
            true,
            null,
            "ready",
            "test",
            "test",
            DateTime.UtcNow);

        public Task<MediaToolStatusSummary> GetStatusAsync(bool force, CancellationToken cancellationToken) =>
            Task.FromResult(Ready);

        public Task<MediaToolStatusSummary> RequireReadyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Ready);
    }

    private sealed class ExportProcessRunner : IExternalProcessRunner
    {
        public string[]? FfmpegArguments { get; private set; }
        public bool FailRender { get; set; }

        public async Task<ProcessExecutionResult> RunAsync(
            string executable,
            IEnumerable<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var values = arguments.ToArray();
            if (string.Equals(executable, "ffmpeg-test", StringComparison.Ordinal))
            {
                FfmpegArguments = values;
                if (FailRender) return new(1, string.Empty, "Fixture render failed.");
                await File.WriteAllBytesAsync(values[^1], "rendered-mp4"u8.ToArray(), cancellationToken);
                return new(0, string.Empty, string.Empty);
            }
            const string probe = """
                {"streams":[{"codec_type":"video","width":1920,"height":1080,"codec_name":"h264"},{"codec_type":"audio","codec_name":"aac","sample_rate":"48000"}],"format":{"duration":"9.0"}}
                """;
            return new(0, probe, string.Empty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
