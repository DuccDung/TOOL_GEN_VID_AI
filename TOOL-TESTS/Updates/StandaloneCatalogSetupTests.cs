extern alias SetupAssembly;
using System.Net;
using System.Net.Http.Json;
using SetupAssembly::TOOL_SETUP;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_TESTS.Updates;

public sealed class StandaloneCatalogSetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-standalone-setup-" + Guid.NewGuid().ToString("N"));
    private static InstallerOptions Options(string version = "1.3.0") => new()
    {
        ServerBaseUrl = "https://api.example.test/", Profile = "Production", Channel = "Stable",
        ModelDeliveryMode = "StaticArchive", SetupMode = "SharedBootstrapper", BootstrapperVersion = version
    };

    [Fact]
    public void Setup_1_3_adds_catalog_five_without_changing_the_binary_distribution_protocol_or_upgrading_older_Setup()
    {
        Assert.Equal("3,4,5", Options().SupportedCatalogProtocols); Assert.Equal(3, Options().DistributionProtocol);
        Assert.Equal("3,4", Options("1.2.0").SupportedCatalogProtocols);
        Assert.Equal("3", Options("1.1.0").SupportedCatalogProtocols);
        var release = StandaloneDesktopDistributionTests.Release();
        LauncherInstallerService.ValidateStandaloneRelease(release, Options());
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateStandaloneRelease(release, Options("1.2.0")));
    }

    [Fact]
    public async Task A_running_Setup_looks_up_catalog_and_refreshes_the_exact_snapshot_without_a_bootstrapper_ID()
    {
        var release = StandaloneDesktopDistributionTests.Release(); var calls = new List<Uri>();
        using var handler = new Handler(request =>
        {
            calls.Add(request.RequestUri!); Assert.Null(request.Headers.Authorization);
            return request.RequestUri!.AbsolutePath.EndsWith("/versions", StringComparison.Ordinal)
                ? new(HttpStatusCode.OK) { Content = JsonContent.Create(new DesktopStandaloneReleaseListResponse([release])) }
                : new(HttpStatusCode.OK) { Content = JsonContent.Create(release) };
        });
        using var service = new LauncherInstallerService(Options(), metadataHandler: handler);
        Assert.Equal(release, Assert.Single(await service.GetStandaloneVersionsAsync(default)));
        Assert.Equal(release, await service.GetStandaloneReleaseAsync(release, default));
        Assert.Equal(2, calls.Count);
        Assert.All(calls, uri => { Assert.Equal("api.example.test", uri.Host); Assert.DoesNotContain("bootstrapperId", uri.Query); });
        Assert.Contains("setupVersion=1.3.0", calls[0].Query); Assert.Contains("catalogProtocol=5", calls[0].Query);
        Assert.EndsWith("/" + release.ReleaseId.ToString("D"), calls[1].AbsolutePath);
    }

    [Fact]
    public async Task Setup_1_2_does_not_query_or_accept_catalog_five()
    {
        var calls = 0;
        using var handler = new Handler(_ => { calls++; throw new InvalidOperationException("An older Setup must not query catalog five."); });
        using var service = new LauncherInstallerService(Options("1.2.0"), metadataHandler: handler);
        Assert.Empty(await service.GetStandaloneVersionsAsync(default)); Assert.Equal(0, calls);
    }

    [Fact]
    public void Exact_snapshot_validation_pins_every_identity_field_including_the_optional_Setup_pointer()
    {
        var release = StandaloneDesktopDistributionTests.Release();
        var setup = DesktopStandaloneCatalogPolicy.ParseSetupUrl(new Uri(Options().ServerBaseUrl),
            DesktopArtifactDownloadPolicy.ModelHostPackagePrefix + "Setup-1.3.0-win-x64-" + new string('b', 64) + ".exe");
        foreach (var changed in new[] { release with { Revision = Guid.NewGuid() }, release with { ReleaseId = Guid.NewGuid() },
            release with { IsMandatory = true }, release with { Setup = setup } })
            Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateStandaloneSnapshot(release, changed, Options()));
    }

    [Fact]
    public void Exact_repair_is_allowed_but_downgrade_or_same_version_replacement_is_rejected()
    {
        var release = StandaloneDesktopDistributionTests.Release();
        LauncherInstallerService.ValidateStandaloneVersionTransition(release, release.Version, release.DeclaredBuildNumber, release.ExpectedSha256);
        LauncherInstallerService.ValidateStandaloneVersionTransition(release, "1.1.9", 999, new string('c', 64));
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateStandaloneVersionTransition(release, "1.3.0", 1, new string('c', 64)));
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateStandaloneVersionTransition(release, release.Version, 8, release.ExpectedSha256));
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateStandaloneVersionTransition(release, release.Version, release.DeclaredBuildNumber, new string('c', 64)));
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("revision")]
    [InlineData("operation")]
    public async Task Failed_final_catalog_approval_never_commits_READY_and_can_restore_the_previous_application(string failure)
    {
        var release = StandaloneDesktopDistributionTests.Release(); var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target); await File.WriteAllTextAsync(Path.Combine(target, "app.dll"), "previous application");
        var transaction = new StaticSetupTransaction(Path.Combine(_root, "transaction"), target);
        Directory.CreateDirectory(transaction.StageRoot); await File.WriteAllTextAsync(Path.Combine(transaction.StageRoot, "app.dll"), "candidate");
        var operation = Guid.NewGuid().ToString("N"); transaction.Begin(release.ExpectedSha256, operation);
        transaction.Snapshot(["app.dll"], ["app.dll"], default); transaction.Apply(null, default);
        Assert.Equal("candidate", await File.ReadAllTextAsync(Path.Combine(target, "app.dll")));
        using var handler = new Handler(_ => failure == "revoked" ? new(HttpStatusCode.NotFound)
            : new(HttpStatusCode.OK) { Content = JsonContent.Create(failure == "revision" ? release with { Revision = Guid.NewGuid() } : release) });
        using var service = new LauncherInstallerService(Options(), metadataHandler: handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RevalidateStandaloneCommitAsync(release, transaction,
            failure == "operation" ? Guid.NewGuid().ToString("N") : operation, default));
        Assert.Equal("Probing", transaction.Current!.State);
        transaction.Recover(); Assert.Equal("RolledBack", transaction.Current!.State);
        Assert.Equal("previous application", await File.ReadAllTextAsync(Path.Combine(target, "app.dll")));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(reply(request)); }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
