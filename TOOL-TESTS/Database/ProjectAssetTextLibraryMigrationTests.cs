namespace TOOL_TESTS.Database;

public sealed class ProjectAssetTextLibraryMigrationTests
{
    [Fact]
    public void Migration407_IsTransactionalIdempotentAndRecordsVersion()
    {
        var sql = ReadRepositoryFile("database", "VideoFactory.4.0.7.ProjectAssetTextLibrary.sql");

        Assert.Contains("SET XACT_ABORT ON", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN TRANSACTION", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IF OBJECT_ID(N'[vf].[ProjectAssets]'", sql, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'[vf].[ProjectAssetVersions]'", sql, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'[vf].[SceneAssetAssignments]'", sql, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'[vf].[ProviderRequestAssetVersions]'", sql, StringComparison.Ordinal);
        Assert.Contains("'4.0.7-project-asset-text-library'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration407_ConstrainsTypesLocksAndImmutableRequestSnapshots()
    {
        var sql = ReadRepositoryFile("database", "VideoFactory.4.0.7.ProjectAssetTextLibrary.sql");
        var leastPrivilege = ReadRepositoryFile("database", "VideoFactory.DesktopLeastPrivilege.sql");

        foreach (var assetType in new[] { "Background", "Prop", "Item" })
        {
            Assert.Contains($"'{assetType}'", sql, StringComparison.Ordinal);
        }
        Assert.Contains("CK_ProjectAssets_LockState", sql, StringComparison.Ordinal);
        Assert.Contains("UQ_ProjectAssetVersions_Asset_Version", sql, StringComparison.Ordinal);
        Assert.Contains("FK_ProviderRequestAssetVersions_ProjectAssetVersions", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Image", ExtractProjectAssetsTable(sql), StringComparison.OrdinalIgnoreCase);
        foreach (var table in new[]
                 {
                     "ProjectAssets",
                     "ProjectAssetVersions",
                     "SceneAssetAssignments",
                     "ProviderRequestAssetVersions"
                 })
        {
            Assert.Contains($"OBJECT::[vf].[{table}] TO [VideoMakerDesktopRole]", leastPrivilege, StringComparison.Ordinal);
        }
    }

    private static string ExtractProjectAssetsTable(string sql)
    {
        var start = sql.IndexOf("CREATE TABLE [vf].[ProjectAssets]", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = sql.IndexOf("CREATE INDEX [IX_ProjectAssets_Project_Status]", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return sql[start..end];
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Cannot locate repository file: {Path.Combine(relativeParts)}");
    }
}
