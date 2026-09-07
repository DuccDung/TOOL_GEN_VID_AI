using System.Text;
using System.Security.Cryptography;
using System.Net;
using Microsoft.Data.Sqlite;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Playback;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubVoiceCoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"videomaker-vietsub-voice-{Guid.NewGuid():N}");

    [Fact]
    public void PhrasePlanner_GroupsOnlyCompatibleCues_AndUsesStableIds()
    {
        var settings = Settings();
        var cues = new[]
        {
            Cue(0, 900, "Xin chào", "speaker_1"),
            Cue(1_000, 1_900, "các bạn", "speaker_1"),
            Cue(2_000, 2_800, "Tôi là An.", "speaker_2"),
            Cue(3_000, 3_700, "Câu mới", "speaker_2")
        };

        var first = VietsubVoicePhrasePlanner.Plan(cues, settings);
        var second = VietsubVoicePhrasePlanner.Plan(cues, settings);

        Assert.Equal(3, first.Count);
        Assert.Equal(2, first[0].CueIds.Count);
        Assert.Equal(first.Select(item => item.PhraseId), second.Select(item => item.PhraseId));
        Assert.Equal("Xin chào các bạn", first[0].Text);
    }

    [Fact]
    public void FitPolicy_BorrowsGap_CompressesSafely_AndRequiresReviewAboveMaximum()
    {
        var settings = Settings();
        var phrase = new VietsubVoicePhrase("p", [Guid.NewGuid()], "speaker_1", new string('a', 100), 0, 1_000);

        var borrowed = VietsubVoiceTimelineFitPolicy.Evaluate(
            phrase,
            new VietsubWavMetadata(1_400, 22_050, 1, 16, 0, 1_400),
            1_500,
            settings);
        var compressed = VietsubVoiceTimelineFitPolicy.Evaluate(
            phrase,
            new VietsubWavMetadata(1_150, 22_050, 1, 16, 0, 1_150),
            1_000,
            settings);
        var borrowedAndCompressed = VietsubVoiceTimelineFitPolicy.Evaluate(
            phrase,
            new VietsubWavMetadata(1_700, 22_050, 1, 16, 0, 1_700),
            1_500,
            settings);
        var review = VietsubVoiceTimelineFitPolicy.Evaluate(
            phrase,
            new VietsubWavMetadata(1_500, 22_050, 1, 16, 0, 1_500),
            1_000,
            settings);

        Assert.Equal(VietsubVoiceTimingStatuses.BorrowedGap, borrowed.Status);
        Assert.Equal(400, borrowed.BorrowedGapMilliseconds);
        Assert.Equal(VietsubVoiceTimingStatuses.Compressed, compressed.Status);
        Assert.Equal(VietsubVoiceTimingStatuses.Compressed, borrowedAndCompressed.Status);
        Assert.Equal(500, borrowedAndCompressed.BorrowedGapMilliseconds);
        Assert.InRange(borrowedAndCompressed.Tempo, 1.13, 1.14);
        Assert.Equal(VietsubVoiceTimingStatuses.ReviewRequired, review.Status);
        Assert.True(review.SuggestedMaximumCharacters < phrase.Text.Length);
    }

    [Fact]
    public void WavInspector_ValidatesPcmAndTrimsLeadingAndTrailingSilence()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "voice.wav");
        WritePcmWav(path, 1_000, 16_000, activeStartMilliseconds: 200, activeEndMilliseconds: 700);

        var metadata = VietsubWavInspector.Inspect(path, analyzeSilence: true);

        Assert.Equal(1_000, metadata.DurationMilliseconds);
        Assert.InRange(metadata.TrimStartMilliseconds, 130, 160);
        Assert.InRange(metadata.TrimEndMilliseconds, 810, 830);
    }

    [Fact]
    public void ComponentStore_FeatureDisabled_DoesNotProbeOrDownload()
    {
        var paths = new VietsubAppPaths(_root);
        using var store = new VietsubVoiceComponentStore(paths, featureEnabled: false);

        var status = store.GetStatus();

        Assert.False(status.Ready);
        Assert.Equal("DISABLED", status.Status);
        Assert.Equal(VietsubVoiceErrorCodes.FeatureDisabled, status.ErrorCode);
    }

    [Fact]
    public void ComponentStore_FeatureEnabledWithoutComponents_RequestsInstallation()
    {
        var paths = new VietsubAppPaths(_root);
        using var store = new VietsubVoiceComponentStore(paths, featureEnabled: true);

        var status = store.GetStatus();

        Assert.False(status.Ready);
        Assert.Equal("NOT_INSTALLED", status.Status);
        Assert.Equal(VietsubVoiceErrorCodes.RuntimeNotInstalled, status.ErrorCode);
        Assert.Contains("Hãy cài", status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComponentStore_RejectsRedirectOutsidePinnedHttpsHosts()
    {
        var paths = new VietsubAppPaths(_root);
        var handler = new RedirectHandler(new Uri("http://127.0.0.1/runtime.zip"));
        using var store = new VietsubVoiceComponentStore(paths, featureEnabled: true, handler);

        var exception = await Assert.ThrowsAsync<VietsubVoiceException>(
            () => store.InstallAsync(null, CancellationToken.None));

        Assert.Equal(VietsubVoiceErrorCodes.RuntimeInstallFailed, exception.Code);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task ComponentStore_AllowsCurrentPinnedHuggingFaceCdnRedirect()
    {
        var paths = new VietsubAppPaths(_root);
        var handler = new RedirectThenErrorHandler(new Uri("https://us.aws.cdn.hf.co/model.onnx"));
        using var store = new VietsubVoiceComponentStore(paths, featureEnabled: true, handler);

        var exception = await Assert.ThrowsAsync<VietsubVoiceException>(
            () => store.InstallAsync(null, CancellationToken.None));

        Assert.Equal(VietsubVoiceErrorCodes.RuntimeInstallFailed, exception.Code);
        Assert.Equal(2, handler.RequestCount);
        Assert.Equal("us.aws.cdn.hf.co", handler.LastRequestUri?.Host);
    }

    [Fact]
    public void ComponentStore_RepairVenv_ClearsIncompleteEnvironmentBeforeRecreatingIt()
    {
        var environmentPath = Path.Combine(_root, "components", "voice", "piper", "runtime", ".venv");

        var arguments = VietsubVoiceComponentStore.BuildVenvArguments(environmentPath);

        Assert.Equal(
            ["venv", "--clear", "--python", "3.11", "--managed-python", environmentPath],
            arguments);
    }

    [Theory]
    [InlineData(VietsubTranslationQualityStatuses.Review)]
    [InlineData(VietsubTranslationQualityStatuses.Invalid)]
    [InlineData(null)]
    public void TranslationPolicy_AllowsCompletedCueRegardlessOfQualityStatus(string? qualityStatus)
    {
        var track = new VietsubSubtitleTrack
        {
            Cues = [Cue(0, 1_000, "Bản dịch đã hoàn thành.", "speaker_1")]
        };
        track.Cues[0].QualityStatus = qualityStatus;
        track.Cues[0].Warnings.Add("QUALITY_WARNING");

        VietsubVoiceTranslationPolicy.EnsureComplete(track);
    }

    [Fact]
    public void TranslationPolicy_RejectsCueWithoutVietnameseText()
    {
        var track = new VietsubSubtitleTrack
        {
            Cues = [Cue(0, 1_000, "", "speaker_1")]
        };

        var exception = Assert.Throws<VietsubVoiceException>(
            () => VietsubVoiceTranslationPolicy.EnsureComplete(track));

        Assert.Equal(VietsubVoiceErrorCodes.TranslationRequired, exception.Code);
    }

    [Fact]
    public void Playback_RequiresRegisteredCurrentProjectTrackAndHash()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "timeline.wav");
        WritePcmWav(path, 500);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        var projectId = Guid.NewGuid();
        var trackId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var project = new VietsubProjectManifest
        {
            ProjectId = projectId,
            ActiveSubtitleTrackId = trackId
        };
        var registry = new VietsubVoicePlaybackRegistry();
        registry.RegisterCurrent(new(
            projectId,
            artifactId,
            trackId,
            1,
            path,
            new FileInfo(path).Length,
            hash));
        var service = new VietsubMediaPlaybackService(null!, voiceRegistry: registry);
        var url = new Uri(VietsubVoicePlaybackRegistry.CreateUrl(projectId, artifactId, hash));

        var allowed = service.Open(url, "GET", null, project);
        var denied = service.Open(url, "GET", null, ProjectWithId(Guid.NewGuid()));
        using var allowedContent = allowed.Content;
        using var deniedContent = denied.Content;

        Assert.Equal(200, allowed.StatusCode);
        Assert.Equal(VietsubPlaybackResourceTypes.Voice, allowed.ResourceType);
        Assert.Equal(403, denied.StatusCode);
    }

    [Fact]
    public async Task Playback_RejectsArtifactImmediatelyAfterTrackRevisionChanges()
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        var projects = new VietsubProjectStore(paths, subtitles);
        var project = await projects.CreateAsync(Guid.NewGuid(), "owner", "Playback revision");
        var track = new VietsubSubtitleTrack
        {
            DisplayName = "OCR",
            LanguageCode = "en",
            Source = "PADDLE_OCR_LOCAL",
            Cues = [Cue(0, 1_000, "Xin chào", "speaker_1")]
        };
        await subtitles.SaveTrackAsync(project.ProjectId, track);
        project.ActiveSubtitleTrackId = track.TrackId;
        await projects.SaveAsync(project);
        var path = paths.GetProjectPath(project.ProjectId, "voice", "timeline.wav");
        WritePcmWav(path, 500);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        var artifactId = Guid.NewGuid();
        var voiceStore = new VietsubVoiceStore(paths, subtitles);
        var registry = new VietsubVoicePlaybackRegistry(voiceStore.IsTrackRevisionCurrent);
        registry.RegisterCurrent(new(
            project.ProjectId,
            artifactId,
            track.TrackId,
            track.Revision,
            path,
            new FileInfo(path).Length,
            hash));
        var service = new VietsubMediaPlaybackService(null!, voiceRegistry: registry);
        var url = new Uri(VietsubVoicePlaybackRegistry.CreateUrl(project.ProjectId, artifactId, hash));

        using var before = service.Open(url, "GET", null, project).Content;
        track.Revision++;
        await subtitles.SaveTrackAsync(project.ProjectId, track);
        var after = service.Open(url, "GET", null, project);
        using var afterContent = after.Content;

        Assert.Equal(403, after.StatusCode);
    }

    [Fact]
    public async Task Executor_PublishesTimelineWhenPhraseExceedsMaximumTempo()
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        var projects = new VietsubProjectStore(paths, subtitles);
        var project = await projects.CreateAsync(Guid.NewGuid(), "owner", "Voice test");
        var cue1 = Cue(0, 1_200, "Xin chào", "speaker_1");
        var cue2 = Cue(1_400, 2_600, "Hẹn gặp lại.", "speaker_2");
        cue1.QualityStatus = VietsubTranslationQualityStatuses.Review;
        cue1.Warnings.Add("QUALITY_WARNING");
        cue2.QualityStatus = VietsubTranslationQualityStatuses.Invalid;
        var track = new VietsubSubtitleTrack
        {
            DisplayName = "OCR",
            LanguageCode = "en",
            Source = "PADDLE_OCR_LOCAL",
            Cues = [cue1, cue2]
        };
        await subtitles.SaveTrackAsync(project.ProjectId, track);
        project.ActiveSubtitleTrackId = track.TrackId;
        await projects.SaveAsync(project);

        var settings = Settings();
        var parameters = new VietsubVoiceJobParameters(
            1,
            track.TrackId,
            track.Revision,
            VietsubVoiceFingerprintBuilder.BuildConfigurationFingerprint(settings),
            settings);
        var jobs = new VietsubJobStore(paths, subtitles);
        var job = await jobs.CreateAsync(
            project.ProjectId,
            VietsubJobTypes.SynthesizeVoiceLocal,
            ["VOICE_PREPARE", "VOICE_SYNTHESIZE", "VOICE_TIMELINE", "VOICE_PUBLISH"],
            parameters.ToJson(),
            track.TrackId,
            track.Revision);
        var voiceStore = new VietsubVoiceStore(paths, subtitles);
        var renderer = new VietsubVoiceTimelineRenderer(
            paths,
            new ReadyMediaPreflight(),
            "ffmpeg.exe",
            new WavWritingProcessRunner());
        var executor = new VietsubVoiceJobExecutor(
            projects,
            subtitles,
            voiceStore,
            new FixedDurationSynthesizer(1_500),
            renderer,
            jobs,
            paths);
        var context = new VietsubJobExecutionContext(
            job,
            (_, _) => ValueTask.CompletedTask,
            (_, _) => ValueTask.CompletedTask);

        await executor.ExecuteAsync(context, CancellationToken.None);

        var workspace = await voiceStore.LoadWorkspaceAsync(
            project.ProjectId,
            track.TrackId,
            track.Revision,
            project.VoiceSettings);
        Assert.NotNull(workspace.Timeline);
        var timeline = workspace.Timeline!;
        Assert.Equal(VietsubVoiceArtifactStatuses.Ready, timeline.Status);
        Assert.Equal(track.Revision, timeline.TrackRevision);
        Assert.True(File.Exists(paths.GetProjectPath(
            project.ProjectId,
            timeline.RelativePath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]))));
        Assert.Equal(VietsubVoiceTimingStatuses.ReviewRequired, timeline.TimingStatus);
        Assert.Contains(workspace.TimingDiagnostics, item => item.Status == VietsubVoiceTimingStatuses.ReviewRequired);
    }

    [LocalVoiceModelFact]
    [Trait("Category", "LocalVoiceIntegration")]
    public async Task PiperFixture_SynthesizesVietnamesePcmWav_WhenApprovedRuntimeIsConfigured()
    {
        var workspaceRoot = Environment.GetEnvironmentVariable("VIDEOMAKER_VOICE_WORKSPACE_ROOT");
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new InvalidOperationException(
                "VIDEOMAKER_VOICE_WORKSPACE_ROOT is required when real Piper verification is explicitly enabled.");
        }

        var paths = new VietsubAppPaths(workspaceRoot);
        using var components = new VietsubVoiceComponentStore(paths, featureEnabled: true);
        var status = components.GetStatus();
        Assert.True(status.Ready, status.Message);
        Directory.CreateDirectory(_root);
        var requests = new[]
        {
            new VietsubVoiceSynthesisItem(2, "probe-a", "Xin chào, đây là giọng đọc tiếng Việt.", Path.Combine(_root, "probe-a.wav")),
            new VietsubVoiceSynthesisItem(7, "probe-b", "Hẹn gặp lại trong video tiếp theo.", Path.Combine(_root, "probe-b.wav"))
        };
        var completed = new List<int>();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await new VietsubPiperVoiceSynthesizer(components).SynthesizeIncrementallyAsync(
            requests,
            item =>
            {
                completed.Add(item.Index);
                return ValueTask.CompletedTask;
            },
            CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal([2, 7], completed);
        Assert.All(requests, item =>
        {
            var metadata = VietsubWavInspector.Inspect(item.OutputPath, analyzeSilence: true);
            Assert.True(metadata.DurationMilliseconds > 100);
            Assert.Equal(16, metadata.BitsPerSample);
        });
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMinutes(10));
    }

    private static VietsubVoiceSettingsSnapshot Settings() => new(
        VietsubVoiceEngines.Piper,
        VietsubVoiceCatalog.PiperEngineVersion,
        VietsubVoiceCatalog.PiperModelId,
        VietsubVoiceCatalog.PiperModelVersion,
        VietsubVoiceCatalog.PiperVoiceId,
        500,
        8_000,
        4_500,
        600,
        1.12,
        1.20,
        true);

    private static VietsubSubtitleCue Cue(long start, long end, string text, string speaker) => new()
    {
        StartMilliseconds = start,
        EndMilliseconds = end,
        OriginalText = "source",
        TranslatedText = text,
        Speaker = speaker,
        QualityStatus = VietsubTranslationQualityStatuses.Valid,
        TranslationSource = VietsubTranslationSources.LocalAuto
    };

    private static VietsubProjectManifest ProjectWithId(Guid projectId) => new()
    {
        ProjectId = projectId,
        ActiveSubtitleTrackId = Guid.NewGuid()
    };

    private static void WritePcmWav(
        string path,
        int durationMilliseconds,
        int sampleRate = 16_000,
        int activeStartMilliseconds = 0,
        int? activeEndMilliseconds = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var frameCount = sampleRate * durationMilliseconds / 1_000;
        var dataBytes = frameCount * 2;
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        var activeStart = sampleRate * activeStartMilliseconds / 1_000;
        var activeEnd = sampleRate * (activeEndMilliseconds ?? durationMilliseconds) / 1_000;
        for (var frame = 0; frame < frameCount; frame++)
        {
            writer.Write((short)(frame >= activeStart && frame < activeEnd ? 4_000 : 0));
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FixedDurationSynthesizer(int durationMilliseconds) : IVietsubVoiceSynthesizer
    {
        public async Task SynthesizeIncrementallyAsync(
            IReadOnlyList<VietsubVoiceSynthesisItem> items,
            Func<VietsubVoiceSynthesisItem, ValueTask> onCompleted,
            CancellationToken cancellationToken)
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WritePcmWav(item.OutputPath, durationMilliseconds);
                await onCompleted(item);
            }
        }
    }

    private sealed class ReadyMediaPreflight : IMediaToolPreflightService
    {
        private static readonly MediaToolStatusSummary Ready = new(true, null, "Ready", "test", "test", DateTime.UtcNow);

        public Task<MediaToolStatusSummary> GetStatusAsync(bool force, CancellationToken cancellationToken) => Task.FromResult(Ready);

        public Task<MediaToolStatusSummary> RequireReadyAsync(CancellationToken cancellationToken) => Task.FromResult(Ready);
    }

    private sealed class WavWritingProcessRunner : IExternalProcessRunner
    {
        public Task<ProcessExecutionResult> RunAsync(
            string executable,
            IEnumerable<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            var output = arguments.Last();
            WritePcmWav(output, 3_000, 48_000);
            return Task.FromResult(new ProcessExecutionResult(0, string.Empty, string.Empty));
        }
    }

    private sealed class RedirectHandler(Uri location) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                RequestMessage = request
            };
            response.Headers.Location = location;
            return Task.FromResult(response);
        }
    }

    private sealed class RedirectThenErrorHandler(Uri location) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRequestUri = request.RequestUri;
            if (RequestCount == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    RequestMessage = request
                };
                redirect.Headers.Location = location;
                return Task.FromResult(redirect);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request
            });
        }
    }
}

internal sealed class LocalVoiceModelFactAttribute : FactAttribute
{
    public LocalVoiceModelFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("VIDEOMAKER_RUN_LOCAL_VOICE_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            Skip = "Run explicitly through scripts/Verify-VietsubVoiceModel.ps1.";
        }
    }
}
