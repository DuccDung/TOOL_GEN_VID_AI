using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Domain.Updates;

namespace TOOL_SERVER.Data;

public sealed partial class AccountDbContext
{
    public DbSet<DesktopReleasePreparation> DesktopReleasePreparations => Set<DesktopReleasePreparation>();
    public DbSet<DesktopBootstrapper> DesktopBootstrappers => Set<DesktopBootstrapper>();
}

internal static class DesktopSharedReleaseMapping
{
    internal static void Configure(ModelBuilder builder, bool legacy = false)
    {
        DesktopDirectReleaseMapping.Configure(builder);
        builder.Entity<DesktopReleasePreparation>(entity =>
        {
            entity.ToTable("DesktopReleasePreparations", "auth");
            entity.HasKey(x => x.PreparationId);
            entity.Property(x => x.ActorUserId).HasMaxLength(450);
            entity.Property(x => x.Version).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.DownloadUrl).HasMaxLength(2000);
            entity.Property(x => x.ReleaseNotes).HasMaxLength(10000);
            entity.Property(x => x.ReleaseNotes).HasColumnType("nvarchar(max)");
            entity.Property(x => x.Profile).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.Channel).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.Platform).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.Property(x => x.InputFingerprint).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            entity.Property(x => x.CreateRequestFingerprint).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            entity.Property(x => x.State).HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.Stage).HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.ErrorCode).HasMaxLength(100).IsUnicode(false);
            entity.Property(x => x.ErrorMessage).HasMaxLength(1000);
            entity.Property(x => x.InspectionEvidenceJson).HasColumnType("nvarchar(max)");
            foreach (var name in new[] { nameof(DesktopReleasePreparation.CreatedAtUtc), nameof(DesktopReleasePreparation.UpdatedAtUtc),
                         nameof(DesktopReleasePreparation.LeaseExpiresAtUtc), nameof(DesktopReleasePreparation.HeartbeatAtUtc),
                         nameof(DesktopReleasePreparation.NextAttemptAtUtc), nameof(DesktopReleasePreparation.VerifiedAtUtc) })
                entity.Property(name).HasColumnType("datetime2(3)");
            entity.HasIndex(x => new { x.ActorUserId, x.ClientRequestId }).IsUnique();
            entity.HasIndex(x => x.ReleaseId).IsUnique().HasFilter("[ReleaseId] IS NOT NULL");
            entity.HasIndex(x => new { x.State, x.NextAttemptAtUtc, x.LeaseExpiresAtUtc });
            if (legacy) entity.HasOne<Models.AppRelease>().WithMany().HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.NoAction);
            else entity.HasOne<AppRelease>().WithMany().HasForeignKey(x => x.ReleaseId).OnDelete(DeleteBehavior.NoAction);
        });
        builder.Entity<DesktopBootstrapper>(entity =>
        {
            entity.ToTable("DesktopBootstrappers", "auth");
            entity.HasKey(x => x.BootstrapperId);
            entity.Property(x => x.Version).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.Profile).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.Platform).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.Channel).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.ServerBaseUrl).HasMaxLength(2000);
            entity.Property(x => x.ModelBundleId).HasMaxLength(100).IsUnicode(false);
            entity.Property(x => x.FileName).HasMaxLength(180);
            entity.Property(x => x.RelativePath).HasMaxLength(1000);
            entity.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            entity.Property(x => x.EmbeddedMetadataJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.CreatedByUserId).HasMaxLength(450);
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(3)");
            entity.Property(x => x.ActivatedAtUtc).HasColumnType("datetime2(3)");
            entity.HasIndex(x => new { x.Profile, x.Platform, x.Channel, x.Version }).IsUnique();
            entity.HasIndex(x => new { x.Profile, x.Platform, x.Channel }).IsUnique().HasFilter("[IsActive] = 1");
        });
        if (legacy)
            builder.Entity<Models.AppReleaseArtifact>().HasOne<DesktopReleasePreparation>().WithMany()
                .HasForeignKey(x => x.VerifiedPreparationId).OnDelete(DeleteBehavior.NoAction);
        else
            builder.Entity<AppReleaseArtifact>().HasOne<DesktopReleasePreparation>().WithMany()
                .HasForeignKey(x => x.VerifiedPreparationId).OnDelete(DeleteBehavior.NoAction);
    }
}
