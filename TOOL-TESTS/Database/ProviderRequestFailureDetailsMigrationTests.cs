namespace TOOL_TESTS.Database;

public sealed class ProviderRequestFailureDetailsMigrationTests
{
    private const string MigrationFile = "VideoFactory.4.1.2.ProviderRequestFailureDetails.sql";

    [Fact]
    public void Migration_IsTransactionalIdempotentAndRecordsVersionOnce()
    {
        var sql = ReadRepositoryFile("database", MigrationFile);

        Assert.Contains("SET XACT_ABORT ON", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN TRANSACTION", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IF COL_LENGTH(N'vf.ProviderRequests', N'ErrorDetailsJson') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("IF COL_LENGTH(N'vf.ProviderRequests', N'ParentProviderRequestId') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE [Version] = '4.1.2-provider-request-failure-details'", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO [ai].[SchemaVersions]", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_DefersDdlThatReferencesNewColumnsUntilAfterTheyExist()
    {
        var sql = ReadRepositoryFile("database", MigrationFile);

        Assert.Matches(
            @"EXEC sys\.sp_executesql N'\s*ALTER TABLE \[vf\]\.\[ProviderRequests\] WITH CHECK\s*ADD CONSTRAINT \[CK_ProviderRequests_ErrorDetailsJson\]",
            sql);
        Assert.Matches(
            @"EXEC sys\.sp_executesql N'\s*ALTER TABLE \[vf\]\.\[ProviderRequests\] WITH CHECK\s*ADD CONSTRAINT \[FK_ProviderRequests_ParentProviderRequest\]",
            sql);
        Assert.Matches(
            @"EXEC sys\.sp_executesql N'\s*CREATE UNIQUE INDEX \[IX_ProviderRequests_Parent\]",
            sql);
    }

    [Fact]
    public void Migration_ConstrainsJsonAndAllowsOnlyOneRepairChild()
    {
        var sql = ReadRepositoryFile("database", MigrationFile);

        Assert.Contains("CHECK ([ErrorDetailsJson] IS NULL OR ISJSON([ErrorDetailsJson]) = 1)", sql, StringComparison.Ordinal);
        Assert.Contains("FOREIGN KEY ([ParentProviderRequestId])", sql, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT [CK_ProviderRequests_Kind]", sql, StringComparison.Ordinal);
        Assert.Contains("'TextRepair'", sql, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX [IX_ProviderRequests_Parent]", sql, StringComparison.Ordinal);
        Assert.Contains("[RequestKind] = ''TextRepair''", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ResponseJson] =", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiKey", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_PrintsReadyOnlyInTheSuccessfulVerificationBatch()
    {
        var sql = ReadRepositoryFile("database", MigrationFile);
        var batches = System.Text.RegularExpressions.Regex.Split(
            sql,
            @"^\s*GO\s*$",
            System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var verificationBatch = Assert.Single(
            batches,
            batch => batch.Contains("THROW 51121", StringComparison.Ordinal));

        Assert.Contains(
            "VideoFactory provider request failure details 4.1.2 are ready.",
            verificationBatch,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GenerationService_DetectsMissingMigrationWithAnOperationalError()
    {
        var source = ReadRepositoryFile("TOOL-SERVER", "Generation", "GenerationService.cs");

        Assert.Contains("Microsoft.EntityFrameworkCore.SqlServer", source, StringComparison.Ordinal);
        Assert.Contains("4.1.2-provider-request-failure-details", source, StringComparison.Ordinal);
        Assert.Contains("content_failure_schema_not_ready", source, StringComparison.Ordinal);
        Assert.Contains("Hãy chạy migration VideoFactory 4.1.2", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopFailureContract_DoesNotExposeStoredPlanOrProviderSecrets()
    {
        var propertyNames = typeof(TOOL_SHARED.Contracts.Generation.ContentLanguageFailureResponse)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("ResponseJson", propertyNames);
        Assert.DoesNotContain("ErrorDetailsJson", propertyNames);
        Assert.DoesNotContain("ApiKey", propertyNames);
        Assert.DoesNotContain("Credential", propertyNames);
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
