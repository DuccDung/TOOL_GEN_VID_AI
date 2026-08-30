namespace TOOL_SHARED.Contracts.Organizations;

public static class OrganizationRoles
{
    public const string Owner = "Owner";
    public const string OrganizationAdmin = "OrganizationAdmin";
    public const string BillingManager = "BillingManager";
    public const string Member = "Member";
    public const string Viewer = "Viewer";
}

public sealed record OrganizationAiReadinessResponse(
    string ProviderCode,
    string? ModelCode,
    bool ProviderEnabled,
    bool ModelEnabled,
    bool CredentialActive,
    bool BudgetEnabled,
    bool Ready,
    IReadOnlyList<string> MissingUsageTypes,
    IReadOnlyList<string> BlockingReasons);

public sealed record OrganizationSummaryResponse(
    Guid OrganizationId,
    string Code,
    string Name,
    string Role,
    string Status,
    decimal MonthlyBudgetLimit,
    decimal ReservedCost,
    decimal ActualCost,
    decimal RemainingBudget,
    string CurrencyCode,
    DateTime PeriodStartsAtUtc,
    DateTime PeriodEndsAtUtc,
    int MemberCount = 0,
    int ActiveMemberCount = 0,
    IReadOnlyList<OrganizationAiReadinessResponse>? AiReadiness = null);

public sealed record OrganizationMemberResponse(
    string UserId,
    string Email,
    string? DisplayName,
    string Role,
    string Status,
    decimal? MonthlyBudgetLimit,
    DateTime JoinedAtUtc);

public sealed record CreateOrganizationRequest(
    string Name,
    string? Code = null,
    decimal MonthlyBudgetLimit = 0,
    string CurrencyCode = "USD");

public sealed record UpdateOrganizationBudgetRequest(
    decimal MonthlyBudgetLimit,
    string CurrencyCode = "USD");

public sealed record AddOrganizationMemberRequest(
    string Email,
    string Role = OrganizationRoles.Member,
    decimal? MonthlyBudgetLimit = null);

public sealed record UpdateOrganizationMemberRequest(
    string Role,
    string Status,
    decimal? MonthlyBudgetLimit = null);

public sealed record OrganizationProviderResponse(
    Guid ProviderId,
    string ProviderCode,
    string DisplayName,
    string? ModelCode,
    bool Configured,
    Guid? CredentialId,
    int? CredentialVersion,
    string? SecretHint,
    string? CredentialStatus,
    DateTime? UpdatedAtUtc);

public sealed record SaveOrganizationProviderCredentialRequest(
    string ApiKey,
    string? Name = null);

public sealed record OrganizationVideoPolicyResponse(
    Guid OrganizationId,
    Guid ProviderId,
    string ProviderCode,
    string ProviderName,
    Guid ProviderModelId,
    string ModelCode,
    string ModelName,
    int PolicyVersion,
    string Resolution,
    bool NativeAudio,
    bool IsActive,
    DateTime UpdatedAtUtc);

public sealed record UpdateOrganizationVideoPolicyRequest(
    Guid ProviderModelId,
    string Resolution = "720p",
    bool NativeAudio = true);

public sealed record OrganizationUsageItemResponse(
    Guid LedgerEntryId,
    string UserId,
    Guid ProjectId,
    Guid? ProviderRequestId,
    string ProviderCode,
    string ModelCode,
    string EntryKind,
    decimal Amount,
    string CurrencyCode,
    DateTime OccurredAtUtc,
    long? InputTokens = null,
    long? OutputTokens = null,
    decimal? VideoSeconds = null);

public sealed record OrganizationUsageGroupResponse(
    string ProviderCode,
    string ModelCode,
    string UserId,
    decimal ActualCost,
    long? InputTokens,
    long? OutputTokens,
    decimal? VideoSeconds);

public sealed record OrganizationUsageResponse(
    Guid OrganizationId,
    DateTime PeriodStartsAtUtc,
    DateTime PeriodEndsAtUtc,
    decimal BudgetLimit,
    decimal ReservedCost,
    decimal ActualCost,
    decimal RemainingBudget,
    string CurrencyCode,
    IReadOnlyList<OrganizationUsageItemResponse> Items,
    long? InputTokens = null,
    long? OutputTokens = null,
    decimal? VideoSeconds = null,
    IReadOnlyList<OrganizationUsageGroupResponse>? Groups = null);

public sealed record OrganizationAuditItemResponse(
    long AuditLogId,
    string? ActorUserId,
    string? ActorEmail,
    string? ActorDisplayName,
    string EventType,
    IReadOnlyDictionary<string, string?> Data,
    string? CorrelationId,
    DateTime OccurredAtUtc);
