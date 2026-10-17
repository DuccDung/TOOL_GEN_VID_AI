extern alias SetupAssembly;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SetupAssembly::TOOL_SETUP;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.Updates;
using TOOL_LOCAL.Configuration;
using TOOL_LOCAL.Updates;

namespace TOOL_TESTS.Updates;

/// <summary>Runtime/transport/transaction fixtures; not an installed-model qualification.</summary>
public sealed class SharedBootstrapperSetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shared-bootstrapper-" + Guid.NewGuid().ToString("N"));
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";
    private static InstallerOptions Options() => new() { ServerBaseUrl = "https://api.invalid/", Profile = "Production",
        Channel = "Stable", ModelDeliveryMode = "StaticArchive", SetupMode = "SharedBootstrapper", BootstrapperVersion = "1.0.0" };
    private static DesktopReleaseResponse Release(string version = "1.2.0", int build = 7)
    {
        var bootstrapper = new DesktopSharedBootstrapperResponse(Guid.NewGuid(), "1.0.0", "Setup.exe", "/api/launcher-distribution/bootstrappers/fixture/download",
            100, new string('b', 64), "https://api.invalid/", "Production", "Stable", "win-x64", 3, ApprovedStaticModelBundle.Current.BundleId);
        return new(Guid.NewGuid(), "VideoMaker", version, build, "Stable", "win-x64", null, null, DateTime.UtcNow,
            "application.zip", DesktopArtifactDownloadPolicy.ApplicationPrefix + version + "/application.zip", 100, Hash,
            DesktopArtifactSourceKinds.ExternalVps, DesktopInstallModes.StaticArchive, 3, null, ApprovedStaticModelBundle.Current.BundleId,
            bootstrapper, "Production", "1.0.0");
    }

    [Fact]
    public void SharedBindingDoesNotContainOrRequireAnApplicationVersionBuildOrHash()
    {
        var options = Options();
        Assert.Empty(options.DesktopVersion); Assert.Equal(0, options.DesktopBuildNumber); Assert.Empty(options.DesktopSha256);
        var binding = options.InspectSharedBootstrapper();
        Assert.Equal(3, binding.DistributionProtocol); Assert.Equal("Production", binding.Profile);
        Assert.Equal("https://api.invalid/", binding.ApiBaseUrl);
        LauncherInstallerService.ValidateStaticRelease(Release("1.2.0", 10), options);
        LauncherInstallerService.ValidateStaticRelease(Release("1.3.0", 1), options);
    }

    [Fact]
    public void NativeBindingDeclarationMustMatchTheRuntimeCompiledProfile()
    {
        var options = Options();
        static string Encode(DesktopBootstrapperBindingInfo binding) => "VideoMaker.SharedBootstrapper.v1:" +
            Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(binding, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        options.ValidateSharedDescription(Encode(options.InspectSharedBootstrapper()));
        Assert.Throws<InvalidOperationException>(() => options.ValidateSharedDescription(Encode(options.InspectSharedBootstrapper() with { Profile = "Staging" })));
        Assert.Throws<InvalidOperationException>(() => options.ValidateSharedDescription(Encode(options.InspectSharedBootstrapper() with { ApiBaseUrl = "https://other.invalid/" })));
        Assert.Throws<InvalidOperationException>(() => options.ValidateSharedDescription(null));
        Assert.Throws<InvalidOperationException>(() => options.ValidateSharedDescription("VideoMaker.SharedBootstrapper.v1:not-base64"));
    }

    [Fact]
    public void DesktopRejectsAnotherApiOrProfileBeforeShowingAnUpdateNotice()
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://api.invalid/") };
        var client = new DesktopUpdateApiClient(http, null!, new DesktopUpdateOptions());
        var release = Release();
        client.ValidateIncomingRelease(release);
        Assert.Throws<InvalidDataException>(() => client.ValidateIncomingRelease(release with { Profile = "Staging" }));
        Assert.Throws<InvalidDataException>(() => client.ValidateIncomingRelease(release with
            { SharedBootstrapper = release.SharedBootstrapper! with { ApiBaseUrl = "https://other.invalid/" } }));
    }

    [Fact]
    public void SharedSetupChecksActualApplicationPolicyProtocolAndProfileBeforePreparingModels()
    {
        JsonElement Policy(int? distribution, string? profile) => JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, required = true, modelUseAllowed = true,
            bundleId = ApprovedStaticModelBundle.Current.BundleId,
            workerVersion = ApprovedStaticModelBundle.Current.RequiredWorkerVersion,
            workerProtocolVersion = ApprovedStaticModelBundle.Current.RequiredWorkerProtocolVersion,
            archiveSha256 = ApprovedStaticModelBundle.Current.ArchiveSha256,
            distributionProtocol = distribution, profile
        });
        StaticBootstrapRunner.ValidatePolicyReport(Policy(3, "Production"), 0, 3, "Production");
        Assert.Throws<InvalidDataException>(() => StaticBootstrapRunner.ValidatePolicyReport(Policy(2, "Production"), 0, 3, "Production"));
        Assert.Throws<InvalidDataException>(() => StaticBootstrapRunner.ValidatePolicyReport(Policy(3, "Staging"), 0, 3, "Production"));
        Assert.Throws<InvalidDataException>(() => StaticBootstrapRunner.ValidatePolicyReport(Policy(null, null), 0, 3, "Production"));
        // Old fixtures and exact protocol-2 binaries need not report new capability fields.
        StaticBootstrapRunner.ValidatePolicyReport(Policy(null, null), 0);
    }

    [Fact]
    public async Task OneLoadedSetupBinaryValidatesTwoDifferentPackagesWithTheSameExecutableHash()
    {
        var path = typeof(InstallerOptions).Assembly.Location;
        var bytes = await File.ReadAllBytesAsync(path);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var first = Release("1.2.0", 99);
        var bootstrapper = first.SharedBootstrapper! with { Sha256 = hash, SizeBytes = bytes.LongLength };
        first = first with { SharedBootstrapper = bootstrapper };
        var second = Release("1.3.0", 1) with { Sha256 = new string('c', 64), SharedBootstrapper = bootstrapper };
        await LauncherInstallerService.ValidateSharedBootstrapperFileAsync(path, first, Options(), CancellationToken.None);
        await LauncherInstallerService.ValidateSharedBootstrapperFileAsync(path, second, Options(), CancellationToken.None);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        await Assert.ThrowsAsync<InvalidDataException>(() => LauncherInstallerService.ValidateSharedBootstrapperFileAsync(path,
            second with { SharedBootstrapper = bootstrapper with { Sha256 = new string('d', 64) } }, Options(), CancellationToken.None));
    }

    [Theory]
    [InlineData("api")]
    [InlineData("api-path")]
    [InlineData("profile")]
    [InlineData("release-profile")]
    [InlineData("channel")]
    [InlineData("model")]
    [InlineData("protocol")]
    [InlineData("minimum")]
    [InlineData("version")]
    [InlineData("external-setup")]
    [InlineData("legacy-setup")]
    [InlineData("outside-prefix")]
    public void SharedReleaseRejectsUnapprovedCapabilityAndProfile(string mutation)
    {
        var release = Release(); var bootstrapper = release.SharedBootstrapper!;
        var changed = mutation switch
        {
            "api" => release with { SharedBootstrapper = bootstrapper with { ApiBaseUrl = "https://another.invalid/" } },
            "api-path" => release with { SharedBootstrapper = bootstrapper with { ApiBaseUrl = "https://api.invalid/other/" } },
            "profile" => release with { SharedBootstrapper = bootstrapper with { Profile = "Staging" } },
            "release-profile" => release with { Profile = "Staging" },
            "channel" => release with { SharedBootstrapper = bootstrapper with { Channel = "Beta" } },
            "model" => release with { ModelBundleId = "different-models", SharedBootstrapper = bootstrapper with { ModelBundleId = "different-models" } },
            "protocol" => release with { DistributionProtocol = 2 },
            "minimum" => release with { MinimumBootstrapperVersion = "2.0.0" },
            "version" => release with { Version = "1.2.0-beta" },
            "external-setup" => release with { SharedBootstrapper = bootstrapper with { DownloadUrl = DesktopArtifactDownloadPolicy.ApplicationPrefix + "setup.exe" } },
            "outside-prefix" => release with { DownloadUrl = "https://103.179.190.182:8443/unapproved/application.zip" },
            _ => release with { SetupArtifact = new(Guid.NewGuid(), "1.2.0", 7, "Stable", "win-x64", Hash,
                "setup.exe", "/setup.exe", 100, Hash) }
        };
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateStaticRelease(changed, Options()));
    }

    [Fact]
    public void SharedSetupAcceptsApplicationZipInTheExistingModelsFileRoute()
    {
        // The server still inspects the actual ZIP to reject a model archive masquerading as an application.
        var release = Release() with { DownloadUrl = DesktopArtifactDownloadPolicy.ModelHostPackagePrefix + "publish-01.zip" };
        LauncherInstallerService.ValidateStaticRelease(release, Options());
    }

    [Fact]
    public async Task VersionLookupUsesProtocolThreeAndOrdersVersionsBeforeBuilds()
    {
        var first = Release("1.9.0", 999); var second = Release("1.10.0", 1);
        var mismatched = Release("2.0.0") with { Profile = "Staging" };
        Uri? called = null;
        using var handler = new Handler(request =>
        {
            called = request.RequestUri;
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new DesktopReleaseListResponse([first, mismatched, second]), new JsonSerializerOptions(JsonSerializerDefaults.Web))) };
        });
        using var service = new LauncherInstallerService(Options(), metadataHandler: handler);
        var versions = await service.GetVersionsAsync(CancellationToken.None);
        Assert.Equal(new[] { second.ReleaseId, first.ReleaseId }, versions.Select(value => value.ReleaseId));
        Assert.Contains("distributionProtocol=3", called!.Query); Assert.Contains("profile=Production", called.Query);
        Assert.Equal("api.invalid", called.Host);
    }

    [Fact]
    public async Task OperationRefreshPinsBootstrapperHistoryInsteadOfReselectingTheActiveSetup()
    {
        var release = Release(); Uri? called = null;
        using var handler = new Handler(request =>
        {
            called = request.RequestUri;
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(release, new JsonSerializerOptions(JsonSerializerDefaults.Web))) };
        });
        using var service = new LauncherInstallerService(Options(), metadataHandler: handler);
        Assert.Equal(release, await service.GetReleaseAsync(release, CancellationToken.None));
        Assert.Equal($"/api/launcher-distribution/versions/{release.ReleaseId:D}", called!.AbsolutePath);
        Assert.Contains($"bootstrapperId={release.SharedBootstrapper!.BootstrapperId:D}", called.Query);
        Assert.Contains("profile=Production", called.Query);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshRejectsRevokedReleaseOrSubstitutedBootstrapper(bool substituted)
    {
        var release = Release();
        using var handler = new Handler(_ => substituted
            ? new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(release with
                { SharedBootstrapper = release.SharedBootstrapper! with { BootstrapperId = Guid.NewGuid() } }, new JsonSerializerOptions(JsonSerializerDefaults.Web))) }
            : new(HttpStatusCode.NotFound));
        using var service = new LauncherInstallerService(Options(), metadataHandler: handler);
        if (substituted) await Assert.ThrowsAsync<InvalidDataException>(() => service.GetReleaseAsync(release, CancellationToken.None));
        else Assert.Null(await service.GetReleaseAsync(release, CancellationToken.None));
    }

    [Theory]
    [InlineData("1.10.0", 1, "1.9.0", 999, true)]
    [InlineData("1.9.0", 999, "1.10.0", 1, false)]
    [InlineData("1.2.0", 8, "1.2.0", 7, true)]
    [InlineData("1.2.0", 7, "1.2.0", 7, false)]
    [InlineData("1.2.0", 6, "1.2.0", 7, false)]
    public void MaintenanceUsesVersionFirstForProtocolThree(string version, int build, string installedVersion, int installedBuild, bool accepted)
    {
        Directory.CreateDirectory(_root);
        var request = Request(Release(version, build), "update", installedVersion, installedBuild);
        if (accepted) DesktopMaintenanceProtocol.ValidateRequest(request, DateTime.UtcNow);
        else Assert.Throws<InvalidDataException>(() => DesktopMaintenanceProtocol.ValidateRequest(request, DateTime.UtcNow));
    }

    [Fact]
    public void StandaloneSharedSetupRejectsDowngradeAndChangedBytesOfTheSameInstalledBuild()
    {
        var release = Release();
        LauncherInstallerService.ValidateSharedVersionTransition(release, release.Version, release.BuildNumber, release.Sha256);
        LauncherInstallerService.ValidateSharedVersionTransition(release with { BuildNumber = 8 }, release.Version, release.BuildNumber, release.Sha256);
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateSharedVersionTransition(release,
            "1.3.0", 1, new string('f', 64)));
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateSharedVersionTransition(release,
            release.Version, release.BuildNumber, new string('f', 64)));
    }

    [Fact]
    public void RepairAndRefreshNeverSubstituteLatestPackageOrAnotherBootstrapper()
    {
        Directory.CreateDirectory(_root);
        var release = Release(); var request = Request(release, "repair", release.Version, release.BuildNumber) with { CurrentPackageSha256 = release.Sha256 };
        var invocation = new MaintenanceInvocation("vm-shared-fixture", request.Nonce);
        SetupMaintenanceHandoff.ValidateRequest(request, invocation, Options(), Sid);
        Assert.Throws<InvalidDataException>(() => SetupMaintenanceHandoff.ValidateRequest(request with { CurrentPackageSha256 = new string('f', 64) }, invocation, Options(), Sid));
        SetupMaintenanceHandoff.ValidateReleaseSnapshot(release, release, Options());
        Assert.Throws<InvalidDataException>(() => SetupMaintenanceHandoff.ValidateReleaseSnapshot(release,
            release with { SharedBootstrapper = release.SharedBootstrapper! with { BootstrapperId = Guid.NewGuid() } }, Options()));
        Assert.Throws<InvalidDataException>(() => SetupMaintenanceHandoff.ValidateReleaseSnapshot(release,
            release with { Sha256 = new string('f', 64) }, Options()));
        Assert.Throws<InvalidDataException>(() => SetupMaintenanceHandoff.ValidateRequest(request with { UserSid = "other" }, invocation, Options(), Sid));
    }

    [Fact]
    public async Task LegacyIpcOmitsNewNullableFieldsInsteadOfBreakingStrictOldReaders()
    {
        var release = Release() with { DistributionProtocol = 2, SharedBootstrapper = null, Profile = null, MinimumBootstrapperVersion = null };
        await using var stream = new MemoryStream();
        await DesktopMaintenanceProtocol.WriteAsync(stream, release, CancellationToken.None);
        var text = Encoding.UTF8.GetString(stream.ToArray());
        Assert.DoesNotContain("sharedBootstrapper", text); Assert.DoesNotContain("minimumBootstrapperVersion", text); Assert.DoesNotContain("\"profile\"", text);
    }

    private DesktopMaintenanceRequest Request(DesktopReleaseResponse release, string action, string version, int build) => new(1,
        Guid.NewGuid().ToString("N"), new string('c', 64), action, release, _root, version, build, new string('d', 64),
        Environment.ProcessId + 1, DateTime.UtcNow.Ticks, Sid, CreatedAtUtc: DateTime.UtcNow);

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(reply(request)); }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
