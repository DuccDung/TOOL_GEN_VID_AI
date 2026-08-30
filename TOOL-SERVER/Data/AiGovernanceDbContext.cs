using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Domain.Organizations;

namespace TOOL_SERVER.Data;

public sealed class AiGovernanceDbContext(DbContextOptions<AiGovernanceDbContext> options) : DbContext(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<OrganizationProviderCredential> OrganizationProviderCredentials => Set<OrganizationProviderCredential>();
    public DbSet<OrganizationVideoPolicy> OrganizationVideoPolicies => Set<OrganizationVideoPolicy>();
    public DbSet<OrganizationBudgetPeriod> OrganizationBudgetPeriods => Set<OrganizationBudgetPeriod>();
    public DbSet<AiBudgetReservation> AiBudgetReservations => Set<AiBudgetReservation>();
    public DbSet<AiUsageLedgerEntry> AiUsageLedger => Set<AiUsageLedgerEntry>();
    public DbSet<OrganizationAuditLog> OrganizationAuditLogs => Set<OrganizationAuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Organization>(entity =>
        {
            entity.ToTable("Organizations", "ai");
            entity.HasKey(x => x.OrganizationId);
            entity.Property(x => x.OrganizationId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.Code).HasMaxLength(80).IsUnicode(false);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.MonthlyBudgetLimit).HasPrecision(19, 6);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsUnicode(false);
            entity.Property(x => x.CreatedByUserId).HasMaxLength(450);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => x.Code).IsUnique();
        });

        builder.Entity<OrganizationMember>(entity =>
        {
            entity.ToTable("OrganizationMembers", "ai");
            entity.HasKey(x => new { x.OrganizationId, x.UserId });
            entity.Property(x => x.UserId).HasMaxLength(450);
            entity.Property(x => x.Role).HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.MonthlyBudgetLimit).HasPrecision(19, 6);
            entity.Property(x => x.JoinedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => new { x.UserId, x.Status });
            entity.HasOne(x => x.Organization)
                .WithMany(x => x.Members)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<OrganizationProviderCredential>(entity =>
        {
            entity.ToTable("OrganizationProviderCredentials", "ai");
            entity.HasKey(x => x.OrganizationProviderCredentialId);
            entity.Property(x => x.OrganizationProviderCredentialId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.Version);
            entity.Property(x => x.Name).HasMaxLength(100);
            entity.Property(x => x.SecretHint).HasMaxLength(16).IsUnicode(false);
            entity.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.CreatedByUserId).HasMaxLength(450);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RetiredAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => new { x.OrganizationId, x.ProviderId, x.Version }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationId, x.ProviderId, x.Status });
        });

        builder.Entity<OrganizationVideoPolicy>(entity =>
        {
            entity.ToTable("OrganizationVideoPolicies", "ai");
            entity.HasKey(x => x.OrganizationId);
            entity.Property(x => x.Resolution).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.UpdatedByUserId).HasMaxLength(450);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => new { x.ProviderId, x.ProviderModelId, x.IsActive });
            entity.HasOne<Organization>()
                .WithOne()
                .HasForeignKey<OrganizationVideoPolicy>(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        builder.Entity<OrganizationBudgetPeriod>(entity =>
        {
            entity.ToTable("OrganizationBudgetPeriods", "ai");
            entity.HasKey(x => x.OrganizationBudgetPeriodId);
            entity.Property(x => x.OrganizationBudgetPeriodId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.StartsAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.EndsAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.HardLimit).HasPrecision(19, 6);
            entity.Property(x => x.ReservedCost).HasPrecision(19, 6);
            entity.Property(x => x.ActualCost).HasPrecision(19, 6);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsUnicode(false);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => new { x.OrganizationId, x.StartsAtUtc }).IsUnique();
        });

        builder.Entity<AiBudgetReservation>(entity =>
        {
            entity.ToTable("BudgetReservations", "ai");
            entity.HasKey(x => x.AiBudgetReservationId);
            entity.Property(x => x.AiBudgetReservationId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.UserId).HasMaxLength(450);
            entity.Property(x => x.OperationKey).HasMaxLength(450);
            entity.Property(x => x.ProviderCode).HasMaxLength(80).IsUnicode(false);
            entity.Property(x => x.ModelCode).HasMaxLength(200);
            entity.Property(x => x.ReservedAmount).HasPrecision(19, 6);
            entity.Property(x => x.ActualAmount).HasPrecision(19, 6);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsUnicode(false);
            entity.Property(x => x.Status).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.ExpiresAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.SettledAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(x => new { x.OrganizationId, x.OperationKey }).IsUnique();
            entity.HasIndex(x => new { x.OrganizationBudgetPeriodId, x.Status, x.UserId });
            entity.HasIndex(x => x.ProviderRequestId).IsUnique();
        });

        builder.Entity<AiUsageLedgerEntry>(entity =>
        {
            entity.ToTable("UsageLedger", "ai");
            entity.HasKey(x => x.AiUsageLedgerEntryId);
            entity.Property(x => x.AiUsageLedgerEntryId).HasDefaultValueSql("NEWSEQUENTIALID()");
            entity.Property(x => x.UserId).HasMaxLength(450);
            entity.Property(x => x.ProviderCode).HasMaxLength(80).IsUnicode(false);
            entity.Property(x => x.ModelCode).HasMaxLength(200);
            entity.Property(x => x.EntryKind).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.Amount).HasPrecision(19, 6);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsUnicode(false);
            entity.Property(x => x.OccurredAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.HasIndex(x => new { x.OrganizationId, x.OccurredAtUtc });
            entity.HasIndex(x => new { x.OrganizationBudgetPeriodId, x.UserId, x.EntryKind });
            entity.HasIndex(x => x.ProviderRequestId);
        });

        builder.Entity<OrganizationAuditLog>(entity =>
        {
            entity.ToTable("OrganizationAuditLogs", "ai");
            entity.HasKey(x => x.OrganizationAuditLogId);
            entity.Property(x => x.OrganizationAuditLogId).ValueGeneratedOnAdd();
            entity.Property(x => x.ActorUserId).HasMaxLength(450);
            entity.Property(x => x.EventType).HasMaxLength(100).IsUnicode(false);
            entity.Property(x => x.IpAddress).HasMaxLength(45).IsUnicode(false);
            entity.Property(x => x.UserAgent).HasMaxLength(1000);
            entity.Property(x => x.CorrelationId).HasMaxLength(100).IsUnicode(false);
            entity.Property(x => x.OccurredAtUtc).HasColumnType("datetime2(3)");
            entity.HasIndex(x => new { x.OrganizationId, x.OccurredAtUtc });
            entity.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
