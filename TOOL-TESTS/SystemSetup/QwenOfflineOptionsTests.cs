using TOOL_LOCAL.SystemSetup;

namespace TOOL_TESTS.SystemSetup;

public sealed class QwenOfflineOptionsTests
{
    [Fact]
    public void Probe_and_stability_options_preserve_explicit_resource_consent()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "vm-qwen-offline-" + Guid.NewGuid().ToString("N"));
        var baseline = QwenOfflineCommand.ParseOptions(["--check-qwen-offline", "--workspace", workspace]);
        Assert.False(baseline.AcceptResourceWarning);
        Assert.Equal(0, baseline.Iterations);
        var soak = QwenOfflineCommand.ParseOptions(["--check-qwen-offline", "--workspace", workspace,
            "--iterations", "30", "--accept-resource-warning"]);
        Assert.True(soak.AcceptResourceWarning);
        Assert.Equal(30, soak.Iterations);
        Assert.False(Directory.Exists(workspace));
    }

    [Theory]
    [InlineData("--iterations", "101")]
    [InlineData("--iterations", "-1")]
    [InlineData("--iterations", "30", "--iterations", "30")]
    [InlineData("--accept-resource-warning", "--accept-resource-warning")]
    [InlineData("--model-path", "untrusted.gguf")]
    [InlineData("--iterations")]
    public void Invalid_options_are_rejected_before_touching_a_workspace(params string[] extra)
    {
        var workspace = Path.Combine(Path.GetTempPath(), "vm-qwen-offline-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<ArgumentException>(() => QwenOfflineCommand.ParseOptions(
            ["--check-qwen-offline", "--workspace", workspace, .. extra]));
        Assert.False(Directory.Exists(workspace));
    }
}
