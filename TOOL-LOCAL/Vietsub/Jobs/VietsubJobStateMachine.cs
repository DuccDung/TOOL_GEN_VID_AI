namespace TOOL_LOCAL.Vietsub.Jobs;

internal static class VietsubJobStateMachine
{
    private static readonly IReadOnlyDictionary<VietsubJobStatus, HashSet<VietsubJobStatus>> AllowedTransitions =
        new Dictionary<VietsubJobStatus, HashSet<VietsubJobStatus>>
        {
            [VietsubJobStatus.Pending] =
                [VietsubJobStatus.Running, VietsubJobStatus.Cancelled],
            [VietsubJobStatus.Running] =
                [
                    VietsubJobStatus.Pausing,
                    VietsubJobStatus.Interrupted,
                    VietsubJobStatus.Completed,
                    VietsubJobStatus.Failed,
                    VietsubJobStatus.Cancelled
                ],
            [VietsubJobStatus.Pausing] =
                [
                    VietsubJobStatus.Paused,
                    VietsubJobStatus.Interrupted,
                    VietsubJobStatus.Completed,
                    VietsubJobStatus.Failed,
                    VietsubJobStatus.Cancelled
                ],
            [VietsubJobStatus.Paused] =
                [VietsubJobStatus.Pending, VietsubJobStatus.Cancelled],
            [VietsubJobStatus.Interrupted] =
                [VietsubJobStatus.Pending, VietsubJobStatus.Cancelled],
            [VietsubJobStatus.Failed] =
                [VietsubJobStatus.Pending, VietsubJobStatus.Cancelled],
            [VietsubJobStatus.Completed] = [],
            [VietsubJobStatus.Cancelled] = []
        };

    public static bool CanTransition(VietsubJobStatus current, VietsubJobStatus next) =>
        current == next || AllowedTransitions[current].Contains(next);

    public static void Apply(
        VietsubLocalJob job,
        VietsubJobStatus next,
        DateTime nowUtc,
        string? errorCode = null,
        string? errorMessage = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!CanTransition(job.Status, next))
        {
            throw new InvalidOperationException(
                $"Không thể chuyển job Vietsub từ {VietsubJobStatusNames.ToStorage(job.Status)} " +
                $"sang {VietsubJobStatusNames.ToStorage(next)}.");
        }

        if (job.Status == next)
        {
            return;
        }

        job.Status = next;
        job.UpdatedAtUtc = nowUtc;
        if (next == VietsubJobStatus.Running)
        {
            job.StartedAtUtc ??= nowUtc;
            job.AttemptCount++;
            job.CompletedAtUtc = null;
            job.ErrorCode = null;
            job.ErrorMessage = null;
        }

        if (next == VietsubJobStatus.Pending)
        {
            job.CompletedAtUtc = null;
            job.ErrorCode = null;
            job.ErrorMessage = null;
            job.StatusMessage = null;
        }

        if (next is VietsubJobStatus.Completed or VietsubJobStatus.Failed or VietsubJobStatus.Cancelled)
        {
            job.CompletedAtUtc = nowUtc;
        }

        if (next == VietsubJobStatus.Completed)
        {
            job.ProgressPercent = 100;
        }

        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            job.ErrorCode = errorCode.Trim();
        }
        if (!string.IsNullOrWhiteSpace(errorMessage))
        {
            job.ErrorMessage = errorMessage.Trim();
        }
    }
}
