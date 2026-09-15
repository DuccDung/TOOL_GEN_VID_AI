using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Models;

namespace TOOL_SERVER.Data;

public partial class VideoFactoryDbContext
{
    public DbSet<ShortVideoOutfit> ShortVideoOutfits => Set<ShortVideoOutfit>();
    public DbSet<ShortVideoOperation> ShortVideoOperations => Set<ShortVideoOperation>();

    private static void ConfigureShortVideo(ModelBuilder model)
    {
        model.Entity<ShortVideoOutfit>(e =>
        {
            e.ToTable("ShortVideoOutfits", "vf");
            e.HasKey(x => x.SceneId);
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.Property(x => x.Background).HasMaxLength(1500);
            e.Property(x => x.Motion).HasMaxLength(2000);
            e.HasOne<Scene>().WithMany().HasForeignKey(x => x.SceneId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ShortVideoOperation>(e =>
        {
            e.ToTable("ShortVideoOperations", "vf");
            e.HasKey(x => x.OperationId);
            e.Property(x => x.Status).HasMaxLength(30).IsConcurrencyToken();
            e.Property(x => x.Kind).HasMaxLength(10);
            e.Property(x => x.UserId).HasMaxLength(450);
            e.Property(x => x.ApprovedByUserId).HasMaxLength(450);
            e.HasIndex(x => new { x.SceneId, x.Revision, x.Kind });
            e.HasOne<Scene>().WithMany().HasForeignKey(x => x.SceneId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
