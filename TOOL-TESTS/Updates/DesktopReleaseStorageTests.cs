using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Domain.Updates;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_TESTS.Updates;

public sealed class DesktopReleaseStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VideoMakerStorageTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAsync_StoresValidPackageAndComputesSha256()
    {
        await using var package = CreatePackage();
        var expectedHash = Convert.ToHexString(SHA256.HashData(package.ToArray())).ToLowerInvariant();
        package.Position = 0;
        var storage = CreateStorage();

        var stored = await storage.SaveAsync(
            Guid.NewGuid(),
            DesktopArtifactKinds.DesktopPackage,
            "VideoMaker.zip",
            package,
            package.Length,
            CancellationToken.None);

        Assert.Equal(expectedHash, stored.Sha256);
        Assert.True(File.Exists(storage.ResolvePath(stored.RelativePath)));
        Assert.Equal(package.Length, stored.SizeBytes);
    }

    [Fact]
    public async Task SaveAsync_RejectsPackageWithoutManifest()
    {
        await using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("TOOL-LOCAL.exe");
        }
        package.Position = 0;

        await Assert.ThrowsAsync<ArgumentException>(() => CreateStorage().SaveAsync(
            Guid.NewGuid(),
            DesktopArtifactKinds.DesktopPackage,
            "VideoMaker.zip",
            package,
            package.Length,
            CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_RejectsSetupWithoutPeHeader()
    {
        await using var stream = new MemoryStream([1, 2, 3, 4]);
        await Assert.ThrowsAsync<ArgumentException>(() => CreateStorage().SaveAsync(
            Guid.NewGuid(),
            DesktopArtifactKinds.Setup,
            "VideoMaker Setup.exe",
            stream,
            stream.Length,
            CancellationToken.None));
    }

    private DesktopReleaseStorage CreateStorage() => new(
        Options.Create(new DesktopReleaseOptions { StorageRoot = _root, MaximumArtifactBytes = 10 * 1024 * 1024 }),
        new TestWebHostEnvironment(_root));

    private static MemoryStream CreatePackage()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var executable = archive.CreateEntry("TOOL-LOCAL.exe").Open()) executable.Write([0x4d, 0x5a]);
            using var manifestStream = archive.CreateEntry("update-manifest.json").Open();
            JsonSerializer.Serialize(manifestStream, new DesktopUpdateManifest("VideoMaker", "1.0.0", 1, "win-x64", ["TOOL-LOCAL.exe", "update-manifest.json"]), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        stream.Position = 0;
        return stream;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class TestWebHostEnvironment(string contentRoot) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "TOOL-TESTS";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = contentRoot;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
