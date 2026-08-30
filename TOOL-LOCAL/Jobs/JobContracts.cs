namespace TOOL_LOCAL.Jobs;

public static class JobStatuses
{
    public const string Pending = "Pending";
    public const string Running = "Running";
    public const string RetryScheduled = "RetryScheduled";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
    public const string Interrupted = "Interrupted";
}

public static class JobTypes
{
    public const string AnalyzeTopic = "AnalyzeTopic";
    public const string GenerateConcept = "GenerateConcept";
    public const string GenerateScript = "GenerateScript";
    public const string GenerateCharacter = "GenerateCharacter";
    public const string GenerateCharacterReference = "GenerateCharacterReference";
    public const string GenerateScenePlan = "GenerateScenePlan";
    public const string GeneratePrompt = "GeneratePrompt";
    public const string GenerateVideo = "GenerateVideo";
    public const string PollVideo = "PollVideo";
    public const string DownloadVideo = "DownloadVideo";
    public const string ValidateVideo = "ValidateVideo";
    public const string GenerateVoice = "GenerateVoice";
    public const string GenerateSubtitle = "GenerateSubtitle";
    public const string RenderFinalVideo = "RenderFinalVideo";
}

public sealed record EnqueueJobCommand(
    Guid ProjectId,
    string JobType,
    Guid? SceneId = null,
    Guid? ParentJobId = null,
    int Priority = 0,
    int MaxAttempts = 3,
    string? IdempotencyKey = null,
    string? PayloadJson = null,
    IReadOnlyCollection<Guid>? DependsOnJobIds = null);

public sealed record ClaimedJob(
    Guid JobId,
    Guid ProjectId,
    Guid? SceneId,
    Guid? ParentJobId,
    string JobType,
    int Attempt,
    int MaxAttempts,
    string? PayloadJson);

public sealed record JobExecutionResult(string? ResultJson = null);

public sealed class RetryableJobException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
