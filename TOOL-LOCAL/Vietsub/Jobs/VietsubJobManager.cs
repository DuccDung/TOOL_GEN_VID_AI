using System.Collections.Concurrent;
using System.Diagnostics;

namespace TOOL_LOCAL.Vietsub.Jobs;

internal sealed class VietsubJobChangedEventArgs(VietsubJobSummary job) : EventArgs
{
    public VietsubJobSummary Job { get; } = job;
}

internal sealed class VietsubJobManager : IAsyncDisposable
{
    private readonly VietsubJobStore _store;
    private readonly VietsubJobExecutorRegistry _executors;
    private readonly SemaphoreSlim _executionSlots;
    private readonly ConcurrentDictionary<Guid, ActiveExecution> _active = new();
    private readonly ConcurrentDictionary<string, byte> _recordedDiagnostics = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new();
    private int _disposed;

    public VietsubJobManager(
        VietsubJobStore store,
        VietsubJobExecutorRegistry executors,
        int maximumConcurrentJobs = 1)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(executors);
        if (maximumConcurrentJobs is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentJobs));
        }

        _store = store;
        _executors = executors;
        _executionSlots = new SemaphoreSlim(maximumConcurrentJobs, maximumConcurrentJobs);
    }

    public event EventHandler<VietsubJobChangedEventArgs>? JobChanged;

    public async Task<VietsubJobSummary> EnqueueAsync(
        Guid projectId,
        string jobType,
        IReadOnlyList<string> stepCodes,
        string parametersJson = "{}",
        Guid? inputTrackId = null,
        int? inputRevision = null,
        int maxAttempts = 3,
        bool startImmediately = true,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var job = await _store.CreateAsync(
            projectId,
            jobType,
            stepCodes,
            parametersJson,
            inputTrackId,
            inputRevision,
            maxAttempts,
            cancellationToken: cancellationToken);
        RaiseChanged(job);
        if (startImmediately)
        {
            await StartAsync(projectId, job.Id, cancellationToken);
        }
        return VietsubJobSummary.From(job);
    }

    public async Task<VietsubJobSummary> StartAsync(
        Guid projectId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        var job = await GetRequiredAsync(projectId, jobId, cancellationToken);
        if (job.Status != VietsubJobStatus.Pending)
        {
            throw InvalidTransition("Chỉ job đang chờ mới có thể bắt đầu.");
        }
        if (job.AttemptCount >= job.MaxAttempts)
        {
            throw new VietsubJobException(
                "vietsub_job_attempts_exhausted",
                "Job đã dùng hết số lần chạy cho phép.");
        }

        var execution = new ActiveExecution(
            CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token));
        if (!_active.TryAdd(jobId, execution))
        {
            throw new VietsubJobException(
                "vietsub_job_already_running",
                "Job đã được đưa vào hàng đợi thực thi.");
        }

        execution.Task = Task.Run(() => RunAsync(projectId, jobId, execution), CancellationToken.None);
        return VietsubJobSummary.From(job);
    }

    public async Task<VietsubJobSummary> PauseAsync(
        Guid projectId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var job = await GetRequiredAsync(projectId, jobId, cancellationToken);
        if (job.Status != VietsubJobStatus.Running || !_active.TryGetValue(jobId, out var execution))
        {
            throw InvalidTransition("Chỉ job đang chạy mới có thể tạm dừng.");
        }

        job = await _store.TransitionAsync(
            projectId,
            jobId,
            VietsubJobStatus.Pausing,
            "PAUSE_REQUESTED",
            "Đang lưu checkpoint để tạm dừng.",
            cancellationToken: cancellationToken);
        execution.Request(ExecutionStopReason.Pause);
        execution.Cancellation.Cancel();
        RaiseChanged(job);
        await execution.Task.WaitAsync(cancellationToken);
        return VietsubJobSummary.From(await GetRequiredAsync(projectId, jobId, cancellationToken));
    }

    public async Task<VietsubJobSummary> ResumeAsync(
        Guid projectId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var job = await GetRequiredAsync(projectId, jobId, cancellationToken);
        if (job.Status is not (VietsubJobStatus.Paused or VietsubJobStatus.Interrupted))
        {
            throw InvalidTransition("Chỉ job đã tạm dừng hoặc bị gián đoạn mới có thể tiếp tục.");
        }

        job = await MoveBackToPendingAsync(projectId, job, "RESUMED", cancellationToken);
        await StartAsync(projectId, jobId, cancellationToken);
        return VietsubJobSummary.From(job);
    }

    public async Task<VietsubJobSummary> RetryAsync(
        Guid projectId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var job = await GetRequiredAsync(projectId, jobId, cancellationToken);
        if (job.Status != VietsubJobStatus.Failed)
        {
            throw InvalidTransition("Chỉ job thất bại mới có thể chạy lại.");
        }

        job = await MoveBackToPendingAsync(projectId, job, "RETRY_QUEUED", cancellationToken);
        await StartAsync(projectId, jobId, cancellationToken);
        return VietsubJobSummary.From(job);
    }

    public async Task<VietsubJobSummary> CancelAsync(
        Guid projectId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var job = await GetRequiredAsync(projectId, jobId, cancellationToken);
        if (job.Status is VietsubJobStatus.Completed or VietsubJobStatus.Cancelled)
        {
            throw InvalidTransition("Job đã kết thúc nên không thể hủy.");
        }

        if (_active.TryGetValue(jobId, out var execution))
        {
            execution.Request(ExecutionStopReason.Cancel);
            execution.Cancellation.Cancel();
            await execution.Task.WaitAsync(cancellationToken);
            return VietsubJobSummary.From(await GetRequiredAsync(projectId, jobId, cancellationToken));
        }

        job = await _store.TransitionAsync(
            projectId,
            jobId,
            VietsubJobStatus.Cancelled,
            "CANCELLED",
            "Job đã bị hủy theo yêu cầu.",
            cancellationToken: cancellationToken);
        RaiseChanged(job);
        return VietsubJobSummary.From(job);
    }

    public async Task<int> RestoreInterruptedJobsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var count = await _store.MarkRunningAsInterruptedAsync(projectId, cancellationToken);
        if (count > 0)
        {
            foreach (var job in await _store.ListAsync(projectId, cancellationToken: cancellationToken))
            {
                if (job.Status == VietsubJobStatus.Interrupted)
                {
                    RaiseChanged(job);
                }
            }
        }
        return count;
    }

    public async Task<IReadOnlyList<VietsubJobSummary>> ListAsync(
        Guid projectId,
        int maximumCount = 20,
        CancellationToken cancellationToken = default) =>
        (await _store.ListAsync(projectId, maximumCount, cancellationToken))
            .Select(VietsubJobSummary.From)
            .ToArray();

    public async Task<VietsubJobSummary?> GetAsync(
        Guid projectId,
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await _store.GetAsync(projectId, jobId, cancellationToken);
        return job is null ? null : VietsubJobSummary.From(job);
    }

    internal void RecordDiagnostic(
        Guid projectId,
        Guid jobId,
        string eventType,
        string message)
    {
        var key = $"{projectId:D}:{jobId:D}:{eventType}";
        if (_recordedDiagnostics.TryAdd(key, 0))
        {
            _ = RecordDiagnosticCoreAsync(projectId, jobId, eventType, message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
        var executions = _active.Values.ToArray();
        foreach (var execution in executions)
        {
            execution.Request(ExecutionStopReason.Shutdown);
            execution.Cancellation.Cancel();
        }
        await Task.WhenAll(executions.Select(item => item.Task));
        _shutdown.Dispose();
        _executionSlots.Dispose();
        await _executors.DisposeAsync();
    }

    private async Task RunAsync(Guid projectId, Guid jobId, ActiveExecution execution)
    {
        var acquiredSlot = false;
        try
        {
            await _executionSlots.WaitAsync(execution.Cancellation.Token);
            acquiredSlot = true;
            var job = await GetRequiredAsync(projectId, jobId, CancellationToken.None);
            if (job.Status != VietsubJobStatus.Pending)
            {
                return;
            }

            job = await _store.TransitionAsync(
                projectId,
                jobId,
                VietsubJobStatus.Running,
                "STARTED",
                $"Bắt đầu lần chạy {job.AttemptCount + 1}/{job.MaxAttempts}.",
                cancellationToken: CancellationToken.None);
            RaiseChanged(job);

            var executor = _executors.Resolve(job.Type);
            var progressWriter = new ProgressWriter(_store, projectId, jobId, RaiseChanged);
            var context = new VietsubJobExecutionContext(
                job,
                progressWriter.ReportAsync,
                async (checkpointJson, cancellationToken) =>
                {
                    var changed = await _store.SaveCheckpointAsync(
                        projectId,
                        jobId,
                        checkpointJson,
                        cancellationToken);
                    RaiseChanged(changed);
                });
            await executor.ExecuteAsync(context, execution.Cancellation.Token);
            await progressWriter.FlushAsync(CancellationToken.None);

            var completed = await _store.TransitionAsync(
                projectId,
                jobId,
                VietsubJobStatus.Completed,
                "COMPLETED",
                "Job đã hoàn thành.",
                cancellationToken: CancellationToken.None);
            RaiseChanged(completed);
        }
        catch (OperationCanceledException)
        {
            await CompleteCancellationAsync(projectId, jobId, execution.StopReason);
        }
        catch (VietsubJobExecutionException exception)
        {
            await FailAsync(projectId, jobId, exception.Code, exception.Message);
        }
        catch (Exception exception)
        {
            await FailAsync(
                projectId,
                jobId,
                "vietsub_job_execution_failed",
                NormalizeFailureMessage(exception.Message));
        }
        finally
        {
            if (acquiredSlot)
            {
                _executionSlots.Release();
            }
            _active.TryRemove(jobId, out _);
            execution.Cancellation.Dispose();
        }
    }

    private async Task CompleteCancellationAsync(
        Guid projectId,
        Guid jobId,
        ExecutionStopReason reason)
    {
        var job = await _store.GetAsync(projectId, jobId, CancellationToken.None);
        if (job is null || job.Status is VietsubJobStatus.Completed or VietsubJobStatus.Cancelled)
        {
            return;
        }

        VietsubJobStatus? next = reason switch
        {
            ExecutionStopReason.Pause when job.Status == VietsubJobStatus.Pausing => VietsubJobStatus.Paused,
            ExecutionStopReason.Cancel => VietsubJobStatus.Cancelled,
            ExecutionStopReason.Shutdown when job.Status is VietsubJobStatus.Running or VietsubJobStatus.Pausing =>
                VietsubJobStatus.Interrupted,
            _ when job.Status is VietsubJobStatus.Running or VietsubJobStatus.Pausing => VietsubJobStatus.Interrupted,
            _ => null
        };
        if (next is null)
        {
            return;
        }

        var code = next == VietsubJobStatus.Interrupted ? "vietsub_job_interrupted" : null;
        var message = next switch
        {
            VietsubJobStatus.Paused => "Job đã tạm dừng tại checkpoint gần nhất.",
            VietsubJobStatus.Cancelled => "Job đã bị hủy theo yêu cầu.",
            _ => "Job bị gián đoạn khi ứng dụng đóng."
        };
        var changed = await _store.TransitionAsync(
            projectId,
            jobId,
            next.Value,
            VietsubJobStatusNames.ToStorage(next.Value),
            message,
            code,
            code is null ? null : message,
            CancellationToken.None);
        RaiseChanged(changed);
    }

    private async Task FailAsync(Guid projectId, Guid jobId, string code, string message)
    {
        var job = await _store.GetAsync(projectId, jobId, CancellationToken.None);
        if (job is null || job.Status is not (VietsubJobStatus.Running or VietsubJobStatus.Pausing))
        {
            return;
        }

        var changed = await _store.TransitionAsync(
            projectId,
            jobId,
            VietsubJobStatus.Failed,
            "FAILED",
            message,
            code,
            message,
            CancellationToken.None);
        RaiseChanged(changed);
    }

    private async Task<VietsubLocalJob> MoveBackToPendingAsync(
        Guid projectId,
        VietsubLocalJob job,
        string eventType,
        CancellationToken cancellationToken)
    {
        if (job.AttemptCount >= job.MaxAttempts)
        {
            throw new VietsubJobException(
                "vietsub_job_attempts_exhausted",
                "Job đã dùng hết số lần chạy cho phép.");
        }
        var changed = await _store.TransitionAsync(
            projectId,
            job.Id,
            VietsubJobStatus.Pending,
            eventType,
            "Job đã được đưa lại vào hàng đợi.",
            cancellationToken: cancellationToken);
        RaiseChanged(changed);
        return changed;
    }

    private async Task<VietsubLocalJob> GetRequiredAsync(
        Guid projectId,
        Guid jobId,
        CancellationToken cancellationToken) =>
        await _store.GetAsync(projectId, jobId, cancellationToken)
        ?? throw new VietsubJobException(
            "vietsub_job_not_found",
            "Không tìm thấy job trong dự án Vietsub hiện tại.");

    private void RaiseChanged(VietsubLocalJob job)
    {
        try
        {
            JobChanged?.Invoke(this, new VietsubJobChangedEventArgs(VietsubJobSummary.From(job)));
        }
        catch (Exception exception)
        {
            RecordDiagnostic(
                job.ProjectId,
                job.Id,
                "JOB_NOTIFICATION_FAILED",
                $"Không thể chuyển sự kiện job tới subscriber ({exception.GetType().Name}).");
        }
    }

    private async Task RecordDiagnosticCoreAsync(
        Guid projectId,
        Guid jobId,
        string eventType,
        string message)
    {
        try
        {
            await _store.AppendDiagnosticEventAsync(
                projectId,
                jobId,
                eventType,
                message,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "Không thể ghi chẩn đoán Vietsub job {0}/{1}: {2}",
                projectId,
                jobId,
                exception.GetType().Name);
        }
    }

    private static string NormalizeFailureMessage(string? message)
    {
        var normalized = string.IsNullOrWhiteSpace(message)
            ? "Job không thể hoàn thành."
            : message.Trim();
        return normalized.Length <= 1000 ? normalized : normalized[..1000];
    }

    private static VietsubJobException InvalidTransition(string message) => new(
        "vietsub_job_transition_invalid",
        message);

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private enum ExecutionStopReason
    {
        None,
        Pause,
        Cancel,
        Shutdown
    }

    private sealed class ActiveExecution(CancellationTokenSource cancellation)
    {
        private int _stopReason;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public Task Task { get; set; } = Task.CompletedTask;

        public ExecutionStopReason StopReason => (ExecutionStopReason)Volatile.Read(ref _stopReason);

        public void Request(ExecutionStopReason reason) =>
            Interlocked.CompareExchange(ref _stopReason, (int)reason, (int)ExecutionStopReason.None);
    }

    private sealed class ProgressWriter(
        VietsubJobStore store,
        Guid projectId,
        Guid jobId,
        Action<VietsubLocalJob> changed)
    {
        private static readonly TimeSpan MinimumWriteInterval = TimeSpan.FromMilliseconds(750);
        private readonly SemaphoreSlim _gate = new(1, 1);
        private VietsubJobProgressUpdate? _pending;
        private DateTime _lastWriteUtc = DateTime.MinValue;

        public async ValueTask ReportAsync(
            VietsubJobProgressUpdate update,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(update);
            await _gate.WaitAsync(cancellationToken);
            try
            {
                _pending = update;
                var now = DateTime.UtcNow;
                var mustWrite = update.CheckpointJson is not null
                    || update.JobProgressPercent >= 100
                    || now - _lastWriteUtc >= MinimumWriteInterval;
                if (mustWrite)
                {
                    await WritePendingAsync(cancellationToken);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task FlushAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                await WritePendingAsync(cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }

        private async Task WritePendingAsync(CancellationToken cancellationToken)
        {
            if (_pending is null)
            {
                return;
            }
            var job = await store.UpdateProgressAsync(projectId, jobId, _pending, cancellationToken);
            _pending = null;
            _lastWriteUtc = DateTime.UtcNow;
            changed(job);
        }
    }
}
