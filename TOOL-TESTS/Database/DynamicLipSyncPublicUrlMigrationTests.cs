namespace TOOL_TESTS.Database;

public sealed class DynamicLipSyncPublicUrlMigrationTests
{
    [Fact]
    public void Migration_UsesExistingServerOwnedSettingsAndDoesNotSeedAUrl()
    {
        var source = ReadRepositoryFile("database", "VideoFactory.4.1.7.DynamicLipSyncPublicUrl.sql");
        var leastPrivilege = ReadRepositoryFile("database", "VideoFactory.DesktopLeastPrivilege.sql");

        Assert.Contains("[vf].[AppSettings]", source, StringComparison.Ordinal);
        Assert.Contains("[auth].[AccountAuditLogs]", source, StringComparison.Ordinal);
        Assert.Contains("WHERE [Version] = '4.1.6-lip-sync-generation'", source, StringComparison.Ordinal);
        Assert.Contains("4.1.7-dynamic-lip-sync-public-url", source, StringComparison.Ordinal);
        Assert.Contains(
            "DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[AppSettings]",
            source,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[AppSettings]",
            leastPrivilege,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Generation:LipSync:PublicBaseUrl", source, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO [ai].[CostRates]", source, StringComparison.OrdinalIgnoreCase);
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
