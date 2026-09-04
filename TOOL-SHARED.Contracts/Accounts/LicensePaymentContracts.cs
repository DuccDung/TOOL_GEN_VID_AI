namespace TOOL_SHARED.Contracts.Accounts;

public sealed record LicenseOfferResponse(
    Guid LicensePlanId,
    string PlanCode,
    string Name,
    string? Description,
    decimal PriceVnd,
    int DurationDays,
    int MaxActivatedDevices,
    IReadOnlyList<string> MarketingFeatures,
    int DisplayOrder,
    bool OrganizationSeatAvailable = false,
    string? OrganizationPoolName = null,
    int? AvailableOrganizationSeats = null);

public sealed record CreateLicensePaymentRequest(
    Guid LicensePlanId,
    string IdempotencyKey);

public sealed record LicensePaymentCheckoutResponse(
    string OrderCode,
    string TransferCode,
    string PlanCode,
    string PlanName,
    int DurationDays,
    decimal AmountVnd,
    string ReceiverBankCode,
    string ReceiverAccountNumber,
    string ReceiverAccountName,
    string TransferContent,
    string QrImageUrl,
    string Status,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime ServerTimeUtc,
    bool ReusedExistingPayment,
    bool IsPaid,
    bool IsFulfilled,
    bool IsExpired,
    Guid? AssignedOrganizationId = null,
    string? AssignedOrganizationName = null,
    string? ProvisioningStatus = null);

public sealed record LicensePaymentStatusResponse(
    string OrderCode,
    string Status,
    DateTime ExpiresAtUtc,
    DateTime ServerTimeUtc,
    DateTime? PaidAtUtc,
    DateTime? FulfilledAtUtc,
    bool IsPaid,
    bool IsFulfilled,
    bool IsExpired,
    string? FailureCode = null,
    string? Message = null,
    Guid? AssignedOrganizationId = null,
    string? AssignedOrganizationName = null,
    string? ProvisioningStatus = null);

public sealed record CurrentLicensePaymentResponse(
    LicensePaymentCheckoutResponse? Payment);
