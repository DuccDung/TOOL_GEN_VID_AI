using TOOL_SHARED.Contracts.Generation;
using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class MultiProviderVideoContractTests
{
    [Fact]
    public void DesktopSubmitContract_DoesNotAllowProviderOrModelSelection()
    {
        var properties = typeof(SubmitVideoRequest)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("ProviderCode", properties);
        Assert.DoesNotContain("ModelCode", properties);
        Assert.DoesNotContain("ProviderModelId", properties);
        Assert.DoesNotContain("Resolution", properties);
        Assert.DoesNotContain("NativeAudio", properties);
        Assert.DoesNotContain("Prompt", properties);
        Assert.DoesNotContain("DurationSeconds", properties);
        Assert.Contains("ProjectId", properties);
        Assert.Contains("SceneId", properties);
        Assert.Contains("IdempotencyKey", properties);
    }

    [Fact]
    public void DesktopSource_UsesOnlyVideoMakerGatewayForVideoGeneration()
    {
        var client = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ServerGenerationClient.cs");
        var workflow = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ProjectGenerationService.cs");

        Assert.Contains("api/generation/videos", client);
        Assert.Contains("SubmitVideoAsync", workflow);
        Assert.DoesNotContain("bytepluses.com", client, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api-singapore.klingai.com", client, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SourceProviderCode = response.ProviderCode", workflow, StringComparison.Ordinal);
        Assert.Contains("localPrompt.ModelCode = task.ModelCode", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KlingAdapter_DoesNotPersistTheSignedOutputUrl()
    {
        const string signedUrl = "https://media.kwaicdn.com/video.mp4?signature=secret";
        var adapter = new KlingVideoProviderAdapter(new StubKlingClient(signedUrl));

        var result = await adapter.GetStatusAsync(
            new ProviderRuntimeConfiguration(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ProviderCodes.Kling,
                "kling-3.0",
                new Uri("https://api-singapore.klingai.com/"),
                "KlingJwt",
                null,
                "credential"),
            "task-1",
            CancellationToken.None);

        Assert.Equal(signedUrl, result.OutputUrl);
        Assert.DoesNotContain(signedUrl, result.ResponseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("signature", result.ResponseJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopWorkflow_TreatsExpiredProviderTasksAsTerminalFailures()
    {
        var workflow = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ProjectGenerationService.cs");
        var projects = ReadRepositoryFile("TOOL-LOCAL", "Projects", "ProjectService.cs");

        Assert.Contains("\"Completed\" or \"Failed\" or \"Cancelled\" or \"Expired\"", workflow);
        Assert.Contains("\"Expired\" => \"Failed\"", workflow);
        Assert.Contains("request.Status != \"Expired\"", projects);
    }

    private sealed class StubKlingClient(string outputUrl) : IKlingVideoClient
    {
        public Task<KlingTaskResult> SubmitAsync(
            ProviderRuntimeConfiguration provider,
            string prompt,
            string aspectRatio,
            int durationSeconds,
            string resolution,
            bool nativeAudio,
            string externalTaskId,
            KlingReferenceImageData? referenceImage,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result(externalTaskId));

        public Task<KlingTaskResult> GetStatusAsync(
            ProviderRuntimeConfiguration provider,
            string externalRequestId,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result(externalRequestId));

        private KlingTaskResult Result(string taskId) =>
            new(
                taskId,
                "Completed",
                100m,
                outputUrl,
                null,
                null,
                1m,
                $"{{\"taskId\":\"{taskId}\",\"outputUrl\":\"{outputUrl}\"}}");
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
