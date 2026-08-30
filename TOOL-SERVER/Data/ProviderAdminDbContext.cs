using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Domain.Providers;

namespace TOOL_SERVER.Data;

public sealed class ProviderAdminDbContext(DbContextOptions<ProviderAdminDbContext> options) : DbContext(options)
{
    public DbSet<AiProvider> Providers => Set<AiProvider>();

    public DbSet<AiProviderModel> ProviderModels => Set<AiProviderModel>();

    public DbSet<AiProviderCredential> ProviderCredentials => Set<AiProviderCredential>();

    public DbSet<AiCostRate> CostRates => Set<AiCostRate>();

    public DbSet<AiProviderRequestLog> ProviderRequests => Set<AiProviderRequestLog>();

    public DbSet<AccountAuditLog> AccountAuditLogs => Set<AccountAuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<AiProvider>(entity =>
        {
            entity.ToTable("Providers", "vf");
            entity.HasKey(x => x.ProviderId);
            entity.Property(x => x.ProviderId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.ProviderCode).HasMaxLength(80).IsUnicode(false);
            entity.Property(x => x.DisplayName).HasMaxLength(200);
            entity.Property(x => x.BaseUrl).HasMaxLength(1000);
            entity.Property(x => x.SecretReference).HasMaxLength(500);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => x.ProviderCode).IsUnique();
            entity.HasMany(x => x.Models).WithOne(x => x.Provider).HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.NoAction);
            entity.HasMany(x => x.Credentials).WithOne(x => x.Provider).HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<AiProviderModel>(entity =>
        {
            entity.ToTable("ProviderModels", "vf");
            entity.HasKey(x => x.ProviderModelId);
            entity.Property(x => x.ProviderModelId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.ModelCode).HasMaxLength(200);
            entity.Property(x => x.DisplayName).HasMaxLength(300);
            entity.Property(x => x.Modality).HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => new { x.ProviderId, x.ModelCode, x.Modality }).IsUnique();
            entity.HasMany(x => x.CostRates).WithOne(x => x.ProviderModel).HasForeignKey(x => x.ProviderModelId).OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<AiProviderCredential>(entity =>
        {
            entity.ToTable("ProviderCredentials", "vf");
            entity.HasKey(x => x.ProviderCredentialId);
            entity.Property(x => x.ProviderCredentialId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.Name).HasMaxLength(100);
            entity.Property(x => x.AuthenticationType).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.HeaderName).HasMaxLength(100).IsUnicode(false);
            entity.Property(x => x.TestPath).HasMaxLength(1000);
            entity.Property(x => x.SecretHint).HasMaxLength(16).IsUnicode(false);
            entity.Property(x => x.TestStatus).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.TestMessage).HasMaxLength(1000);
            entity.Property(x => x.LastTestedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => new { x.ProviderId, x.Name }).IsUnique();
        });

        builder.Entity<AiCostRate>(entity =>
        {
            entity.ToTable("CostRates", "vf");
            entity.HasKey(x => x.CostRateId);
            entity.Property(x => x.CostRateId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.UsageType).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.Unit).HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.UnitPrice).HasPrecision(19, 8);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsUnicode(false);
            entity.Property(x => x.EffectiveFromUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.EffectiveToUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
        });

        builder.Entity<AiProviderRequestLog>(entity =>
        {
            entity.ToTable("ProviderRequests", "vf");
            entity.HasKey(x => x.ProviderRequestId);
            entity.Property(x => x.RequestKind).HasMaxLength(40).IsUnicode(false);
            entity.Property(x => x.ProviderCode).HasMaxLength(80).IsUnicode(false);
            entity.Property(x => x.ModelCode).HasMaxLength(200);
            entity.Property(x => x.Status).HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.EstimatedCost).HasPrecision(19, 6);
            entity.Property(x => x.ActualCost).HasPrecision(19, 6);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsUnicode(false);
            entity.Property(x => x.ErrorCode).HasMaxLength(100).IsUnicode(false);
            entity.Property(x => x.ErrorMessage).HasMaxLength(4000);
            entity.Property(x => x.SubmittedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.CompletedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
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
            entity.Property(x => x.OccurredAtUtc).HasColumnType("datetime2(3)");
        });
    }
}

