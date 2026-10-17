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
    public void Construction_and_path_resolution_do_not_create_the_release_store()
    {
        Assert.False(Directory.Exists(_root));
        var storage = CreateStorage();
        Assert.Equal(Path.Combine(_root, "release", "Setup.exe"), storage.ResolvePath(Path.Combine("release", "Setup.exe")));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void Metadata_status_and_path_resolution_work_even_when_a_sentinel_file_prevents_creating_the_store()
    {
        Directory.CreateDirectory(_root);
        var sentinel = Path.Combine(_root, "blocked-parent");
        File.WriteAllText(sentinel, "A file deliberately prevents creating storage below this path.");
        var blockedRoot = Path.Combine(sentinel, "App_Releases");
        var options = Options.Create(new DesktopReleaseOptions
        {
            StorageRoot = blockedRoot, PublicationMode = "StandaloneCatalog", SharedBootstrapperEnabled = false,
            Profile = "Production", ApiBaseUrl = "https://metadata.invalid/",
            AllowedExternalPrefixes = ["https://103.179.190.182:8443/models/"]
        });
        var storage = new DesktopReleaseStorage(options, new TestWebHostEnvironment(_root));
        Assert.Equal(Path.Combine(blockedRoot, "release", "Setup.exe"), storage.ResolvePath(Path.Combine("release", "Setup.exe")));
        var service = new DesktopBootstrapperService(null!, storage, null!, options, TimeProvider.System);
        var status = service.Status();
        Assert.Equal("StandaloneCatalog", status.PublicationMode); Assert.True(status.Configured);
        Assert.False(Directory.Exists(blockedRoot)); Assert.True(File.Exists(sentinel));
        Assert.Single(Directory.EnumerateFileSystemEntries(_root));
    }

    [Theory]
    [InlineData("Unknown", "Setup.exe", 2)]
    [InlineData("Setup", "Setup.zip", 2)]
    [InlineData("Setup", "Setup.exe", 0)]
    [InlineData("Setup", "Setup.exe", 10485761)]
    public async Task Rejected_save_parameters_do_not_create_a_store_or_read_the_payload(string kind, string fileName, long length)
    {
        var storage = CreateStorage();
        using var source = new MemoryStream([77, 90]);
        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveAsync(Guid.NewGuid(), kind, fileName, source, length, default));
        Assert.Equal(0, source.Position); Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task A_valid_Setup_upload_creates_the_missing_store_recursively_and_promotes_only_the_complete_file()
    {
        var storage = CreateStorage(); Assert.False(Directory.Exists(_root));
        byte[] bytes = [77, 90, 1, 2, 3, 4]; using var source = new MemoryStream(bytes);
        var stored = await storage.SaveAsync(Guid.NewGuid(), DesktopArtifactKinds.Setup, "Setup.exe", source, source.Length, default);
        var path = storage.ResolvePath(stored.RelativePath);
        Assert.True(Directory.Exists(_root)); Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), stored.Sha256);
        Assert.Equal(bytes.LongLength, stored.SizeBytes);
        Assert.Empty(Directory.GetFiles(_root, "*.upload", SearchOption.AllDirectories));
        Assert.Single(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

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
