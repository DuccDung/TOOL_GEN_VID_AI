namespace TOOL_TESTS.Database;

public sealed class LipSyncGenerationMigrationTests
{
    [Fact]
    public void Migration_AddsLineageApprovalAndKeepsDesktopFromMutatingGenerationState()
    {
        var source = ReadRepositoryFile("database", "VideoFactory.4.1.6.LipSyncGeneration.sql");

        Assert.Contains("[vf].[LipSyncInputSessions]", source, StringComparison.Ordinal);
        Assert.Contains("[vf].[LipSyncGenerations]", source, StringComparison.Ordinal);
        Assert.Contains("[PreparedVideoSha256]", source, StringComparison.Ordinal);
        Assert.Contains("[PreparedAudioSha256]", source, StringComparison.Ordinal);
        Assert.Contains("[ApprovedLipSyncGenerationId]", source, StringComparison.Ordinal);
        Assert.Contains("[CK_Projects_LipSyncSnapshot]", source, StringComparison.Ordinal);
        Assert.Contains("EXEC(N'ALTER TABLE [vf].[Projects] WITH CHECK", source, StringComparison.Ordinal);
        Assert.Contains("'LipSync'", source, StringComparison.Ordinal);
        Assert.Contains("WHERE [Version] = '4.1.5-speech-verification-review'", source, StringComparison.Ordinal);
        Assert.Contains("[FK_LipSyncInputSessions_Organizations]", source, StringComparison.Ordinal);
        Assert.Contains("[FK_LipSyncInputSessions_Users]", source, StringComparison.Ordinal);
        Assert.Contains("[FK_LipSyncGenerations_ApprovedUsers]", source, StringComparison.Ordinal);
        Assert.Contains("[CK_LipSyncInputSessions_Lifecycle]", source, StringComparison.Ordinal);
        Assert.Contains("[CK_LipSyncGenerations_Lifecycle]", source, StringComparison.Ordinal);
        Assert.Contains("[ApprovedAtUtc] IS NOT NULL AND [ApprovedByUserId] IS NOT NULL", source, StringComparison.Ordinal);
        Assert.Contains("[CK_ProviderRequests_Status]", source, StringComparison.Ordinal);
        Assert.Contains("'Expired'", source, StringComparison.Ordinal);
        Assert.Contains("DENY INSERT, UPDATE, DELETE ON OBJECT::[vf].[LipSyncInputSessions]", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DENY INSERT, UPDATE, DELETE ON OBJECT::[vf].[LipSyncGenerations]", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DENY UPDATE ON OBJECT::[vf].[Projects]", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DENY UPDATE ON OBJECT::[vf].[Scenes] ([ApprovedLipSyncGenerationId])", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO [ai].[CostRates]", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SET [IsEnabled] = 1", source, StringComparison.OrdinalIgnoreCase);
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
