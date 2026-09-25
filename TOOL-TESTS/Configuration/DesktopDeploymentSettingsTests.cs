using System.Diagnostics;
using System.Text.Json.Nodes;

namespace TOOL_TESTS.Configuration;

public sealed class DesktopDeploymentSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "deployment-settings-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReleaseProfileAcceptsApiOnlySettingsWithoutPrivatePaths()
    {
        var result = await ValidateAsync(Settings());
        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Theory]
    [InlineData("endpoint")]
    [InlineData("loopback")]
    [InlineData("workspace")]
    [InlineData("workspace-traversal")]
    [InlineData("ffmpeg")]
    [InlineData("voice-root")]
    [InlineData("direct-sql")]
    [InlineData("secret")]
    public async Task ReleaseProfileRejectsMachineDependentOrUnsafeSettings(string defect)
    {
        var settings = Settings();
        switch (defect)
        {
            case "endpoint": settings["Server"]!["BaseUrl"] = "https://another.invalid/"; break;
            case "loopback": settings["Server"]!["BaseUrl"] = "https://localhost:7202/"; break;
            case "workspace": settings["Storage"]!["WorkspaceRoot"] = "D:\\developer\\workspace"; break;
            case "workspace-traversal": settings["Storage"]!["WorkspaceRoot"] = "%LOCALAPPDATA%/../outside"; break;
            case "ffmpeg": settings["MediaTools"]!["FfmpegPath"] = "C:\\private\\ffmpeg.exe"; break;
            case "voice-root": settings["LocalVoice"] = new JsonObject { ["ComponentRoot"] = "D:\\private" }; break;
            case "direct-sql": settings["Database"] = new JsonObject { ["ConnectionString"] = "Server=dev;Integrated Security=true" }; break;
            case "secret": settings["Provider"] = new JsonObject { ["ApiKey"] = "never-print-this-fixture-value" }; break;
        }
        var result = await ValidateAsync(settings, expectedEndpoint: defect == "loopback" ? "https://localhost:7202/" : null);
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("never-print-this-fixture-value", result.Output);
    }

    [Theory]
    [InlineData("Server=DUNGDEV;Integrated Security=true")]
    [InlineData("Server=deployment;User ID=limited;Password=never-print-this-fixture-value")]
    public async Task TransitionalProfileStillRejectsDeveloperHostAndEmbeddedPassword(string connection)
    {
        var settings = Settings();
        settings["Database"] = new JsonObject { ["ConnectionString"] = connection };
        var result = await ValidateAsync(settings, transitional: true);
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("never-print-this-fixture-value", result.Output);
    }

    [Fact]
    public async Task TransitionalProfileRequiresExplicitSelection()
    {
        var settings = Settings();
        settings["Database"] = new JsonObject { ["ConnectionString"] = "Server=deployment.example;Integrated Security=true" };
        Assert.NotEqual(0, (await ValidateAsync(settings)).ExitCode);
        var selected = await ValidateAsync(settings, transitional: true);
        Assert.True(selected.ExitCode == 0, selected.Output);
    }

    [Fact]
    public async Task CurrentSqlDependentBuildRejectsAnEmptyConnectionInsteadOfPretendingApiSupport()
    {
        var result = await ValidateAsync(Settings(), requireSql: true);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("still requires workflow SQL", result.Output);
    }

    [Fact]
    public async Task InvalidJsonDoesNotExposeItsContentsInTheValidationError()
    {
        var result = await ValidateAsync(Settings(), rawJson: "{\"ApiKey\":\"never-print-this-fixture-value\",");
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("never-print-this-fixture-value", result.Output);
    }

    private async Task<(int ExitCode, string Output)> ValidateAsync(JsonObject settings, bool transitional = false, string? expectedEndpoint = null,
        bool requireSql = false, string? rawJson = null)
    {
        Directory.CreateDirectory(_root);
        var settingsPath = Path.Combine(_root, "appsettings.json");
        await File.WriteAllTextAsync(settingsPath, rawJson ?? settings.ToJsonString());
        var script = Path.Combine(FindRepositoryRoot(), "scripts", "Test-DesktopDeploymentSettings.ps1");
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
            "-SettingsPath", settingsPath, "-ExpectedServerBaseUrl", expectedEndpoint ?? "https://deployment.invalid/" })
            start.ArgumentList.Add(argument);
        if (transitional) start.ArgumentList.Add("-AllowTransitionalSql");
        if (requireSql) start.ArgumentList.Add("-RequireTransitionalSql");
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        return (process.ExitCode, await stdout + await stderr);
    }

    private static JsonObject Settings() => JsonNode.Parse("""
        {"Server":{"BaseUrl":"https://deployment.invalid/"},
         "Storage":{"WorkspaceRoot":"%LOCALAPPDATA%/ToolGenPostVideo/workspace"},
         "MediaTools":{"FfmpegPath":"tools/ffmpeg/ffmpeg.exe","FfprobePath":"tools/ffmpeg/ffprobe.exe"},
         "Features":{"VietsubLocalTranslationEnabled":false}}
        """)!.AsObject();

    private static string FindRepositoryRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "TOOL_GEN_POST_VIDEO.slnx"))) return folder.FullName;
        throw new InvalidOperationException("The deployment script tests require the source checkout.");
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
