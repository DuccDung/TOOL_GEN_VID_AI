namespace TOOL_TESTS.LocalVoice;

public sealed class LocalVoiceMigrationTests
{
    [Fact]
    public void Migration_IsIdempotentMetadataOnly_AndDoesNotDependOnCloudLipSync()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "TOOL_GEN_POST_VIDEO.slnx"))) current = current.Parent;
        Assert.NotNull(current);
        var sql = File.ReadAllText(Path.Combine(current!.FullName, "database", "VideoFactory.4.1.8.LocalVoiceConsistency.sql"));
        Assert.Contains("COL_LENGTH(N'vf.Projects', N'LocalVoicePolicyVersion') IS NULL", sql);
        Assert.Contains("CK_Projects_LocalVoicePolicyVersion", sql);
        Assert.Contains("BEGIN TRANSACTION", sql);
        Assert.Contains("ROLLBACK TRANSACTION", sql);
        Assert.Contains("4.1.8-local-voice-consistency", sql);
        Assert.DoesNotContain("CREATE TABLE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("[LipSyncGenerations]", sql);
        Assert.DoesNotContain("GRANT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiKey", sql, StringComparison.OrdinalIgnoreCase);
    }
}
