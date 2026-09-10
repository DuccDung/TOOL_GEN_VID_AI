namespace TOOL_TESTS.Database;

public sealed class VietsubCloudMigrationTests
{
    [Fact]
    public void Migration_PreservesVideoReferencesAndRequiresExactlyOneProjectKind()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "TOOL_GEN_POST_VIDEO.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var sql = File.ReadAllText(Path.Combine(directory.FullName, "database", "VideoFactory.4.1.6.VietsubCloudTranslation.sql"));
        Assert.Contains("SET XACT_ABORT ON", sql); Assert.Contains("BEGIN TRANSACTION", sql); Assert.Contains("ROLLBACK TRANSACTION", sql);
        Assert.Contains("IF OBJECT_ID(N'vs.CloudTranslationAttempts', N'U') IS NULL", sql);
        Assert.Contains("UNIQUE ([JobId], [Ordinal], [Attempt])", sql);
        Assert.Contains("ALTER COLUMN [ProjectId] uniqueidentifier NULL", sql);
        Assert.Contains("FOREIGN KEY ([VietsubProjectId]) REFERENCES [vs].[Projects]([ProjectId])", sql);
        Assert.Contains("([ProjectId] IS NOT NULL AND [VietsubProjectId] IS NULL) OR ([ProjectId] IS NULL AND [VietsubProjectId] IS NOT NULL)", sql);
        Assert.Contains("EXEC(N'ALTER TABLE [ai].[BudgetReservations] WITH CHECK", sql);
        Assert.DoesNotContain("DROP CONSTRAINT", sql); Assert.DoesNotContain("DELETE FROM", sql); Assert.DoesNotContain("GRANT", sql);
        Assert.True(sql.IndexOf("Vietsub Cloud schema verification failed.", StringComparison.Ordinal)
            < sql.IndexOf("Runtime remains disabled until configured.", StringComparison.Ordinal));
    }
}
