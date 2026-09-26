using TOOL_LOCAL.Vietsub.Jobs;

namespace TOOL_TESTS.Vietsub;

internal static class VietsubJobWaiter
{
    internal static async Task<VietsubJobSummary> WaitAsync(VietsubJobManager manager, Guid projectId,
        Guid jobId, string[] expectedStatuses, TimeSpan timeout)
    {
        var completed = new TaskCompletionSource<VietsubJobSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
        VietsubJobSummary? last = null;
        void Observe(VietsubJobSummary? job)
        {
            if (job?.Id != jobId || job.ProjectId != projectId) return;
            Volatile.Write(ref last, job);
            if (expectedStatuses.Contains(job.Status)) completed.TrySetResult(job);
        }
        void Changed(object? sender, VietsubJobChangedEventArgs args) => Observe(args.Job);
        manager.JobChanged += Changed;
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            // Subscribe before the snapshot, so completion between start and wait is safe.
            // Polling GetAsync every 20ms repeatedly initializes/validates the SQLite schema.
            Observe(await manager.GetAsync(projectId, jobId, deadline.Token));
            await completed.Task.WaitAsync(deadline.Token);
            var persisted = await manager.GetAsync(projectId, jobId, deadline.Token);
            Assert.NotNull(persisted);
            Assert.Contains(persisted.Status, expectedStatuses);
            return persisted;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            var status = Volatile.Read(ref last)?.Status ?? "NOT_OBSERVED";
            var code = Volatile.Read(ref last)?.ErrorCode;
            var safeCode = code is not null && System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Za-z0-9_]{1,80}$")
                ? code : "NONE";
            throw new TimeoutException($"Job did not reach {string.Join('/', expectedStatuses)} within {timeout.TotalSeconds}s; last status={status}; error={safeCode}.");
        }
        finally { manager.JobChanged -= Changed; }
    }
}
