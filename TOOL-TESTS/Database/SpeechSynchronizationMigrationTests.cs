namespace TOOL_TESTS.Database;

public sealed class SpeechSynchronizationMigrationTests
{
    private const string MigrationFile = "VideoFactory.4.1.3.SpeechSynchronization.sql";

    [Fact]
    public void Migration_IsTransactionalIdempotentAndRecordsVersion()
    {
        var sql = ReadMigration();

        Assert.Contains("SET XACT_ABORT ON", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN TRANSACTION", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IF COL_LENGTH(N'vf.Projects', N'SpeechProductionPolicy') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("IF OBJECT_ID(N'[vf].[SpeechVerificationReports]', N'U') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[SourceMediaAssetId] uniqueidentifier NOT NULL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_SpeechVerificationReports_TermsJson", sql, StringComparison.Ordinal);
        Assert.Contains("'Submitting'", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE [Version] = '4.1.3-speech-synchronization'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_PreservesExistingRequestKindsAndAddsTranscription()
    {
        var sql = ReadMigration();

        Assert.Contains("'TextRepair'", sql, StringComparison.Ordinal);
        Assert.Contains("'Transcription'", sql, StringComparison.Ordinal);
        Assert.Contains("CK_ProviderRequests_Kind", sql, StringComparison.Ordinal);
        Assert.Contains("CK_ProviderModels_Modality", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopRole_CanReadButCannotWriteVerificationTruth()
    {
        var sql = ReadMigration();

        Assert.Contains("GRANT SELECT ON OBJECT::[vf].[SpeechVerificationReports]", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DENY INSERT, UPDATE, DELETE ON OBJECT::[vf].[SpeechVerificationReports]", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadMigration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "database", MigrationFile);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Cannot locate {MigrationFile}.");
    }
}
