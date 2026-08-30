using Microsoft.AspNetCore.Identity;

namespace TOOL_SERVER.Domain.Accounts;

public sealed class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }

    public string AccountStatus { get; set; } = AccountStatuses.Active;

    public string PreferredLanguageCode { get; set; } = "vi-VN";

    public string TimeZoneId { get; set; } = "SE Asia Standard Time";

    public DateTime? LastLoginAtUtc { get; set; }

    public DateTime? PasswordChangedAtUtc { get; set; }

    public DateTime? TermsAcceptedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

public static class AccountStatuses
{
    public const string PendingVerification = "PendingVerification";
    public const string Active = "Active";
    public const string Locked = "Locked";
    public const string Suspended = "Suspended";
    public const string Deleted = "Deleted";
}
