using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Domain.Updates;

namespace TOOL_SERVER.Data;

public sealed class AccountDbContext(DbContextOptions<AccountDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<RegisteredDevice> RegisteredDevices => Set<RegisteredDevice>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<AccountAuditLog> AccountAuditLogs => Set<AccountAuditLog>();

    public DbSet<LicensePlan> LicensePlans => Set<LicensePlan>();

    public DbSet<UserLicense> UserLicenses => Set<UserLicense>();

    public DbSet<LicenseActivation> LicenseActivations => Set<LicenseActivation>();

    public DbSet<AppRelease> AppReleases => Set<AppRelease>();

    public DbSet<AppReleaseArtifact> AppReleaseArtifacts => Set<AppReleaseArtifact>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        ConfigureIdentity(builder);
        ConfigureDevices(builder);
        ConfigureSessions(builder);
        ConfigureRefreshTokens(builder);
        ConfigureAudit(builder);
        ConfigureLicenses(builder);
        ConfigureDesktopReleases(builder);
    }

    private static void ConfigureIdentity(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("AspNetUsers", "dbo");
            entity.Property(x => x.DisplayName).HasMaxLength(200);
            entity.Property(x => x.AccountStatus).HasMaxLength(30).IsUnicode(false).HasDefaultValue(AccountStatuses.Active);
            entity.Property(x => x.PreferredLanguageCode).HasMaxLength(10).IsUnicode(false).HasDefaultValue("vi-VN");
            entity.Property(x => x.TimeZoneId).HasMaxLength(100).HasDefaultValue("SE Asia Standard Time");
            entity.Property(x => x.LastLoginAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.PasswordChangedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.TermsAcceptedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.DeletedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
        });

        builder.Entity<IdentityRole>().ToTable("AspNetRoles", "dbo");
        builder.Entity<IdentityRoleClaim<string>>().ToTable("AspNetRoleClaims", "dbo");
        builder.Entity<IdentityUserClaim<string>>().ToTable("AspNetUserClaims", "dbo");
        builder.Entity<IdentityUserLogin<string>>().ToTable("AspNetUserLogins", "dbo");
        builder.Entity<IdentityUserRole<string>>().ToTable("AspNetUserRoles", "dbo");
        builder.Entity<IdentityUserToken<string>>().ToTable("AspNetUserTokens", "dbo");
    }

    private static void ConfigureDevices(ModelBuilder builder)
    {
        builder.Entity<RegisteredDevice>(entity =>
        {
            entity.ToTable("RegisteredDevices", "auth");
            entity.HasKey(x => x.DeviceId);
            entity.Property(x => x.DeviceId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.UserId).HasMaxLength(450);
            entity.Property(x => x.DeviceFingerprintHash).HasColumnType("binary(32)");
            entity.Property(x => x.DeviceName).HasMaxLength(200);
            entity.Property(x => x.OperatingSystem).HasMaxLength(200);
            entity.Property(x => x.ApplicationVersion).HasMaxLength(50);
            entity.Property(x => x.RevokedReason).HasMaxLength(500);
            entity.Property(x => x.FirstSeenAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.LastSeenAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.RevokedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => new { x.UserId, x.DeviceFingerprintHash }).IsUnique();
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    private static void ConfigureSessions(ModelBuilder builder)
    {
        builder.Entity<UserSession>(entity =>
        {
            entity.ToTable("UserSessions", "auth");
            entity.HasKey(x => x.SessionId);
            entity.Property(x => x.SessionId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.UserId).HasMaxLength(450);
            entity.Property(x => x.Status).HasMaxLength(20).IsUnicode(false).HasDefaultValue(SessionStatuses.Active);
            entity.Property(x => x.StartedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.LastSeenAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.AbsoluteExpiresAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RevokedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RevokedReason).HasMaxLength(500);
            entity.Property(x => x.IpAddress).HasMaxLength(45).IsUnicode(false);
            entity.Property(x => x.UserAgent).HasMaxLength(1000);
            entity.Property(x => x.ApplicationVersion).HasMaxLength(50);
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Device).WithMany(x => x.Sessions).HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    private static void ConfigureRefreshTokens(ModelBuilder builder)
    {
        builder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens", "auth");
            entity.HasKey(x => x.RefreshTokenId);
            entity.Property(x => x.RefreshTokenId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.UserId).HasMaxLength(450);
            entity.Property(x => x.TokenHash).HasColumnType("binary(32)");
            entity.Property(x => x.TokenPrefix).HasMaxLength(16).IsUnicode(false);
            entity.Property(x => x.JwtId).HasMaxLength(100);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.ExpiresAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.UsedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RevokedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RevokedReason).HasMaxLength(500);
            entity.Property(x => x.CreatedByIpAddress).HasMaxLength(45).IsUnicode(false);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Session).WithMany(x => x.RefreshTokens).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.ReplacedByToken).WithMany().HasForeignKey(x => x.ReplacedByTokenId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    private static void ConfigureAudit(ModelBuilder builder)
    {
        builder.Entity<AccountAuditLog>(entity =>
        {
            entity.ToTable("AccountAuditLogs", "auth");
            entity.HasKey(x => x.AccountAuditLogId);
            entity.Property(x => x.AccountAuditLogId).ValueGeneratedOnAdd();
            entity.Property(x => x.UserId).HasMaxLength(450);
            entity.Property(x => x.EventType).HasMaxLength(100).IsUnicode(false);
            entity.Property(x => x.IpAddress).HasMaxLength(45).IsUnicode(false);
            entity.Property(x => x.UserAgent).HasMaxLength(1000);
            entity.Property(x => x.CorrelationId).HasMaxLength(100).IsUnicode(false);
            entity.Property(x => x.OccurredAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        });
    }

    private static void ConfigureLicenses(ModelBuilder builder)
    {
        builder.Entity<LicensePlan>(entity =>
        {
            entity.ToTable("LicensePlans", "auth");
            entity.HasKey(x => x.LicensePlanId);
            entity.Property(x => x.LicensePlanId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.PlanCode).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => x.PlanCode).IsUnique();
        });

        builder.Entity<UserLicense>(entity =>
        {
            entity.ToTable("UserLicenses", "auth");
            entity.HasKey(x => x.UserLicenseId);
            entity.Property(x => x.UserLicenseId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.UserId).HasMaxLength(450);
            entity.Property(x => x.LicenseKeyHash).HasColumnType("binary(32)");
            entity.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.StartsAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.ExpiresAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.GrantedByUserId).HasMaxLength(450);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RevokedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RevokedReason).HasMaxLength(500);
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.GrantedByUser).WithMany().HasForeignKey(x => x.GrantedByUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.LicensePlan).WithMany(x => x.UserLicenses).HasForeignKey(x => x.LicensePlanId).OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<LicenseActivation>(entity =>
        {
            entity.ToTable("LicenseActivations", "auth");
            entity.HasKey(x => x.LicenseActivationId);
            entity.Property(x => x.LicenseActivationId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.ActivatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.LastVerifiedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RevokedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RevokedReason).HasMaxLength(500);
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => new { x.UserLicenseId, x.DeviceId }).IsUnique();
            entity.HasOne(x => x.UserLicense).WithMany(x => x.Activations).HasForeignKey(x => x.UserLicenseId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    private static void ConfigureDesktopReleases(ModelBuilder builder)
    {
        builder.Entity<AppRelease>(entity =>
        {
            entity.ToTable("AppReleases", "auth");
            entity.HasKey(x => x.AppReleaseId);
            entity.Property(x => x.AppReleaseId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.Version).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.BuildNumber).HasDefaultValue(1);
            entity.Property(x => x.Channel).HasMaxLength(20).IsUnicode(false).HasDefaultValue(DesktopReleaseChannels.Stable);
            entity.Property(x => x.Platform).HasMaxLength(20).IsUnicode(false).HasDefaultValue(DesktopReleasePlatforms.WindowsX64);
            entity.Property(x => x.MinimumSupportedDesktopVersion).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.DownloadUrl).HasMaxLength(2000);
            entity.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            entity.Property(x => x.PublishedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.Version, x.BuildNumber, x.Channel, x.Platform }).IsUnique();
            entity.HasIndex(x => new { x.Platform, x.Channel, x.IsActive, x.PublishedAtUtc, x.BuildNumber });
        });

        builder.Entity<AppReleaseArtifact>(entity =>
        {
            entity.ToTable("AppReleaseArtifacts", "auth");
            entity.HasKey(x => x.AppReleaseArtifactId);
            entity.Property(x => x.AppReleaseArtifactId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.Kind).HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.FileName).HasMaxLength(260);
            entity.Property(x => x.RelativePath).HasMaxLength(1000);
            entity.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.HasIndex(x => new { x.AppReleaseId, x.Kind }).IsUnique();
            entity.HasOne(x => x.Release)
                .WithMany(x => x.Artifacts)
                .HasForeignKey(x => x.AppReleaseId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
