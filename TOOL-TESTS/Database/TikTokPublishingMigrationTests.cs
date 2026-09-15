namespace TOOL_TESTS.Database;

public sealed class TikTokPublishingMigrationTests
{
    private const string MigrationFile = "VideoFactory.4.1.6.TikTokPublishing.sql";

    [Fact]
    public void Migration_IsTransactionalIdempotentAndRecordsVersion()
    {
        var sql = ReadMigration();

        Assert.Contains("SET XACT_ABORT ON", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN TRANSACTION", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IF SCHEMA_ID(N'social') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'[social].[TikTokConnections]', N'U') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'[social].[TikTokOAuthSessions]', N'U') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'[social].[TikTokPublishJobs]', N'U') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE [Version] = '4.1.6-tiktok-publishing'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_SeparatesUsersAndNeverGrantsDesktopAccess()
    {
        var sql = ReadMigration();

        Assert.Contains("CREATE UNIQUE INDEX [UX_TikTokConnections_UserId]", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX [UX_TikTokPublishJobs_UserRequest]", sql, StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY ([UserId])", sql, StringComparison.Ordinal);
        Assert.Contains("[ProtectedAccessToken] nvarchar(max) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[ProtectedRefreshToken] nvarchar(max) NOT NULL", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("VideoMakerDesktopRole", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GRANT", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadMigration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "database", MigrationFile);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Cannot locate {MigrationFile}.");
    }
}
