using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_TESTS.Updates;

public sealed class DesktopDirectPackageResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-direct-resolver-" + Guid.NewGuid().ToString("N"));
    private const string Api = "https://api.example.test/";

    [Theory]
    [InlineData("")]
    [InlineData("publish/")]
    public async Task A_hash_bound_plain_archive_materializes_real_local_metadata_without_executing_its_PE(string prefix)
    {
        var (path, catalog) = await Archive(prefix);
        var resolved = await DesktopDirectPackageResolver.ResolveAsync(path, catalog, default);
        Assert.Equal(new FileInfo(path).Length, resolved.Release.SizeBytes);
        Assert.Equal(catalog.ExpectedSha256, resolved.Release.Sha256);
        Assert.Equal(7, resolved.Release.BuildNumber);
        Assert.Null(resolved.Release.PackageMetadata);
        var stage = Path.Combine(_root, "stage");
        await DesktopDirectPackageResolver.ExtractAndMaterializeAsync(path, stage, resolved, default);
        Assert.Equal(await File.ReadAllBytesAsync(Fixture), await File.ReadAllBytesAsync(Path.Combine(stage, "TOOL-LOCAL.exe")));
        Assert.Equal("payload", await File.ReadAllTextAsync(Path.Combine(stage, "workers", "fixture.txt")));
        foreach (var name in new[] { "update-manifest.json", "distribution-profile.json", "package-inventory.json" })
            using (JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(stage, name)))) { }
        Assert.Empty(Directory.GetFiles(_root, ".direct-pe-*"));
    }

    [Theory]
    [InlineData("1.2.1", 7)]
    [InlineData("1.2.0", 8)]
    public async Task Declared_filename_hash_does_not_replace_native_version_and_build_validation(string version, int build)
    {
        var (path, catalog) = await Archive("", version, build);
        await Assert.ThrowsAsync<InvalidDataException>(() => DesktopDirectPackageResolver.ResolveAsync(path, catalog, default));
        Assert.Empty(Directory.GetFiles(_root, ".direct-pe-*"));
        Assert.False(Directory.Exists(Path.Combine(_root, "stage")));
    }

    [Fact]
    public async Task Whole_archive_mutation_after_resolution_cannot_materialize_stale_metadata()
    {
        var (path, catalog) = await Archive("");
        var resolved = await DesktopDirectPackageResolver.ResolveAsync(path, catalog, default);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        using (var writer = new StreamWriter(zip.CreateEntry("added.txt").Open())) await writer.WriteAsync("changed after inspection");
        var stage = Path.Combine(_root, "stage");
        await Assert.ThrowsAsync<InvalidDataException>(() => DesktopDirectPackageResolver.ExtractAndMaterializeAsync(path, stage, resolved, default));
        Assert.False(File.Exists(Path.Combine(stage, "TOOL-LOCAL.exe")));
    }

    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "DistributionResources", "DistributionResourcesA.exe");

    private async Task<(string Path, DesktopDirectReleaseResponse Release)> Archive(string prefix, string version = "1.2.0", int build = 7)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "package.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(Fixture, prefix + "TOOL-LOCAL.exe");
            using (var writer = new StreamWriter(zip.CreateEntry(prefix + "workers/fixture.txt").Open())) await writer.WriteAsync("payload");
            using var settings = zip.CreateEntry(prefix + "appsettings.json").Open();
            await JsonSerializer.SerializeAsync(settings, new
            {
                Server = new { BaseUrl = Api },
                ModelDistribution = new { Mode = "StaticArchive", Enabled = false },
                Features = new { VietsubEnabled = true, VietsubOcrEnabled = true, VietsubLocalTranslationEnabled = true, VietsubLocalVoiceEnabled = true }
            });
        }
        var hash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        var file = DesktopDirectCatalogPolicy.FileName(version, build, hash);
        var setup = new DesktopSharedBootstrapperResponse(Guid.NewGuid(), "1.2.0", "Setup.exe",
            "api/launcher-distribution/bootstrappers/fixture/download", 512, new string('b', 64), Api,
            "Production", "Stable", "win-x64", 3, ApprovedStaticModelBundle.Current.BundleId, "3,4");
        return (path, new(Guid.NewGuid(), version, build, file, DesktopArtifactDownloadPolicy.ModelHostPackagePrefix + file,
            hash, "Production", "Stable", "win-x64", ApprovedStaticModelBundle.Current.BundleId,
            Guid.NewGuid(), DateTime.UtcNow, false, null, setup));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
