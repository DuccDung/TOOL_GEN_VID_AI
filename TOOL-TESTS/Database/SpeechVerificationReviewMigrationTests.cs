namespace TOOL_TESTS.Database;

public sealed class SpeechVerificationReviewMigrationTests
{
    private const string MigrationFile = "VideoFactory.4.1.5.SpeechVerificationReview.sql";

    [Fact]
    public void Migration_IsTransactionalIdempotentAndRecordsVersion()
    {
        var sql = ReadMigration();

        Assert.Contains("SET XACT_ABORT ON", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN TRANSACTION", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IF COL_LENGTH(N'vf.SpeechVerificationReports', N'ReviewApproved') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("IF COL_LENGTH(N'vf.SpeechVerificationReports', N'ReviewReason') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("IF COL_LENGTH(N'vf.SpeechVerificationReports', N'ReviewedByUserId') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("IF COL_LENGTH(N'vf.SpeechVerificationReports', N'ReviewedAtUtc') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE [Version] = '4.1.5-speech-verification-review'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Migration_RequiresCompleteAuditForNeedsReviewOverride()
    {
        var sql = ReadMigration();

        Assert.Contains("CK_SpeechVerificationReports_Review", sql, StringComparison.Ordinal);
        Assert.Contains("[ReviewApproved] = 1 AND [Status] = ''NeedsReview''", sql, StringComparison.Ordinal);
        Assert.Contains("LEN(LTRIM(RTRIM([ReviewReason]))) BETWEEN 10 AND 1000", sql, StringComparison.Ordinal);
        Assert.Contains("[ReviewedByUserId] IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("[ReviewedAtUtc] IS NOT NULL", sql, StringComparison.Ordinal);
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
