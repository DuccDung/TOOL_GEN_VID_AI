namespace TOOL_LOCAL.Bilibili;

internal sealed class BilibiliService : IDisposable
{
    private readonly IBilibiliRuntime _runtime;
    private readonly BilibiliDownloader _downloader;
    private readonly Func<CancellationToken, Task> _authorize;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, BilibiliEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DownloadContext> _jobs = new(StringComparer.Ordinal);
    private CancellationTokenSource? _active;
    private CancellationTokenSource? _activeJob;
    private string? _activeJobId;
    private string _operation = "Idle";
    private string _folder;
    private bool _runtimeReady;
    private bool _disposed;
    private long _revision;
    private BilibiliScan? _scan;
    public event Action<BilibiliNotification>? Changed;

    private sealed class DownloadContext(BilibiliDownloadJob job, string folder)
    {
        public BilibiliDownloadJob Job { get; set; } = job;
        public string Folder { get; } = folder;
        public string? Hash { get; set; }
    }

    public BilibiliService(IBilibiliRuntime runtime, BilibiliDownloader downloader,
        Func<CancellationToken, Task> authorize, string? initialFolder = null)
    {
        _runtime = runtime;
        _downloader = downloader;
        _authorize = authorize;
        _folder = BilibiliFiles.RequireLocalDirectory(initialFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Bilibili"));
    }

    public BilibiliState Snapshot()
    {
        lock (_sync) return new(_revision, _operation, _runtimeReady, _runtime.Version,
            Path.GetFileName(_folder.TrimEnd(Path.DirectorySeparatorChar)), _scan, _entries.Values.ToArray(),
            _jobs.Values.Select(x => x.Job).ToArray());
    }

    public async Task RefreshAsync(CancellationToken token)
    {
        var ready = await _runtime.IsReadyAsync(token);
        lock (_sync) { _runtimeReady = ready; StateChanged(); }
    }

    public Task InstallAsync(CancellationToken token) => RunAsync("Installing", async activeToken =>
    {
        await _authorize(activeToken);
        await _runtime.InstallAsync(activeToken);
        lock (_sync) _runtimeReady = true;
    }, token);

    public Task ScanAsync(string input, CancellationToken token)
    {
        var url = BilibiliUrl.Normalize(input);
        return RunAsync("Scanning", async activeToken =>
        {
            await _authorize(activeToken);
            lock (_sync)
            {
                _entries.Clear();
                _scan = new(Guid.NewGuid().ToString("N"), "Scanning", 0, false, "Đang quét danh sách video…");
                StateChanged();
            }
            try
            {
                var complete = await _downloader.ScanAsync(url, entry =>
                {
                    lock (_sync)
                    {
                        _entries[entry.Id] = entry;
                        _scan = _scan! with { Count = _entries.Count };
                        Notify("bilibili.scan.progress", new BilibiliScanUpdate(++_revision, _scan, [entry]));
                    }
                }, activeToken);
                lock (_sync) _scan = _scan! with { Status = complete ? "Completed" : "Partial", Complete = complete,
                    Message = complete ? $"Đã quét xong {_entries.Count} video." : "Đã giữ các video tìm được. Một số mục chưa đọc được; danh sách chưa đầy đủ." };
            }
            catch (Exception exception)
            {
                lock (_sync) _scan = _scan! with { Status = exception is OperationCanceledException ? "Cancelled" : "Partial",
                    Complete = false, Message = SafeMessage(exception) + " Danh sách chưa đầy đủ." };
                if (exception is not OperationCanceledException) throw;
            }
        }, token);
    }

    public void SetFolder(string folder)
    {
        folder = BilibiliFiles.RequireLocalDirectory(folder);
        if (folder.Length > 140)
            throw new BilibiliException("bilibili_folder_too_long", "Hãy chọn thư mục lưu có đường dẫn ngắn hơn 140 ký tự để đủ chỗ cho tên video.");
        lock (_sync)
        {
            if (_operation != "Idle") throw Busy();
            _folder = folder;
            StateChanged();
        }
    }

    public string GetFolder(string? jobId = null)
    {
        lock (_sync)
        {
            var folder = jobId is null ? _folder : _jobs.TryGetValue(jobId, out var context)
                ? context.Folder : throw new BilibiliException("bilibili_job_missing", "Không tìm thấy lượt tải.");
            BilibiliFiles.RequireLocalDirectory(folder);
            if (!Directory.Exists(folder)) throw new BilibiliException("bilibili_folder_missing", "Thư mục chưa tồn tại. Hãy tải video hoặc chọn thư mục khác.");
            return folder;
        }
    }

    public Task DownloadAsync(string scanId, IReadOnlyList<string> ids, string quality, CancellationToken token)
    {
        if (ids.Count == 0 || ids.Count > BilibiliDownloader.MaximumEntries || ids.Any(string.IsNullOrWhiteSpace)
            || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || quality is not ("best" or "1080" or "720" or "480"))
            throw new BilibiliException("bilibili_selection_invalid", "Hãy chọn video và chất lượng tải hợp lệ.");
        return RunAsync("Downloading", async activeToken =>
        {
            await _authorize(activeToken);
            List<DownloadContext> selected;
            lock (_sync)
            {
                if (_scan?.Id != scanId || ids.Any(id => !_entries.ContainsKey(id)))
                    throw new BilibiliException("bilibili_scan_stale", "Danh sách video đã thay đổi. Hãy chọn lại video.");
                if (_jobs.Count + ids.Count > BilibiliDownloader.MaximumEntries)
                    throw new BilibiliException("bilibili_queue_full", "Hàng đợi phiên này đã đầy. Hãy mở lại ứng dụng sau khi các lượt tải hoàn tất.");
                selected = ids.Select(id => new DownloadContext(new BilibiliDownloadJob(Guid.NewGuid().ToString("N"),
                    _entries[id], quality, "Queued", 0), _folder)).ToList();
                foreach (var context in selected) _jobs.Add(context.Job.Id, context);
                StateChanged();
            }
            await RunQueueAsync(selected, activeToken);
        }, token);
    }

    public Task RetryAsync(string jobId, CancellationToken token) => RunAsync("Downloading", async activeToken =>
    {
        await _authorize(activeToken);
        DownloadContext context;
        lock (_sync)
        {
            if (!_jobs.TryGetValue(jobId, out context!) || context.Job.Status is not ("Failed" or "Cancelled"))
                throw new BilibiliException("bilibili_retry_invalid", "Chỉ thử lại lượt tải lỗi hoặc đã hủy.");
            UpdateJob(context, context.Job with { Status = "Queued", Percent = 0, Message = null, BytesPerSecond = null });
        }
        await RunQueueAsync([context], activeToken);
    }, token);

    public void Cancel(string? jobId)
    {
        lock (_sync)
        {
            if (jobId is null) { _active?.Cancel(); return; }
            if (!_jobs.TryGetValue(jobId, out var context)) return;
            if (_activeJobId == jobId) _activeJob?.Cancel();
            else if (context.Job.Status == "Queued")
                UpdateJob(context, context.Job with { Status = "Cancelled", Message = "Đã hủy lượt tải." });
        }
    }

    private async Task RunQueueAsync(IReadOnlyList<DownloadContext> queue, CancellationToken token)
    {
        foreach (var context in queue)
        {
            CancellationTokenSource jobCancellation;
            lock (_sync)
            {
                if (context.Job.Status == "Cancelled") continue;
                if (token.IsCancellationRequested)
                {
                    UpdateJob(context, context.Job with { Status = "Cancelled", Message = "Đã hủy hàng đợi." });
                    continue;
                }
                _activeJob = jobCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                _activeJobId = context.Job.Id;
                UpdateJob(context, context.Job with { Status = "Downloading", Message = "Đang lấy video…" });
            }
            using (jobCancellation)
            {
                try
                {
                    await _authorize(jobCancellation.Token);
                    var result = await _downloader.DownloadAsync(context.Job, context.Folder,
                        progress => { lock (_sync) UpdateJob(context, progress); }, jobCancellation.Token);
                    lock (_sync)
                    {
                        context.Hash = result.Hash;
                        UpdateJob(context, context.Job with { Status = "Completed", Percent = 100,
                            FileName = result.FileName, BytesPerSecond = null, Message = "Đã tải và kiểm tra video." });
                    }
                }
                catch (Exception exception)
                {
                    lock (_sync) UpdateJob(context, context.Job with { Status = exception is OperationCanceledException ? "Cancelled" : "Failed",
                        BytesPerSecond = null, Message = SafeMessage(exception) });
                }
                finally { lock (_sync) { _activeJob = null; _activeJobId = null; } }
            }
        }
    }

    private async Task RunAsync(string operation, Func<CancellationToken, Task> action, CancellationToken token)
    {
        if (!await _gate.WaitAsync(0, token)) throw Busy();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var active = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            lock (_sync) { _active = active; _operation = operation; StateChanged(); }
            try { await action(active.Token); }
            finally
            {
                lock (_sync)
                {
                    _active = null; _operation = "Idle"; StateChanged();
                    if (_disposed) CleanSessionStaging();
                }
            }
        }
        finally { _gate.Release(); }
    }

