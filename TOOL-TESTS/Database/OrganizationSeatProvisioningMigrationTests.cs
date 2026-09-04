namespace TOOL_TESTS.Database;

public sealed class OrganizationSeatProvisioningMigrationTests
{
    [Fact]
    public void Migration_IsTransactionalIdempotentConstrainedAndServerOnly()
    {
        var sql = ReadRepositoryFile("database", "VideoFactory.4.0.11.OrganizationSeatProvisioning.sql");
        var leastPrivilege = ReadRepositoryFile("database", "VideoFactory.DesktopLeastPrivilege.sql");

        Assert.Contains("SET XACT_ABORT ON", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN TRANSACTION", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4.0.11-organization-seat-provisioning", sql, StringComparison.Ordinal);
        foreach (var table in new[]
                 {
                     "OrganizationPools",
                     "OrganizationPoolOrganizations",
                     "LicensePlanOrganizationPools",
                     "OrganizationSeatAssignments"
                 })
        {
            Assert.Contains($"IF OBJECT_ID(N'[ai].[{table}]'", sql, StringComparison.Ordinal);
            Assert.Contains($"OBJECT::[ai].[{table}] TO [VideoMakerDesktopRole]", leastPrivilege, StringComparison.Ordinal);
        }
        Assert.Contains("CK_OrganizationPoolOrganizations_Counts", sql, StringComparison.Ordinal);
        Assert.Contains("UQ_OrganizationSeatAssignments_Payment", sql, StringComparison.Ordinal);
        Assert.Contains("COL_LENGTH(N'ai.OrganizationMembers', N'IsProvisioningManaged')", sql, StringComparison.Ordinal);
        Assert.Contains("DF_OrganizationMembers_IsProvisioningManaged", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE [IsAutoAssignmentEnabled] = 1", sql, StringComparison.Ordinal);
        Assert.Contains("DENY SELECT, INSERT, UPDATE, DELETE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("EncryptedPayload", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void VerificationScript_IsReadOnlyAndChecksCountersOrphansIndexesAndDesktopDenies()
    {
        var sql = ReadRepositoryFile("database", "Verify.VideoFactory.4.0.11.OrganizationSeatProvisioning.sql");

        Assert.DoesNotMatch(
            new System.Text.RegularExpressions.Regex(
                "^\\s*(INSERT|UPDATE|DELETE|ALTER|CREATE|DROP|MERGE|TRUNCATE)\\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.Multiline),
            sql);
        Assert.Contains("4.0.11-organization-seat-provisioning", sql, StringComparison.Ordinal);
        Assert.Contains("UQ_OrganizationPoolOrganizations_AutoOrganization", sql, StringComparison.Ordinal);
        Assert.Contains("UQ_OrganizationSeatAssignments_Payment", sql, StringComparison.Ordinal);
        Assert.Contains("contains orphaned rows", sql, StringComparison.Ordinal);
        Assert.Contains("Stored seat counters do not match consuming assignments", sql, StringComparison.Ordinal);
        Assert.Contains("VideoMakerDesktopRole is missing an object-level DENY", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE payment.[Status] = 'Paid'", sql, StringComparison.Ordinal);
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
