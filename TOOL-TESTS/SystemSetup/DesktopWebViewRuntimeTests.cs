using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using TOOL_LOCAL.SystemSetup;

namespace TOOL_TESTS.SystemSetup;

public sealed class DesktopWebViewRuntimeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "webview-loader-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void MissingBundledLoaderDoesNotAttemptToUseAnInstalledRuntime()
    {
        var nativeCalls = 0;
        var status = DesktopWebViewRuntime.Inspect(_root, _ => nativeCalls++, () => { nativeCalls++; return "1.0"; });

        Assert.Equal("webview2_loader_missing", status.ErrorCode);
        Assert.False(status.IsReady);
        Assert.Equal(0, nativeCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DamagedOrWrongArchitectureLoaderIsRejectedBeforeNativeCalls(bool wrongArchitecture)
    {
        var loader = CopyLoader();
        if (wrongArchitecture)
        {
            var bytes = File.ReadAllBytes(loader);
            var peOffset = BitConverter.ToInt32(bytes, 0x3c);
            bytes[peOffset + 4] = 0x4c; // IMAGE_FILE_MACHINE_I386, not AMD64.
            bytes[peOffset + 5] = 0x01;
            File.WriteAllBytes(loader, bytes);
        }
        else File.WriteAllText(loader, "damaged-loader");

        var nativeCalls = 0;
        var status = DesktopWebViewRuntime.Inspect(_root, _ => nativeCalls++, () => { nativeCalls++; return "1.0"; });

        Assert.Equal("webview2_loader_invalid", status.ErrorCode);
        Assert.Equal(0, nativeCalls);
        Assert.False(status.IsReady);
    }

    [Fact]
    public void UsesAbsoluteBundledDirectoryBeforeProbingRuntime()
    {
        var loader = CopyLoader();
        string? configuredDirectory = null;
        var status = DesktopWebViewRuntime.Inspect(_root,
            directory => configuredDirectory = directory,
            () =>
            {
                Assert.Equal(Path.GetDirectoryName(loader), configuredDirectory);
                Assert.NotNull(configuredDirectory);
                Assert.True(Path.IsPathFullyQualified(configuredDirectory));
                return "1.0";
            });

        Assert.True(status.IsReady);
        Assert.Null(status.ErrorCode);
    }

    [Theory]
    [InlineData("missing-runtime", "webview2_runtime_missing")]
    [InlineData("empty-version", "webview2_runtime_missing")]
    [InlineData("load-failed", "webview2_loader_load_failed")]
    [InlineData("native-image", "webview2_loader_invalid")]
    [InlineData("access", "webview2_initialization_failed")]
    public void ReportsActionableErrorsWithoutPrivateExceptionText(string failure, string code)
    {
        CopyLoader();
        var status = DesktopWebViewRuntime.Inspect(_root, _ => { }, () => failure switch
        {
            "missing-runtime" => throw new WebView2RuntimeNotFoundException(),
            "empty-version" => "",
            "load-failed" => throw new DllNotFoundException("private-runtime-value"),
            "native-image" => throw new BadImageFormatException("private-runtime-value"),
            _ => throw new UnauthorizedAccessException("private-runtime-value")
        });

        Assert.Equal(code, status.ErrorCode);
        Assert.False(status.IsReady);
        Assert.DoesNotContain("private-runtime-value", JsonSerializer.Serialize(status));
        Assert.DoesNotContain(_root, JsonSerializer.Serialize(status));
    }

    [Fact]
    public void CanRetryAfterRestoringMissingLoader()
    {
        var missing = DesktopWebViewRuntime.Inspect(_root, _ => { }, () => "1.0");
        Assert.Equal("webview2_loader_missing", missing.ErrorCode);
        CopyLoader();
        Assert.True(DesktopWebViewRuntime.Inspect(_root, _ => { }, () => "1.0").IsReady);
    }

    [Fact]
    public async Task DesktopProbeLoadsItsOwnWebViewWithWindowsOnlyPathAndUnrelatedWorkingDirectory()
    {
        Directory.CreateDirectory(_root);
        var configuration = typeof(DesktopWebViewRuntimeTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var executable = Path.Combine(FindRepositoryRoot(), "TOOL-LOCAL", "bin", configuration,
            "net10.0-windows", "win-x64", "TOOL-LOCAL.exe");
        Assert.True(File.Exists(executable), "Build the desktop before running the startup regression test.");
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("--check-webview2");
        start.Environment["PATH"] = Environment.SystemDirectory + ";" + Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        var output = await stdout;
        Assert.True(process.ExitCode is 0 or 2, await stderr);
        using var report = JsonDocument.Parse(output);
        var webView = report.RootElement.GetProperty("WebView2");
        if (process.ExitCode == 0)
        {
            Assert.Equal("READY", webView.GetProperty("State").GetString());
            Assert.Equal(JsonValueKind.Null, webView.GetProperty("ErrorCode").ValueKind);
        }
        else
        {
            // A machine without Evergreen may legitimately lack the browser; it must
            // still load the bundled DLL and distinguish that from a broken package.
            Assert.Equal("webview2_runtime_missing", webView.GetProperty("ErrorCode").GetString());
        }
        Assert.False(report.RootElement.GetProperty("ServerAccessChecked").GetBoolean());
    }

    private string CopyLoader()
    {
        var source = Path.Combine(AppContext.BaseDirectory, DesktopWebViewRuntime.LoaderRelativePath);
        var destination = Path.Combine(_root, DesktopWebViewRuntime.LoaderRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
        return destination;
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "TOOL_GEN_POST_VIDEO.slnx"))) return directory.FullName;
        throw new InvalidOperationException("Startup regression requires the source checkout.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
