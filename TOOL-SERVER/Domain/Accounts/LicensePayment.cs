namespace TOOL_SERVER.Domain.Accounts;

public sealed class LicensePayment
{
    public Guid LicensePaymentId { get; set; }
    public string UserId { get; set; } = null!;
    public Guid LicensePlanId { get; set; }
    public string OrderCode { get; set; } = null!;
    public string TransferCode { get; set; } = null!;
    public string IdempotencyKey { get; set; } = null!;
    public decimal PriceSnapshotVnd { get; set; }
    public int DurationSnapshotDays { get; set; }
    public string PlanCodeSnapshot { get; set; } = null!;
    public string PlanNameSnapshot { get; set; } = null!;
    public string? EntitlementSnapshotJson { get; set; }
    public string Status { get; set; } = LicensePaymentStatuses.Pending;
    public string ReceiverBankCodeSnapshot { get; set; } = null!;
    public string ReceiverAccountNumberSnapshot { get; set; } = null!;
    public string ReceiverAccountNameSnapshot { get; set; } = null!;
    public long? ProviderTransactionId { get; set; }
    public string? ProviderReferenceCode { get; set; }
    public Guid? FulfilledUserLicenseId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? FulfilledAtUtc { get; set; }
    public string? FailureCode { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public ApplicationUser User { get; set; } = null!;
    public LicensePlan LicensePlan { get; set; } = null!;
    public UserLicense? FulfilledUserLicense { get; set; }
}

public static class LicensePaymentStatuses
{
    public const string Pending = "Pending";
    public const string Paid = "Paid";
    public const string Fulfilled = "Fulfilled";
    public const string Expired = "Expired";
    public const string Failed = "Failed";
}
