using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.TikTok.Domain;

namespace TOOL_SERVER.TikTok.Data;

public sealed class TikTokDbContext(DbContextOptions<TikTokDbContext> options) : DbContext(options)
{
    public DbSet<TikTokConnection> Connections => Set<TikTokConnection>();
    public DbSet<TikTokOAuthSession> OAuthSessions => Set<TikTokOAuthSession>();
    public DbSet<TikTokPublishJob> PublishJobs => Set<TikTokPublishJob>();
    public DbSet<TikTokPublishAttempt> PublishAttempts => Set<TikTokPublishAttempt>();
    public DbSet<TikTokAppCredential> AppCredentials => Set<TikTokAppCredential>();
    public DbSet<TikTokIntegrationSetting> IntegrationSettings => Set<TikTokIntegrationSetting>();
    public DbSet<AccountAuditLog> AccountAuditLogs => Set<AccountAuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<TikTokConnection>(entity =>
        {
            entity.ToTable("TikTokConnections", "social");
            entity.HasKey(x => x.TikTokConnectionId);
            entity.Property(x => x.TikTokConnectionId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.UserId).HasMaxLength(450);
            entity.Property(x => x.OpenId).HasMaxLength(128).IsUnicode(false);
            entity.Property(x => x.CreatorUsername).HasMaxLength(150);
            entity.Property(x => x.CreatorNickname).HasMaxLength(200);
            entity.Property(x => x.Scopes).HasMaxLength(1000).IsUnicode(false);
            entity.Property(x => x.ProtectedAccessToken).HasColumnType("nvarchar(max)");
            entity.Property(x => x.ProtectedRefreshToken).HasColumnType("nvarchar(max)");
            entity.Property(x => x.AccessTokenExpiresAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RefreshTokenExpiresAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RevokedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.Property(x => x.AppKeyHash).HasMaxLength(64).IsUnicode(false);
            entity.Property(x => x.ProtectedAvatarUrl).HasColumnType("nvarchar(max)");
            entity.Property(x => x.AvatarExpiresAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.DisconnectedAtUtc).HasColumnType("datetime2(3)");
            entity.HasIndex(x => new { x.UserId, x.AppKeyHash, x.OpenId }).IsUnique().HasFilter(null)
                .HasDatabaseName("UX_TikTokConnections_UserAppOpenId");
            entity.HasIndex(x => new { x.UserId, x.RevokedAtUtc });
            entity.HasOne<TikTokAppCredential>().WithMany().HasForeignKey(x => x.TikTokAppCredentialId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<TikTokOAuthSession>(entity =>
        {
            entity.ToTable("TikTokOAuthSessions", "social");
            entity.HasKey(x => x.TikTokOAuthSessionId);
            entity.Property(x => x.TikTokOAuthSessionId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.UserId).HasMaxLength(450);
            entity.Property(x => x.StateHash).HasColumnType("binary(32)");
            entity.Property(x => x.CodeChallenge).HasMaxLength(64).IsUnicode(false);
            entity.Property(x => x.RedirectUri).HasMaxLength(512).IsUnicode(false);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.ExpiresAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.ConsumedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => new { x.UserId, x.DeviceId, x.ExpiresAtUtc });
            entity.HasIndex(x => x.TikTokAppCredentialId);
            entity.HasOne<TikTokAppCredential>()
                .WithMany()
                .HasForeignKey(x => x.TikTokAppCredentialId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<TikTokConnection>().WithMany().HasForeignKey(x => x.TargetConnectionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<TikTokAppCredential>(entity =>
        {
            entity.ToTable("TikTokAppCredentials", "social");
            entity.HasKey(x => x.TikTokAppCredentialId);
            entity.Property(x => x.TikTokAppCredentialId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.ProtectedPayload).HasColumnType("nvarchar(max)");
            entity.Property(x => x.ClientKeyHint).HasMaxLength(32).IsUnicode(false);
            entity.Property(x => x.SecretHint).HasMaxLength(32).IsUnicode(false);
            entity.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.CreatedByUserId).HasMaxLength(450);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.VerificationRequestedByUserId).HasMaxLength(450);
            entity.Property(x => x.VerificationExpiresAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.LastTestedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.LastTestFailureCode).HasMaxLength(100).IsUnicode(false);
            entity.Property(x => x.ActivatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RetiredAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RevokedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => x.Version).IsUnique();
            entity.HasIndex(x => x.Status);
        });

        builder.Entity<TikTokIntegrationSetting>(entity =>
        {
            entity.ToTable("TikTokIntegrationSettings", "social");
            entity.HasKey(x => x.TikTokIntegrationSettingId);
            entity.Property(x => x.AuditEvidence).HasMaxLength(500);
            entity.Property(x => x.UpdatedByUserId).HasMaxLength(450);
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
        });

        builder.Entity<TikTokPublishJob>(entity =>
        {
            entity.ToTable("TikTokPublishJobs", "social");
            entity.HasKey(x => x.TikTokPublishJobId);
            entity.Property(x => x.TikTokPublishJobId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.UserId).HasMaxLength(450);
            entity.Property(x => x.TikTokPublishId).HasMaxLength(64).IsUnicode(false);
            entity.Property(x => x.ProtectedUploadUrl).HasColumnType("nvarchar(max)");
            entity.Property(x => x.UploadUrlExpiresAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.Status).HasMaxLength(40).IsUnicode(false);
            entity.Property(x => x.FailureReason).HasMaxLength(200).IsUnicode(false);
            entity.Property(x => x.PublicPostIds).HasMaxLength(1000).IsUnicode(false);
            entity.Property(x => x.CreatorUsernameSnapshot).HasMaxLength(150);
            entity.Property(x => x.CreatorNicknameSnapshot).HasMaxLength(200);
            entity.Property(x => x.NextPollAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => new { x.UserId, x.ClientRequestId }).IsUnique();
            entity.HasIndex(x => new { x.Status, x.UpdatedAtUtc });
            entity.HasIndex(x => new { x.UserId, x.TikTokConnectionId, x.CreatedAtUtc });
            entity.HasOne<TikTokConnection>().WithMany().HasForeignKey(x => x.TikTokConnectionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<TikTokPublishAttempt>(entity =>
        {
            entity.ToTable("TikTokPublishAttempts", "social");
            entity.HasKey(x => x.TikTokPublishAttemptId);
            entity.Property(x => x.UserId).HasMaxLength(450);
            entity.Property(x => x.RequestHash).HasMaxLength(64).IsUnicode(false);
            entity.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => new { x.UserId, x.ClientRequestId }).IsUnique();
            entity.HasOne<TikTokConnection>().WithMany().HasForeignKey(x => x.TikTokConnectionId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<TikTokPublishJob>().WithMany().HasForeignKey(x => x.TikTokPublishJobId)
                .OnDelete(DeleteBehavior.NoAction);
        });

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
}
