namespace TOOL_TESTS.Database;

public sealed class VietnameseSeedTextRepairMigrationTests
{
    private const string MigrationFileName = "VideoFactory.4.0.1.VietnameseSeedTextRepair.sql";

    [Fact]
    public void Migration_IsAsciiOnlySoSqlcmdCannotCorruptItsRepairValues()
    {
        var script = ReadMigration();

        Assert.All(script, character => Assert.InRange((int)character, 0, 127));
    }

    [Fact]
    public void Migration_RepairsOnlyKnownCorruptBuiltInPlanValues()
    {
        var script = ReadMigration();

        Assert.Contains("'trial-7'", script, StringComparison.Ordinal);
        Assert.Contains("'monthly-30'", script, StringComparison.Ordinal);
        Assert.Contains("'half-year-180'", script, StringComparison.Ordinal);
        Assert.Contains("CONVERT(varbinary(max), p.[Name]) = r.[CorruptName]", script, StringComparison.Ordinal);
        Assert.Contains("CONVERT(varbinary(max), p.[Description]) = r.[CorruptDescription]", script, StringComparison.Ordinal);
        Assert.Contains("4.0.1-vietnamese-seed-text-repair", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_ContainsExpectedUtf16ValuesForVietnamesePlanNames()
    {
        var script = ReadMigration();

        Assert.Contains(ToSqlHex("Dùng thử 7 ngày"), script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ToSqlHex("Gói 30 ngày"), script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ToSqlHex("Gói 180 ngày"), script, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToSqlHex(string value) =>
        $"0x{Convert.ToHexString(System.Text.Encoding.Unicode.GetBytes(value))}";

    private static string ReadMigration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "database", MigrationFileName);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate database/{MigrationFileName} from {AppContext.BaseDirectory}.");
    }
}
