namespace TOOL_TESTS.Database;

public sealed class SceneNativeAudioStatusMigrationTests
{
    private const string MigrationFile = "VideoFactory.4.0.6.NativeAudioWorkflowStatuses.sql";

    [Fact]
    public void Migration406_IsTransactionalIdempotentAndVerifiesBothConstraints()
    {
        var sql = ReadRepositoryFile("database", MigrationFile);

        Assert.Contains("SET XACT_ABORT ON", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BEGIN TRANSACTION", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("@SceneStatusDefinition", sql, StringComparison.Ordinal);
        Assert.Contains("@VideoGenerationStatusDefinition", sql, StringComparison.Ordinal);
        Assert.Contains("OBJECT_ID(N'[vf].[Scenes]'", sql, StringComparison.Ordinal);
        Assert.Contains("OBJECT_ID(N'[vf].[VideoGenerations]'", sql, StringComparison.Ordinal);
        Assert.Contains("'4.0.6-native-audio-workflow-statuses'", sql, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(sql, "WITH CHECK CHECK CONSTRAINT"));
        Assert.Equal(2, CountOccurrences(sql, "[is_disabled] = 0"));
        Assert.Equal(2, CountOccurrences(sql, "[is_not_trusted] = 0"));
    }

    [Fact]
    public void Migration406_AllowsEveryStatusOnTheTableThatPersistsIt()
    {
        var sql = ReadRepositoryFile("database", MigrationFile);
        var sceneConstraint = ExtractConstraintCreation(
            sql,
            "[vf].[Scenes]",
            "CK_Scenes_Status");
        var generationConstraint = ExtractConstraintCreation(
            sql,
            "[vf].[VideoGenerations]",
            "CK_VideoGenerations_Status");

        foreach (var status in new[] { "PromptInvalid", "AudioReviewRequired", "NativeAudioInvalid" })
        {
            Assert.Contains($"'{status}'", sceneConstraint, StringComparison.Ordinal);
        }

        foreach (var status in new[] { "AudioReviewRequired", "NativeAudioInvalid" })
        {
            Assert.Contains($"'{status}'", generationConstraint, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("'PromptInvalid'", generationConstraint, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeAudioWorkflow_StatusWritesAreCoveredByMigration406()
    {
        var sql = ReadRepositoryFile("database", MigrationFile);
        var generationSource = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ProjectGenerationService.cs");
        var sceneConstraint = ExtractConstraintCreation(
            sql,
            "[vf].[Scenes]",
            "CK_Scenes_Status");
        var generationConstraint = ExtractConstraintCreation(
            sql,
            "[vf].[VideoGenerations]",
            "CK_VideoGenerations_Status");

        Assert.DoesNotContain("scene.Status = \"PromptInvalid\"", generationSource, StringComparison.Ordinal);
        Assert.Contains("generationToApprove.Status = outputAudioEnabled", generationSource, StringComparison.Ordinal);
        Assert.Contains("sceneToApprove.Status = outputAudioEnabled", generationSource, StringComparison.Ordinal);

        Assert.Contains("'PromptInvalid'", sceneConstraint, StringComparison.Ordinal);
        Assert.Contains("'AudioReviewRequired'", sceneConstraint, StringComparison.Ordinal);
        Assert.Contains("'NativeAudioInvalid'", sceneConstraint, StringComparison.Ordinal);
        Assert.Contains("'AudioReviewRequired'", generationConstraint, StringComparison.Ordinal);
        Assert.Contains("'NativeAudioInvalid'", generationConstraint, StringComparison.Ordinal);
    }

    private static string ExtractConstraintCreation(string sql, string tableName, string constraintName)
    {
        var marker = $"ALTER TABLE {tableName} WITH CHECK ADD CONSTRAINT [{constraintName}]";
        var start = sql.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Cannot find creation statement for {constraintName}.");
        var end = sql.IndexOf("));", start, StringComparison.Ordinal);
        Assert.True(end > start, $"Cannot find end of creation statement for {constraintName}.");
        return sql[start..(end + 3)];
    }

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
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
