using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Domain.Updates;

namespace TOOL_SERVER.Data;

public sealed partial class AccountDbContext
{
    public DbSet<DesktopDirectRelease> DesktopDirectReleases => Set<DesktopDirectRelease>();
}

internal static class DesktopDirectReleaseMapping
{
    internal static void Configure(ModelBuilder builder)
    {
        builder.Entity<DesktopDirectRelease>(entity =>
        {
            entity.ToTable("DesktopDirectReleases", "auth");
            entity.HasKey(x => x.DirectReleaseId);
            entity.Property(x => x.Version).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.DownloadUrl).HasMaxLength(2000);
            entity.Property(x => x.FileName).HasMaxLength(180);
            entity.Property(x => x.ExpectedSha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            entity.Property(x => x.Profile).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.Channel).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.Platform).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.ModelBundleId).HasMaxLength(100).IsUnicode(false);
            entity.Property(x => x.MinimumBootstrapperVersion).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.ReleaseNotes).HasColumnType("nvarchar(max)");
            entity.Property(x => x.Revision).IsConcurrencyToken();
            entity.Property(x => x.ActorUserId).HasMaxLength(450);
            entity.Property(x => x.CreateRequestFingerprint).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            foreach (var name in new[] { nameof(DesktopDirectRelease.CreatedAtUtc), nameof(DesktopDirectRelease.UpdatedAtUtc),
                         nameof(DesktopDirectRelease.PublishedAtUtc), nameof(DesktopDirectRelease.FirstPublishedAtUtc) })
                entity.Property(name).HasColumnType("datetime2(3)");
            entity.HasIndex(x => new { x.Profile, x.Channel, x.Platform, x.Version }).IsUnique();
            entity.HasIndex(x => new { x.ActorUserId, x.ClientRequestId }).IsUnique();
            entity.HasIndex(x => new { x.Profile, x.Channel, x.Platform, x.IsActive, x.PublishedAtUtc });
        });
    }
}
