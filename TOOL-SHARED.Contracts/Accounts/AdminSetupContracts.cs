using System.Text.Json.Serialization;

namespace TOOL_SHARED.Contracts.Accounts;

public sealed record AdminSetupOrganization(Guid OrganizationId, string Name, string Role);
public sealed record AdminSetupModel(Guid ModelId, string ProviderCode, string ModelCode, string Name, string Modality);
public sealed record AdminSetupRate(Guid ModelId, string UsageType, decimal? Price, string Unit, string Fingerprint);
public sealed record AdminSetupConnection(string ProviderCode, bool Active, int Version, string? Hint);
public sealed record AdminSetupBootstrap(IReadOnlyList<AdminSetupOrganization> Organizations,
    IReadOnlyList<AdminSetupModel> Models, bool PaymentsReady, IReadOnlyList<AdminSetupDraft> Drafts);

// Only safe form values are durable. Credentials have a separate, transient command.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdminSetupForm(string OrganizationName = "", Guid? VideoModelId = null,
    decimal OrganizationBudget = 0, decimal? MemberBudget = null, int Capacity = 10,
    string PlanName = "", decimal? PriceVnd = null, int DurationDays = 30, int MaxDevices = 1,
    string? Description = null, IReadOnlyList<string>? MarketingFeatures = null);
public sealed record AdminSetupDraft(Guid DraftId, Guid? OrganizationId, Guid? PoolId, string State,
    int Step, long Revision, AdminSetupForm Form, Guid? PlanId, DateTime UpdatedAtUtc);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAdminSetupDraftRequest(Guid DraftId, Guid? OrganizationId = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveAdminSetupDraftRequest(long Revision, int Step, AdminSetupForm Form);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdminSetupCommand(Guid OperationId, long Revision);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdminSetupConnectionRequest(Guid OperationId, long Revision, string ProviderCode,
    string ApiKey, int ExpectedVersion);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdminSetupPricingRequest(Guid OperationId, long Revision, bool ConfirmGlobalChange,
    IReadOnlyList<AdminSetupRate> Rates);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AdminSetupPublishRequest(Guid OperationId, long Revision, bool Publish, string ExpectedReadiness);
public sealed record AdminSetupCheck(string Code, string Label, bool Ready, string? Message, int Step);
public sealed record AdminSetupReadiness(bool Ready, bool AiReady, bool AllocationReady, bool PaymentsReady,
    decimal RemainingBudget, int AvailableSeats, string Fingerprint, IReadOnlyList<AdminSetupCheck> Checks,
    IReadOnlyList<AdminSetupModel> Models, IReadOnlyList<AdminSetupRate> Rates,
    IReadOnlyList<AdminSetupConnection> Connections);
public sealed record AdminSetupOperation(Guid OperationId, string Command, string State, Guid? ResultId,
    string? ErrorCode, bool Replayed = false);
public sealed record AdminSetupResult(AdminSetupDraft Draft, AdminSetupOperation Operation,
    AdminLicensePlanResponse? Plan = null);
