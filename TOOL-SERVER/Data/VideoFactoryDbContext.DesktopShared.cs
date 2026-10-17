using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Domain.Updates;

namespace TOOL_SERVER.Data;

public partial class VideoFactoryDbContext
{
    public DbSet<DesktopDirectRelease> DesktopDirectReleases => Set<DesktopDirectRelease>();
    public DbSet<DesktopReleasePreparation> DesktopReleasePreparations => Set<DesktopReleasePreparation>();
    public DbSet<DesktopBootstrapper> DesktopBootstrappers => Set<DesktopBootstrapper>();
    partial void OnModelCreatingPartial(ModelBuilder modelBuilder) => DesktopSharedReleaseMapping.Configure(modelBuilder, legacy: true);
}
