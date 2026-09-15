using System.Text.Json;
using TOOL_LOCAL.Configuration;
using TOOL_LOCAL.LocalVoice;

namespace TOOL_TESTS.LocalVoice;

public sealed class LocalVoiceDeploymentTests
{
    [Fact]
    public void UserRuntimeSettings_PreserveMediaWorkspace_AndDoNotInstallOnLoad()
    {
        using var fixture = new LocalVoiceMediaTests.Fixture();
        var workspace = Path.Combine(fixture.Root, "existing-media");
        var components = Path.Combine(fixture.Root, "models");
        var temporary = Path.Combine(fixture.Root, "temporary");
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.json"), JsonSerializer.Serialize(new {
            Server = new { BaseUrl = "https://localhost:7202/" },
            Database = new { ConnectionString = "Server=(local);Database=test;Integrated Security=true" },
            Storage = new { WorkspaceRoot = workspace },
            Features = new { VeoLocalVoiceConsistencyEnabled = false }
        }));
        File.WriteAllText(Path.Combine(fixture.Root, "appsettings.user.json"), JsonSerializer.Serialize(new {
            Features = new { VeoLocalVoiceConsistencyEnabled = true },
            LocalVoice = new { ComponentRoot = components, TemporaryRoot = temporary }
        }));
        var options = DesktopOptions.Load(fixture.Root);
        var runtime = new LocalVoiceRuntime(options.Storage.WorkspaceRoot, options.Features.VeoLocalVoiceConsistencyEnabled,
            componentRootOverride: options.LocalVoice.ComponentRoot, temporaryRootOverride: options.LocalVoice.TemporaryRoot);
        Assert.Equal(workspace, options.Storage.WorkspaceRoot);
        Assert.Equal("NOT_INSTALLED", runtime.GetStatus().Status);
        Assert.False(Directory.Exists(components));
        Assert.False(Directory.Exists(temporary));
        Assert.False(Directory.Exists(workspace));
    }

    [Theory]
    [InlineData("relative/models")]
    [InlineData(@"\\server\share\models")]
    [InlineData(@"C:\")]
    public void InvalidComponentLocations_AreRejected(string path)
    {
        Assert.Throws<ArgumentException>(() => new LocalVoiceRuntime(Path.GetTempPath(), true, path));
    }

    [Fact]
    public void TemporaryFiles_CannotEnterComponentManifest()
    {
        using var fixture = new LocalVoiceMediaTests.Fixture();
        Assert.Throws<ArgumentException>(() => new LocalVoiceRuntime(fixture.Root, true, fixture.Root,
            temporaryRootOverride: Path.Combine(fixture.Root, "temp")));
    }

    [Fact]
    public void ChildProcesses_UseDedicatedTempAndAnAllowlistedEnvironment()
    {
        using var fixture = new LocalVoiceMediaTests.Fixture();
        var temp = Path.Combine(fixture.Root, "temp");
        var runtime = new LocalVoiceRuntime(fixture.Root, true, Path.Combine(fixture.Root, "models"), temporaryRootOverride: temp);
        var info = runtime.CreateProcessStartInfo("python.exe");
        Assert.Equal(temp, info.Environment["TEMP"]);
        Assert.Equal(temp, info.Environment["TMP"]);
        Assert.True(info.CreateNoWindow);
        Assert.False(info.UseShellExecute);
        var allowed = new[] { "SystemRoot", "WINDIR", "PATH", "COMSPEC", "PATHEXT", "TEMP", "TMP", "PYTHONDONTWRITEBYTECODE" };
        Assert.All(info.Environment.Keys, key => Assert.Contains(key, allowed, StringComparer.OrdinalIgnoreCase));
        Assert.False(Directory.Exists(temp));
    }

    [Theory]
    [InlineData("--prepare-local-voice")]
    [InlineData("--verify-local-voice")]
    [InlineData("--check-local-voice")]
    public void Maintenance_OnlyAcceptsAnExactCommandWithoutProjectOrMediaArguments(string command)
    {
        Assert.True(LocalVoiceMaintenance.IsMaintenanceCommand([command]));
        Assert.False(LocalVoiceMaintenance.IsMaintenanceCommand([command, "untrusted-media.mp4"]));
        Assert.False(LocalVoiceMaintenance.IsMaintenanceCommand(["--convert-local-voice"]));
    }
}
