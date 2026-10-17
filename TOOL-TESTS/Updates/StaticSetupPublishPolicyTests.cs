using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TOOL_TESTS.Updates;

public sealed class StaticSetupPublishPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-static-publish-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Static_release_accepts_required_local_components_without_catalog_keys_or_bundled_models()
    {
        var result = await ValidateAsync(WriteSettings());
        Assert.Equal(0, result.ExitCode);
        using var report = JsonDocument.Parse(result.Output.Trim());
        Assert.Equal("StaticArchive", report.RootElement.GetProperty("ModelDeliveryMode").GetString());
        Assert.False(report.RootElement.GetProperty("PayloadsBundled").GetBoolean());
        Assert.False(report.RootElement.GetProperty("DistributionEnabled").GetBoolean());
    }

    [Theory]
    [InlineData("VietsubEnabled")]
    [InlineData("VietsubOcrEnabled")]
    [InlineData("VietsubLocalTranslationEnabled")]
    [InlineData("VietsubLocalVoiceEnabled")]
    public async Task Required_component_cannot_be_disabled_to_make_full_setup_appear_ready(string feature)
    {
        var result = await ValidateAsync(WriteSettings(disabledFeature: feature));
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("all four required local components", result.Error);
    }

    [Theory]
    [InlineData("signed-catalog")]
    [InlineData("source-override")]
    [InlineData("private-cache")]
    [InlineData("wrong-mode")]
    [InlineData("include-qwen")]
    [InlineData("piper-path")]
    [InlineData("trust-root")]
    public async Task Static_profile_rejects_ambiguous_sources_and_payloads(string mutation)
    {
        var result = await ValidateAsync(WriteSettings(mutation: mutation), mutation: mutation);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Theory]
    [InlineData("Bundled")]
    [InlineData("Server")]
    public async Task Static_profile_cannot_be_published_with_a_legacy_setup_mode(string deliveryMode)
    {
        var result = await ValidateAsync(WriteSettings(), deliveryMode);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("StaticArchive release", result.Error);
    }

    private string WriteSettings(string? disabledFeature = null, string? mutation = null)
    {
        Directory.CreateDirectory(_root);
        var features = new Dictionary<string, bool>
        {
            ["VietsubEnabled"] = true, ["VietsubOcrEnabled"] = true,
            ["VietsubLocalTranslationEnabled"] = true, ["VietsubLocalVoiceEnabled"] = true
        };
        if (disabledFeature is not null) features[disabledFeature] = false;
        var settings = new
        {
            Features = features,
            ModelDistribution = new
            {
                Mode = mutation == "wrong-mode" ? "Server" : "StaticArchive",
                Enabled = mutation == "signed-catalog",
                BaseUrl = mutation == "source-override" ? "https://unexpected.invalid:8443/" : "",
                CacheRoot = mutation == "private-cache" ? @"D:\DeveloperCache" : ""
            }
        };
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(settings));
        return path;
    }

    private async Task<(int ExitCode, string Output, string Error)> ValidateAsync(string settings,
        string mode = "StaticArchive", string? mutation = null)
    {
        var runner = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(runner, """
            param($Validator,$Settings,$Mode,$Mutation)
            $ErrorActionPreference = 'Stop'
            [Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
            $arguments = @{ SettingsPath=$Settings; ModelDeliveryMode=$Mode }
            if ($Mutation -eq 'include-qwen') { $arguments.IncludeQwenModel=$true }
            if ($Mutation -eq 'piper-path') { $arguments.PiperBundlePath='fixture-unexpected' }
            if ($Mutation -eq 'trust-root') { $arguments.TrustRootPath='fixture-unexpected' }
            & $Validator @arguments | ConvertTo-Json -Compress
            """, new UTF8Encoding(true));
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[]
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", runner,
            "-Validator", Path.Combine(FindRepositoryRoot(), "scripts", "Test-ModelDistributionPublishSettings.ps1"),
            "-Settings", settings, "-Mode", mode, "-Mutation", mutation ?? "none"
        }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await output, await error);
    }

    private static string FindRepositoryRoot()
    {
        for (var item = new DirectoryInfo(AppContext.BaseDirectory); item is not null; item = item.Parent)
            if (File.Exists(Path.Combine(item.FullName, "TOOL_GEN_POST_VIDEO.slnx"))) return item.FullName;
        throw new InvalidOperationException("This test requires the source checkout.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
