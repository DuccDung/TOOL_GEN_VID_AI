using TOOL_LOCAL.Media;
using TOOL_LOCAL.Configuration;
using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;

namespace TOOL_TESTS.SystemSetup;

public sealed class SystemSetupAdapterTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DesktopComposition_OnlyCreatesAndRequiresQwenWhenTranslationIsEnabled(bool vietsub, bool translation)
    {
        var root = Path.Combine(Path.GetTempPath(), "setup-flags-" + Guid.NewGuid().ToString("N"));
        using var handler = new RejectHttp();
        using var http = new HttpClient(handler);
        var features = new DesktopFeatureOptions { VietsubEnabled = vietsub, VietsubLocalTranslationEnabled = translation };
        var created = 0;
        await using var provider = DesktopComponentComposition.CreateTranslationProvider(features, () =>
        {
            created++;
            return new QwenGgufVietsubTranslationProvider(new VietsubTranslationComponentStore(
                VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km, root, http, approvedLocalModelCandidates: []));
        });
        var enabled = vietsub && translation;
        Assert.Equal(enabled ? 1 : 0, created);
        Assert.Equal(enabled, DesktopComponentComposition.IsLocalTranslationEnabled(features));
        var adapter = new QwenSetupAdapter(provider);
        var inspected = adapter.Inspect();
        Assert.Equal(enabled ? "NOT_INSTALLED" : "DISABLED", inspected.State);
        var checkedComponent = await adapter.RunAsync(false, false, (_, _, _, _) => { }, default);
        Assert.Equal(inspected.State, checkedComponent.State);
        if (!enabled)
        {
            var installed = await adapter.RunAsync(true, false, (_, _, _, _) => { }, default);
            Assert.Equal("DISABLED", installed.State);
            Assert.False(installed.CanInstall);
        }
        Assert.Equal(0, handler.Calls);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Qwen_CheckMissingModel_DoesNotSendHttpOrCreateFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "setup-check-" + Guid.NewGuid().ToString("N"));
        using var handler = new RejectHttp();
        using var http = new HttpClient(handler);
        using var store = new VietsubTranslationComponentStore(VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km, root, http,
            approvedLocalModelCandidates: []);
        await using var provider = new QwenGgufVietsubTranslationProvider(store);
        var result = await new QwenSetupAdapter(provider).RunAsync(false, false, (_, _, _, _) => { }, default);
        Assert.Equal("NOT_INSTALLED", result.State);
        Assert.Equal(0, handler.Calls);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Piper_CheckMissingModel_DoesNotDownloadOrCreatePython()
    {
        var root = Path.Combine(Path.GetTempPath(), "setup-check-" + Guid.NewGuid().ToString("N"));
        using var handler = new RejectHttp();
        using var store = new VietsubVoiceComponentStore(new(root), true, handler);
        var result = await new PiperSetupAdapter(store, true).RunAsync(false, false, (_, _, _, _) => { }, default);
        Assert.Equal("NOT_INSTALLED", result.State);
        Assert.Equal(0, handler.Calls);
        Assert.Empty(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Path.Combine(root, "vietsub", "components")));
        Directory.Delete(root, true);
    }

    [Fact]
    [Trait("Category", "OcrIntegration")]
    public async Task OcrAdapter_RechecksAndRecognizesBundledEnglishChineseFixtures()
    {
        await using var recognizer = new PaddleVietsubOcrRecognizer();
        var adapter = new OcrSetupAdapter(recognizer, true);
        var first = await adapter.RunAsync(false, false, (_, _, _, _) => { }, default);
        Assert.Equal("READY", first.State);
        var second = await adapter.RunAsync(false, false, (_, _, _, _) => { }, default);
        Assert.Equal("READY", second.State);
        Assert.True(second.CheckedAtUtc >= first.CheckedAtUtc);
    }

    [Fact]
    public async Task MediaAdapter_RejectsPathFallbackEvenWhenExecutableExists()
    {
        var paths = new MediaToolPaths("ffmpeg", "ffprobe");
        var result = await new MediaSetupAdapter(new MediaToolPreflightService(paths, new ExternalProcessRunner(), TimeProvider.System), paths)
            .RunAsync(false, false, (_, _, _, _) => { }, default);
        Assert.Equal("REPAIR_REQUIRED", result.State);
        Assert.Equal("media_tool_bundle_missing", result.ErrorCode);
    }

    [Fact]
    public async Task MediaAdapter_ValidatesBundleAndRealAudioFixture()
    {
        var paths = new MediaToolPaths(Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe"),
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffprobe.exe"),
            Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg"));
        var result = await new MediaSetupAdapter(new MediaToolPreflightService(paths, new ExternalProcessRunner(), TimeProvider.System), paths)
            .RunAsync(false, false, (_, _, _, _) => { }, default);
        Assert.Equal("READY", result.State);
    }

    [SetupPiperFact]
    [Trait("Category", "SetupPiperIntegration")]
    public async Task Piper_CleanPrivateRuntime_InstallsPinnedDependenciesAndProbesVietnamese()
    {
        var workspace = Environment.GetEnvironmentVariable("VIDEOMAKER_SETUP_PIPER_WORKSPACE")
            ?? throw new InvalidOperationException("Explicit isolated workspace is required.");
        using var store = new VietsubVoiceComponentStore(new VietsubAppPaths(workspace), true);
        var adapter = new PiperSetupAdapter(store, true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(25));
        var result = await adapter.RunAsync(true, false, (_, _, _, _) => { }, timeout.Token);
        Assert.Equal("READY", result.State);
        var check = await adapter.RunAsync(false, false, (_, _, _, _) => { }, timeout.Token);
        Assert.Equal("READY", check.State);
        Assert.True(store.GetStatus().Ready);
    }

    private sealed class SetupPiperFactAttribute : FactAttribute
    {
        public SetupPiperFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("VIDEOMAKER_RUN_SETUP_PIPER") != "1")
                Skip = "Opt-in: downloads a private Python/Piper runtime and voice model into the explicitly selected test workspace.";
        }
    }
    private sealed class RejectHttp : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; throw new InvalidOperationException("GET/check must not download.");
        }
    }
}