    private void UpdateJob(DownloadContext context, BilibiliDownloadJob value)
    {
        if (value.Status == "Downloading" && context.Job.Status == "Downloading")
            value = value with { Percent = Math.Max(context.Job.Percent, value.Percent) };
        context.Job = value;
        Notify("bilibili.job", new BilibiliJobUpdate(++_revision, value));
    }
    private void StateChanged() { _revision++; Notify("bilibili.state", Snapshot()); }
    private void Notify(string type, object payload) { if (!_disposed) Changed?.Invoke(new(type, payload)); }
    private static BilibiliException Busy() => new("bilibili_busy", "Hãy chờ hoặc hủy thao tác Bilibili đang chạy.");
    internal static string SafeMessage(Exception exception) => exception switch
    {
        BilibiliException known => known.Message,
        OperationCanceledException => "Đã hủy thao tác.",
        TOOL_LOCAL.Media.MediaToolUnavailableException => "Bộ FFmpeg chưa sẵn sàng. Hãy kiểm tra công cụ media trong Cài đặt.",
        TOOL_LOCAL.Authentication.AccountClientException => "Phiên đăng nhập hoặc gói sử dụng chưa sẵn sàng. Hãy kiểm tra tài khoản.",
        UnauthorizedAccessException => "Không có quyền ghi vào thư mục đã chọn.",
        IOException => "Không đọc/ghi được tệp. Hãy kiểm tra dung lượng trống, quyền thư mục và thử lại.",
        _ => "Thao tác chưa hoàn tất. Hãy kiểm tra kết nối và thử lại."
    };

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true; _lifetime.Cancel();
            if (_active is null) CleanSessionStaging();
        }
        // Active operations own and dispose their cancellation sources after their process has exited.
    }

    private void CleanSessionStaging()
    {
        foreach (var context in _jobs.Values) BilibiliFiles.CleanStaging(context.Folder, context.Job.Id);
    }
}
