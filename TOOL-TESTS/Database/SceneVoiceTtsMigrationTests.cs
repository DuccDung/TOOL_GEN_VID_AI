namespace TOOL_TESTS.Database;

public sealed class SceneVoiceTtsMigrationTests
{
    private const string MigrationFileName = "VideoFactory.4.0.3.SceneVoiceTts.sql";

    [Fact]
    public void Migration_IsIdempotentAndRecordsSchemaVersion()
    {
        var script = ReadMigration();

        Assert.Contains("COL_LENGTH(N'vf.Projects', N'VoiceCode') IS NULL", script);
        Assert.Contains("COL_LENGTH(N'vf.VoiceGenerations', N'SceneId') IS NULL", script);
        Assert.Contains("OBJECT_ID(N'[vf].[GeneratedVoiceOutputs]', N'U') IS NULL", script);
        Assert.Contains("4.0.3-scene-voice-tts", script);
        Assert.Contains("BEGIN TRANSACTION", script);
        Assert.Contains("IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION", script);
    }

    [Fact]
    public void Migration_VersionsVoiceBySceneAndPreservesLegacyRows()
    {
        var script = ReadMigration();

        Assert.Contains("DROP CONSTRAINT [UQ_VoiceGenerations_Project_Version]", script);
        Assert.Contains("CREATE UNIQUE INDEX [UX_VoiceGenerations_Scene_Version]", script);
        Assert.Contains("WHERE [SceneId] IS NOT NULL", script);
        Assert.Contains("[NarrationHash] char(64) NULL", script);
        Assert.Contains("[VoiceSnapshotJson] nvarchar(max) NULL", script);
        Assert.Contains("FK_VoiceGenerations_Scenes", script);
    }

    [Fact]
    public void Migration_ProtectsAndValidatesTemporaryWavPayload()
    {
        var script = ReadMigration();

        Assert.Contains("[Payload] varbinary(max) NOT NULL", script);
        Assert.Contains("[RowVersion] rowversion NOT NULL", script);
        Assert.Contains("[SizeBytes] <= 52428800", script);
        Assert.Contains("DATALENGTH([Payload]) = [SizeBytes]", script);
        Assert.Contains("[MimeType] IN ('audio/wav','audio/x-wav')", script);
        Assert.Contains("[SampleRate] BETWEEN 8000 AND 192000", script);
        Assert.Contains("DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::[vf].[GeneratedVoiceOutputs]", script);
    }

    [Fact]
    public void Migration_VerifiesBeforePrintingReady()
    {
        var script = ReadMigration();
        var verificationIndex = script.IndexOf("THROW 51034", StringComparison.Ordinal);
        var readyIndex = script.IndexOf(
            "PRINT N'VideoFactory scene voice/TTS schema 4.0.3 is ready.'",
            StringComparison.Ordinal);

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
