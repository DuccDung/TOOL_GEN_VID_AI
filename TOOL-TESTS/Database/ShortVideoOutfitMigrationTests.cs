namespace TOOL_TESTS.Database;

public sealed class ShortVideoOutfitMigrationTests
{
    [Fact]
    public void Migration_GuardsBothTablesAndVersion_AndDoesNotSeedPricesOrBackfillOldProjects()
    {
        var sql = Read("VideoFactory.4.1.9.ShortVideoCharacterOutfit.sql");
        Assert.Contains("SET XACT_ABORT ON", sql);
        Assert.Contains("BEGIN TRANSACTION", sql);
        Assert.Contains("IF OBJECT_ID(N'vf.ShortVideoOutfits', N'U') IS NULL", sql);
        Assert.Contains("IF OBJECT_ID(N'vf.ShortVideoOperations', N'U') IS NULL", sql);
        Assert.Contains("WHERE Version='4.1.9-short-video-outfit'", sql);
        Assert.Contains("FOREIGN KEY (OrganizationId) REFERENCES ai.Organizations", sql);
        Assert.Contains("Revision > 0", sql);
        Assert.Contains("CK_ShortVideoOperations_Approval", sql);
        Assert.DoesNotContain("UPDATE vf.Projects", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProviderModelRates", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopPrincipal_IsDeniedBothNewServerOwnedTables()
    {
        var migration = Read("VideoFactory.4.1.9.ShortVideoCharacterOutfit.sql");
        var permissions = Read("VideoFactory.DesktopLeastPrivilege.sql");
        foreach (var table in new[] { "ShortVideoOutfits", "ShortVideoOperations" })
        {
            Assert.Contains($"DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::vf.{table} TO VideoMakerDesktopRole", migration);
            Assert.Contains($"DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[{table}] TO [VideoMakerDesktopRole]", permissions);
        }
    }
    private static string Read(string file)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "database", file);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException(file);
    }
}
