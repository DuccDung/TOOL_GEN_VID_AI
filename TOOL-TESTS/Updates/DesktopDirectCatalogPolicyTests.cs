using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_TESTS.Updates;

public sealed class DesktopDirectCatalogPolicyTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly Uri Api = new("https://api.example.test/base/");

    [Theory]
    [InlineData("https://103.179.190.182:8443/models/")]
    [InlineData("https://103.179.190.182:8443/apps/videomaker/")]
    public void Catalog_contains_declared_identity_without_a_fabricated_archive_size(string prefix)
    {
        var release = Release(prefix);
        DesktopDirectCatalogPolicy.ValidateRelease(release, Api, "Production");
        var json = JsonSerializer.Serialize(release, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("sizeBytes", json.Split("sharedBootstrapper")[0]);
        Assert.Equal(Hash, DesktopDirectCatalogPolicy.ParseUrl(Api, release.DownloadUrl).Sha256);
        Assert.Equal(17, DesktopDirectCatalogPolicy.ParseFileName(release.FileName).BuildNumber);
    }

    [Theory]
    [InlineData("VideoMaker-01.2.3-17-win-x64-")]
    [InlineData("VideoMaker-1.2.3-0-win-x64-")]
    [InlineData("VideoMaker-1.2.3-017-win-x64-")]
    [InlineData("VideoMaker-1.2.3-2147483648-win-x64-")]
    [InlineData("VideoMaker-2147483648.2.3-17-win-x64-")]
    [InlineData("VideoMaker-1.2.3-17-win-arm64-")]
    [InlineData("videomaker-1.2.3-17-win-x64-")]
    public void Invalid_declared_version_build_or_platform_is_rejected(string prefix) =>
        Assert.Throws<InvalidDataException>(() => DesktopDirectCatalogPolicy.ParseFileName(prefix + Hash + ".zip"));

    [Theory]
    [InlineData("?sha256=bad")]
    [InlineData("#sha256=bad")]
    [InlineData("/other.zip")]
    [InlineData("%20")]
    public void URL_suffix_cannot_change_the_frozen_identity(string suffix) =>
        Assert.Throws<InvalidDataException>(() => DesktopDirectCatalogPolicy.ParseUrl(Api, Release().DownloadUrl + suffix));

    [Theory]
    [InlineData("https://evil.example/models/")]
    [InlineData("http://103.179.190.182:8443/models/")]
    [InlineData("https://103.179.190.182/models/")]
    [InlineData("https://103.179.190.182:8443/models2/")]
    [InlineData("https://103.179.190.182:8443/models/../private/")]
    public void Source_validation_remains_an_exact_HTTPS_allowlist(string prefix) =>
        Assert.Throws<InvalidDataException>(() => DesktopDirectCatalogPolicy.ParseUrl(Api, prefix + Release().FileName));

    [Fact]
    public void Changing_catalog_identity_without_changing_filename_is_rejected()
    {
        var release = Release();
        foreach (var changed in new[]
        {
            release with { Version = "1.2.4" }, release with { DeclaredBuildNumber = 18 },
            release with { ExpectedSha256 = new string('a', 64) }, release with { FileName = "other.zip" },
            release with { Revision = Guid.Empty }, release with { CatalogProtocol = 3 },
            release with { ApplicationDistributionProtocol = 4 }, release with { Profile = "Other" },
            release with { ModelBundleId = "unapproved-models" }, release with { Channel = "Beta" }
        }) Assert.Throws<InvalidDataException>(() => DesktopDirectCatalogPolicy.ValidateRelease(changed, Api));
    }

    [Theory]
    [InlineData("1.1.0", "3,4")]
    [InlineData("1.2.0", "3")]
    [InlineData("1.2.0", "4")]
    [InlineData("1.2.0", "3,4,4")]
    [InlineData("1.2.0", "3, 4")]
    public void Old_or_ambiguous_Setup_capabilities_are_not_upgraded_implicitly(string version, string capabilities)
    {
        var release = Release();
        Assert.Throws<InvalidDataException>(() => DesktopDirectCatalogPolicy.ValidateRelease(release with
        { SharedBootstrapper = release.SharedBootstrapper with { Version = version, SupportedCatalogProtocols = capabilities } }, Api));
    }

    [Fact]
    public void Default_legacy_bootstrapper_keeps_protocol_three_only()
    {
        var bootstrapper = Release().SharedBootstrapper with { SupportedCatalogProtocols = "3" };
        Assert.True(DesktopDirectCatalogPolicy.SupportsCatalog(bootstrapper, 3));
        Assert.False(DesktopDirectCatalogPolicy.SupportsCatalog(bootstrapper));
    }

    [Fact]
    public async Task Incorrect_archive_hash_is_rejected_before_any_PE_extraction_or_execution()
    {
        var root = Path.Combine(Path.GetTempPath(), "vm-direct-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "package.zip");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("TOOL-LOCAL.exe").Open())) writer.Write("inert, must never execute");
            await Assert.ThrowsAsync<InvalidDataException>(() => DesktopDirectPackageResolver.ResolveAsync(path, Release(), CancellationToken.None));
            Assert.Single(Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Renaming_a_package_to_its_digest_does_not_rewrite_its_bytes()
    {
        var data = Encoding.UTF8.GetBytes("controlled immutable package bytes");
        var digest = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var name = DesktopDirectCatalogPolicy.FileName("1.0.2", 3, digest);
        Assert.Equal(digest, DesktopDirectCatalogPolicy.ParseFileName(name).Sha256);
        Assert.True(name.Length <= DesktopArtifactDownloadPolicy.MaximumArtifactFileNameLength);
    }

    private static DesktopDirectReleaseResponse Release(string prefix = "https://103.179.190.182:8443/models/")
    {
        var name = DesktopDirectCatalogPolicy.FileName("1.2.3", 17, Hash);
        var bootstrapper = new DesktopSharedBootstrapperResponse(Guid.NewGuid(), "1.2.0", "VideoMaker Setup.exe",
            "api/launcher-distribution/bootstrappers/current/download", 512, new string('b', 64), Api.AbsoluteUri,
            "Production", "Stable", "win-x64", 3, ApprovedStaticModelBundle.Current.BundleId, "3,4");
        return new(Guid.NewGuid(), "1.2.3", 17, name, prefix + name, Hash, "Production", "Stable", "win-x64",
            ApprovedStaticModelBundle.Current.BundleId, Guid.NewGuid(), DateTime.UtcNow, false, null, bootstrapper);
    }
}
