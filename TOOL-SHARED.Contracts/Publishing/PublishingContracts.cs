using TOOL_SHARED.Contracts.Generation;

namespace TOOL_SHARED.Contracts.Publishing;

public static class PublishingPlatforms
{
    public const string TikTok = "TikTok";
    public const string Facebook = "Facebook";
    public const string YouTube = "YouTube";
    public static bool IsValid(string? value) => value is TikTok or Facebook or YouTube;
}

public sealed record PublishingTarget(string Platform, Guid ConnectionId, string Privacy,
    bool MadeForKids = false, bool BrandOrganic = false, bool BrandContent = false, string? Title = null);
public sealed record PublishingImage(Guid ImageId, string Role, ShortVideoImageInfo Info);
public sealed record UploadPublishingImageRequest(Guid OrganizationId, string Role, ShortVideoImageInput Image);
public sealed record PublishingScheduleInput(string Title, string Description,
    Guid CharacterImageId, Guid ProductImageId, string StartDate, string EndDate,
    string PublishTime, string TimeZoneId, int Weekdays, int LeadMinutes, string LatePolicy,
    int DurationSeconds, string AspectRatio, decimal MaximumCostPerRun,
    IReadOnlyList<PublishingTarget> Targets);
public sealed record SavePublishingScheduleRequest(Guid OrganizationId, Guid ScheduleId,
    int ExpectedRevision, PublishingScheduleInput Input);
public sealed record ChangePublishingScheduleRequest(Guid OrganizationId, Guid ScheduleId,
    int ExpectedRevision, string Action, bool ConfirmAutomaticGeneration = false);
public sealed record PublishingScheduleSummary(Guid ScheduleId, int Revision, string Status,
    PublishingScheduleInput Input, DateTime? NextPublishAtUtc, DateTime UpdatedAtUtc);
public sealed record PublishingRunSummary(Guid RunId, Guid ScheduleId, string Title,
    DateTime GenerateAtUtc, DateTime PublishAtUtc, DateTime DeadlineAtUtc, string Status,
    string? ErrorCode, string? Message, Guid? ProjectId, decimal EstimatedCost,
    IReadOnlyList<PublishingDeliverySummary> Deliveries, DateTime UpdatedAtUtc,
    string? MediaSha256, PublishingScheduleInput Input, bool CanResume);
public sealed record PublishingDeliverySummary(Guid DeliveryId, string Platform, string AccountName,
    string Status, string? PostUrl, string? Message);
public sealed record PublishingConnectionSummary(Guid ConnectionId, string Platform,
    string DisplayName, string Status);
public sealed record PublishingPlatformReadiness(string Platform, bool Configured, string? Message);
public sealed record PublishingState(bool Enabled, string? UnavailableReason,
    IReadOnlyList<PublishingScheduleSummary> Schedules, IReadOnlyList<PublishingRunSummary> Runs,
    IReadOnlyList<PublishingConnectionSummary> Connections,
    IReadOnlyList<PublishingPlatformReadiness> Platforms);
public sealed record StartPublishingOAuthRequest(Guid OrganizationId, string Platform);
public sealed record StartPublishingOAuthResponse(string AuthorizationUrl);
public sealed record PublishingRunActionRequest(Guid OrganizationId, Guid RunId, string Action);

// A post is approved only after reviewing the concrete generated media and metadata.
public sealed record ApprovePublishingRunRequest(Guid OrganizationId, Guid RunId,
    string MediaSha256, bool Approve, IReadOnlyList<PublishingTarget> Targets);
