using Microsoft.Data.Sqlite;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubJobTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"videomaker-vietsub-jobs-{Guid.NewGuid():N}");

    [Fact]
    public async Task Store_InitializesSchema3_AndRejectsSecondActiveJobForProject()
    {
        var (paths, store) = CreateStore();
        var projectId = Guid.NewGuid();

        var first = await store.CreateAsync(
            projectId,
            VietsubJobTypes.OcrLocal,
            ["PROBE", "OCR"]);

        var conflict = await Assert.ThrowsAsync<VietsubJobException>(() =>
            store.CreateAsync(
                projectId,
                VietsubJobTypes.TranslateLocal,
                ["TRANSLATE"]));
        Assert.Equal("vietsub_job_already_active", conflict.Code);
        Assert.Equal(VietsubJobStatus.Pending, first.Status);

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.GetProjectPath(projectId, "project.db"),
            Pooling = false
        }.ToString());
        await connection.OpenAsync();
        await using var version = connection.CreateCommand();
        version.CommandText = "SELECT schema_version FROM schema_info LIMIT 1;";
        Assert.Equal(3L, Convert.ToInt64(await version.ExecuteScalarAsync()));
        await using var tables = connection.CreateCommand();
        tables.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('local_jobs', 'local_job_steps', 'local_job_events');
            """;
        Assert.Equal(3L, Convert.ToInt64(await tables.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Store_MigratesSchema2DatabaseToSchema3()
    {
        var paths = new VietsubAppPaths(_root);
        var projectId = Guid.NewGuid();
        paths.CreateProjectDirectories(projectId);
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.GetProjectPath(projectId, "project.db"),
            Pooling = false
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE schema_info(schema_version INTEGER NOT NULL);
                INSERT INTO schema_info(schema_version) VALUES(2);
                """;
            await command.ExecuteNonQueryAsync();
        }

        await new VietsubSubtitleStore(paths).InitializeAsync(projectId);

        await using var verify = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.GetProjectPath(projectId, "project.db"),
            Pooling = false
        }.ToString());
        await verify.OpenAsync();
        await using var version = verify.CreateCommand();
        version.CommandText = "SELECT schema_version FROM schema_info LIMIT 1;";
        Assert.Equal(3L, Convert.ToInt64(await version.ExecuteScalarAsync()));
        await using var index = verify.CreateCommand();
        index.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index' AND name = 'ux_local_jobs_project_active';
            """;
        Assert.Equal(1L, Convert.ToInt64(await index.ExecuteScalarAsync()));
    }

    [Fact]
    public void StateMachine_RejectsInvalidTransition_AndTracksAttempt()
    {
        var now = DateTime.UtcNow;
        var job = new VietsubLocalJob
        {
            Id = Guid.NewGuid(),
            ProjectId = Guid.NewGuid(),
            Type = VietsubJobTypes.OcrLocal,
            Status = VietsubJobStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        VietsubJobStateMachine.Apply(job, VietsubJobStatus.Running, now.AddSeconds(1));

        Assert.Equal(1, job.AttemptCount);
        Assert.Equal(VietsubJobStatus.Running, job.Status);
        Assert.Throws<InvalidOperationException>(() =>
            VietsubJobStateMachine.Apply(job, VietsubJobStatus.Pending, now.AddSeconds(2)));
    }

    [Fact]
    public async Task Manager_CompletesJob_PersistsProgressCheckpointAndEvents()
    {
        var (_, store) = CreateStore();
        var executor = new CompletingExecutor();
        await using var manager = new VietsubJobManager(
            store,
            new VietsubJobExecutorRegistry([executor]));
        var projectId = Guid.NewGuid();

        var queued = await manager.EnqueueAsync(
            projectId,
            VietsubJobTypes.OcrLocal,
            ["PROBE", "OCR"]);
        var completed = await WaitForStatusAsync(
            manager,
            projectId,
            queued.Id,
            VietsubJobStatusNames.Completed);

        Assert.Equal(100, completed.ProgressPercent);
        Assert.Equal(1, completed.AttemptCount);
        Assert.All(completed.Steps, step => Assert.Equal(VietsubJobStatusNames.Completed, step.Status));
        var persisted = await store.GetAsync(projectId, queued.Id);
        Assert.Equal("{\"frame\":12}", persisted!.CheckpointJson);
        var events = await store.LoadEventsAsync(projectId, queued.Id);
        Assert.Contains(events, item => item.EventType == "STARTED");
        Assert.Contains(events, item => item.EventType == "COMPLETED");
    }

    [Fact]
    public async Task Manager_PausesAndResumesFromPersistedCheckpoint()
    {
        var (_, store) = CreateStore();
        var executor = new PausingThenCompletingExecutor();
        await using var manager = new VietsubJobManager(
            store,
            new VietsubJobExecutorRegistry([executor]));
        var projectId = Guid.NewGuid();
        var queued = await manager.EnqueueAsync(
            projectId,
            VietsubJobTypes.OcrLocal,
            ["OCR"]);
        await executor.FirstAttemptStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var paused = await manager.PauseAsync(projectId, queued.Id);

        Assert.Equal(VietsubJobStatusNames.Paused, paused.Status);
        Assert.Equal("{\"frame\":4}", (await store.GetAsync(projectId, queued.Id))!.CheckpointJson);

        await manager.ResumeAsync(projectId, queued.Id);
        var completed = await WaitForStatusAsync(
            manager,
            projectId,
            queued.Id,
            VietsubJobStatusNames.Completed);

        Assert.Equal(2, completed.AttemptCount);
        Assert.Equal(2, executor.AttemptCount);
    }

    [Fact]
    public async Task Store_RecoversRunningJobAsInterrupted()
    {
        var (_, store) = CreateStore();
        var projectId = Guid.NewGuid();
        var job = await store.CreateAsync(projectId, VietsubJobTypes.OcrLocal, ["OCR"]);
        await store.TransitionAsync(
            projectId,
            job.Id,
            VietsubJobStatus.Running,
            "STARTED");

        var recovered = await store.MarkRunningAsInterruptedAsync(projectId);
        var persisted = await store.GetAsync(projectId, job.Id);

        Assert.Equal(1, recovered);
        Assert.Equal(VietsubJobStatus.Interrupted, persisted!.Status);
        Assert.Equal("vietsub_job_interrupted", persisted.ErrorCode);
    }

    [Fact]
    public async Task SubtitleStore_SavesTrackAndJobCheckpointAtomically()
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        var jobs = new VietsubJobStore(paths, subtitles);
        var projectId = Guid.NewGuid();
        var job = await jobs.CreateAsync(projectId, VietsubJobTypes.OcrLocal, ["OCR"]);
        await jobs.TransitionAsync(projectId, job.Id, VietsubJobStatus.Running, "STARTED");
        var now = DateTime.UtcNow;
        var track = new VietsubSubtitleTrack
        {
            TrackId = Guid.NewGuid(),
            DisplayName = "OCR EN",
            LanguageCode = "en",
            Source = "PADDLE_OCR_LOCAL",
            Revision = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await subtitles.SaveTrackAndJobCheckpointAsync(
            projectId,
            track,
            job.Id,
            "{\"frame\":10}");

        Assert.Equal("{\"frame\":10}", (await jobs.GetAsync(projectId, job.Id))!.CheckpointJson);
        Assert.Equal(1, Assert.Single(await subtitles.LoadTracksAsync(projectId)).Revision);

        track.Revision = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            subtitles.SaveTrackAndJobCheckpointAsync(
                projectId,
                track,
                Guid.NewGuid(),
                "{\"frame\":20}"));

        Assert.Equal(1, Assert.Single(await subtitles.LoadTracksAsync(projectId)).Revision);
        Assert.Equal("{\"frame\":10}", (await jobs.GetAsync(projectId, job.Id))!.CheckpointJson);
    }

    private (VietsubAppPaths Paths, VietsubJobStore Store) CreateStore()
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        return (paths, new VietsubJobStore(paths, subtitles));
    }

    private static async Task<VietsubJobSummary> WaitForStatusAsync(
        VietsubJobManager manager,
        Guid projectId,
        Guid jobId,
        string expectedStatus)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            var job = await manager.GetAsync(projectId, jobId, timeout.Token);
            if (job?.Status == expectedStatus)
            {
                return job;
            }
            await Task.Delay(20, timeout.Token);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class CompletingExecutor : IVietsubJobExecutor
    {
        public string JobType => VietsubJobTypes.OcrLocal;

        public async Task ExecuteAsync(
            VietsubJobExecutionContext context,
            CancellationToken cancellationToken)
        {
            await context.ReportProgressAsync(
                new VietsubJobProgressUpdate("PROBE", 100, 20, "Đã đọc video."),
                cancellationToken);
            await context.ReportProgressAsync(
                new VietsubJobProgressUpdate(
                    "OCR",
                    100,
                    100,
                    "Đã nhận dạng.",
                    "{\"frame\":12}"),
                cancellationToken);
        }
    }

    private sealed class PausingThenCompletingExecutor : IVietsubJobExecutor
    {
        private int _attemptCount;

        public TaskCompletionSource FirstAttemptStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int AttemptCount => Volatile.Read(ref _attemptCount);

        public string JobType => VietsubJobTypes.OcrLocal;

        public async Task ExecuteAsync(
            VietsubJobExecutionContext context,
            CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _attemptCount);
            if (attempt == 1)
            {
                await context.ReportProgressAsync(
                    new VietsubJobProgressUpdate(
                        "OCR",
                        25,
                        25,
                        "Đã xử lý frame 4.",
                        "{\"frame\":4}"),
                    cancellationToken);
                FirstAttemptStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return;
            }

            Assert.Equal("{\"frame\":4}", context.Job.CheckpointJson);
            await context.ReportProgressAsync(
                new VietsubJobProgressUpdate("OCR", 100, 100, "Hoàn thành."),
                cancellationToken);
        }
    }
}
