using System.Text.Json;
using TOOL_LOCAL.Configuration;
using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_TESTS.SystemSetup;

public sealed class DesktopReadinessCommandTests
{
    [Fact]
    public void ApplicationAndDiagnosticsUsePerUserVoiceComponentsWithLegacyCompatibility()
    {
        var root = Path.Combine(Path.GetTempPath(), "desktop-voice-composition-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new VietsubAppPaths(root);
            var features = new DesktopFeatureOptions { VietsubEnabled = true, VietsubLocalVoiceEnabled = true };
            using (var components = DesktopComponentComposition.CreateVoiceComponents(paths, features))
                Assert.Equal(Path.Combine(SystemSetupPaths.ComponentsRoot, "voice", "piper"), components.ComponentDirectory);

            var legacy = Path.Combine(paths.RootDirectory, "components", "voice", "piper");
            Directory.CreateDirectory(legacy);
            using var legacyComponents = DesktopComponentComposition.CreateVoiceComponents(paths, features);
            Assert.Equal(legacy, legacyComponents.ComponentDirectory);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CheckUsesInstalledEvidenceWithoutInvokingModelInstallationOrInference()
    {
        var inspector = new InspectOnly();
        var result = await DesktopReadinessCommand.InspectAsync([inspector], default);
        Assert.Equal("NOT_INSTALLED", Assert.Single(result).State);
        Assert.Equal(1, inspector.Inspections);
        Assert.Equal(0, inspector.Runs);
    }

    [Fact]
    public async Task CheckContinuesAfterProbeFailureWithoutIncludingPrivateErrorText()
    {
        var probe = new ProbeOnly();
        var result = await DesktopReadinessCommand.InspectAsync([probe, new InspectOnly()], default);
        Assert.Equal(2, result.Count);
        Assert.Equal("component_check_failed", result[0].ErrorCode);
        Assert.Equal("NOT_INSTALLED", result[1].State);
        Assert.False(probe.InstallRequested);
        Assert.DoesNotContain("private-value", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task CheckHonorsCancellationBeforeNativeProbe()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var inspector = new InspectOnly();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DesktopReadinessCommand.InspectAsync([inspector], cancel.Token));
        Assert.Equal(0, inspector.Inspections);
    }

    [Fact]
    public void MaintenanceCommandRequiresExactArguments()
    {
        Assert.True(DesktopReadinessCommand.Matches(["--check-desktop"]));
        Assert.True(DesktopReadinessCommand.Matches(["--check-webview2"]));
        Assert.False(DesktopReadinessCommand.Matches(["--check-webview2", "--install"]));
        Assert.False(DesktopReadinessCommand.Matches(["--check-desktop", "--install"]));
        Assert.False(DesktopReadinessCommand.Matches([]));
    }

    private sealed class InspectOnly : ISetupComponentAdapter, ISetupComponentStatusInspector
    {
        public int Inspections;
        public int Runs;
        public SetupComponent Component => new("model", "model", "1", "UNKNOWN", "", false, false, true, 0);
        public SetupComponent Inspect() { Inspections++; return Component with { State = "NOT_INSTALLED" }; }
        public Task<SetupComponent> RunAsync(bool install, bool accepted, Action<string, double?, long?, long?> progress, CancellationToken token)
        {
            Runs++;
            throw new InvalidOperationException("An installed model inspection must not execute the model.");
        }
    }

    private sealed class ProbeOnly : ISetupComponentAdapter
    {
        public bool InstallRequested;
        public SetupComponent Component => new("media", "media", "1", "UNKNOWN", "", false, true, true, 0);
        public Task<SetupComponent> RunAsync(bool install, bool accepted, Action<string, double?, long?, long?> progress, CancellationToken token)
        {
            InstallRequested = install;
            throw new IOException("private-value");
        }
    }
}
