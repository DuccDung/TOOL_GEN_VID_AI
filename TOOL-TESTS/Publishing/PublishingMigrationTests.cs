using TOOL_SERVER.Publishing;
using Microsoft.EntityFrameworkCore;

namespace TOOL_TESTS.Publishing;

public sealed class PublishingMigrationTests
{
    [Fact]
    public void MigrationCoversEveryMappedTableAndKeepsDesktopOutOfSocialSchema()
    {
        var sql = Read();
        using var db = new PublishingDbContext(new DbContextOptionsBuilder<PublishingDbContext>().UseSqlServer("Server=invalid;Database=invalid;Integrated Security=true;TrustServerCertificate=true").Options);
        foreach (var entity in db.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            Assert.Contains($"IF OBJECT_ID(N'social.{table}', N'U') IS NULL", sql);
            foreach (var property in entity.GetProperties()) Assert.Contains(property.GetColumnName(), sql);
        }
        Assert.Contains("CREATE UNIQUE INDEX IX_PublishingRuns_ScheduleId_PublishAtUtc", sql);
        Assert.Contains("DENY SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::social TO VideoMakerDesktopRole", sql);
        Assert.Contains("SET XACT_ABORT ON", sql); Assert.Contains("4.1.10-publishing-schedules", sql);
        Assert.DoesNotContain("UPDATE vf.Projects", sql); Assert.DoesNotContain("ProviderModelRates", sql);
    }
    private static string Read()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        { var path = Path.Combine(directory.FullName, "database", "VideoFactory.4.1.10.PublishingSchedules.sql"); if (File.Exists(path)) return File.ReadAllText(path); }
        throw new FileNotFoundException("Publishing migration");
    }
}
