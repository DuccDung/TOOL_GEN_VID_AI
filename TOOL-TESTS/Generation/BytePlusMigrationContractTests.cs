namespace TOOL_TESTS.Generation;

public sealed class BytePlusMigrationContractTests
{
    [Fact]
    public void Migration404_IsIdempotentProtectedAndDoesNotSeedProductionPricing()
    {
        var sql = ReadRepositoryFile("database", "VideoFactory.4.0.4.BytePlusSeedance.sql");

        Assert.Contains("SET XACT_ABORT ON", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN TRANSACTION", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IF NOT EXISTS", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'4.0.4-byteplus-seedance'", sql, StringComparison.Ordinal);
        Assert.Contains("[ai].[OrganizationVideoPolicies]", sql, StringComparison.Ordinal);
        Assert.Contains("[vf].[GeneratedVideoOutputs]", sql, StringComparison.Ordinal);
        Assert.Contains("[VideoProviderCode]", sql, StringComparison.Ordinal);
        Assert.Contains("[VideoModelCode]", sql, StringComparison.Ordinal);
        Assert.Contains("'byteplus'", sql, StringComparison.Ordinal);
        Assert.Contains("dreamina-seedance-2-0-260128", sql, StringComparison.Ordinal);
        Assert.Contains("dreamina-seedance-2-5-260628", sql, StringComparison.Ordinal);
        Assert.Contains("DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[ai].[OrganizationVideoPolicies]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[GeneratedVideoOutputs]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO [vf].[CostRates]", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration404_SeedsBytePlusAndSeedanceDisabled()
    {
        var sql = ReadRepositoryFile("database", "VideoFactory.4.0.4.BytePlusSeedance.sql");

        Assert.Contains("'https://ark.ap-southeast.bytepluses.com/api/v3/'", sql, StringComparison.Ordinal);
        Assert.Contains("N'BytePlus ModelArk',\n            'https://ark.ap-southeast.bytepluses.com/api/v3/',\n            0,", sql, StringComparison.Ordinal);
        Assert.Contains("N'Dreamina Seedance 2.0', 'Video',\n            0, 0,", sql, StringComparison.Ordinal);
        Assert.Contains("N'Dreamina Seedance 2.5', 'Video',\n            0, 0,", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration404_BackfillIsCompiledAfterProjectSnapshotColumnsAreAdded()
    {
        var sql = ReadRepositoryFile("database", "VideoFactory.4.0.4.BytePlusSeedance.sql");

        Assert.Contains("EXEC(N'UPDATE [vf].[Projects]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\n    UPDATE [vf].[Projects]\n", sql, StringComparison.OrdinalIgnoreCase);
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
