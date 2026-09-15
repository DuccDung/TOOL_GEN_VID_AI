namespace TOOL_SHARED.Contracts.Generation;

public static class ShortVideoVeo
{
    public const int DefaultDurationSeconds = 8;
    public static bool SupportsDuration(int seconds) => seconds is 4 or 6 or 8;
    public static bool SupportsAspectRatio(string ratio) => ratio is "9:16" or "16:9";
}

public sealed record ShortVideoVeoMigrationRequest(Guid ProjectId, Guid OrganizationId,
    string? ExpectedProviderCode, int DurationSeconds, string AspectRatio);
public sealed record ShortVideoVeoMigrationResponse(Guid ProjectId, string ProviderCode,
    string ModelCode, int DurationSeconds, string AspectRatio, bool PreservedComposition);
