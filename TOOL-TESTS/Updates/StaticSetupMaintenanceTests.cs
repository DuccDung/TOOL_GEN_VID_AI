extern alias SetupAssembly;
using System.Text.Json;
using SetupAssembly::TOOL_SETUP;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;

namespace TOOL_TESTS.Updates;

public sealed class StaticSetupMaintenanceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "setup-maintenance-" + Guid.NewGuid().ToString("N"));
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Sid = "S-1-5-21-1000-1000-1000-1001";
    private static readonly string Bundle = ApprovedStaticModelBundle.Current.BundleId;
    private static InstallerOptions Options() => new() { ServerBaseUrl = "https://api.invalid/", Channel = "Stable", ModelDeliveryMode = "StaticArchive",
        DesktopVersion = "1.0.0", DesktopBuildNumber = 7, DesktopSha256 = Hash };
    private DesktopMaintenanceRequest Request()
    {
        Directory.CreateDirectory(_root); var releaseId = Guid.NewGuid();
        var setup = new DesktopSetupArtifactResponse(releaseId, "1.0.0", 7, "Stable", "win-x64", Hash, "VideoMaker Setup.exe",
            "/api/launcher-distribution/versions/" + releaseId + "/setup/download", 100, new string('b', 64), ModelBundleId: Bundle);
        var release = new DesktopReleaseResponse(releaseId, "VideoMaker", "1.0.0", 7, "Stable", "win-x64", null, null, DateTime.UtcNow,
            "app.zip", "https://103.179.190.182:8443/apps/videomaker/1.0.0-7/app.zip", 100, Hash,
            SourceKind: DesktopArtifactSourceKinds.ExternalVps, InstallMode: DesktopInstallModes.StaticArchive,
            DistributionProtocol: 2, SetupArtifact: setup, ModelBundleId: Bundle);
        return new(1, Guid.NewGuid().ToString("N"), new string('c', 64), "update", release, _root,
            "1.0.0", 6, new string('d', 64), Environment.ProcessId + 1, DateTime.UtcNow.Ticks, Sid, CreatedAtUtc: DateTime.UtcNow);
    }

    [Fact]
    public void MaintenanceArgumentsAreBoundedAndCannotAcceptArbitraryExecutableOrTargetArguments()
    {
        var pipe = "vm-maintenance-" + Guid.NewGuid().ToString("N"); var nonce = new string('a', 64);
        Assert.Equal(new MaintenanceInvocation(pipe, nonce), SetupMaintenanceHandoff.ParseArguments(["--maintenance-pipe", pipe, "--maintenance-nonce", nonce]));
        foreach (var invalid in new[] { new[] { "--target", _root, "--maintenance-nonce", nonce },
            new[] { "--maintenance-pipe", "../outside", "--maintenance-nonce", nonce },
            new[] { "--maintenance-pipe", pipe, "--maintenance-nonce", "short" },
            new[] { "--maintenance-pipe", pipe, "--maintenance-pipe", pipe } })
            Assert.Throws<InvalidDataException>(() => SetupMaintenanceHandoff.ParseArguments(invalid));
    }

    [Fact]
    public void RequestRequiresSameUserNonceExactSetupAndFreshProtocolTwoSnapshot()
    {
        var request = Request(); var invocation = new MaintenanceInvocation("vm-maintenance-test", request.Nonce);
        SetupMaintenanceHandoff.ValidateRequest(request, invocation, Options(), Sid);
        foreach (var invalid in new[] { request with { UserSid = "other" }, request with { Nonce = new string('e', 64) },
            request with { CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2) }, request with { Action = "anything" },
            request with { Release = request.Release with { SetupArtifact = null } },
            request with { Release = request.Release with { SetupArtifact = request.Release.SetupArtifact! with { PackageSha256 = new string('e', 64) } } } })
            Assert.Throws<InvalidDataException>(() => SetupMaintenanceHandoff.ValidateRequest(invalid, invocation, Options(), Sid));
    }

    [Fact]
    public void RepairCannotFallBackToTheLatestOrAnotherPackageForTheSameVersion()
    {
        var update = Request(); var request = update with { Action = "repair", CurrentVersion = "1.0.0", CurrentBuildNumber = 7, CurrentPackageSha256 = Hash };
        var invocation = new MaintenanceInvocation("vm-maintenance-test", request.Nonce);
        SetupMaintenanceHandoff.ValidateRequest(request, invocation, Options(), Sid);
        Assert.Throws<InvalidDataException>(() => SetupMaintenanceHandoff.ValidateRequest(request with { CurrentBuildNumber = 6 }, invocation, Options(), Sid));
        Assert.Throws<InvalidDataException>(() => SetupMaintenanceHandoff.ValidateRequest(request with { CurrentPackageSha256 = new string('f', 64) }, invocation, Options(), Sid));
    }

    [Fact]
    public void MetadataRefreshCannotReplaceTheRequestedPackageOrExactSetupBeforeAck()
    {
        var request = Request();
        SetupMaintenanceHandoff.ValidateReleaseSnapshot(request.Release, request.Release, Options());
        Assert.Throws<InvalidDataException>(() => SetupMaintenanceHandoff.ValidateReleaseSnapshot(request.Release,
            request.Release with { DownloadUrl = "https://103.179.190.182:8443/models/another-app.zip" }, Options()));
        Assert.Throws<InvalidDataException>(() => SetupMaintenanceHandoff.ValidateReleaseSnapshot(request.Release,
            request.Release with { SetupArtifact = request.Release.SetupArtifact! with { Sha256 = new string('f', 64) } }, Options()));
    }

    [Fact]
    public void OwnershipAllowsRepairWithoutReadyModelReceiptButRejectsAnotherCommit()
    {
        Directory.CreateDirectory(_root); var metadata = Path.Combine(_root, "isolated-metadata"); var operation = Guid.NewGuid().ToString("N");
        Write(Path.Combine(_root, "static-setup-receipt.json"), new { schemaVersion = 1, bundleId = Bundle, targetRoot = _root,
            version = "1.0.0", buildNumber = 7, packageSha256 = Hash, operationId = operation, completedAtUtc = DateTime.UtcNow });
        var journalPath = Path.Combine(metadata, "installer-transactions", StaticSetupTransaction.TargetKey(_root), "transaction.json");
        Write(journalPath, new { schemaVersion = 1, operationId = operation, targetRoot = _root, releaseSha256 = Hash,
            state = "Committed", changes = Array.Empty<object>() });
        // No executable, model runtime or READY receipt exists: standalone ownership validation
        // remains usable for repairing an application that cannot start.
        LauncherInstallerService.ValidateExistingInstallation(_root);
        SetupMaintenanceHandoff.ValidateOwnership(_root, "1.0.0", 7, Hash, metadata);
        Write(journalPath, new { schemaVersion = 1, operationId = Guid.NewGuid().ToString("N"), targetRoot = _root, releaseSha256 = Hash,
            state = "Committed", changes = Array.Empty<object>() });
        Assert.Throws<InvalidDataException>(() => SetupMaintenanceHandoff.ValidateOwnership(_root, "1.0.0", 7, Hash, metadata));
    }

    [Fact]
    public void StandaloneSetupDoesNotAdoptLegacyOrUnmanagedDataAsAnOwnedRepairTarget()
    {
        Directory.CreateDirectory(_root); var legacy = Path.Combine(_root, "TOOL-LOCAL.exe"); File.WriteAllText(legacy, "legacy untouched");
        Assert.Throws<InvalidOperationException>(() => LauncherInstallerService.ValidateExistingInstallation(_root));
        Assert.Equal("legacy untouched", File.ReadAllText(legacy));
        File.Delete(legacy); var media = Path.Combine(_root, "user.mp4"); File.WriteAllText(media, "user untouched");
        Assert.Throws<InvalidOperationException>(() => LauncherInstallerService.ValidateExistingInstallation(_root));
        Assert.Equal("user untouched", File.ReadAllText(media));
    }

    private static void Write(string path, object value)
    { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))); }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
