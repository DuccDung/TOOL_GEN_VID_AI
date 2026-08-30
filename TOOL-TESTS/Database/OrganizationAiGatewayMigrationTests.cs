namespace TOOL_TESTS.Database;

public sealed class OrganizationAiGatewayMigrationTests
{
    [Fact]
    public void Migration_DefersReferencesToColumnsAddedInTheSameBatch()
    {
        var script = ReadMigration();

        Assert.Contains("EXEC(N'CREATE INDEX [IX_Projects_Organization_Status]", script);
        Assert.Contains("EXEC(N'CREATE UNIQUE INDEX [UQ_ProviderRequests_Organization_Idempotency]", script);
        Assert.Contains("EXEC(N'CREATE INDEX [IX_ProviderRequests_Organization_User_Created]", script);
        Assert.Contains("EXEC sys.sp_executesql\n            N'UPDATE p", script);

        Assert.DoesNotContain("\n        CREATE INDEX [IX_Projects_Organization_Status]", script);
        Assert.DoesNotContain("\n        CREATE UNIQUE INDEX [UQ_ProviderRequests_Organization_Idempotency]", script);
        Assert.DoesNotContain("\n        CREATE INDEX [IX_ProviderRequests_Organization_User_Created]", script);
        Assert.DoesNotContain("\n        UPDATE p\n", script);
    }

    [Fact]
    public void Migration_ValidatesRecordedVersionBeforePrintingReady()
    {
        var script = ReadMigration();
        var validationIndex = script.IndexOf(
            "VideoFactory AI Gateway migration did not create ai.SchemaVersions.",
            StringComparison.Ordinal);
        var readyIndex = script.IndexOf(
            "VideoFactory AI Gateway schema 4.0.0 is ready.",
            StringComparison.Ordinal);

        Assert.True(validationIndex >= 0);
        Assert.True(readyIndex > validationIndex);
    }

    private static string ReadMigration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "database",
                "VideoFactory.4.0.0.OrganizationAiGateway.sql");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Cannot locate the organization AI gateway migration from the test output directory.");
    }
}
