using System.Text.Json;
using System.Collections.Concurrent;
using TOOL_LOCAL.Vietsub;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Subtitles;
using TOOL_LOCAL.Vietsub.Translation;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubTranslationBridgeTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"videomaker-vietsub-translation-bridge-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("IMPORTED_SRT", 1, true)]
    [InlineData("PADDLE_OCR_LOCAL", 0, true)]
    [InlineData("PADDLE_OCR_LOCAL", 1, false)]
    public async Task Bridge_RequiresActiveOcrTrackWithCuesBeforeCallingProvider(
        string trackSource,
        int cueCount,
        bool activateTrack)
    {
        var provider = new BridgeTranslationProvider();
        await using var fixture = await CreateFixtureAsync(
            trackSource,
            cueCount,
            activateTrack,
            provider);
        var responses = new ConcurrentQueue<string>();
        using var bridge = fixture.CreateBridge(responses.Enqueue);
        await OpenProjectAsync(bridge, fixture.Project.ProjectId);
        responses.Clear();

        await StartTranslationAsync(bridge, fixture.Track);

        using var error = ParseSingleMessage(responses, "vietsub.error");
        Assert.Equal(
            VietsubTranslationErrorCodes.SourceTrackRequired,
            error.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(
            "Bạn cần quét OCR nhận dạng phụ đề trước khi dịch.",
            error.RootElement.GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(0, provider.CallCount);
        Assert.Empty(await fixture.Jobs.ListAsync(fixture.Project.ProjectId));
    }

    [Fact]
    public async Task Bridge_ReportsMissingRuntimeWithoutExposingLocalPaths()
    {
        await using var fixture = await CreateFixtureAsync(
            "PADDLE_OCR_LOCAL",
            1,
            activateTrack: true,
            provider: null);
        var responses = new List<string>();
        using var bridge = fixture.CreateBridge(responses.Add);

        await bridge.TryHandleAsync(
            """{"type":"vietsub.translation.runtime.status","requestId":"translation-runtime","payload":{}}""");

        using var response = ParseSingleMessage(responses, "vietsub.translation.runtime.status");
        var payload = response.RootElement.GetProperty("payload");
        Assert.Equal(VietsubTranslationRuntimeStatusNames.NotInstalled, payload.GetProperty("status").GetString());
        Assert.False(payload.GetProperty("ready").GetBoolean());
        Assert.Equal(VietsubTranslationErrorCodes.RuntimeNotInstalled, payload.GetProperty("errorCode").GetString());
        Assert.DoesNotContain(_root, response.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bridge_ReportsDisabledFeatureAsInformationalInsteadOfMissingRuntime()
    {
        await using var fixture = await CreateFixtureAsync(
            "PADDLE_OCR_LOCAL",
            1,
            activateTrack: true,
            provider: null,
            translationFeatureEnabled: false);
        var responses = new List<string>();
        using var bridge = fixture.CreateBridge(responses.Add);

        await bridge.TryHandleAsync(
            """{"type":"vietsub.translation.runtime.status","requestId":"translation-disabled","payload":{}}""");

        using var response = ParseSingleMessage(responses, "vietsub.translation.runtime.status");
        var payload = response.RootElement.GetProperty("payload");
        Assert.Equal(VietsubTranslationRuntimeStatusNames.Disabled, payload.GetProperty("status").GetString());
        Assert.False(payload.GetProperty("ready").GetBoolean());
        Assert.Equal(VietsubTranslationErrorCodes.FeatureDisabled, payload.GetProperty("errorCode").GetString());
        Assert.Contains("Đây không phải lỗi", payload.GetProperty("message").GetString());
        Assert.DoesNotContain(_root, response.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Registry_DisabledFeatureRejectsInstallWithStableFeatureCode()
    {
        var registry = new VietsubTranslationProviderRegistry(featureEnabled: false);

        var error = await Assert.ThrowsAsync<VietsubTranslationException>(() =>
            registry.InstallDefaultAsync(progress: null, CancellationToken.None));

        Assert.Equal(VietsubTranslationErrorCodes.FeatureDisabled, error.Code);
        Assert.Contains("Đây không phải lỗi", error.Message);
    }

    [Fact]
    public async Task Bridge_ValidOcrTrack_StartsOneTranslationJobAndRefreshesSubtitlesOnCompletion()
    {
        var provider = new BridgeTranslationProvider();
        await using var fixture = await CreateFixtureAsync(
            "PADDLE_OCR_LOCAL",
            2,
            activateTrack: true,
            provider);
        var responses = new ConcurrentQueue<string>();
        using var bridge = fixture.CreateBridge(responses.Enqueue);
        await OpenProjectAsync(bridge, fixture.Project.ProjectId);
        responses.Clear();

        await StartTranslationAsync(bridge, fixture.Track);
        var job = Assert.Single(await fixture.Jobs.ListAsync(fixture.Project.ProjectId));
        await WaitForTerminalAsync(fixture.Manager, fixture.Project.ProjectId, job.Id);
        await WaitUntilAsync(() => Task.FromResult(
            responses.Any(response => HasMessageType(response, "vietsub.translation.completed"))
            && responses.Any(response => HasMessageType(response, "vietsub.subtitle.changed"))));

        Assert.Equal(1, provider.CallCount);
        Assert.Single(await fixture.Jobs.ListAsync(fixture.Project.ProjectId));
        Assert.All(
            Assert.Single(await fixture.Subtitles.LoadTracksAsync(fixture.Project.ProjectId)).Cues,
            cue => Assert.False(string.IsNullOrWhiteSpace(cue.TranslatedText)));
        var project = await fixture.Projects.LoadForBackgroundJobAsync(fixture.Project.ProjectId);
        Assert.Equal(VietsubProjectStatuses.Completed, project.Status);
    }

    [Fact]
    public async Task Bridge_low_ram_warning_requires_confirmation_before_creating_job()
    {
        var provider = new WarningBridgeTranslationProvider();
        await using var fixture = await CreateFixtureAsync(
            "PADDLE_OCR_LOCAL",
            1,
            activateTrack: true,
            provider);
        var responses = new ConcurrentQueue<string>();
        using var bridge = fixture.CreateBridge(responses.Enqueue);
        await OpenProjectAsync(bridge, fixture.Project.ProjectId);
        responses.Clear();

        await StartTranslationAsync(bridge, fixture.Track, confirmResourceWarning: false);

        using var error = ParseSingleMessage(responses, "vietsub.error");
        Assert.Equal(
            VietsubTranslationErrorCodes.ResourceConfirmationRequired,
            error.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(await fixture.Jobs.ListAsync(fixture.Project.ProjectId));
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task Bridge_confirmed_low_ram_warning_snapshots_admission_and_runs_job()
    {
        var provider = new WarningBridgeTranslationProvider();
        await using var fixture = await CreateFixtureAsync(
            "PADDLE_OCR_LOCAL",
            1,
            activateTrack: true,
            provider);
        var responses = new ConcurrentQueue<string>();
        using var bridge = fixture.CreateBridge(responses.Enqueue);
        await OpenProjectAsync(bridge, fixture.Project.ProjectId);
        responses.Clear();

        await StartTranslationAsync(bridge, fixture.Track, confirmResourceWarning: true);
        var job = Assert.Single(await fixture.Jobs.ListAsync(fixture.Project.ProjectId));
        await WaitForTerminalAsync(fixture.Manager, fixture.Project.ProjectId, job.Id);
        var parameters = VietsubTranslationJobParameters.Parse(job.ParametersJson);

        Assert.Equal(3, parameters.StrategyVersion);
        Assert.True(parameters.ResourceWarningAccepted);
        Assert.True(provider.LastResourceWarningAccepted);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task Bridge_runtime_install_passes_only_explicit_resource_confirmation()
    {
        var provider = new WarningBridgeTranslationProvider();
        await using var fixture = await CreateFixtureAsync(
            "PADDLE_OCR_LOCAL",
            1,
            activateTrack: true,
            provider);
        var responses = new ConcurrentQueue<string>();
        using var bridge = fixture.CreateBridge(responses.Enqueue);
        await OpenProjectAsync(bridge, fixture.Project.ProjectId);
        responses.Clear();

        await bridge.TryHandleAsync(
            """{"type":"vietsub.translation.runtime.install","requestId":"install-warning","payload":{"confirmResourceWarning":false}}""");
        using (var error = ParseSingleMessage(responses, "vietsub.error"))
        {
            Assert.Equal(
                VietsubTranslationErrorCodes.ResourceConfirmationRequired,
                error.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        Assert.Equal(0, provider.InstallCallCount);
        responses.Clear();

        await bridge.TryHandleAsync(
            """{"type":"vietsub.translation.runtime.install","requestId":"install-confirmed","payload":{"confirmResourceWarning":true}}""");

        Assert.Equal(1, provider.InstallCallCount);
        Assert.True(provider.LastInstallResourceWarningAccepted);
    }

    [Fact]
    public async Task Bridge_RejectsUnknownTranslationRunModeBeforeCreatingJob()
    {
        var provider = new BridgeTranslationProvider();
        await using var fixture = await CreateFixtureAsync(
            "PADDLE_OCR_LOCAL",
            1,
            activateTrack: true,
            provider);
        var responses = new List<string>();
        using var bridge = fixture.CreateBridge(responses.Add);
        await OpenProjectAsync(bridge, fixture.Project.ProjectId);
        responses.Clear();

        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.job.translate",
            requestId = "translation-invalid-mode",
            payload = new
            {
                runMode = "UNKNOWN",
                expectedTrackId = fixture.Track.TrackId,
                expectedTrackRevision = fixture.Track.Revision
            }
        }));

        using var error = ParseSingleMessage(responses, "vietsub.error");
        Assert.Equal(
            VietsubTranslationErrorCodes.ContextInvalid,
            error.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0, provider.CallCount);
        Assert.Empty(await fixture.Jobs.ListAsync(fixture.Project.ProjectId));
    }

    private async Task<BridgeFixture> CreateFixtureAsync(
        string trackSource,
        int cueCount,
        bool activateTrack,
        IVietsubLocalTranslationProvider? provider,
        bool translationFeatureEnabled = true)
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        var projects = new VietsubProjectStore(paths, subtitles);
        var project = await projects.CreateAsync(Guid.NewGuid(), "bridge-owner", "Translation bridge");
        var track = new VietsubSubtitleTrack
        {
            DisplayName = "OCR English",
            LanguageCode = "en",
            Source = trackSource,
            Cues = Enumerable.Range(0, cueCount).Select(index => new VietsubSubtitleCue
            {
                StartMilliseconds = index * 2_000L,
                EndMilliseconds = index * 2_000L + 1_500,
                OriginalText = $"Source subtitle {index + 1}"
            }).ToList()
        };
        await subtitles.SaveTrackAsync(project.ProjectId, track);
        project.ActiveSubtitleTrackId = activateTrack ? track.TrackId : null;
        project.SourceLanguageCode = "en";
        project.TargetLanguageCode = "vi";
        project.ServerSynchronized = true;
        project.Status = VietsubProjectStatuses.Ready;
        project.TranslationSettings = new VietsubTranslationSettings
        {
            SourceLanguageCode = "en",
            TargetLanguageCode = "vi",
            EnginePolicy = VietsubTranslationEnginePolicies.ContextualRequired
        };
        await projects.SaveAsync(project);

        var jobs = new VietsubJobStore(paths, subtitles);
        var translations = new VietsubTranslationStore(paths, subtitles);
        var providers = new VietsubTranslationProviderRegistry(
            provider is null ? [] : [provider],
            featureEnabled: translationFeatureEnabled);
        var executor = new VietsubTranslationJobExecutor(
            projects,
            subtitles,
            translations,
            providers,
            jobs,
            paths);
        var manager = new VietsubJobManager(
            jobs,
            new VietsubJobExecutorRegistry([executor]));
        var service = new VietsubTranslationService(
            new AllowLocalJobAuthorizer(),
            subtitles,
            providers,
            manager);
        return new BridgeFixture(paths, subtitles, projects, project, track, jobs, manager, service);
    }

    private static Task OpenProjectAsync(VietsubWebBridge bridge, Guid projectId) =>
        bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.project.open",
            requestId = "translation-open",
            payload = new { projectId }
        }));

    private static Task StartTranslationAsync(
        VietsubWebBridge bridge,
        VietsubSubtitleTrack track,
        bool confirmResourceWarning = false) =>
        bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.job.translate",
            requestId = "translation-start",
            payload = new
            {
                runMode = VietsubTranslationRunModes.Continue,
                expectedTrackId = track.TrackId,
                expectedTrackRevision = track.Revision,
                confirmResourceWarning
            }
        }));

    private static JsonDocument ParseSingleMessage(IEnumerable<string> responses, string messageType) =>
        JsonDocument.Parse(Assert.Single(responses, response => HasMessageType(response, messageType)));

    private static bool HasMessageType(string response, string messageType)
    {
        using var json = JsonDocument.Parse(response);
        return json.RootElement.GetProperty("type").GetString() == messageType;
    }

    private static async Task WaitForTerminalAsync(
        VietsubJobManager manager,
        Guid projectId,
        Guid jobId)
    {
        await WaitUntilAsync(async () =>
        {
            var job = await manager.GetAsync(projectId, jobId);
            return job?.Status is VietsubJobStatusNames.Completed or VietsubJobStatusNames.Failed;
        });
        Assert.Equal(
            VietsubJobStatusNames.Completed,
            (await manager.GetAsync(projectId, jobId))?.Status);
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!await condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed record BridgeFixture(
        VietsubAppPaths Paths,
        VietsubSubtitleStore Subtitles,
        VietsubProjectStore Projects,
        VietsubProjectManifest Project,
        VietsubSubtitleTrack Track,
        VietsubJobStore Jobs,
        VietsubJobManager Manager,
        VietsubTranslationService TranslationService) : IAsyncDisposable
    {
        public VietsubWebBridge CreateBridge(Action<string> postJson) => new(
            true,
            postJson,
            Projects,
            () => new VietsubUserContext(Project.OwnerUserId, Project.OrganizationId),
            subtitleService: new VietsubSubtitleService(Paths, Subtitles),
            jobManager: Manager,
            translationService: TranslationService);

        public ValueTask DisposeAsync() => Manager.DisposeAsync();
    }

    private sealed class AllowLocalJobAuthorizer : IVietsubLocalJobAuthorizer
    {
        public Task AuthorizeAsync(
            string userId,
            Guid organizationId,
            VietsubProjectManifest project,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class BridgeTranslationProvider : IVietsubLocalTranslationProvider
    {
        private int _callCount;

        public VietsubLocalTranslationCapabilities Capabilities { get; } = new(
            "bridge-contextual-fixture",
            "1.0.0",
            ["en", "zh"],
            "vi",
            true,
            false,
            12,
            3,
            6_000,
            8_000);

        public int CallCount => Volatile.Read(ref _callCount);

        public Task<VietsubTranslationSceneResult> TranslateAsync(
            VietsubTranslationSceneRequest request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new VietsubTranslationSceneResult(
                Capabilities.EngineId,
                Capabilities.EngineVersion,
                request.Cues.Where(cue => cue.IsTarget).Select(cue =>
                    new VietsubTranslationItemResult(
                        cue.CueAlias,
                        $"Bản dịch kiểm thử {cue.CueIndex + 1}",
                        0.95,
                        [])).ToArray()));
        }
    }

    private sealed class WarningBridgeTranslationProvider : IVietsubManagedLocalTranslationProvider
    {
        private readonly BridgeTranslationProvider _inner = new();
        private int _installCallCount;

        public VietsubLocalTranslationCapabilities Capabilities => _inner.Capabilities;

        public string RuntimeProfileId => VietsubTranslationWorkerProfiles.StandardProfileId;

        public bool LowMemoryMode => false;

        public int CallCount => _inner.CallCount;

        public int InstallCallCount => Volatile.Read(ref _installCallCount);

        public bool LastResourceWarningAccepted { get; private set; }

        public bool LastInstallResourceWarningAccepted { get; private set; }

        public bool TrySelectRuntimeProfile(string profileId) =>
            string.Equals(profileId, RuntimeProfileId, StringComparison.Ordinal);

        public VietsubTranslationRuntimeStatus GetRuntimeStatus(bool selectLowerMemoryProfile = true) => new(
            VietsubTranslationRuntimeStatusNames.Ready,
            true,
            Capabilities.EngineId,
            Capabilities.EngineVersion,
            Capabilities.SupportedSourceLanguages,
            Capabilities.SupportsSceneContext,
            Capabilities.SupportsReviewPass,
            "Engine kiểm thử đã sẵn sàng.",
            RuntimeProfileId: RuntimeProfileId,
            RequiresResourceConfirmation: true,
            ResourceWarningCode: VietsubTranslationErrorCodes.ResourceConfirmationRequired,
            ResourceWarningMessage: "RAM thấp hơn mức khuyến nghị; bạn vẫn có thể tiếp tục.");

        public Task InstallAsync(
            IProgress<VietsubTranslationRuntimeInstallProgress>? progress,
            CancellationToken cancellationToken,
            bool resourceWarningAccepted = false)
        {
            Interlocked.Increment(ref _installCallCount);
            LastInstallResourceWarningAccepted = resourceWarningAccepted;
            return Task.CompletedTask;
        }

        public Task<VietsubTranslationSceneResult> TranslateAsync(
            VietsubTranslationSceneRequest request,
            CancellationToken cancellationToken)
        {
            LastResourceWarningAccepted = request.ResourceWarningAccepted;
            return _inner.TranslateAsync(request, cancellationToken);
        }
    }
}
