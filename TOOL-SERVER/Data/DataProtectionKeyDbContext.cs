using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace TOOL_SERVER.Data;

public sealed class DataProtectionKeyDbContext(DbContextOptions<DataProtectionKeyDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<DataProtectionKey>(entity =>
        {
            entity.ToTable("DataProtectionKeys", "dbo");
            entity.HasKey(x => x.Id);
        });
    }
}
