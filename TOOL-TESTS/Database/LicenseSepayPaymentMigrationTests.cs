namespace TOOL_TESTS.Database;

public sealed class LicenseSepayPaymentMigrationTests
{
    [Fact]
    public void Migration_IsIdempotentFailClosedAndKeepsPaymentServerOnly()
    {
        var source = ReadRepositoryFile(
            "database",
            "VideoFactory.4.0.10.LicenseSepayPayments.sql");

        Assert.Contains("SET XACT_ABORT ON", source, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'[auth].[LicensePayments]'", source, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT [CK_LicensePayments_Price]", source, StringComparison.Ordinal);
        Assert.Contains("CREATE UNIQUE INDEX [UQ_LicensePayments_ProviderTransactionId]", source, StringComparison.Ordinal);
        Assert.Contains("WHERE [ProviderTransactionId] IS NOT NULL", source, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT [DF_LicensePlans_IsPublic] DEFAULT (0)", source, StringComparison.Ordinal);
        Assert.Contains("[DefaultDurationDays] <= 3650", source, StringComparison.Ordinal);
        Assert.Contains(
            "EXEC sys.sp_executesql N'\n            ALTER TABLE [auth].[LicensePlans] WITH CHECK\n                ADD CONSTRAINT [CK_LicensePlans_SalePriceVnd]",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "EXEC sys.sp_executesql N'\n            ALTER TABLE [auth].[LicensePlans] WITH CHECK\n                ADD CONSTRAINT [CK_LicensePlans_PublicSale]",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "EXEC sys.sp_executesql N'\n            ALTER TABLE [auth].[LicensePlans] WITH CHECK\n                ADD CONSTRAINT [CK_LicensePlans_DisplayOrder]",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "EXEC sys.sp_executesql N'\n            ALTER TABLE [auth].[LicensePlans] WITH CHECK\n                ADD CONSTRAINT [CK_LicensePlans_MarketingFeaturesJson]",
            source,
            StringComparison.Ordinal);
        Assert.Contains("DENY SELECT, INSERT, UPDATE, DELETE", source, StringComparison.Ordinal);
        Assert.Contains("4.0.10-license-sepay-payments", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WebhookApiKey", source, StringComparison.OrdinalIgnoreCase);
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
