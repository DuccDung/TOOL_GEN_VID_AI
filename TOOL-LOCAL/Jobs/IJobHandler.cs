namespace TOOL_LOCAL.Jobs;

public interface IJobHandler
{
    string JobType { get; }

    Task<JobExecutionResult> ExecuteAsync(JobExecutionContext context, CancellationToken cancellationToken);
}

public sealed class JobExecutionContext(
    ClaimedJob job,
    Func<decimal, CancellationToken, Task> reportProgress)
{
    public ClaimedJob Job { get; } = job;

    public Task ReportProgressAsync(decimal progressPercent, CancellationToken cancellationToken = default)
    {
        if (progressPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(progressPercent));
        }

        return reportProgress(progressPercent, cancellationToken);
    }
}
