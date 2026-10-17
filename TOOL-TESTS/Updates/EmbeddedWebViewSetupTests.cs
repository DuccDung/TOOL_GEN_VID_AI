extern alias SetupAssembly;
using System.Security.Cryptography;
using System.Text;
using SetupAssembly::TOOL_SETUP;

namespace TOOL_TESTS.Updates;

/// <summary>Only checks verified private extraction. No WebView installer or process is executed.</summary>
public sealed class EmbeddedWebViewSetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "setup-embedded-webview-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Installer = Encoding.UTF8.GetBytes("controlled-installer-resource");
    private static InstallerOptions Options(long? length = null, string? hash = null) => new()
    {
        SetupMode = "SharedBootstrapper", WebView2InstallerSizeBytes = length ?? Installer.LongLength,
        WebView2InstallerSha256 = hash ?? Convert.ToHexString(SHA256.HashData(Installer)).ToLowerInvariant(),
        WebView2PublisherThumbprint = new string('a', 40)
    };

    [Fact]
    public async Task EmbeddedResourceIsPromotedOnlyAfterItsPinnedSizeAndHashMatch()
    {
        using var resource = new MemoryStream(Installer, writable: false);
        var path = await ApprovedWebViewInstaller.PrepareEmbeddedInstallerAsync(resource, _root, Options(), CancellationToken.None);
        Assert.Equal(Path.Combine(_root, "webview2-runtime-x64.exe"), path);
        Assert.Equal(Installer, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("checksum")]
    public async Task InvalidEmbeddedResourceLeavesNoExecutableOrPartFile(string mutation)
    {
        var bytes = mutation == "short" ? Installer[..^1] : mutation == "long" ? Installer.Concat(new byte[] { 0 }).ToArray() : Installer;
        using var resource = new MemoryStream(bytes, writable: false);
        await Assert.ThrowsAsync<InvalidDataException>(() => ApprovedWebViewInstaller.PrepareEmbeddedInstallerAsync(resource, _root,
            Options(hash: mutation == "checksum" ? new string('b', 64) : null), CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task VerifiedPrivateCacheIsReusedWithoutReadingResourceAgain()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "webview2-runtime-x64.exe");
        await File.WriteAllBytesAsync(path, Installer);
        using var empty = new MemoryStream();
        Assert.Equal(path, await ApprovedWebViewInstaller.PrepareEmbeddedInstallerAsync(empty, _root, Options(), CancellationToken.None));
        Assert.Equal(Installer, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task CorruptExistingPrivateCacheIsNotExecutedOrSilentlyReplaced()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "webview2-runtime-x64.exe");
        await File.WriteAllTextAsync(path, "corrupt-cache-preserved");
        using var resource = new MemoryStream(Installer, writable: false);
        await Assert.ThrowsAsync<InvalidDataException>(() => ApprovedWebViewInstaller.PrepareEmbeddedInstallerAsync(resource,
            _root, Options(), CancellationToken.None));
        Assert.Equal("corrupt-cache-preserved", await File.ReadAllTextAsync(path));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task CancellationDoesNotPromoteAnUnverifiedResource()
    {
        using var resource = new MemoryStream(Installer, writable: false);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ApprovedWebViewInstaller.PrepareEmbeddedInstallerAsync(resource,
            _root, Options(), cancellation.Token));
        Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task MissingPinFailsBeforeCreatingAPrivateDirectory()
    {
        using var resource = new MemoryStream(Installer, writable: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ApprovedWebViewInstaller.PrepareEmbeddedInstallerAsync(resource,
            _root, new InstallerOptions(), CancellationToken.None));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task MissingStageAndMissingEmbeddedInstallerCannotReportSuccess()
    {
        // Ordinary test builds deliberately omit the 214 MB approved installer resource.
        var stage = Path.Combine(_root, "stage"); var prerequisite = Path.Combine(_root, "private-prerequisites");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ApprovedWebViewInstaller.InstallAsync(stage,
            Options(), CancellationToken.None, prerequisite));
        Assert.Contains("WebView2", error.Message);
        Assert.False(Directory.Exists(prerequisite));
    }

    [Fact]
    public async Task CorruptStageInstallerFailsBeforeAnyEmbeddedFallbackOrExecution()
    {
        var stage = Path.Combine(_root, "stage"); var prerequisite = Path.Combine(_root, "private-prerequisites");
        Directory.CreateDirectory(Path.Combine(stage, "prerequisites"));
        var path = Path.Combine(stage, "prerequisites", "webview2-runtime-x64.exe");
        await File.WriteAllTextAsync(path, "untrusted-package-installer");
        await Assert.ThrowsAsync<InvalidDataException>(() => ApprovedWebViewInstaller.InstallAsync(stage,
            Options(), CancellationToken.None, prerequisite));
        Assert.Equal("untrusted-package-installer", await File.ReadAllTextAsync(path));
        Assert.False(Directory.Exists(prerequisite));
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
