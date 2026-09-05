using System.Text.Json;
using TOOL_LOCAL.Vietsub;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Subtitles;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubOcrLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"videomaker-vietsub-ocr-lifecycle-{Guid.NewGuid():N}");

    [Fact]
    public async Task OpeningProcessingProject_ReconcilesCompletedOcrJobAndActivatesOutputTrack()
    {
        var fixture = await CreateCompletedOcrProjectAsync(hasTranslatedActiveTrack: false);
        var responses = new List<string>();
        await using var manager = new VietsubJobManager(
            fixture.Jobs,
            new VietsubJobExecutorRegistry());
        using var bridge = new VietsubWebBridge(
            true,
            responses.Add,
            fixture.Projects,
            () => new VietsubUserContext(fixture.UserId, fixture.OrganizationId),
            subtitleService: fixture.SubtitleService,
            jobManager: manager);

        await OpenProjectAsync(bridge, fixture.ProjectId);

        var reloaded = await fixture.Projects.LoadForBackgroundJobAsync(fixture.ProjectId);
        Assert.Equal(VietsubProjectStatuses.Completed, reloaded.Status);
        Assert.Equal(fixture.OutputTrackId, reloaded.ActiveSubtitleTrackId);
        Assert.Contains(responses, response => HasMessageType(response, "vietsub.ocr.completed"));
        Assert.DoesNotContain(responses, response => HasMessageType(response, "vietsub.ocr.activation.required"));
    }

    [Fact]
    public async Task OpeningProcessingProject_PreservesTranslatedTrackUntilOcrActivationIsConfirmed()
    {
        var fixture = await CreateCompletedOcrProjectAsync(hasTranslatedActiveTrack: true);
        var responses = new List<string>();
        await using var manager = new VietsubJobManager(
            fixture.Jobs,
            new VietsubJobExecutorRegistry());
        using var bridge = new VietsubWebBridge(
            true,
            responses.Add,
            fixture.Projects,
            () => new VietsubUserContext(fixture.UserId, fixture.OrganizationId),
            subtitleService: fixture.SubtitleService,
            jobManager: manager);

        await OpenProjectAsync(bridge, fixture.ProjectId);

        var reloaded = await fixture.Projects.LoadForBackgroundJobAsync(fixture.ProjectId);
        Assert.Equal(VietsubProjectStatuses.Ready, reloaded.Status);
        Assert.Equal(fixture.PreviousTrackId, reloaded.ActiveSubtitleTrackId);
        Assert.Contains(responses, response => HasMessageType(response, "vietsub.ocr.activation.required"));
        Assert.DoesNotContain(responses, response => HasMessageType(response, "vietsub.ocr.completed"));
    }

    [Fact]
    public async Task BackgroundNotificationFailure_DoesNotBlockCompletedTrackActivation()
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        var projects = new VietsubProjectStore(paths, subtitles);
        var subtitleService = new VietsubSubtitleService(paths, subtitles);
        var jobs = new VietsubJobStore(paths, subtitles);
        var organizationId = Guid.NewGuid();
        const string userId = "ocr-notification-owner";
        var project = await projects.CreateAsync(organizationId, userId, "OCR notification failure");
        project.ServerSynchronized = true;
        project.Status = VietsubProjectStatuses.Processing;
        await projects.SaveAsync(project);

        var outputTrack = new VietsubSubtitleTrack
        {
            DisplayName = "OCR output",
            LanguageCode = "en",
            Source = "PADDLE_OCR_LOCAL",
            Cues =
            [
                new VietsubSubtitleCue
                {
                    StartMilliseconds = 0,
                    EndMilliseconds = 1_000,
                    OriginalText = "OCR output"
                }
            ]
        };
        await subtitles.SaveTrackAsync(project.ProjectId, outputTrack);
        var job = await jobs.CreateAsync(
            project.ProjectId,
            VietsubJobTypes.OcrLocal,
            ["OCR_PREPARE", "OCR_WRITE_ARTIFACT"]);
        await jobs.BindOutputTrackAsync(project.ProjectId, job.Id, outputTrack.TrackId);

        await using var manager = new VietsubJobManager(
            jobs,
            new VietsubJobExecutorRegistry([new CompletingOcrExecutor()]));
        var responses = new List<string>();
        using var bridge = new VietsubWebBridge(
            true,
            response =>
            {
                if (HasMessageType(response, "vietsub.job.changed"))
                {
                    throw new InvalidOperationException("Simulated WebView delivery failure.");
                }
                responses.Add(response);
            },
            projects,
            () => new VietsubUserContext(userId, organizationId),
            subtitleService: subtitleService,
            jobManager: manager);

        await OpenProjectAsync(bridge, project.ProjectId);
        await manager.StartAsync(project.ProjectId, job.Id);
        await WaitUntilAsync(async () =>
        {
            var current = await projects.LoadForBackgroundJobAsync(project.ProjectId);
            return current.Status == VietsubProjectStatuses.Completed
                && current.ActiveSubtitleTrackId == outputTrack.TrackId;
        });
        await WaitUntilAsync(async () =>
            (await jobs.LoadEventsAsync(project.ProjectId, job.Id))
                .Any(item => item.EventType == "JOB_NOTIFICATION_FAILED"));

        var reloaded = await projects.LoadForBackgroundJobAsync(project.ProjectId);
        Assert.Equal(VietsubProjectStatuses.Completed, reloaded.Status);
        Assert.Equal(outputTrack.TrackId, reloaded.ActiveSubtitleTrackId);
        Assert.Contains(responses, response => HasMessageType(response, "vietsub.ocr.completed"));
    }

    private async Task<OcrLifecycleFixture> CreateCompletedOcrProjectAsync(bool hasTranslatedActiveTrack)
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        var projects = new VietsubProjectStore(paths, subtitles);
        var subtitleService = new VietsubSubtitleService(paths, subtitles);
        var jobs = new VietsubJobStore(paths, subtitles);
        var organizationId = Guid.NewGuid();
        const string userId = "ocr-lifecycle-owner";
        var project = await projects.CreateAsync(organizationId, userId, "OCR lifecycle");
        Guid? previousTrackId = null;
        if (hasTranslatedActiveTrack)
        {
            var previousTrack = new VietsubSubtitleTrack
            {
                DisplayName = "Existing translated track",
                LanguageCode = "en",
                Source = "IMPORTED_SRT",
                Cues =
                [
                    new VietsubSubtitleCue
                    {
                        StartMilliseconds = 0,
                        EndMilliseconds = 1_000,
                        OriginalText = "Existing",
                        TranslatedText = "Existing translation"
                    }
                ]
            };
            await subtitles.SaveTrackAsync(project.ProjectId, previousTrack);
            previousTrackId = previousTrack.TrackId;
            project.ActiveSubtitleTrackId = previousTrack.TrackId;
        }

        var outputTrack = new VietsubSubtitleTrack
        {
            DisplayName = "OCR output",
            LanguageCode = "en",
            Source = "PADDLE_OCR_LOCAL",
            Cues =
            [
                new VietsubSubtitleCue
                {
                    StartMilliseconds = 0,
                    EndMilliseconds = 1_000,
                    OriginalText = "OCR output"
                }
            ]
        };
        await subtitles.SaveTrackAsync(project.ProjectId, outputTrack);
        var job = await jobs.CreateAsync(
            project.ProjectId,
            VietsubJobTypes.OcrLocal,
            ["OCR_PREPARE", "OCR_EXTRACT_FRAMES", "OCR_RECOGNIZE", "OCR_BUILD_CUES", "OCR_WRITE_ARTIFACT"]);
        await jobs.TransitionAsync(project.ProjectId, job.Id, VietsubJobStatus.Running, "STARTED");
        await jobs.BindOutputTrackAsync(project.ProjectId, job.Id, outputTrack.TrackId);
        await jobs.TransitionAsync(project.ProjectId, job.Id, VietsubJobStatus.Completed, "COMPLETED");
        project.Status = VietsubProjectStatuses.Processing;
        project.ServerSynchronized = true;
        await projects.SaveAsync(project);
        return new OcrLifecycleFixture(
            projects,
            subtitleService,
            jobs,
            organizationId,
            userId,
            project.ProjectId,
            previousTrackId,
            outputTrack.TrackId);
    }

    private static Task<bool> OpenProjectAsync(VietsubWebBridge bridge, Guid projectId) =>
        bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.project.open",
            requestId = "open-ocr-project",
            payload = new { projectId }
        }));

    private static bool HasMessageType(string response, string expectedType)
    {
        using var document = JsonDocument.Parse(response);
        return document.RootElement.TryGetProperty("type", out var type)
            && type.GetString() == expectedType;
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await condition())
        {
            await Task.Delay(25, timeout.Token);
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

    private sealed record OcrLifecycleFixture(
        VietsubProjectStore Projects,
        VietsubSubtitleService SubtitleService,
        VietsubJobStore Jobs,
        Guid OrganizationId,
        string UserId,
        Guid ProjectId,
        Guid? PreviousTrackId,
        Guid OutputTrackId);

    private sealed class CompletingOcrExecutor : IVietsubJobExecutor
    {
        public string JobType => VietsubJobTypes.OcrLocal;

        public Task ExecuteAsync(
            VietsubJobExecutionContext context,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
