using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_TESTS.Updates;

public sealed class StandaloneDesktopDistributionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-standalone-policy-" + Guid.NewGuid().ToString("N"));
    private static readonly Uri Api = new("https://api.example.test/");
    internal static DesktopStandaloneReleaseResponse Release(string version = "1.2.0", int build = 7, string? hash = null)
    {
        hash ??= new string('a', 64);
        var name = DesktopDirectCatalogPolicy.FileName(version, build, hash);
        return new(Guid.NewGuid(), version, build, name, DesktopArtifactDownloadPolicy.ModelHostPackagePrefix + name,
            hash, Api.AbsoluteUri, "Production", "Stable", "win-x64", ApprovedStaticModelBundle.Current.BundleId,
            Guid.NewGuid(), DateTime.UtcNow, false, "standalone fixture");
    }

    [Fact]
    public void A_running_Setup_needs_no_registered_Setup_artifact_or_invented_measured_metadata()
    {
        var release = Release();
        DesktopStandaloneCatalogPolicy.ValidateRelease(release, Api, "Production");
        Assert.Null(release.Setup); Assert.Equal(5, release.CatalogProtocol); Assert.Equal(3, release.ApplicationDistributionProtocol);
        var json = JsonSerializer.SerializeToElement(release, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(json.TryGetProperty("sizeBytes", out _)); Assert.False(json.TryGetProperty("sharedBootstrapper", out _));
        Assert.False(json.TryGetProperty("packageMetadata", out _)); Assert.True(json.TryGetProperty("expectedSha256", out _));
    }

    [Fact]
    public void Catalog_identity_API_model_and_protocol_are_frozen_independently_of_the_optional_Setup_link()
    {
        var release = Release();
        foreach (var changed in new[]
        {
            release with { Version = "1.2.1" }, release with { DeclaredBuildNumber = 8 },
            release with { ExpectedSha256 = new string('b', 64) }, release with { Revision = Guid.Empty },
            release with { CatalogProtocol = 4 }, release with { ApplicationDistributionProtocol = 5 },
            release with { MinimumSetupVersion = "1.2.0" }, release with { ModelBundleId = "unapproved" },
            release with { Channel = "Beta" }, release with { Platform = "win-arm64" },
            release with { ApiBaseUrl = "https://other.example.test/" }, release with { Profile = "Other" }
        }) Assert.Throws<InvalidDataException>(() => DesktopStandaloneCatalogPolicy.ValidateRelease(changed, Api, "Production"));
    }

    [Fact]
    public void Canonical_Setup_URL_declares_the_exact_executable_digest_without_measuring_or_registering_it()
    {
        var hash = new string('b', 64);
        var file = $"Setup-1.3.0-win-x64-{hash}.exe";
        var setup = DesktopStandaloneCatalogPolicy.ParseSetupUrl(Api, DesktopArtifactDownloadPolicy.ModelHostPackagePrefix + file);
        Assert.Equal("1.3.0", setup.Version); Assert.Equal(file, setup.FileName); Assert.Equal(hash, setup.Sha256);
        DesktopStandaloneCatalogPolicy.ValidateSetup(setup, Api, "1.3.0");
        DesktopStandaloneCatalogPolicy.ValidateRelease(Release() with { Setup = setup }, Api);
        Assert.DoesNotContain(typeof(DesktopStandaloneSetupResponse).GetProperties(), x => x.Name is "SizeBytes" or "BootstrapperId");
        Assert.Throws<InvalidDataException>(() => DesktopStandaloneCatalogPolicy.ValidateSetup(setup with { Sha256 = new string('c', 64) }, Api));
        Assert.Throws<InvalidDataException>(() => DesktopStandaloneCatalogPolicy.ValidateSetup(setup with { Version = "1.3.1" }, Api));
    }

    [Theory]
    [InlineData("http://103.179.190.182:8443/models/Setup-1.3.0-win-x64-")]
    [InlineData("https://outside.invalid/models/Setup-1.3.0-win-x64-")]
    [InlineData("https://103.179.190.182:8443/models2/Setup-1.3.0-win-x64-")]
    [InlineData("https://103.179.190.182:8443/models/Setup-1.2.0-win-x64-")]
    [InlineData("https://103.179.190.182:8443/models/Setup-01.3.0-win-x64-")]
    [InlineData("https://103.179.190.182:8443/models/Setup-1.3.0-win-arm64-")]
    [InlineData("https://user:password@103.179.190.182:8443/models/Setup-1.3.0-win-x64-")]
    public void Unsafe_legacy_or_ambiguous_Setup_links_cannot_be_used_for_desktop_handoff(string prefix) =>
        Assert.Throws<InvalidDataException>(() => DesktopStandaloneCatalogPolicy.ParseSetupUrl(Api, prefix + new string('b', 64) + ".exe"));

    [Fact]
    public void Runtime_allowlist_cannot_expand_the_compiled_Setup_source_policy()
    {
        var bad = "https://outside.invalid/models/";
        var url = bad + "Setup-1.3.0-win-x64-" + new string('b', 64) + ".exe";
        Assert.Throws<InvalidDataException>(() => DesktopStandaloneCatalogPolicy.ParseSetupUrl(Api, url, [bad]));
        var approved = DesktopArtifactDownloadPolicy.ModelHostPackagePrefix + "Setup-1.3.0-win-x64-" + new string('b', 64) + ".exe";
        Assert.Throws<InvalidDataException>(() => DesktopStandaloneCatalogPolicy.ParseSetupUrl(Api, approved, [DesktopArtifactDownloadPolicy.ApplicationPrefix]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("publish/")]
    public async Task A_plain_hash_bound_archive_materializes_measured_metadata_without_a_fake_shared_Setup(string prefix)
    {
        var (path, release) = await Archive(prefix);
        var resolved = await DesktopStandalonePackageResolver.ResolveAsync(path, release, default);
        Assert.Equal(new FileInfo(path).Length, resolved.Release.SizeBytes);
        Assert.Equal(release.ExpectedSha256, resolved.Release.Sha256); Assert.Equal(7, resolved.Release.BuildNumber);
        Assert.Null(resolved.Release.SharedBootstrapper); Assert.Null(resolved.Release.SetupArtifact); Assert.Null(resolved.Release.PackageMetadata);
        Assert.Equal(3, resolved.Release.DistributionProtocol);
        var stage = Path.Combine(_root, "stage");
        await DesktopStandalonePackageResolver.ExtractAndMaterializeAsync(path, stage, resolved, default);
        Assert.Equal(await File.ReadAllBytesAsync(Fixture), await File.ReadAllBytesAsync(Path.Combine(stage, "TOOL-LOCAL.exe")));
        foreach (var name in new[] { "update-manifest.json", "distribution-profile.json", "package-inventory.json" })
            using (JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(stage, name)))) { }
        Assert.Empty(Directory.GetFiles(_root, "*-pe-*.exe"));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("version")]
    [InlineData("build")]
    [InlineData("api")]
    [InlineData("model")]
    public async Task Archive_or_native_binding_changes_are_rejected_before_any_installation(string mutation)
    {
        var (path, release) = await Archive("", mutation == "api" ? "https://other.example.test/" : Api.AbsoluteUri);
        release = mutation switch
        {
            "hash" => Release(hash: new string('b', 64)),
            "version" => Release("1.2.1", hash: release.ExpectedSha256),
            "build" => Release(build: 8, hash: release.ExpectedSha256),
            "model" => release with { ModelBundleId = "unapproved-model" },
            _ => release
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => DesktopStandalonePackageResolver.ResolveAsync(path, release, default));
        Assert.False(Directory.Exists(Path.Combine(_root, "stage")));
        Assert.Empty(Directory.GetFiles(_root, "*-pe-*.exe"));
    }

    [Fact]
    public async Task Changing_archive_bytes_after_resolution_cannot_write_the_previously_approved_application()
    {
        var (path, release) = await Archive("");
        var resolved = await DesktopStandalonePackageResolver.ResolveAsync(path, release, default);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
        using (var writer = new StreamWriter(zip.CreateEntry("added.txt").Open())) await writer.WriteAsync("changed");
        var stage = Path.Combine(_root, "stage");
        await Assert.ThrowsAsync<InvalidDataException>(() => DesktopStandalonePackageResolver.ExtractAndMaterializeAsync(path, stage, resolved, default));
        Assert.False(File.Exists(Path.Combine(stage, "TOOL-LOCAL.exe")));
    }

    [Theory]
    [InlineData("api")]
    [InlineData("profile")]
    [InlineData("digest")]
    public async Task Generated_local_metadata_remains_bound_to_the_original_catalog(string mutation)
    {
        var (path, release) = await Archive("");
        var resolved = await DesktopStandalonePackageResolver.ResolveAsync(path, release, default);
        var profile = resolved.Metadata.DistributionProfile;
        var changed = mutation switch
        {
            "api" => resolved with { Metadata = resolved.Metadata with { DistributionProfile = profile with { ServerBaseUrl = "https://outside.invalid/" } } },
            "profile" => resolved with { Release = resolved.Release with { Profile = "Other" } },
            _ => resolved with { Release = resolved.Release with { Sha256 = new string('c', 64) } }
        };
        var stage = Path.Combine(_root, "stage");
        await Assert.ThrowsAsync<InvalidDataException>(() => DesktopStandalonePackageResolver.ExtractAndMaterializeAsync(path, stage, changed, default));
        Assert.False(File.Exists(Path.Combine(stage, "TOOL-LOCAL.exe")));
    }

    [Fact]
    public async Task Typed_IPC_schema_three_roundtrips_without_inventing_a_bootstrapper_and_rejects_a_mismatched_ACK()
    {
        var now = DateTime.UtcNow; var request = Maintenance(now);
        DesktopMaintenanceProtocol.ValidateRequest(request, now);
        await using var stream = new MemoryStream();
        await DesktopMaintenanceProtocol.WriteAsync(stream, request, default); stream.Position = 0;
        var decoded = Assert.IsType<DesktopStandaloneMaintenanceRequest>(await DesktopMaintenanceProtocol.ReadMaintenanceRequestAsync(stream, default));
        Assert.Equal(request, decoded); Assert.True(stream.Length < DesktopMaintenanceProtocol.MaximumFrameBytes);
        var ack = new DesktopMaintenanceAck(3, request.OperationId, request.Nonce, request.Release.ReleaseId,
            request.Release.ExpectedSha256, request.TargetRoot, 42, now.Ticks, request.UserSid, now.AddSeconds(30));
        DesktopMaintenanceProtocol.ValidateAck(request, ack, 42, now.Ticks, now);
        foreach (var changed in new[] { ack with { SchemaVersion = 2 }, ack with { Nonce = new string('f', 64) },
            ack with { PackageSha256 = new string('f', 64) }, ack with { ReleaseId = Guid.NewGuid() }, ack with { SetupProcessId = 43 } })
            Assert.Throws<InvalidDataException>(() => DesktopMaintenanceProtocol.ValidateAck(request, changed, 42, now.Ticks, now));
    }

    [Fact]
    public void Repair_IPC_requires_the_exact_installed_version_build_and_package_digest()
    {
        var now = DateTime.UtcNow; var release = Release();
        var request = Maintenance(now) with { Action = "repair", CurrentVersion = release.Version,
            CurrentBuildNumber = release.DeclaredBuildNumber, CurrentPackageSha256 = release.ExpectedSha256, Release = release };
        DesktopMaintenanceProtocol.ValidateRequest(request, now);
        foreach (var changed in new[] { request with { CurrentVersion = "1.1.0" }, request with { CurrentBuildNumber = 6 },
            request with { CurrentPackageSha256 = new string('c', 64) }, request with { SchemaVersion = 2 },
            request with { CreatedAtUtc = now.AddMinutes(-2) }, request with { ParentProcessId = 0 } })
            Assert.Throws<InvalidDataException>(() => DesktopMaintenanceProtocol.ValidateRequest(changed, now));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("downgraded-schema")]
    public async Task IPC_rejects_ambiguous_unknown_or_cross_protocol_payloads(string mutation)
    {
        var json = JsonSerializer.Serialize(Maintenance(DateTime.UtcNow), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json = mutation switch
        {
            "duplicate" => json[..^1] + ",\"Nonce\":\"" + new string('d', 64) + "\"}",
            "unknown" => json[..^1] + ",\"unexpected\":true}",
            _ => json.Replace("\"schemaVersion\":3", "\"schemaVersion\":2", StringComparison.Ordinal)
        };
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json + "\n"));
        var error = await Record.ExceptionAsync(() => DesktopMaintenanceProtocol.ReadMaintenanceRequestAsync(stream, default));
        Assert.True(error is InvalidDataException or JsonException, $"Unexpected parser result: {error?.GetType().FullName ?? "accepted"}");
    }

    private DesktopStandaloneMaintenanceRequest Maintenance(DateTime now) => new(3, Guid.NewGuid().ToString("N"), new string('d', 64),
        "update", Release(), _root, "1.1.0", 2, new string('e', 64), Environment.ProcessId + 1, now.Ticks,
        "S-1-5-21-fixture", CreatedAtUtc: now);
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "DistributionResources", "DistributionResourcesA.exe");
    private async Task<(string Path, DesktopStandaloneReleaseResponse Release)> Archive(string prefix, string? api = null)
    {
        Directory.CreateDirectory(_root); var path = Path.Combine(_root, "package.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(Fixture, prefix + "TOOL-LOCAL.exe");
            using (var writer = new StreamWriter(zip.CreateEntry(prefix + "workers/fixture.txt").Open())) await writer.WriteAsync("payload");
            using var settings = zip.CreateEntry(prefix + "appsettings.json").Open();
            await JsonSerializer.SerializeAsync(settings, new
            {
                Server = new { BaseUrl = api ?? Api.AbsoluteUri }, ModelDistribution = new { Mode = "StaticArchive", Enabled = false },
                Features = new { VietsubEnabled = true, VietsubOcrEnabled = true, VietsubLocalTranslationEnabled = true, VietsubLocalVoiceEnabled = true }
            });
        }
        return (path, Release(hash: Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path)))));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
