namespace TOOL_TESTS.Database;

public sealed class GptImageCharacterReferenceMigrationTests
{
    private const string MigrationFileName = "VideoFactory.4.0.2.GptImageCharacterReference.sql";

    [Fact]
    public void Migration_IsIdempotentAndRecordsItsSchemaVersion()
    {
        var script = ReadMigration();

        Assert.Contains("COL_LENGTH(N'vf.ProviderRequests', N'CharacterId') IS NULL", script);
        Assert.Contains("OBJECT_ID(N'[vf].[GeneratedImageOutputs]', N'U') IS NULL", script);
        Assert.Contains("COL_LENGTH(N'vf.MediaAssets', N'SourceProviderRequestId') IS NULL", script);
        Assert.Contains("4.0.2-gpt-image-character-reference", script);
        Assert.Contains("BEGIN TRANSACTION", script);
        Assert.Contains("IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION", script);
    }

    [Fact]
    public void Migration_StoresBinaryOutsideProviderResponseAndLimitsPayload()
    {
        var script = ReadMigration();

        Assert.Contains("[Payload] varbinary(max) NOT NULL", script);
        Assert.Contains("DATALENGTH([Payload]) = [SizeBytes]", script);
        Assert.Contains("[SizeBytes] <= 10485760", script);
        Assert.Contains("[ExpiresAtUtc] datetime2(3) NOT NULL", script);
        Assert.Contains("[DownloadedAtUtc] datetime2(3) NULL", script);
        Assert.Contains("PRIMARY KEY CLUSTERED ([ProviderRequestId])", script);
        Assert.DoesNotContain("b64_json", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Migration_DeniesDesktopPayloadAccessAndLinksGeneratedAssetToRequest()
    {
        var script = ReadMigration();

        Assert.Contains("DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[GeneratedImageOutputs]", script);
        Assert.Contains("[SourceProviderRequestId]", script);
        Assert.Contains("CREATE UNIQUE INDEX [UX_MediaAssets_SourceProviderRequest]", script);
        Assert.Contains("FOREIGN KEY ([SourceProviderRequestId]) REFERENCES [vf].[ProviderRequests]", script);
    }

    [Fact]
    public void Migration_UsesGuardedThrowStatementsAndVerifiesBeforePrintingReady()
    {
        var script = ReadMigration();

        Assert.DoesNotContain(";THROW", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN\n        THROW 51020", script);
        Assert.Contains("BEGIN\n        THROW 51021", script);
        Assert.Contains("THROW 51022, 'GPT-Image-2 character reference schema verification failed.'", script);
        Assert.Contains("THROW 51024, 'GPT-Image-2 character reference schema version was not recorded.'", script);

        var verificationIndex = script.IndexOf("THROW 51024", StringComparison.Ordinal);
        var readyIndex = script.IndexOf("PRINT N'VideoFactory GPT-Image-2 character reference schema 4.0.2 is ready.'", StringComparison.Ordinal);
        Assert.True(verificationIndex >= 0 && readyIndex > verificationIndex);
    }

    private static string ReadMigration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "database", MigrationFileName);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Cannot locate database/{MigrationFileName} from the test output directory.");
    }
}
