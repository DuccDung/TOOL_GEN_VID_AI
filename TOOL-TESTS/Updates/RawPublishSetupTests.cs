extern alias SetupAssembly;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SetupAssembly::TOOL_SETUP;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_TESTS.Updates;

/// <summary>Metadata transport fixtures. They do not install an application or qualify model inference.</summary>
public sealed class RawPublishSetupTests
{
    private static InstallerOptions Options() => new() { ServerBaseUrl = "https://api.invalid/", ModelDeliveryMode = "StaticArchive",
        SetupMode = "SharedBootstrapper", BootstrapperVersion = "1.1.0" };
    private static readonly byte[] Metadata = Encoding.UTF8.GetBytes("{\"schemaVersion\":1}");
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [Fact]
    public async Task DescriptorIsDownloadedFromTheApiWithExactLengthAndHash()
    {
        var calls = 0;
        using var handler = new Handler(request =>
        {
            calls++;
            Assert.Equal("https://api.invalid/api/launcher-distribution/versions/fixture/package-metadata", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.Equal("identity", request.Headers.AcceptEncoding.Single().Value);
            Assert.Equal("application/json", request.Headers.Accept.Single().MediaType);
            return Response(Metadata);
        });
        using var service = new LauncherInstallerService(Options(), metadataHandler: handler);
        Assert.Equal(Metadata, await service.DownloadPackageMetadataBytesAsync(
            "/api/launcher-distribution/versions/fixture/package-metadata", Metadata.LongLength, Hash(Metadata), CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("https://outside.invalid/package-metadata")]
    [InlineData("https://103.179.190.182:8443/models/metadata.json")]
    [InlineData("http://api.invalid/metadata")]
    [InlineData("/metadata?redirect=outside")]
    public async Task DescriptorCannotBeSubstitutedWithAnExternalResource(string url)
    {
        var calls = 0;
        using var handler = new Handler(_ => { calls++; return Response(Metadata); });
        using var service = new LauncherInstallerService(Options(), metadataHandler: handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadPackageMetadataBytesAsync(url,
            Metadata.LongLength, Hash(Metadata), CancellationToken.None));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4 * 1024 * 1024 + 1)]
    public async Task InvalidDescriptorSizeIsRejectedBeforeOutbound(long size)
    {
        var calls = 0;
        using var handler = new Handler(_ => { calls++; return Response(Metadata); });
        using var service = new LauncherInstallerService(Options(), metadataHandler: handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadPackageMetadataBytesAsync("/metadata", size,
            Hash(Metadata), CancellationToken.None));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("redirect")]
    [InlineData("mime")]
    [InlineData("missing-mime")]
    [InlineData("encoding")]
    [InlineData("length")]
    [InlineData("short-body")]
    [InlineData("extra-body")]
    [InlineData("checksum")]
    public async Task DescriptorResponseMustMatchItsImmutableSnapshot(string mutation)
    {
        var calls = 0;
        using var handler = new Handler(_ =>
        {
            calls++;
            var response = Response(mutation == "short-body" ? Metadata[..^1] :
                mutation == "extra-body" ? Metadata.Concat(new byte[] { 0 }).ToArray() : Metadata);
            switch (mutation)
            {
                case "redirect": response.StatusCode = HttpStatusCode.Found; response.Headers.Location = new Uri("https://outside.invalid/metadata"); break;
                case "mime": response.Content.Headers.ContentType = new("text/html"); break;
                case "missing-mime": response.Content.Headers.ContentType = null; break;
                case "encoding": response.Content.Headers.ContentEncoding.Add("gzip"); break;
                case "length": response.Content.Headers.ContentLength = Metadata.LongLength + 1; break;
                case "short-body":
                case "extra-body": response.Content.Headers.ContentLength = Metadata.LongLength; break;
            }
            return response;
        });
        using var service = new LauncherInstallerService(Options(), metadataHandler: handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadPackageMetadataBytesAsync("/metadata", Metadata.LongLength,
            mutation == "checksum" ? new string('a', 64) : Hash(Metadata), CancellationToken.None));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task MalformedHashIsRejectedBeforeOutbound()
    {
        var calls = 0;
        using var handler = new Handler(_ => { calls++; return Response(Metadata); });
        using var service = new LauncherInstallerService(Options(), metadataHandler: handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadPackageMetadataBytesAsync("/metadata", Metadata.LongLength,
            "invalid", CancellationToken.None));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task ExactApiRefreshHydratesMetadataOmittedByAnOlderProtocolThreeDesktop()
    {
        var approved = Release(); var requested = approved with { PackageMetadata = null };
        using var handler = new Handler(_ => Response(JsonSerializer.SerializeToUtf8Bytes(approved,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        using var service = new LauncherInstallerService(Options(), metadataHandler: handler);
        var hydrated = await service.GetReleaseAsync(requested, CancellationToken.None);
        Assert.Equal(approved, hydrated);
        Assert.NotNull(hydrated!.PackageMetadata);
        Assert.Equal(requested.Sha256, hydrated.Sha256);
        // A normal snapshot comparison remains strict: this exception is only for exact API hydration.
        Assert.Throws<InvalidDataException>(() => SetupMaintenanceHandoff.ValidateReleaseSnapshot(requested, approved, Options()));
        SetupMaintenanceHandoff.ValidateReleaseSnapshot(requested, approved, Options(), allowMetadataAugmentation: true);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("length")]
    [InlineData("url")]
    [InlineData("removed")]
    [InlineData("application-bytes")]
    [InlineData("bootstrapper")]
    public async Task ApiRefreshCannotReplaceAnExistingDescriptorOrApplicationSnapshot(string mutation)
    {
        var requested = Release(); var metadata = requested.PackageMetadata!;
        var approved = mutation switch
        {
            "hash" => requested with { PackageMetadata = metadata with { Sha256 = new string('c', 64) } },
            "length" => requested with { PackageMetadata = metadata with { SizeBytes = metadata.SizeBytes + 1 } },
            "url" => requested with { PackageMetadata = metadata with { DownloadUrl = "/api/launcher-distribution/versions/other/package-metadata" } },
            "removed" => requested with { PackageMetadata = null },
            "application-bytes" => requested with { Sha256 = new string('c', 64) },
            _ => requested with { SharedBootstrapper = requested.SharedBootstrapper! with { BootstrapperId = Guid.NewGuid() } }
        };
        using var handler = new Handler(_ => Response(JsonSerializer.SerializeToUtf8Bytes(approved,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        using var service = new LauncherInstallerService(Options(), metadataHandler: handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.GetReleaseAsync(requested, CancellationToken.None));
        Assert.Throws<InvalidDataException>(() => SetupMaintenanceHandoff.ValidateReleaseSnapshot(requested, approved,
            Options(), allowMetadataAugmentation: true));
    }

    [Fact]
    public void RawPublishRequiresTheMetadataCapableSharedSetupVersion()
    {
        var release = Release();
        LauncherInstallerService.ValidateStaticRelease(release, Options());
        var old = new InstallerOptions { ServerBaseUrl = "https://api.invalid/", ModelDeliveryMode = "StaticArchive",
            SetupMode = "SharedBootstrapper", BootstrapperVersion = "1.0.0" };
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateStaticRelease(release, old));
    }

    private static DesktopReleaseResponse Release()
    {
        var releaseId = Guid.NewGuid(); var archiveHash = new string('a', 64);
        var bootstrapper = new DesktopSharedBootstrapperResponse(Guid.NewGuid(), "1.1.0", "Setup.exe",
            "/api/launcher-distribution/bootstrappers/fixture/download", 100, new string('b', 64), "https://api.invalid/",
            "Production", "Stable", "win-x64", 3, ApprovedStaticModelBundle.Current.BundleId);
        var metadata = new DesktopPackageMetadataArtifactResponse(1,
            DesktopPackageMetadataPolicy.MetadataPath(releaseId, bootstrapper.BootstrapperId, Hash(Metadata)),
            Metadata.LongLength, Hash(Metadata), archiveHash);
        return new(releaseId, "VideoMaker", "1.2.0", 7, "Stable", "win-x64", null, null, DateTime.UtcNow, "publish.zip",
            DesktopArtifactDownloadPolicy.ApplicationPrefix + "1.2.0/publish.zip", 100, archiveHash,
            DesktopArtifactSourceKinds.ExternalVps, DesktopInstallModes.StaticArchive, 3, null, ApprovedStaticModelBundle.Current.BundleId,
            bootstrapper, "Production", "1.1.0", metadata);
    }

    private static HttpResponseMessage Response(byte[] bytes)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return response;
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(reply(request)); }
}
