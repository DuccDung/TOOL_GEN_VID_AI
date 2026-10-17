using System.Text.Json.Serialization;

namespace TOOL_SHARED.Contracts.Accounts;

public sealed record AdminLicensePlanGroupOption(
    Guid OrganizationPoolId,
    string Name,
    int AvailableSeats,
    bool Ready,
    string? BlockingCode,
    string? BlockingMessage,
    AdminLicensePlanGroupAvailability? VietsubLocal = null,
    AdminLicensePlanGroupAvailability? VietsubCloud = null,
    AdminLicensePlanSetupTarget? SetupTarget = null);

public sealed record AdminLicensePlanGroupAvailability(
    int AvailableSeats, bool Ready, string? BlockingCode, string? BlockingMessage,
    AdminLicensePlanSetupTarget? SetupTarget = null);

public sealed record AdminLicensePlanSetupTarget(Guid? OrganizationId, string Target,
    string? ProviderCode = null, Guid? ModelId = null);

public sealed record AdminLicensePlanSetupResponse(
    bool PaymentsReady,
    string? PaymentMessage,
    IReadOnlyList<AdminLicensePlanGroupOption> Groups);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ComposeLicensePlanRequest(
    Guid CreationId,
    string Name,
    int? DurationDays,
    decimal? PriceVnd,
    int MaxActivatedDevices,
    Guid? OrganizationPoolId,
    bool Publish,
    string? Description = null,
    IReadOnlyList<string>? MarketingFeatures = null,
    int MaxConcurrentSessions = 1,
    int OfflineGraceHours = 0,
    int DisplayOrder = 0,
    decimal? MemberMonthlyAiBudget = null,
    Guid? SourcePlanId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PlanId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? ExpectedUpdatedAtUtc = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsActive = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? ExpectedMappingUpdatedAtUtc = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ProductScope = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? VietsubCloudEnabled = null);

public sealed record AdminLicensePlanEditorResponse(
    AdminLicensePlanResponse Plan,
    Guid? OrganizationPoolId,
    decimal? MemberMonthlyAiBudget,
    DateTime? MappingUpdatedAtUtc);

public sealed record ComposeLicensePlanResponse(
    AdminLicensePlanResponse Plan,
    Guid? OrganizationPoolId,
    bool Published,
    bool Replayed);
