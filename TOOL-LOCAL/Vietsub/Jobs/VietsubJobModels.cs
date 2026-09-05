namespace TOOL_LOCAL.Vietsub.Jobs;

internal enum VietsubJobStatus
{
    Pending,
    Running,
    Pausing,
    Paused,
    Interrupted,
    Completed,
    Failed,
    Cancelled
}

internal static class VietsubJobStatusNames
{
    public const string Pending = "PENDING";
    public const string Running = "RUNNING";
    public const string Pausing = "PAUSING";
    public const string Paused = "PAUSED";
    public const string Interrupted = "INTERRUPTED";
    public const string Completed = "COMPLETED";
    public const string Failed = "FAILED";
    public const string Cancelled = "CANCELLED";

    public static string ToStorage(VietsubJobStatus status) => status switch
    {
        VietsubJobStatus.Pending => Pending,
        VietsubJobStatus.Running => Running,
        VietsubJobStatus.Pausing => Pausing,
        VietsubJobStatus.Paused => Paused,
        VietsubJobStatus.Interrupted => Interrupted,
        VietsubJobStatus.Completed => Completed,
        VietsubJobStatus.Failed => Failed,
        VietsubJobStatus.Cancelled => Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static VietsubJobStatus Parse(string value) => value switch
    {
        Pending => VietsubJobStatus.Pending,
        Running => VietsubJobStatus.Running,
        Pausing => VietsubJobStatus.Pausing,
        Paused => VietsubJobStatus.Paused,
        Interrupted => VietsubJobStatus.Interrupted,
        Completed => VietsubJobStatus.Completed,
        Failed => VietsubJobStatus.Failed,
        Cancelled => VietsubJobStatus.Cancelled,
        _ => throw new InvalidDataException($"Trạng thái job Vietsub không được hỗ trợ: {value}.")
    };

    public static bool IsActive(VietsubJobStatus status) => status is
        VietsubJobStatus.Pending or
        VietsubJobStatus.Running or
        VietsubJobStatus.Pausing or
        VietsubJobStatus.Paused;
}

internal static class VietsubJobTypes
{
    public const string ExtractAudio = "EXTRACT_AUDIO";
    public const string TranscribeLocal = "TRANSCRIBE_LOCAL";
    public const string OcrLocal = "OCR_LOCAL";
    public const string TranslateLocal = "TRANSLATE_LOCAL";
    public const string TranslateCloud = "TRANSLATE_CLOUD";
    public const string SynthesizeVoiceLocal = "SYNTHESIZE_VOICE_LOCAL";
    public const string ExportVideoLocal = "EXPORT_VIDEO_LOCAL";

    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        ExtractAudio,
        TranscribeLocal,
        OcrLocal,
        TranslateLocal,
        TranslateCloud,
        SynthesizeVoiceLocal,
        ExportVideoLocal
    };

    public static string Normalize(string value)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (!Allowed.Contains(normalized))
        {
            throw new ArgumentException("Loại job Vietsub không được hỗ trợ.", nameof(value));
        }

        return normalized;
    }
}

internal sealed class VietsubLocalJob
{
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public string Type { get; set; } = string.Empty;

    public VietsubJobStatus Status { get; set; } = VietsubJobStatus.Pending;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public double ProgressPercent { get; set; }

    public string? StatusMessage { get; set; }

    public Guid? InputTrackId { get; set; }

    public Guid? OutputTrackId { get; set; }

    public int? InputRevision { get; set; }

    public string ParametersJson { get; set; } = "{}";

    public string? CheckpointJson { get; set; }

    public string? MetricsJson { get; set; }

    public int AttemptCount { get; set; }

    public int MaxAttempts { get; set; } = 3;

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public List<VietsubLocalJobStep> Steps { get; set; } = [];
}

internal sealed class VietsubLocalJobStep
{
    public int Index { get; set; }

    public string Code { get; set; } = string.Empty;

    public VietsubJobStatus Status { get; set; } = VietsubJobStatus.Pending;

    public double ProgressPercent { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }
}

internal sealed record VietsubJobStepSummary(
    string Code,
    string Status,
    double ProgressPercent,
    string? ErrorCode,
    string? ErrorMessage);

internal sealed record VietsubJobSummary(
    Guid Id,
    Guid ProjectId,
    string Type,
    string Status,
    double ProgressPercent,
    string? StatusMessage,
    Guid? OutputTrackId,
    int AttemptCount,
    int MaxAttempts,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<VietsubJobStepSummary> Steps)
{
    public static VietsubJobSummary From(VietsubLocalJob job) => new(
        job.Id,
        job.ProjectId,
        job.Type,
        VietsubJobStatusNames.ToStorage(job.Status),
        job.ProgressPercent,
        job.StatusMessage,
        job.OutputTrackId,
        job.AttemptCount,
        job.MaxAttempts,
        job.ErrorCode,
        job.ErrorMessage,
        job.CreatedAtUtc,
        job.UpdatedAtUtc,
        job.CompletedAtUtc,
        job.Steps
            .OrderBy(step => step.Index)
            .Select(step => new VietsubJobStepSummary(
                step.Code,
                VietsubJobStatusNames.ToStorage(step.Status),
                step.ProgressPercent,
                step.ErrorCode,
                step.ErrorMessage))
            .ToArray());
}

internal sealed record VietsubJobProgressUpdate(
    string StepCode,
    double StepProgressPercent,
    double JobProgressPercent,
    string? StatusMessage = null,
    string? CheckpointJson = null,
    string? MetricsJson = null);

internal sealed class VietsubJobExecutionException(
    string code,
    string message,
    bool retryable = true,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;

    public bool Retryable { get; } = retryable;
}

internal sealed class VietsubJobException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
