using System.Globalization;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_LOCAL.Vietsub.Media;

internal sealed record VietsubTimelineThumbnailReady(
    Guid MediaId,
    string SourceSha256,
    int ProfileVersion,
    int Index,
    string Url,
    long Revision,
    long TimestampMilliseconds,
    long StartMilliseconds,
    long EndMilliseconds);

internal sealed record VietsubTimelineThumbnailFailed(
    Guid MediaId,
    string SourceSha256,
    int ProfileVersion,
    int Index,
    string ErrorCode);

internal sealed class VietsubTimelineThumbnailService : IAsyncDisposable
{
    internal const int ThumbnailCount = 12;
    internal const int ProfileVersion = 1;
    private readonly VietsubAppPaths _paths;
    private readonly VietsubMediaImportService _mediaImportService;
    private readonly IMediaToolPreflightService _preflight;
    private readonly string _ffmpegPath;
    private readonly IExternalProcessRunner _processRunner;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly LinkedList<ThumbnailWorkItem> _queue = [];
    private readonly Dictionary<string, ThumbnailWorkItem> _pending = new(StringComparer.Ordinal);
    private readonly Task _worker;
    private CancellationTokenSource? _activeSourceCancellation;
    private ActiveSource? _activeSource;
    private bool _disposed;

    public VietsubTimelineThumbnailService(
        VietsubAppPaths paths,
        VietsubMediaImportService mediaImportService,
        IMediaToolPreflightService preflight,
        string ffmpegPath,
        IExternalProcessRunner processRunner)
    {
        _paths = paths;
        _mediaImportService = mediaImportService;
        _preflight = preflight;
        _ffmpegPath = ffmpegPath;
        _processRunner = processRunner;
        _worker = Task.Run(ProcessQueueAsync);
    }

    public event EventHandler<VietsubTimelineThumbnailReady>? ThumbnailReady;

    public event EventHandler<VietsubTimelineThumbnailFailed>? ThumbnailFailed;

    public void Request(VietsubProjectManifest project, IReadOnlyList<int> indices)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(indices);
        var source = ResolveSource(project);
        Activate(source);
        var missing = new List<int>();

