namespace TOOL_TESTS.Generation;

public sealed class NativeAudioWorkflowTests
{
    [Fact]
    public void DesktopWorkflow_LeavesModelVariantToServerAndProbesDownloadedClip()
    {
        var source = ReadRepositoryFile("TOOL-LOCAL", "Generation", "ProjectGenerationService.cs");

        Assert.Contains("new SubmitVideoRequest(", source);
        Assert.DoesNotContain("new SubmitKlingVideoRequest(", source, StringComparison.Ordinal);
        Assert.Contains("mediaProbe.ProbeAsync(partialPath", source);
        Assert.Contains("nativeAudioExpected = true", source);
        Assert.Contains("audioQualityValidator.AnalyzeAsync(partialPath", source);
        Assert.Contains("nativeAudioQuality.FailureCode", source);
        Assert.Contains("AudioReviewRequired", source);
        Assert.Contains("NativeAudioInvalid", source);
        Assert.Contains("spokenTextHash", source);

        var workflowStart = source.IndexOf("public async Task<int> GenerateVideosAsync", StringComparison.Ordinal);
        var generateSceneStart = source.IndexOf("private async Task GenerateSceneAsync", StringComparison.Ordinal);
        Assert.True(workflowStart >= 0 && generateSceneStart > workflowStart);
        Assert.DoesNotContain("EnsureSceneNarrationAsync", source[workflowStart..generateSceneStart]);
    }

    [Fact]
    public void ServerWorkflow_ResolvesProjectPolicyBeforePricingAndOutbound()
    {
        var source = ReadRepositoryFile("TOOL-SERVER", "Generation", "GenerationService.cs");
        var validation = source.IndexOf("ValidateVideoRequest(request);", StringComparison.Ordinal);
        var policy = source.IndexOf("policyResolver.ResolveAsync", validation, StringComparison.Ordinal);
        var quote = source.IndexOf("costEstimator.QuoteVideoAsync", policy, StringComparison.Ordinal);
        var outbound = source.IndexOf("router.Resolve(provider.ProviderCode).SubmitAsync", quote, StringComparison.Ordinal);

        Assert.True(validation >= 0);
        Assert.True(policy > validation);
        Assert.True(quote > policy);
        Assert.True(outbound > quote);
        Assert.Contains("snapshot.Resolution", source);
        Assert.Contains("snapshot.NativeAudio", source);
        Assert.Contains("KlingNativeAudioPromptComposer.Compose", source);
        Assert.Contains("SeedanceNativeAudioPromptComposer", source);
    }

    [Fact]
    public void Readiness_QuotesTheSameNativeAudioVariantShownByTheDesktop()
    {
        var server = ReadRepositoryFile("TOOL-SERVER", "Generation", "GenerationService.cs");
        var contracts = ReadRepositoryFile("TOOL-SHARED.Contracts", "Generation", "GenerationContracts.cs");
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");

        Assert.Contains("snapshot.Resolution", server);
        Assert.Contains("snapshot.NativeAudio", server);
        Assert.Contains("EstimatedVideoCostPerSecond", contracts);
        Assert.Contains("estimatedVideoCostPerSecond", app);
        Assert.Contains("Provider Native Audio", app);
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
