using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Jobs;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubOcrExecutorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"videomaker-vietsub-ocr-executor-{Guid.NewGuid():N}");

    [Fact]
    public async Task Executor_CreatesNewTrackAndAtomicSrt_WithoutActivatingIt()
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        var projects = new VietsubProjectStore(paths, subtitles);
        var project = await projects.CreateAsync(Guid.NewGuid(), "owner", "OCR fake end-to-end");
        var sourcePath = paths.GetProjectPath(project.ProjectId, "source", "fake.mp4");
        var sourceBytes = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);
        project.SourceVideo = new VietsubMediaReference
        {
            MediaId = Guid.NewGuid(),
            ImportMode = VietsubMediaImportModes.Copy,
            OriginalPath = sourcePath,
            WorkspaceRelativePath = Path.Combine("source", "fake.mp4"),
            FileName = "fake.mp4",
            SizeBytes = sourceBytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant(),
            SourceLastWriteAtUtc = File.GetLastWriteTimeUtc(sourcePath),
            Metadata = new VietsubMediaMetadata
            {
                DurationSeconds = 1,
                Width = 160,
                Height = 90,
                HasVideo = true
            }
        };
        await projects.SaveAsync(project);

        var jobStore = new VietsubJobStore(paths, subtitles);
        var executor = new VietsubOcrJobExecutor(
            projects,
            new FakeSourceResolver(sourcePath),
            new FakeFrameReader(),
            new FakeRecognizer(),
            subtitles,
            jobStore,
            paths);
        await using var manager = new VietsubJobManager(
            jobStore,
            new VietsubJobExecutorRegistry([executor]));
        var observedProgress = new ConcurrentQueue<double>();
        manager.JobChanged += (_, eventArgs) => observedProgress.Enqueue(eventArgs.Job.ProgressPercent);
        var parameters = VietsubOcrJobParameters.Create(
            project.SourceVideo.MediaId,
            project.SourceVideo.Sha256,
            1,
            160,
            90,
            0,
            new VietsubOcrSettings());

        var queued = await manager.EnqueueAsync(
            project.ProjectId,
            VietsubJobTypes.OcrLocal,
            ["OCR_PREPARE", "OCR_EXTRACT_FRAMES", "OCR_RECOGNIZE", "OCR_BUILD_CUES", "OCR_WRITE_ARTIFACT"],
            parameters.ToJson());
        var completed = await WaitForCompletionAsync(manager, project.ProjectId, queued.Id);

        Assert.NotNull(completed.OutputTrackId);
        var track = Assert.Single(await subtitles.LoadTracksAsync(project.ProjectId));
        Assert.Equal(completed.OutputTrackId, track.TrackId);
        Assert.Equal("PADDLE_OCR_LOCAL", track.Source);
        Assert.Equal("Hello subtitle", Assert.Single(track.Cues).OriginalText);
        var artifact = Assert.Single(track.Artifacts);
        Assert.Equal("SRT_ORIGINAL", artifact.ArtifactType);
        Assert.True(File.Exists(paths.GetProjectPath(project.ProjectId, artifact.WorkspaceRelativePath)));
        Assert.False(File.Exists(paths.GetProjectPath(project.ProjectId, artifact.WorkspaceRelativePath) + ".partial"));
        Assert.Null((await projects.LoadForBackgroundJobAsync(project.ProjectId)).ActiveSubtitleTrackId);
        Assert.Contains(observedProgress, value => value is > 0 and < 100);
        Assert.Equal(observedProgress.OrderBy(value => value), observedProgress);
        var events = await jobStore.LoadEventsAsync(project.ProjectId, queued.Id);
        Assert.Contains(events, item => item.Message == "Đang kiểm tra video nguồn và runtime OCR.");
    }

    [Fact]
    public async Task Executor_PauseResume_RestoresPendingCueFromCheckpoint()
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        var projects = new VietsubProjectStore(paths, subtitles);
        var project = await projects.CreateAsync(Guid.NewGuid(), "owner", "OCR pause resume");
        var sourcePath = paths.GetProjectPath(project.ProjectId, "source", "long-fake.mp4");
        var sourceBytes = new byte[] { 5, 6, 7, 8 };
        await File.WriteAllBytesAsync(sourcePath, sourceBytes);
        project.SourceVideo = new VietsubMediaReference
        {
            MediaId = Guid.NewGuid(),
            ImportMode = VietsubMediaImportModes.Copy,
            OriginalPath = sourcePath,
            WorkspaceRelativePath = Path.Combine("source", "long-fake.mp4"),
            FileName = "long-fake.mp4",
            SizeBytes = sourceBytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant(),
            SourceLastWriteAtUtc = File.GetLastWriteTimeUtc(sourcePath),
            Metadata = new VietsubMediaMetadata
            {
                DurationSeconds = 20,
                Width = 160,
                Height = 90,
                HasVideo = true
            }
        };
        await projects.SaveAsync(project);

        var frameReader = new PauseAwareFrameReader();
        var jobStore = new VietsubJobStore(paths, subtitles);
        var executor = new VietsubOcrJobExecutor(
            projects,
            new FakeSourceResolver(sourcePath),
            frameReader,
            new FakeRecognizer(),
            subtitles,
            jobStore,
            paths);
        await using var manager = new VietsubJobManager(
            jobStore,
            new VietsubJobExecutorRegistry([executor]));
        var parameters = VietsubOcrJobParameters.Create(
            project.SourceVideo.MediaId,
            project.SourceVideo.Sha256,
            20,
            160,
            90,
            0,
            new VietsubOcrSettings());
        var queued = await manager.EnqueueAsync(
            project.ProjectId,
            VietsubJobTypes.OcrLocal,
            ["OCR_PREPARE", "OCR_EXTRACT_FRAMES", "OCR_RECOGNIZE", "OCR_BUILD_CUES", "OCR_WRITE_ARTIFACT"],
            parameters.ToJson());
        await frameReader.FirstPassReadyToPause.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var paused = await manager.PauseAsync(project.ProjectId, queued.Id);

        Assert.Equal(VietsubJobStatusNames.Paused, paused.Status);
        Assert.False(string.IsNullOrWhiteSpace(
            (await jobStore.GetAsync(project.ProjectId, queued.Id))!.CheckpointJson));

        await manager.ResumeAsync(project.ProjectId, queued.Id);
        var completed = await WaitForCompletionAsync(manager, project.ProjectId, queued.Id);
        var outputTrack = Assert.Single(await subtitles.LoadTracksAsync(project.ProjectId));
        var cue = Assert.Single(outputTrack.Cues);

        Assert.Equal(2, completed.AttemptCount);
        Assert.Equal(0, cue.StartMilliseconds);
        Assert.Equal(20_000, cue.EndMilliseconds);
        Assert.Equal("Hello subtitle", cue.OriginalText);
    }

    private static async Task<VietsubJobSummary> WaitForCompletionAsync(
        VietsubJobManager manager,
        Guid projectId,
        Guid jobId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var job = await manager.GetAsync(projectId, jobId, timeout.Token);
            if (job?.Status == VietsubJobStatusNames.Completed)
            {
                return job;
            }
            if (job?.Status == VietsubJobStatusNames.Failed)
            {
                throw new Xunit.Sdk.XunitException($"OCR fake failed: {job.ErrorCode} - {job.ErrorMessage}");
            }
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

    private sealed class FakeSourceResolver(string path) : IVietsubOcrSourceResolver
    {
        public Task<string> ResolveVerifiedSourcePathAsync(
            Guid projectId,
            VietsubMediaReference media,
            CancellationToken cancellationToken = default) => Task.FromResult(path);
    }

    private sealed class FakeFrameReader : IVietsubOcrFrameReader
    {
        public async IAsyncEnumerable<VietsubRawVideoFrame> ReadAsync(
            string sourcePath,
            int sourceWidth,
            int sourceHeight,
            int rotationDegrees,
            VietsubNormalizedRegion normalizedRegion,
            VietsubOcrProfile profile,
            long startMilliseconds = 0,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var pixels = new byte[160 * 40 * 3];
            for (var offset = 0; offset < pixels.Length; offset += 3)
            {
                pixels[offset] = 10;
                pixels[offset + 1] = 10;
                pixels[offset + 2] = 10;
            }
            for (var index = 0; index < 3; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new VietsubRawVideoFrame(
                    index,
                    startMilliseconds + index * profile.SampleIntervalMilliseconds,
                    160,
                    40,
                    (byte[])pixels.Clone());
                await Task.Yield();
            }
        }
    }

    private sealed class PauseAwareFrameReader : IVietsubOcrFrameReader
    {
        private int _readCount;

        public TaskCompletionSource FirstPassReadyToPause { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<VietsubRawVideoFrame> ReadAsync(
            string sourcePath,
            int sourceWidth,
            int sourceHeight,
            int rotationDegrees,
            VietsubNormalizedRegion normalizedRegion,
            VietsubOcrProfile profile,
            long startMilliseconds = 0,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var readCount = Interlocked.Increment(ref _readCount);
            var pixels = new byte[160 * 40 * 3];
            var finalTimestamp = readCount == 1 ? 16_000L : 19_750L;
            var frameIndex = 0L;
            for (var timestamp = startMilliseconds;
                 timestamp <= finalTimestamp;
                 timestamp += profile.SampleIntervalMilliseconds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new VietsubRawVideoFrame(
                    frameIndex++,
                    timestamp,
                    160,
                    40,
                    (byte[])pixels.Clone());
                await Task.Yield();
            }

            if (readCount == 1)
            {
                FirstPassReadyToPause.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }
    }

    private sealed class FakeRecognizer : IVietsubOcrRecognizer
    {
        public Task<VietsubOcrRuntimeStatus> GetRuntimeStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new VietsubOcrRuntimeStatus(
                true,
                null,
                "ready",
                [VietsubOcrLanguageCodes.English, VietsubOcrLanguageCodes.Chinese]));

        public Task<VietsubOcrRecognitionResult> RecognizeAsync(
            VietsubRawVideoFrame frame,
            string languageCode,
            CancellationToken cancellationToken) =>
            Task.FromResult(new VietsubOcrRecognitionResult("Hello subtitle", 0.95f));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
