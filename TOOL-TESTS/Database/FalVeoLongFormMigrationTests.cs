namespace TOOL_TESTS.Database;

public sealed class FalVeoLongFormMigrationTests
{
    [Fact]
    public void Migration_ExecutesQuotedConstraintNameThroughSpExecuteSql()
    {
        var source = ReadRepositoryFile(
            "database",
            "VideoFactory.4.0.9.FalVeoLongForm.sql");

        Assert.Contains("DECLARE @dropPolicyPkSql nvarchar(max)", source, StringComparison.Ordinal);
        Assert.Contains("QUOTENAME(@existingPolicyPk)", source, StringComparison.Ordinal);
        Assert.Contains("EXEC sys.sp_executesql @dropPolicyPkSql", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EXEC(N'ALTER TABLE", source, StringComparison.Ordinal);
        Assert.Contains("SET [PolicyScope] = ''Default''", source, StringComparison.Ordinal);
        Assert.Contains("CHECK ([PolicyScope] IN (''Default'', ''LongForm''))", source, StringComparison.Ordinal);
        Assert.Contains("PRIMARY KEY CLUSTERED ([OrganizationId], [PolicyScope])", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\n    UPDATE [ai].[OrganizationVideoPolicies]", source, StringComparison.Ordinal);
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
