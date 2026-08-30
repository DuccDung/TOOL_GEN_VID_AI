using System;
using System.Collections.Generic;

namespace TOOL_SERVER.Models;

public partial class AspNetUser
{
    public string Id { get; set; } = null!;

    public string? UserName { get; set; }

    public string? NormalizedUserName { get; set; }

    public string? Email { get; set; }

    public string? NormalizedEmail { get; set; }

    public bool EmailConfirmed { get; set; }

    public string? PasswordHash { get; set; }

    public string? SecurityStamp { get; set; }

    public string? ConcurrencyStamp { get; set; }

    public string? PhoneNumber { get; set; }

    public bool PhoneNumberConfirmed { get; set; }

    public bool TwoFactorEnabled { get; set; }

    public DateTimeOffset? LockoutEnd { get; set; }

    public bool LockoutEnabled { get; set; }

    public int AccessFailedCount { get; set; }

    public string? DisplayName { get; set; }

    public string AccountStatus { get; set; } = null!;

    public string PreferredLanguageCode { get; set; } = null!;

    public string TimeZoneId { get; set; } = null!;

    public DateTime? LastLoginAtUtc { get; set; }

    public DateTime? PasswordChangedAtUtc { get; set; }

    public DateTime? TermsAcceptedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? DeletedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = null!;

    public virtual ICollection<AspNetUserClaim> AspNetUserClaims { get; set; } = new List<AspNetUserClaim>();

    public virtual ICollection<AspNetUserLogin> AspNetUserLogins { get; set; } = new List<AspNetUserLogin>();

    public virtual ICollection<AspNetUserToken> AspNetUserTokens { get; set; } = new List<AspNetUserToken>();

    public virtual ICollection<Project> Projects { get; set; } = new List<Project>();

    public virtual ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();

    public virtual ICollection<RegisteredDevice> RegisteredDevices { get; set; } = new List<RegisteredDevice>();

    public virtual ICollection<UserLicense> UserLicenseGrantedByUsers { get; set; } = new List<UserLicense>();

    public virtual ICollection<UserLicense> UserLicenseUsers { get; set; } = new List<UserLicense>();

    public virtual ICollection<UserSession> UserSessions { get; set; } = new List<UserSession>();

    public virtual ICollection<AspNetRole> Roles { get; set; } = new List<AspNetRole>();
}
