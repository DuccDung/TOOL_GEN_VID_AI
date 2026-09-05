namespace TOOL_TESTS.Generation;

public sealed class SceneFirstFrameDesktopWorkflowTests
{
    [Fact]
    public void DesktopWorkflow_UsesPartValidationAtomicMoveAndServerMaterialization()
    {
        var workflow = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ProjectGenerationService.cs");
        var method = Slice(
            workflow,
            "public async Task<SceneFirstFrameSummary> GenerateSceneFirstFrameAsync(",
            "public Task<SceneFirstFrameSummary> ApproveSceneFirstFrameAsync(");

        Assert.Contains(".part", method, StringComparison.Ordinal);
        Assert.Contains("ValidateDownloadedSceneFirstFrameAsync(partialPath", method, StringComparison.Ordinal);
        Assert.Contains("File.Move(partialPath, finalPath, false)", method, StringComparison.Ordinal);
        Assert.Contains("finally", method, StringComparison.Ordinal);
        Assert.Contains("File.Delete(partialPath)", method, StringComparison.Ordinal);
        Assert.Contains("MaterializeSceneFirstFrameAsync", method, StringComparison.Ordinal);
        Assert.True(
            method.IndexOf("File.Move(partialPath, finalPath, false)", StringComparison.Ordinal) <
            method.IndexOf("MaterializeSceneFirstFrameAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void DownloadRetry_ReusesRetainedOutputWithoutGeneratingOrMaterializingAgain()
    {
        var workflow = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ProjectGenerationService.cs");
        var method = Slice(
            workflow,
            "public async Task<SceneFirstFrameSummary> RetrySceneFirstFrameDownloadAsync(",
            "private static void ValidateSceneFirstFrameMetadata(");

        Assert.Contains("DownloadSceneFirstFrameAsync", method, StringComparison.Ordinal);
        Assert.Contains("File.Move(partialPath, finalPath, false)", method, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateSceneFirstFrameAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("MaterializeSceneFirstFrameAsync", method, StringComparison.Ordinal);
        Assert.DoesNotContain("SubmitVideoAsync", method, StringComparison.Ordinal);
    }

    [Fact]
    public void FalDesktopSubmitRequiresApprovedCurrentLocalFrameWhileKlingKeepsLegacyReference()
    {
        var workflow = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ProjectGenerationService.cs");
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");

        Assert.Contains("x.Status == SceneFirstFrameStatuses.Approved && x.IsCurrent", workflow, StringComparison.Ordinal);
        Assert.Contains("firstFrame = await LoadSceneFirstFrameInputAsync", workflow, StringComparison.Ordinal);
        Assert.Contains("else if (scene.Character?.Reference is not null)", workflow, StringComparison.Ordinal);
        Assert.Contains("ReferenceImage: referenceImage", workflow, StringComparison.Ordinal);
        Assert.Contains("FirstFrame: firstFrame", workflow, StringComparison.Ordinal);
        Assert.Contains("frame.status === 'Approved' && frame.isCurrent", app, StringComparison.Ordinal);
        Assert.Contains("Boolean(approvedFirstFrame?.previewUrl)", app, StringComparison.Ordinal);
        Assert.Contains("'Đang tạo'", app, StringComparison.Ordinal);
        Assert.Contains("'Tạo thất bại'", app, StringComparison.Ordinal);
        Assert.Contains("'Chưa tải xong'", app, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopGatewayNeverContainsProviderCredentialsOrDirectProviderEndpoints()
    {
        var client = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ServerGenerationClient.cs");

        Assert.Contains("/api/generation/images/scene-first-frames/", client, StringComparison.Ordinal);
        Assert.DoesNotContain("api.openai.com", client, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("queue.fal.run", client, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiKey", client, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing start marker: {startMarker}");
        Assert.True(end > start, $"Missing end marker: {endMarker}");
        return source[start..end];
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
