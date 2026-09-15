namespace TOOL_TESTS.Database;

public sealed class TikTokAdminCredentialsMigrationTests
{
    private const string MigrationFile = "VideoFactory.4.1.7.TikTokAdminCredentials.sql";

    [Fact]
    public void Migration_IsTransactionalIdempotentAndOrderedAfterPublishingSchema()
    {
        var sql = ReadMigration();

        Assert.Contains("SET XACT_ABORT ON", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET QUOTED_IDENTIFIER ON", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SET NUMERIC_ROUNDABORT OFF", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN TRANSACTION", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OBJECT_ID(N'[social].[TikTokConnections]'", sql, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'[social].[TikTokAppCredentials]', N'U') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'[social].[TikTokIntegrationSettings]', N'U') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE [Version] = '4.1.7-tiktok-admin-credentials'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_ProtectsCredentialLifecycleAndDoesNotGrantDesktopSqlAccess()
    {
        var sql = ReadMigration();

        Assert.Contains("[ProtectedPayload] nvarchar(max) NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("UX_TikTokAppCredentials_Active", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE [Status] = 'Active'", sql, StringComparison.Ordinal);
        Assert.Contains("UX_TikTokAppCredentials_Pending", sql, StringComparison.Ordinal);
        Assert.Contains("[TikTokAppCredentialId] uniqueidentifier NULL", sql, StringComparison.Ordinal);
        Assert.Contains("FK_TikTokOAuthSessions_AppCredentials", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientSecret", sql, StringComparison.OrdinalIgnoreCase);
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
