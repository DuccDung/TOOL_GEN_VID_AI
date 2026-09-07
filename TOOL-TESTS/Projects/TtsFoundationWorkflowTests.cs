using TOOL_LOCAL.Projects;
using TOOL_SERVER.Generation;
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
    public void CanonicalVoiceConfiguration_DoesNotRequireSpeechVerification()
    {
        var options = new SpeechSynchronizationOptions
        {
            CanonicalVoiceEnabled = true,
            SpeechVerificationEnabled = false
        };

        options.Validate();
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
        Assert.Contains("openAiVoiceReady", app);
        Assert.Contains("canonicalVoiceReady", app);
        Assert.Contains("speechProductionPolicy", app);
        Assert.Contains("voiceSpeakingRate", app);
        Assert.Contains("Canonical Voice", app);
        Assert.Contains("VoicePickerModal", app);
        Assert.Contains("voice-picker-preview", app);
        Assert.Contains("voice-catalog.preview.quote", app);
        Assert.Contains("voice-catalog.previewed", app);
        Assert.Contains("voice-catalog.preview.quote", bridge);
        Assert.Contains("voice-catalog.previewed", bridge);
        Assert.Contains("voicePreviewProjectName={project?.project.name}", app);
        Assert.Contains("contextProjectName: voicePreviewProjectName", app);
        Assert.Contains("contextProjectId: quote.contextProjectId", app);
        Assert.Contains("GetVoiceCatalogPreviewContextQuoteAsync", bridge);
        Assert.Contains("refreshDashboard: false", bridge);
        Assert.DoesNotContain("unavailableReason", app, StringComparison.Ordinal);
        Assert.Contains("Có thể nghe thử trước khi tạo dự án", app);
        Assert.Contains("getAvailableVoiceOptions(providerStatus.openAiVoiceOptions)", app);
        Assert.Contains("không phát sinh chi phí", app);
        Assert.Contains("OpenAiBuiltInVoiceCatalog.IsSupported", bridge);
        Assert.Contains("OpenAiBuiltInVoiceCatalog.IsSupported", service);
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
