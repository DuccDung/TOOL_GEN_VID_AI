using TOOL_LOCAL.Projects;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Projects;

public sealed class TtsFoundationWorkflowTests
{
    [Fact]
    public void SharedVoiceRequest_UsesSceneIdentityAndHashInsteadOfClientNarration()
    {
        var request = new GenerateSceneVoiceRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            2,
            new string('a', 64),
            "voice-idempotency");

        Assert.Equal(2, request.ScenePlanVersion);
        Assert.Null(typeof(GenerateSceneVoiceRequest).GetProperty("Narration"));
        Assert.False(new GenerationProviderStatusResponse(false, null, false, null).OpenAiVoiceReady);
    }

    [Fact]
    public void ProjectCreationContract_DefaultsDeferredTtsSettingsToNull()
    {
        var command = new CreateProjectCommand(
            "Project",
            "Topic",
            "YouTube",
            "16:9",
            75,
            null,
            "vi-VN",
            Guid.NewGuid());

        Assert.Null(command.VoiceCode);
        Assert.Null(command.VoiceSpeakingRate);
    }

    [Fact]
    public void DefaultWebWorkflow_UsesProviderNativeAudioWithoutRemovingLegacyTtsStorage()
    {
        var bridge = ReadRepositoryFile("TOOL-LOCAL", "WebView", "DashboardBridge.cs");
        var service = ReadRepositoryFile("TOOL-LOCAL", "Projects", "ProjectService.cs");
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var leastPrivilege = ReadRepositoryFile("database", "VideoFactory.DesktopLeastPrivilege.sql");

        Assert.Contains("scene.native-audio.approve", bridge);
        Assert.Contains("\"ProviderNative\"", service);
        Assert.Contains("Provider Native Audio", app);
        Assert.DoesNotContain("openAiVoiceReady", app);
        Assert.DoesNotContain("voiceSpeakingRate", app);
        Assert.Contains("GeneratedVoiceOutputs", leastPrivilege);
        Assert.DoesNotContain("api.openai.com", app, StringComparison.OrdinalIgnoreCase);
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
