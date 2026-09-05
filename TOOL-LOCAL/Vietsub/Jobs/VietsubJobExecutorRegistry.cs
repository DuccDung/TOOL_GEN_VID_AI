namespace TOOL_LOCAL.Vietsub.Jobs;

internal interface IVietsubJobExecutor
{
    string JobType { get; }

    Task ExecuteAsync(
        VietsubJobExecutionContext context,
        CancellationToken cancellationToken);
}

internal sealed class VietsubJobExecutionContext(
    VietsubLocalJob job,
    Func<VietsubJobProgressUpdate, CancellationToken, ValueTask> reportProgress,
    Func<string, CancellationToken, ValueTask> saveCheckpoint)
{
    public VietsubLocalJob Job { get; } = job;

    public ValueTask ReportProgressAsync(
        VietsubJobProgressUpdate update,
        CancellationToken cancellationToken = default) =>
        reportProgress(update, cancellationToken);

    public ValueTask SaveCheckpointAsync(
        string checkpointJson,
        CancellationToken cancellationToken = default) =>
        saveCheckpoint(checkpointJson, cancellationToken);
}

internal sealed class VietsubJobExecutorRegistry : IAsyncDisposable
{
    private readonly Dictionary<string, IVietsubJobExecutor> _executors =
        new(StringComparer.Ordinal);

    public VietsubJobExecutorRegistry(IEnumerable<IVietsubJobExecutor>? executors = null)
    {
        foreach (var executor in executors ?? [])
        {
            Register(executor);
        }
    }

    public void Register(IVietsubJobExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        var jobType = VietsubJobTypes.Normalize(executor.JobType);
        if (!_executors.TryAdd(jobType, executor))
        {
            throw new InvalidOperationException($"Executor cho job {jobType} đã được đăng ký.");
        }
    }

    public IVietsubJobExecutor Resolve(string jobType)
    {
        var normalized = VietsubJobTypes.Normalize(jobType);
        return _executors.TryGetValue(normalized, out var executor)
            ? executor
            : throw new VietsubJobExecutionException(
                "vietsub_job_executor_unavailable",
                $"Chưa có bộ thực thi cho job {normalized}.",
                retryable: false);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in _executors.Values.OfType<IAsyncDisposable>())
        {
            await disposable.DisposeAsync();
        }
        _executors.Clear();
    }
}
