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
            f.Manager = new(jobs, new VietsubJobExecutorRegistry([new VietsubCloudTranslationJobExecutor(projects, f.Subtitles, translations, f.Paths, auth, f.Client, jobs)]));
            f.Service = new(auth, f.Client, f.Subtitles, f.Paths, jobs, f.Manager);
            f.Session = new(projects, f.Project, TimeSpan.FromMilliseconds(10));
            return f;
        }
        public async Task<VietsubJobSummary> Start() => (await Service.StartAsync(Session, "owner", Project.OrganizationId,
            new("CONTINUE", Track.TrackId, Track.Revision), default))!;
        public async Task<VietsubJobSummary> Terminal(Guid id)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (true)
            {
                var job = (await Manager.GetAsync(Project.ProjectId, id, timeout.Token))!;
                if (job.Status is "COMPLETED" or "FAILED") return job;
                await Task.Delay(20, timeout.Token);
            }
        }
        public async ValueTask DisposeAsync()
        { await Manager.DisposeAsync(); await Session.DisposeAsync(); SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
    private sealed class FakeAuthorizer : IVietsubLocalJobAuthorizer
    { public Task AuthorizeAsync(string user, Guid org, VietsubProjectManifest project, CancellationToken ct) => Task.CompletedTask; }
    private sealed class FakeClient : IVietsubCloudTranslationClient
    {
        public VietsubCloudStartRequest? Input; private VietsubCloudJobResponse? job;
        public int Starts, ResultReads; public bool Acked, LoseStartResponse, LoseAckResponse;
        public Func<Task>? AfterStart;
        public Task<VietsubCloudAvailability> AvailabilityAsync(Guid project, Guid org, CancellationToken ct) => Task.FromResult(new VietsubCloudAvailability(true, null, null));
        public Task<VietsubCloudJobResponse?> FindAsync(Guid project, Guid org, Guid operation, CancellationToken ct) => Task.FromResult(operation == Input?.ClientOperationId ? job : null);
        public async Task<VietsubCloudJobResponse> StartAsync(Guid project, VietsubCloudStartRequest input, CancellationToken ct)
        {
            Starts++; Input = input; var count = input.Cues.Count(x => x.IsTarget);
            job = new(Guid.NewGuid(), input.ClientOperationId, input.TrackId, VietsubCloudSnapshot.Hash(input), "COMPLETED", count, count, 0, null, null, DateTime.UtcNow.AddDays(7), false);
            if (AfterStart != null) await AfterStart();
            if (LoseStartResponse) { LoseStartResponse = false; throw new HttpRequestException("lost start response"); }
            return job;
        }
        public Task<VietsubCloudJobResponse> GetAsync(Guid project, Guid org, Guid id, CancellationToken ct) => Task.FromResult(job!);
        public Task<VietsubCloudResultPage> ResultsAsync(Guid project, Guid org, Guid id, int cursor, CancellationToken ct)
        {
            ResultReads++; Assert.False(Acked);
            return Task.FromResult(new VietsubCloudResultPage(job!.JobId, job.SnapshotHash, 1, false,
                Input!.Cues.Where(x => x.IsTarget).Select(x => new VietsubCloudCueResult(x.CueId, x.InputFingerprint, "Xin chào", [])).ToArray()));
        }
        public Task<VietsubCloudJobResponse> ControlAsync(Guid project, Guid org, Guid id, string action, CancellationToken ct)
        {
            Assert.Equal("ack", action); Acked = true;
            if (LoseAckResponse) { LoseAckResponse = false; throw new HttpRequestException("lost acknowledgement"); }
            return Task.FromResult(job!);
        }
    }
}
