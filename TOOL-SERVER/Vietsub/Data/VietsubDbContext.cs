using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Vietsub.Domain;

namespace TOOL_SERVER.Vietsub.Data;

public sealed class VietsubDbContext(DbContextOptions<VietsubDbContext> options) : DbContext(options)
{
    public DbSet<VietsubProject> Projects => Set<VietsubProject>();

    public DbSet<OrganizationAuditLog> OrganizationAuditLogs => Set<OrganizationAuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<VietsubProject>(entity =>
        {
            entity.ToTable("Projects", "vs");
            entity.HasKey(project => project.ProjectId);
            entity.Property(project => project.CreatedByUserId).HasMaxLength(450);
            entity.Property(project => project.Name).HasMaxLength(120);
            entity.Property(project => project.Status).HasMaxLength(20).IsUnicode(false);
            entity.Property(project => project.SourceLanguageCode).HasMaxLength(16).IsUnicode(false);
            entity.Property(project => project.TargetLanguageCode).HasMaxLength(16).IsUnicode(false);
            entity.Property(project => project.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(project => project.UpdatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(project => project.ArchivedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(project => project.RowVersion).IsRowVersion().IsConcurrencyToken();
            entity.HasIndex(project => new
            {
                project.OrganizationId,
                project.CreatedByUserId,
                project.IsArchived,
                project.UpdatedAtUtc
            });
        });

        builder.Entity<OrganizationAuditLog>(entity =>
        {
            entity.ToTable("OrganizationAuditLogs", "ai");
            entity.HasKey(audit => audit.OrganizationAuditLogId);
            entity.Property(audit => audit.OrganizationAuditLogId).ValueGeneratedOnAdd();
            entity.Property(audit => audit.ActorUserId).HasMaxLength(450);
            entity.Property(audit => audit.EventType).HasMaxLength(100).IsUnicode(false);
            entity.Property(audit => audit.IpAddress).HasMaxLength(45).IsUnicode(false);
            entity.Property(audit => audit.UserAgent).HasMaxLength(1000);
            entity.Property(audit => audit.CorrelationId).HasMaxLength(100).IsUnicode(false);
            entity.Property(audit => audit.OccurredAtUtc).HasColumnType("datetime2(3)");
            entity.HasIndex(audit => new { audit.OrganizationId, audit.OccurredAtUtc });
        });
    }
}
