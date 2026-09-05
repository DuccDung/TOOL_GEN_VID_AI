namespace TOOL_TESTS.Database;

public sealed class SceneFirstFrameMigrationTests
{
    private const string MigrationFile = "VideoFactory.4.1.1.SceneFirstFrames.sql";

    [Fact]
    public void Migration_IsTransactionalIdempotentAndRecordsVersionOnce()
    {
        var sql = ReadRepositoryFile("database", MigrationFile);

        Assert.Contains("SET XACT_ABORT ON", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN TRANSACTION", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IF OBJECT_ID(N'[vf].[SceneFirstFrames]'", sql, StringComparison.Ordinal);
        Assert.Contains("IF COL_LENGTH(N'vf.ProviderRequests', N'InputSceneFirstFrameId') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE [Version] = '4.1.1-scene-first-frames'", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO [ai].[SchemaVersions]", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("INSERT INTO [vf].[SceneFirstFrames]", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_ConstrainsLifecycleSnapshotsAndDesktopWrites()
    {
        var sql = ReadRepositoryFile("database", MigrationFile);
        var leastPrivilege = ReadRepositoryFile("database", "VideoFactory.DesktopLeastPrivilege.sql");

        Assert.Contains("UNIQUE ([SceneId], [Version])", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE [Status] = 'Approved'", sql, StringComparison.Ordinal);
        Assert.Contains("'PendingReview','Approved','Rejected','Superseded','Invalidated'", sql, StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY ([InputSceneFirstFrameId])", sql, StringComparison.Ordinal);
        Assert.Contains("DENY SELECT, INSERT, UPDATE, DELETE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[vf].[SceneFirstFrames]", leastPrivilege, StringComparison.Ordinal);
        Assert.DoesNotContain("1024", sql, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Cannot locate {string.Join('/', parts)}.");
    }
}
