using System.Text.Json;
using TOOL_LOCAL.LocalVoice;

namespace TOOL_TESTS.LocalVoice;

public sealed class LocalVoiceRuntimeTests
{
    [Fact]
    public void DisabledAndNotInstalled_AreDistinctAndStatusCheckDoesNotWrite()
    {
        var root = Path.Combine(Path.GetTempPath(), "vm-voice-status-" + Guid.NewGuid().ToString("N"));
        Assert.Equal("DISABLED", new LocalVoiceRuntime(root, false).GetStatus().Status);
        Assert.Equal("NOT_INSTALLED", new LocalVoiceRuntime(root, true).GetStatus().Status);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task MissingRequiredComponent_IsRejectedBeforeStartingPython()
    {
        using var f = new LocalVoiceMediaTests.Fixture();
        await File.WriteAllTextAsync(Path.Combine(f.Root, "manifest.json"), JsonSerializer.Serialize(new { files = Array.Empty<object>() }));
        var runtime = new LocalVoiceRuntime(f.Root, true, f.Root);
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.ProbeInstalledAsync(default));
        Assert.False(File.Exists(Path.Combine(f.Root, "ready.json")));
    }

    [Fact]
    public async Task ManifestTraversal_IsRejectedBeforeStartingPython()
    {
        using var f = new LocalVoiceMediaTests.Fixture();
        await File.WriteAllTextAsync(Path.Combine(f.Root, "manifest.json"), JsonSerializer.Serialize(new {
            files = new[] { new { path = "../outside.exe", size = 0, sha256 = "fake" } }
        }));
        var runtime = new LocalVoiceRuntime(f.Root, true, f.Root);
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.ProbeInstalledAsync(default));
        Assert.False(File.Exists(Path.Combine(f.Root, "ready.json")));
    }
}
