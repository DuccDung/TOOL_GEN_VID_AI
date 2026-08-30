namespace TOOL_LOCAL.Jobs;

public sealed class PersistentJobRunner : IAsyncDisposable
{
    private const int LeaseSeconds = 120;
    private readonly IJobStore _jobStore;
    private readonly IReadOnlyDictionary<string, IJobHandler> _handlers;
    private readonly string _workerId;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _workerTask;

    public PersistentJobRunner(IJobStore jobStore, IEnumerable<IJobHandler> handlers)
    {
        _jobStore = jobStore;
        _handlers = handlers.ToDictionary(x => x.JobType, StringComparer.Ordinal);
        _workerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_workerTask is not null)
        {
            return Task.CompletedTask;
        }

        _workerTask = RunAsync(_shutdown.Token);
        return _jobStore.RecoverExpiredAsync(cancellationToken);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var job = await _jobStore.ClaimAsync(_workerId, LeaseSeconds, cancellationToken);
                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }

                await ExecuteJobAsync(job, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private async Task ExecuteJobAsync(ClaimedJob job, CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(job.JobType, out var handler))
        {
            await _jobStore.FailAsync(
                job.JobId,
                _workerId,
                "JOB_HANDLER_NOT_FOUND",
                $"No handler is registered for job type '{job.JobType}'.",
                cancellationToken);
            return;
        }

        using var heartbeatShutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = RunHeartbeatAsync(job.JobId, heartbeatShutdown.Token);

        try
        {
            var context = new JobExecutionContext(
                job,
                (progress, token) => _jobStore.HeartbeatAsync(job.JobId, _workerId, LeaseSeconds, progress, token));
            var result = await handler.ExecuteAsync(context, cancellationToken);
            await _jobStore.CompleteAsync(job.JobId, _workerId, result.ResultJson, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leave the running lease intact; crash recovery will resume it safely.
        }
        catch (RetryableJobException exception)
        {
            await _jobStore.RetryOrFailAsync(
                job,
                _workerId,
                exception.Code,
                exception.Message,
                RetryDelay(job.Attempt),
                cancellationToken);
        }
        catch (Exception exception)
        {
            await _jobStore.RetryOrFailAsync(
                job,
                _workerId,
                "UNHANDLED_JOB_ERROR",
                exception.Message,
                RetryDelay(job.Attempt),
                cancellationToken);
        }
        finally
        {
            heartbeatShutdown.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when the job completes before the next heartbeat.
            }
        }
    }

    private async Task RunHeartbeatAsync(Guid jobId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var updated = await _jobStore.HeartbeatAsync(jobId, _workerId, LeaseSeconds, null, cancellationToken);
            if (!updated)
            {
                return;
            }
        }
    }

    private static TimeSpan RetryDelay(int attempt) => attempt switch
    {
        <= 1 => TimeSpan.FromSeconds(5),
        2 => TimeSpan.FromSeconds(30),
        _ => TimeSpan.FromSeconds(120)
    };

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        if (_workerTask is not null)
        {
            try
            {
                await _workerTask;
            }
            catch (OperationCanceledException)
            {
                // Normal application shutdown.
            }
        }

        _shutdown.Dispose();
    }
}
