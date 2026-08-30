namespace TOOL_LOCAL.Jobs;

public interface IJobStore
{
    Task<Guid> EnqueueAsync(EnqueueJobCommand command, CancellationToken cancellationToken = default);

    Task RecoverExpiredAsync(CancellationToken cancellationToken = default);

    Task<ClaimedJob?> ClaimAsync(string workerId, int leaseSeconds, CancellationToken cancellationToken = default);

    Task<bool> HeartbeatAsync(
        Guid jobId,
        string workerId,
        int leaseSeconds,
        decimal? progressPercent,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(Guid jobId, string workerId, string? resultJson, CancellationToken cancellationToken = default);

    Task RetryOrFailAsync(
        ClaimedJob job,
        string workerId,
        string errorCode,
        string errorMessage,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default);

    Task FailAsync(
        Guid jobId,
        string workerId,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken = default);
}