        foreach (var index in indices.Distinct())
        {
            if (index is < 0 or >= ThumbnailCount)
            {
                continue;
            }

            var ready = TryCreateReady(source, index);
            if (ready is not null)
            {
                ThumbnailReady?.Invoke(this, ready);
                continue;
            }

            missing.Add(index);
        }
        if (missing.Count > 0)
        {
            QueueBatch(source, missing, prioritize: true);
        }
    }

    public async Task<IReadOnlyList<string>> EnsureAsync(
        VietsubProjectManifest project,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var source = ResolveSource(project);
        Activate(source);
        var generated = new List<string>(ThumbnailCount);
        for (var index = 0; index < ThumbnailCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ready = TryCreateReady(source, index);
            if (ready is null)
            {
                var task = QueueBatch(source, [index], prioritize: false)[0];
                await task.WaitAsync(cancellationToken);
                ready = TryCreateReady(source, index);
            }
            if (ready is null)
            {
                throw new VietsubMediaException(
                    "vietsub_thumbnail_generation_failed",
                    "FFmpeg không thể tạo ảnh timeline cho video.");
            }
            generated.Add(ready.Url);
            progress?.Report((index + 1d) * 100d / ThumbnailCount);
        }
        return generated;
    }

    public void CancelActive()
    {
        lock (_sync)
        {
            _activeSource = null;
            _activeSourceCancellation?.Cancel();
            _activeSourceCancellation?.Dispose();
            _activeSourceCancellation = null;
            CancelQueuedWork();
        }
    }

    public IReadOnlyList<string> GetExistingUrls(VietsubProjectManifest project) =>
        GetExistingTimelineThumbnails(project).Select(item => item.Url).ToArray();

    public IReadOnlyList<VietsubTimelineThumbnailSummary> GetExistingTimelineThumbnails(
        VietsubProjectManifest project)
    {
        if (project.SourceVideo is not { } media)
        {
            return [];
        }

        return Enumerable.Range(0, ThumbnailCount)
            .Select(index => TryCreateReady(new ActiveSource(
                project.ProjectId,
                media.MediaId,
                string.Empty,
                media.Sha256.ToLowerInvariant(),
                media.Metadata.DurationSeconds), index))
            .Where(item => item is not null)
            .Select(item => new VietsubTimelineThumbnailSummary(
                item!.Index,
                item.ProfileVersion,
                item.SourceSha256,
                item.Url,
                item.Revision,
                item.TimestampMilliseconds,
                item.StartMilliseconds,
                item.EndMilliseconds))
            .ToArray();
    }

    public string? ResolveExistingPath(Guid projectId, string sha256, int index)
    {
        var path = ResolveArtifactPath(projectId, sha256, index);
        return path is not null && IsUsable(path) ? path : null;
    }

    internal string? ResolveArtifactPath(Guid projectId, string sha256, int index) =>
        !IsSha256(sha256) || index is < 0 or >= ThumbnailCount
            ? null
            : GetThumbnailPath(projectId, sha256, index);

    internal bool HasStaleArtifacts(Guid projectId, string currentSha256)
    {
        if (!IsSha256(currentSha256))
        {
            return false;
        }

        try
        {
            var root = _paths.GetProjectPath(projectId, "thumbnails", $"v{ProfileVersion}");
            return Directory.Exists(root)
                && Directory.EnumerateDirectories(root)
                    .Where(path => !string.Equals(
                        Path.GetFileName(path),
                        currentSha256,
                        StringComparison.OrdinalIgnoreCase))
                    .SelectMany(path => Directory.EnumerateFiles(path, "*.jpg", SearchOption.TopDirectoryOnly))
                    .Any(IsUsable);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private ActiveSource ResolveSource(VietsubProjectManifest project)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var media = project.SourceVideo
            ?? throw new VietsubMediaException("vietsub_media_source_required", "Dự án chưa có video nguồn.");
        var status = _mediaImportService.GetSourceStatus(project.ProjectId, media);
        if (!status.Available || status.Changed || string.IsNullOrWhiteSpace(status.EffectivePath))
        {
            throw new VietsubMediaException(
                status.IssueCode ?? "vietsub_media_source_unavailable",
                "Video nguồn không còn sẵn sàng để tạo ảnh timeline.");
        }
        return new(
            project.ProjectId,
            media.MediaId,
            status.EffectivePath,
            media.Sha256.ToLowerInvariant(),
            media.Metadata.DurationSeconds);
    }

    private void Activate(ActiveSource source)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_activeSource?.HasSameIdentity(source) == true)
            {
                return;
            }
            _activeSourceCancellation?.Cancel();
            _activeSourceCancellation?.Dispose();
            CancelQueuedWork();
            _activeSourceCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token);
            _activeSource = source;
        }
    }

    private IReadOnlyList<Task> QueueBatch(
        ActiveSource source,
        IReadOnlyList<int> indices,
        bool prioritize)
    {
        var tasks = new List<Task>(indices.Count);
        var queued = false;
        lock (_sync)
        {
            if (_activeSource?.HasSameIdentity(source) != true
                || _activeSourceCancellation is null)
            {
                return indices.Select(_ => Task.CompletedTask).ToArray();
            }

            var ordered = prioritize ? indices.Reverse() : indices;
            foreach (var index in ordered)
            {
                var key = GetPendingKey(source, index);
                if (_pending.TryGetValue(key, out var existing))
                {
                    var node = _queue.Find(existing);
                    if (prioritize && node is not null)
                    {
                        _queue.Remove(node);
                        _queue.AddFirst(node);
                    }
                    tasks.Add(existing.Completion.Task);
                    continue;
                }

                var item = new ThumbnailWorkItem(
                    source,
                    index,
                    key,
                    _activeSourceCancellation.Token,
                    new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
                _pending.Add(key, item);
                if (prioritize)
                {
                    _queue.AddFirst(item);
                }
                else
                {
                    _queue.AddLast(item);
                }
                tasks.Add(item.Completion.Task);
                queued = true;
            }
        }
        if (queued)
        {
            _queueSignal.Release();
        }
        if (prioritize)
        {
            tasks.Reverse();
        }
        return tasks;
    }

    private async Task ProcessQueueAsync()
    {
        while (!_lifetimeCancellation.IsCancellationRequested)
        {
            try
            {
                await _queueSignal.WaitAsync(_lifetimeCancellation.Token);
            }
            catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
            {
                break;
            }

            while (TryTakeNext(out var item))
            {
                await GenerateAndNotifyAsync(item);
            }
        }
    }

    private bool TryTakeNext(out ThumbnailWorkItem item)
    {
        lock (_sync)
        {
            if (_disposed || _queue.First is null)
            {
                item = null!;
                return false;
            }
            item = _queue.First.Value;
            _queue.RemoveFirst();
            return true;
        }
    }

    private async Task GenerateAndNotifyAsync(ThumbnailWorkItem item)
    {
        try
        {
            item.CancellationToken.ThrowIfCancellationRequested();
            var outputPath = GetThumbnailPath(
                item.Source.ProjectId,
                item.Source.SourceSha256,
                item.Index);
            if (!IsUsable(outputPath))
            {
                await _preflight.RequireReadyAsync(item.CancellationToken);
                await GenerateAsync(
                    item.Source.SourcePath,
                    item.Source.DurationSeconds,
                    item.Index,
                    outputPath,
                    item.CancellationToken);
            }

            var ready = TryCreateReady(item.Source, item.Index);
            if (ready is not null && IsActive(item.Source))
            {
                ThumbnailReady?.Invoke(this, ready);
                item.Completion.TrySetResult();
            }
            else
            {
                item.Completion.TrySetCanceled(item.CancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            item.Completion.TrySetCanceled(item.CancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (IsActive(item.Source))
            {
                ThumbnailFailed?.Invoke(this, new(
                    item.Source.MediaId,
                    item.Source.SourceSha256,
                    ProfileVersion,
                    item.Index,
                    exception is VietsubMediaException mediaException
                        ? mediaException.Code
                        : "vietsub_thumbnail_generation_failed"));
            }
            item.Completion.TrySetException(exception);
        }
        finally
        {
            lock (_sync)
            {
                if (_pending.TryGetValue(item.PendingKey, out var pending)
                    && ReferenceEquals(pending, item))
                {
                    _pending.Remove(item.PendingKey);
                }
            }
        }
    }

    private void CancelQueuedWork()
    {
        foreach (var item in _queue)
        {
            _pending.Remove(item.PendingKey);
            item.Completion.TrySetCanceled(item.CancellationToken);
        }
        _queue.Clear();
    }

    private bool IsActive(ActiveSource source)
    {
        lock (_sync)
        {
            return !_disposed && _activeSource?.HasSameIdentity(source) == true;
        }
    }

    private VietsubTimelineThumbnailReady? TryCreateReady(ActiveSource source, int index)
    {
        var path = GetThumbnailPath(source.ProjectId, source.SourceSha256, index);
        if (!IsUsable(path))
        {
            return null;
        }
        var (timestamp, start, end) = GetTimeline(source.DurationSeconds, index);
        return new(
            source.MediaId,
            source.SourceSha256,
            ProfileVersion,
            index,
            Playback.VietsubMediaPlaybackService.CreateThumbnailUrl(
                source.ProjectId,
                source.MediaId,
                source.SourceSha256,
                index),
            File.GetLastWriteTimeUtc(path).Ticks,
            timestamp,
            start,
            end);
    }

    private async Task GenerateAsync(
        string sourcePath,
        decimal durationSeconds,
        int index,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var outputDirectory = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(outputDirectory);
        var partialPath = Path.Combine(
            outputDirectory,
            $"{index:D3}.{Guid.NewGuid():N}.partial.jpg");
        try
        {
            var timestamp = GetTimestamp(durationSeconds, index)
                .ToString("0.###", CultureInfo.InvariantCulture);
            var result = await _processRunner.RunAsync(
                _ffmpegPath,
                [
                    "-hide_banner", "-loglevel", "error",
                    "-ss", timestamp,
                    "-i", sourcePath,
                    "-map", "0:v:0",
                    "-frames:v", "1",
                    "-vf", "scale=240:135:force_original_aspect_ratio=increase,crop=240:135",
                    "-an", "-sn", "-dn",
                    "-threads", "1",
                    "-q:v", "5",
                    "-update", "1",
                    "-y", partialPath
                ],
                TimeSpan.FromMinutes(2),
                cancellationToken);
            if (result.ExitCode != 0 || !IsUsable(partialPath))
            {
                throw new VietsubMediaException(
                    "vietsub_thumbnail_generation_failed",
                    "FFmpeg không thể tạo ảnh timeline cho video.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partialPath, outputPath, overwrite: true);
        }
        finally
        {
            TryDelete(partialPath);
        }
    }

    private string GetThumbnailPath(Guid projectId, string sha256, int index)
    {
        if (!IsSha256(sha256) || index is < 0 or >= ThumbnailCount)
        {
            throw new ArgumentException("Định danh thumbnail không hợp lệ.");
        }
        return _paths.GetProjectPath(
            projectId,
            "thumbnails",
            $"v{ProfileVersion}",
            sha256.ToLowerInvariant(),
            $"{index:D3}.jpg");
    }

    private static (long Timestamp, long Start, long End) GetTimeline(
        decimal durationSeconds,
        int index)
    {
        var durationMilliseconds = Math.Max(
            0L,
            (long)Math.Round(durationSeconds * 1000m, MidpointRounding.AwayFromZero));
        var start = durationMilliseconds * index / ThumbnailCount;
        var end = durationMilliseconds * (index + 1L) / ThumbnailCount;
        var timestamp = durationMilliseconds * (index * 2L + 1L) / (ThumbnailCount * 2L);
        return (timestamp, start, Math.Max(start + 1, end));
    }

    private static decimal GetTimestamp(decimal durationSeconds, int index)
    {
        if (durationSeconds <= 0)
        {
            return 0;
        }
        var position = durationSeconds * (index + 0.5m) / ThumbnailCount;
        return Math.Clamp(position, 0, Math.Max(0, durationSeconds - 0.05m));
    }

    private static string GetPendingKey(ActiveSource source, int index) =>
        $"{source.ProjectId:N}:{source.SourceSha256}:{index}";

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsUsable(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 128)
            {
                return false;
            }

            Span<byte> magic = stackalloc byte[3];
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            return stream.Read(magic) == magic.Length
                && magic[0] == 0xff
                && magic[1] == 0xd8
                && magic[2] == 0xff;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _activeSource = null;
            _activeSourceCancellation?.Cancel();
            _activeSourceCancellation?.Dispose();
            _activeSourceCancellation = null;
            _lifetimeCancellation.Cancel();
            pending = _pending.Values.Select(item => item.Completion.Task).ToArray();
            CancelQueuedWork();
        }
        _queueSignal.Release();
        try
        {
            await Task.WhenAll(pending.Append(_worker));
        }
        catch (Exception exception) when (exception is OperationCanceledException
            or VietsubMediaException
            or MediaToolUnavailableException
            or IOException
            or UnauthorizedAccessException
            or TimeoutException)
        {
        }
        _queueSignal.Dispose();
        _lifetimeCancellation.Dispose();
    }

    private sealed record ActiveSource(
        Guid ProjectId,
        Guid MediaId,
        string SourcePath,
        string SourceSha256,
        decimal DurationSeconds)
    {
        public bool HasSameIdentity(ActiveSource other) =>
            ProjectId == other.ProjectId
            && MediaId == other.MediaId
            && string.Equals(SourceSha256, other.SourceSha256, StringComparison.OrdinalIgnoreCase)
            && string.Equals(SourcePath, other.SourcePath, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ThumbnailWorkItem(
        ActiveSource Source,
        int Index,
        string PendingKey,
        CancellationToken CancellationToken,
        TaskCompletionSource Completion);
}
