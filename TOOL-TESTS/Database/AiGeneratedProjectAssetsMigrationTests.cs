namespace TOOL_TESTS.Database;

public sealed class AiGeneratedProjectAssetsMigrationTests
{
    [Fact]
    public void Migration_AddsStableAssetKeyAndAiProvenanceWithoutProviderSecrets()
    {
        var script = File.ReadAllText(FindRepositoryFile("database", "VideoFactory.4.0.8.AiGeneratedProjectAssets.sql"));

        Assert.Contains("[AssetKey]", script, StringComparison.Ordinal);
        Assert.Contains("[SourceKind]", script, StringComparison.Ordinal);
        Assert.Contains("[SourcePlanVersion]", script, StringComparison.Ordinal);
        Assert.Contains("[GeneratedByProviderRequestId]", script, StringComparison.Ordinal);
        Assert.Contains("UQ_ProjectAssets_Project_AssetKey", script, StringComparison.Ordinal);
        Assert.Contains("4.0.8-ai-generated-project-assets", script, StringComparison.Ordinal);
        Assert.Contains("EXEC sys.sp_executesql", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ApiKey", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Credential", script, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Không tìm thấy migration AI-generated project assets.");
    }
}
