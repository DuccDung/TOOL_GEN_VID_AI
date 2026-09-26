using Microsoft.Data.Sqlite;
using TOOL_LOCAL.Vietsub.Api;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Translation;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubCloudDesktopTests
{
    [Fact]
    public async Task Cloud_TranslatesEntireTrackAndPreservesManualLockedAndValidLocalCues()
    {
        await using var f = await Fixture.Create(105);
        f.Track.Cues[1].TranslatedText = "Bản sửa tay"; f.Track.Cues[1].TranslationSource = "MANUAL";
        f.Track.Cues[2].OriginalLocked = true;
        f.Track.Cues[3].TranslationLocked = true;
        f.Track.Cues[4].TranslatedText = "Bản local hợp lệ"; f.Track.Cues[4].TranslationSource = "LOCAL_AUTO"; f.Track.Cues[4].QualityStatus = "VALID";
        await f.Subtitles.SaveTrackAsync(f.Project.ProjectId, f.Track);
        var job = await f.Start(); var done = await f.Terminal(job.Id);
        Assert.Equal("COMPLETED", done.Status);
        Assert.Equal(f.Track.TrackId, done.OutputTrackId);
        Assert.Equal(105, f.Client.Input!.Cues.Count); Assert.Equal(101, f.Client.Input.Cues.Count(x => x.IsTarget));
        var track = Assert.Single(await f.Subtitles.LoadTracksAsync(f.Project.ProjectId));
        Assert.Equal("Bản sửa tay", track.Cues[1].TranslatedText); Assert.Empty(track.Cues[2].TranslatedText);
        Assert.Empty(track.Cues[3].TranslatedText); Assert.Equal("Bản local hợp lệ", track.Cues[4].TranslatedText);
        Assert.Equal(101, track.Cues.Count(x => x.TranslationSource == "CLOUD_AUTO"));
        Assert.Equal(102, track.Revision); Assert.Equal(1, f.Client.Starts); Assert.True(f.Client.Acked);
        Assert.True(Directory.GetFiles(f.Paths.GetProjectPath(f.Project.ProjectId, "subtitles"), "*.srt").Length > 0);
        Assert.Empty(Directory.GetFiles(f.Paths.GetProjectPath(f.Project.ProjectId, "subtitles"), "*.partial"));
    }

    [Fact]
    public async Task LostStartResponse_RetryFindsExistingOperationAndDoesNotPostAgain()
    {
        await using var f = await Fixture.Create(); f.Client.LoseStartResponse = true;
        var job = await f.Start(); Assert.Equal("FAILED", (await f.Terminal(job.Id)).Status);
        await f.Service.ControlRemoteAsync(f.Project.ProjectId, job.Id, "RETRY", default);
        await f.Manager.RetryAsync(f.Project.ProjectId, job.Id);
        Assert.Equal("COMPLETED", (await f.Terminal(job.Id)).Status);
        Assert.Equal(1, f.Client.Starts); Assert.True(f.Client.Acked);
    }

    [Fact]
    public async Task ManualEditDuringCloud_RequestDoesNotOverwriteAndReportsPartialResult()
    {
        await using var f = await Fixture.Create(2);
        f.Client.AfterStart = async () =>
        {
            var current = Assert.Single(await f.Subtitles.LoadTracksAsync(f.Project.ProjectId));
            current.Cues[0].TranslatedText = "Tôi đã sửa"; current.Cues[0].TranslationSource = "MANUAL";
            current.Cues[0].UpdatedAtUtc = DateTime.UtcNow.AddMilliseconds(1); current.Revision++;
            await f.Subtitles.SaveTrackAsync(f.Project.ProjectId, current);
        };
        var done = await f.Terminal((await f.Start()).Id);
        Assert.Equal("FAILED", done.Status); Assert.Equal("CLOUD_CUE_CHANGED", done.ErrorCode);
        var current = Assert.Single(await f.Subtitles.LoadTracksAsync(f.Project.ProjectId));
        Assert.Equal("Tôi đã sửa", current.Cues[0].TranslatedText); Assert.Equal("CLOUD_AUTO", current.Cues[1].TranslationSource);
        Assert.False(f.Client.Acked); Assert.Equal(1, f.Client.Starts);
    }

    [Fact]
    public async Task LostAcknowledgement_ReplayUsesLocalReceiptsWithoutIncrementingRevisionOrPosting()
    {
        await using var f = await Fixture.Create(); f.Client.LoseAckResponse = true;
        var job = await f.Start(); Assert.Equal("FAILED", (await f.Terminal(job.Id)).Status);
        var firstRevision = Assert.Single(await f.Subtitles.LoadTracksAsync(f.Project.ProjectId)).Revision;
        await f.Manager.RetryAsync(f.Project.ProjectId, job.Id);
        Assert.Equal("COMPLETED", (await f.Terminal(job.Id)).Status);
        Assert.Equal(firstRevision, Assert.Single(await f.Subtitles.LoadTracksAsync(f.Project.ProjectId)).Revision);
        Assert.Equal(1, f.Client.Starts); Assert.Equal(1, f.Client.ResultReads);
    }

    [Fact]
    public async Task StaleTrack_IsRejectedBeforeCreatingCloudJob()
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<VietsubTranslationException>(() => f.Service.StartAsync(f.Session, "owner", f.Project.OrganizationId,
            new("CONTINUE", f.Track.TrackId, f.Track.Revision + 1), default));
        Assert.Equal(0, f.Client.Starts);
    }

    [Fact]
    public async Task CancelledJob_RecoversAllServerResultsWithoutAnotherPost()
    {
        await using var f = await Fixture.Create(3);
        f.Client.LoseStartResponse = true;
        var first = await f.Start();
        Assert.Equal("FAILED", (await f.Terminal(first.Id)).Status);
        await f.Manager.CancelAsync(f.Project.ProjectId, first.Id);
        Assert.All(Assert.Single(await f.Subtitles.LoadTracksAsync(f.Project.ProjectId)).Cues, x => Assert.Empty(x.TranslatedText));
        var next = await f.Service.StartAsync(f.Session, "owner", f.Project.OrganizationId,
            new("CONTINUE", f.Track.TrackId, f.Track.Revision), default);
        Assert.Null(next);
        Assert.Equal(1, f.Client.Starts);
        Assert.Equal("CANCELLED", (await f.Manager.GetAsync(f.Project.ProjectId, first.Id))!.Status);
        var saved = Assert.Single(await f.Subtitles.LoadTracksAsync(f.Project.ProjectId));
        Assert.Equal(4, saved.Revision);
        Assert.All(saved.Cues, x => Assert.Equal("CLOUD_AUTO", x.TranslationSource));
        Assert.NotEmpty(Directory.GetFiles(f.Paths.GetProjectPath(f.Project.ProjectId, "subtitles"), "*.srt"));
        // Replaying recovery must not increment revisions or resend any cue.
        Assert.Null(await f.Service.StartAsync(f.Session, "owner", f.Project.OrganizationId,
            new("CONTINUE", saved.TrackId, saved.Revision), default));
        Assert.Equal(saved.Revision, Assert.Single(await f.Subtitles.LoadTracksAsync(f.Project.ProjectId)).Revision);
    }

    [Fact]
    public async Task CancelledJob_NewOperationOnlyTargetsCuesMissingAfterRecovery()
    {
        await using var f = await Fixture.Create(3);
        f.Client.LoseStartResponse = true;
        f.Client.FirstResultCount = 1;
        var first = await f.Start();
        await f.Terminal(first.Id);
        await f.Manager.CancelAsync(f.Project.ProjectId, first.Id);
        var next = await f.Start();
        Assert.Equal("COMPLETED", (await f.Terminal(next.Id)).Status);
        Assert.Equal(2, f.Client.Starts);
        Assert.False(f.Client.Input!.Cues[0].IsTarget);
        Assert.Equal(2, f.Client.Input.Cues.Count(x => x.IsTarget));
        Assert.Equal(2, f.Client.Input.TrackRevision);
        Assert.Equal(4, Assert.Single(await f.Subtitles.LoadTracksAsync(f.Project.ProjectId)).Revision);
    }

    [Fact]
    public async Task Recovery_NetworkFailurePreventsNewPostAndCanBeRetried()
    {
        await using var f = await Fixture.Create();
        f.Client.LoseStartResponse = true;
        var first = await f.Start();
        await f.Terminal(first.Id);
        await f.Manager.CancelAsync(f.Project.ProjectId, first.Id);
        f.Client.LoseResultsResponse = true;
        var error = await Assert.ThrowsAsync<VietsubTranslationException>(() => f.Start());
        Assert.Equal("CLOUD_SYNC_PENDING", error.Code);
        Assert.Equal(1, f.Client.Starts);
        Assert.Null(await f.Service.StartAsync(f.Session, "owner", f.Project.OrganizationId,
            new("CONTINUE", f.Track.TrackId, f.Track.Revision), default));
        Assert.Equal(1, f.Client.Starts);
    }

    [Fact]
    public async Task Recovery_PreservesManualLockedAndChangedSourceCues()
    {
        await using var f = await Fixture.Create(5);
        f.Client.LoseStartResponse = true;
        var first = await f.Start();
        await f.Terminal(first.Id);
        await f.Manager.CancelAsync(f.Project.ProjectId, first.Id);
        var edited = Assert.Single(await f.Subtitles.LoadTracksAsync(f.Project.ProjectId));
        edited.Cues[0].TranslatedText = "Đã sửa tay";
        edited.Cues[0].TranslationSource = "MANUAL";
        edited.Cues[1].TranslationLocked = true;
        edited.Cues[2].OriginalLocked = true;
        edited.Cues[3].OriginalText = "Changed source";
        edited.Cues[3].UpdatedAtUtc = DateTime.UtcNow.AddSeconds(1);
        edited.Revision++;
        await f.Subtitles.SaveTrackAsync(f.Project.ProjectId, edited);
        var next = (await f.Service.StartAsync(f.Session, "owner", f.Project.OrganizationId,
            new("CONTINUE", edited.TrackId, edited.Revision), default))!;
        Assert.Equal("COMPLETED", (await f.Terminal(next.Id)).Status);
        Assert.Equal([edited.Cues[3].CueId], f.Client.Input!.Cues.Where(x => x.IsTarget).Select(x => x.CueId));
        var saved = Assert.Single(await f.Subtitles.LoadTracksAsync(f.Project.ProjectId));
        Assert.Equal("Đã sửa tay", saved.Cues[0].TranslatedText);
        Assert.Empty(saved.Cues[1].TranslatedText);
        Assert.Empty(saved.Cues[2].TranslatedText);
        Assert.Equal("Changed source", saved.Cues[3].OriginalText);
        Assert.Equal("CLOUD_AUTO", saved.Cues[4].TranslationSource);
    }

    [Fact]
    public async Task Recovery_MissingSnapshotDoesNotExposePathOrSubmitNewOperation()
    {
        await using var f = await Fixture.Create();
        f.Client.LoseStartResponse = true;
        var first = await f.Start();
        await f.Terminal(first.Id);
        await f.Manager.CancelAsync(f.Project.ProjectId, first.Id);
        var path = VietsubCloudTranslationService.SnapshotPath(f.Paths, f.Project.ProjectId, f.Client.Input!.ClientOperationId);
        File.Delete(path);
        var error = await Assert.ThrowsAsync<VietsubTranslationException>(() => f.Start());
        Assert.Equal("CLOUD_LOCAL_STORAGE", error.Code);
        Assert.DoesNotContain(path, error.Message);
        Assert.Equal(1, f.Client.Starts);
    }

    [Fact]
    public async Task CancelledRemoteStillFinishing_BlocksNewOperationUntilResultsAreFinal()
    {
        await using var f = await Fixture.Create();
        f.Client.LoseStartResponse = true;
        var first = await f.Start();
        await f.Terminal(first.Id);
        await f.Manager.CancelAsync(f.Project.ProjectId, first.Id);
        f.Client.RemoteStillActive = true;
        var error = await Assert.ThrowsAsync<VietsubTranslationException>(() => f.Start());
        Assert.Equal("CLOUD_JOB_ACTIVE", error.Code);
        Assert.Equal(1, f.Client.Starts);
        Assert.Equal(0, f.Client.ResultReads);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "videomaker-cloud-test-" + Guid.NewGuid().ToString("N"));
        public VietsubAppPaths Paths = null!; public VietsubSubtitleStore Subtitles = null!;
        public VietsubProjectManifest Project = null!; public VietsubSubtitleTrack Track = null!;
        public VietsubProjectSession Session = null!; public VietsubJobManager Manager = null!;
        public VietsubCloudTranslationService Service = null!; public FakeClient Client = new();
        public static async Task<Fixture> Create(int count = 1)
        {
            var f = new Fixture(); f.Paths = new(f.root); f.Subtitles = new(f.Paths);
            var projects = new VietsubProjectStore(f.Paths, f.Subtitles);
            f.Project = await projects.CreateAsync(Guid.NewGuid(), "owner", "Cloud fixture");
            f.Track = new() { DisplayName = "OCR", LanguageCode = "en", Source = "PADDLE_OCR_LOCAL",
                Cues = Enumerable.Range(0, count).Select(i => new VietsubSubtitleCue { StartMilliseconds = i * 3000,
                    EndMilliseconds = i * 3000 + 3000, OriginalText = "Hello " + i, Speaker = "Alice" }).ToList() };
            await f.Subtitles.SaveTrackAsync(f.Project.ProjectId, f.Track);
            f.Project.ActiveSubtitleTrackId = f.Track.TrackId; f.Project.SourceLanguageCode = "en";
            await projects.SaveAsync(f.Project);
            var jobs = new VietsubJobStore(f.Paths, f.Subtitles); var translations = new VietsubTranslationStore(f.Paths, f.Subtitles);
            var auth = new FakeAuthorizer();
            var results = new VietsubCloudTranslationResults(projects, f.Subtitles, translations, f.Paths, f.Client);
            f.Manager = new(jobs, new VietsubJobExecutorRegistry([new VietsubCloudTranslationJobExecutor(projects, f.Subtitles, results, f.Paths, auth, f.Client, jobs)]),
                runtimeGate: new(Path.Combine(f.root, "runtime-lease")));
            f.Service = new(auth, f.Client, f.Subtitles, f.Paths, jobs, f.Manager, results);
            f.Session = new(projects, f.Project, TimeSpan.FromMilliseconds(10));
            return f;
        }
        public async Task<VietsubJobSummary> Start() => (await Service.StartAsync(Session, "owner", Project.OrganizationId,
            new("CONTINUE", Track.TrackId, Track.Revision), default))!;
        public Task<VietsubJobSummary> Terminal(Guid id) => VietsubJobWaiter.WaitAsync(
            Manager, Project.ProjectId, id, ["COMPLETED", "FAILED"], TimeSpan.FromSeconds(20));
        public async ValueTask DisposeAsync()
        { await Manager.DisposeAsync(); await Session.DisposeAsync(); VietsubTestStorage.ClearPools(root); Directory.Delete(root, true); }
    }
    private sealed class FakeAuthorizer : IVietsubLocalJobAuthorizer
    { public Task AuthorizeAsync(string user, Guid org, VietsubProjectManifest project, CancellationToken ct) => Task.CompletedTask; }
    private sealed class FakeClient : IVietsubCloudTranslationClient
    {
        public VietsubCloudStartRequest? Input; private VietsubCloudJobResponse? job;
        private readonly Dictionary<Guid, (VietsubCloudStartRequest Input, VietsubCloudJobResponse Job, int ResultCount)> history = new();
        public int Starts, ResultReads; public bool Acked, LoseStartResponse, LoseAckResponse;
        public bool LoseResultsResponse, RemoteStillActive;
        public int? FirstResultCount;
        public Func<Task>? AfterStart;
        public Task<VietsubCloudAvailability> AvailabilityAsync(Guid project, Guid org, CancellationToken ct) => Task.FromResult(new VietsubCloudAvailability(true, null, null));
        public Task<VietsubCloudJobResponse?> FindAsync(Guid project, Guid org, Guid operation, CancellationToken ct) =>
            Task.FromResult(operation == Guid.Empty && RemoteStillActive ? job : history.Values
                .Where(x => x.Input.ClientOperationId == operation).Select(x => x.Job).SingleOrDefault());
        public async Task<VietsubCloudJobResponse> StartAsync(Guid project, VietsubCloudStartRequest input, CancellationToken ct)
        {
            Starts++; Input = input; Acked = false; var count = input.Cues.Count(x => x.IsTarget);
            var resultCount = Starts == 1 ? FirstResultCount ?? count : count;
            job = new(Guid.NewGuid(), input.ClientOperationId, input.TrackId, VietsubCloudSnapshot.Hash(input),
                resultCount < count ? "CANCELLED" : "COMPLETED", count, resultCount, 0, null, null, DateTime.UtcNow.AddDays(7), false);
            history.Add(job.JobId, (input, job, resultCount));
            if (AfterStart != null) await AfterStart();
            if (LoseStartResponse) { LoseStartResponse = false; throw new HttpRequestException("lost start response"); }
            return job;
        }
        public Task<VietsubCloudJobResponse> GetAsync(Guid project, Guid org, Guid id, CancellationToken ct) => Task.FromResult(history[id].Job);
        public Task<VietsubCloudResultPage> ResultsAsync(Guid project, Guid org, Guid id, int cursor, CancellationToken ct)
        {
            ResultReads++; Assert.False(Acked);
            if (LoseResultsResponse) { LoseResultsResponse = false; throw new HttpRequestException("lost results"); }
            var prior = history[id];
            return Task.FromResult(new VietsubCloudResultPage(id, prior.Job.SnapshotHash, 1, false,
                prior.Input.Cues.Where(x => x.IsTarget).Take(prior.ResultCount)
                    .Select(x => new VietsubCloudCueResult(x.CueId, x.InputFingerprint, "Xin chào", [])).ToArray()));
        }
        public Task<VietsubCloudJobResponse> ControlAsync(Guid project, Guid org, Guid id, string action, CancellationToken ct)
        {
            Assert.Equal("ack", action); Acked = true;
            if (LoseAckResponse) { LoseAckResponse = false; throw new HttpRequestException("lost acknowledgement"); }
            return Task.FromResult(job!);
        }
    }
}
