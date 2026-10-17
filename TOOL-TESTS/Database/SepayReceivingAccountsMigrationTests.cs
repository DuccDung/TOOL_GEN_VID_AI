namespace TOOL_TESTS.Database;

public sealed class SepayReceivingAccountsMigrationTests
{
    [Fact]
    public void RestrictedAccountMigrationRequiresVerifiedProtectionBeforeCreatingTables()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        const string file = "VideoFactory.4.1.12.SepayReceivingAccountsPermissions.sql";
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "database", file))) directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName, "database", file));
        Assert.True(source.IndexOf("THROW 51122", StringComparison.Ordinal) < source.IndexOf("CREATE TABLE", StringComparison.Ordinal));
        Assert.Contains("class = 3 AND major_id = SCHEMA_ID(N'auth')", source);
        Assert.Contains("@SchemaDenyCount <> 4 AND @ObjectDenyCount <> 8", source);
        Assert.Contains("THROW 51123", source);
        Assert.True(source.IndexOf("THROW 51123", StringComparison.Ordinal) < source.IndexOf("COMMIT TRANSACTION", StringComparison.Ordinal));
        Assert.Contains("4.1.10-sepay-receiving-accounts", source);
        Assert.Contains("4.1.12-sepay-receiver-permissions", source);
        Assert.DoesNotContain("UPDATE [auth].[SepayPaymentSettings]", source);
        Assert.DoesNotContain("GRANT SELECT", source);
        Assert.DoesNotContain("EXECUTE AS", source);
    }

    [Fact]
    public void MigrationPreservesAdminConfigurationAndKeepsDesktopOutOfPaymentSettings()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "database", "VideoFactory.4.1.10.SepayReceivingAccounts.sql"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName, "database", "VideoFactory.4.1.10.SepayReceivingAccounts.sql"));
        Assert.Contains("SET XACT_ABORT ON", source);
        Assert.Contains("IF OBJECT_ID(N'[auth].[SepayReceivingAccounts]'", source);
        Assert.Contains("IF OBJECT_ID(N'[auth].[SepayPaymentSettings]'", source);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM [auth].[SepayPaymentSettings] WHERE [SettingsId] = 1)", source);
        Assert.Contains("VALUES (1, 0, NULL, NEWID(), SYSUTCDATETIME())", source);
        Assert.Contains("CK_SepayPaymentSettings_Singleton", source);
        Assert.Contains("CK_SepayPaymentSettings_EnabledReceiver", source);
        Assert.Contains("FK_SepayPaymentSettings_ReceivingAccount", source);
        Assert.Contains("UQ_SepayReceivingAccounts_BankAccount", source);
        Assert.Contains("DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[auth].[SepayReceivingAccounts] TO [VideoMakerDesktopRole]", source);
        Assert.Contains("DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[auth].[SepayPaymentSettings] TO [VideoMakerDesktopRole]", source);
        Assert.Contains("4.1.10-sepay-receiving-accounts", source);
        Assert.DoesNotContain("UPDATE [auth].[SepayPaymentSettings]", source);
        Assert.DoesNotContain("WebhookApiKey", source);
    }
}
