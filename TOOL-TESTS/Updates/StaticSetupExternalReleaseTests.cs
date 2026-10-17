extern alias SetupAssembly;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using SetupAssembly::TOOL_SETUP;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_TESTS.Updates;

public sealed class StaticSetupExternalReleaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "setup-external-" + Guid.NewGuid().ToString("N"));
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static InstallerOptions Options() => new() { ServerBaseUrl = "https://api.invalid/", Channel = "Stable", ModelDeliveryMode = "StaticArchive",
        DesktopVersion = "1.0.0", DesktopBuildNumber = 7, DesktopSha256 = Hash };
    private static DesktopReleaseResponse Release() => new(Guid.NewGuid(), "VideoMaker", "1.0.0", 7, "Stable", "win-x64", null, null,
        DateTime.UtcNow, "app.zip", DesktopArtifactDownloadPolicy.ApplicationPrefix + "1.0.0-7/app.zip", 100, Hash,
        SourceKind: DesktopArtifactSourceKinds.ExternalVps, InstallMode: DesktopInstallModes.StaticArchive,
        DistributionProtocol: DesktopDistributionProtocol.Current, ModelBundleId: ApprovedStaticModelBundle.Current.BundleId);

    [Fact]
    public void ExternalOriginIsIndependentFromApiButDoesNotChangeStaticBinding()
    {
        var release = Release(); var options = Options();
        LauncherInstallerService.ValidateStaticRelease(release, options);
        foreach (var invalid in new[] { release with { BuildNumber = 8 }, release with { Sha256 = new string('b', 64) },
            release with { DistributionProtocol = 1 }, release with { InstallMode = "Legacy" }, release with { ModelBundleId = "other" },
            release with { SizeBytes = DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes + 1 },
            release with { DownloadUrl = "https://103.179.190.182:443/apps/videomaker/app.zip" },
            release with { SourceKind = "Internal" } })
            Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateStaticRelease(invalid, options));
    }

    [Fact]
    public async Task VersionsAdvertisesProtocolTwoAndCannotSelectALegacyOrDifferentPinnedRelease()
    {
        var correct = Release(); var requests = new List<Uri>();
        using var handler = new Handler(request =>
        {
            requests.Add(request.RequestUri!);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new DesktopReleaseListResponse([
                correct, correct with { ReleaseId = Guid.NewGuid(), InstallMode = "Legacy" }, correct with { ReleaseId = Guid.NewGuid(), BuildNumber = 8 }
            ]), new JsonSerializerOptions(JsonSerializerDefaults.Web))) };
        });
        using var service = new LauncherInstallerService(Options(), metadataHandler: handler);
        Assert.Equal(correct, Assert.Single(await service.GetVersionsAsync(CancellationToken.None)));
        Assert.Contains("distributionProtocol=2", Assert.Single(requests).Query);
        Assert.Equal("api.invalid", requests[0].Host);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("application/json")]
    [InlineData(null)]
    public void PackageMimeRejectsErrorDocuments(string? mime) =>
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidatePackageMime(mime));

    [Fact]
    public void BoundMetadataInspectionDoesNotNeedNetworkOrRuntime()
    {
        var binding = Options().InspectBinding();
        Assert.Equal(2, binding.DistributionProtocol); Assert.Equal("StaticArchive", binding.InstallMode);
        Assert.Equal(Hash, binding.PackageSha256); Assert.Equal(ApprovedStaticModelBundle.Current.BundleId, binding.ModelBundleId);
        Assert.Equal("https://api.invalid/", binding.ServerBaseUrl);
    }

    [Fact]
    public void RestoringOldCommitBeforeMutationKeepsItBoundAndPreservesAlreadyVerifiedStage()
    {
        var target = Path.Combine(_root, "installed"); var transaction = new StaticSetupTransaction(Path.Combine(_root, "transaction"), target);
        Directory.CreateDirectory(transaction.StageRoot); File.WriteAllText(Path.Combine(transaction.StageRoot, "app.dll"), "old");
        transaction.Begin(Hash); transaction.Snapshot(["app.dll"], [], CancellationToken.None); transaction.Apply(null, CancellationToken.None); transaction.Commit();
        var previous = transaction.Current!;
        File.WriteAllText(transaction.PackagePath, "verified package"); File.WriteAllText(Path.Combine(transaction.StageRoot, "app.dll"), "new");
        var next = Guid.NewGuid().ToString("N"); transaction.Begin(new string('b', 64), next, preserveStagedPayload: true);
        Assert.Equal("verified package", File.ReadAllText(transaction.PackagePath));
        Assert.Equal("new", File.ReadAllText(Path.Combine(transaction.StageRoot, "app.dll")));
        transaction.RestoreCommittedBeforeMutation(previous);
        var restored = new StaticSetupTransaction(transaction.Root, target).Current!;
        Assert.Equal(previous.OperationId, restored.OperationId); Assert.Equal(previous.ReleaseSha256, restored.ReleaseSha256);
        Assert.Equal("Committed", restored.State);
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "app.dll")));
        transaction.Begin(new string('b', 64), next, preserveStagedPayload: true);
        transaction.Snapshot(["app.dll"], ["app.dll"], CancellationToken.None); transaction.Apply(null, CancellationToken.None);
        Assert.Throws<InvalidOperationException>(() => transaction.RestoreCommittedBeforeMutation(previous));
    }

    [Fact]
    public async Task ExternalDownloadResumesExactStrongEtagWithoutApiCredentials()
    {
        Directory.CreateDirectory(_root); var path = Path.Combine(_root, "package.zip");
        var bytes = Enumerable.Range(0, 8192).Select(i => (byte)i).ToArray(); bytes[0] = (byte)'P'; bytes[1] = (byte)'K'; bytes[2] = 3; bytes[3] = 4;
        var release = Release() with { SizeBytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() };
        await File.WriteAllBytesAsync(path + ".part", bytes[..128]);
        await File.WriteAllTextAsync(path + ".receipt.json", JsonSerializer.Serialize(new { url = release.DownloadUrl, sha256 = release.Sha256,
            sizeBytes = release.SizeBytes, eTag = "\"fixture-etag\"" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var calls = 0;
        using var handler = new Handler(request =>
        {
            calls++; Assert.Equal(release.DownloadUrl, request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
            Assert.Equal(128, request.Headers.Range!.Ranges.Single().From);
            Assert.Equal("\"fixture-etag\"", request.Headers.IfRange!.EntityTag!.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes[128..]) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(128, bytes.Length - 1, bytes.Length);
            response.Headers.ETag = new EntityTagHeaderValue("\"fixture-etag\""); return response;
        });
        using var service = new LauncherInstallerService(Options(), artifactHandler: handler);
        await service.DownloadStaticPackageAsync(release, path, null, CancellationToken.None);
        Assert.Equal(1, calls); Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".part")); Assert.False(File.Exists(path + ".receipt.json"));
    }

    [Fact]
    public async Task RedirectIsNotFollowedOrPromoted()
    {
        Directory.CreateDirectory(_root); var path = Path.Combine(_root, "package.zip"); var calls = 0;
        using var handler = new Handler(_ => { calls++; return new(HttpStatusCode.Found) { Headers = { Location = new Uri("https://outside.invalid/app.zip") } }; });
        using var service = new LauncherInstallerService(Options(), artifactHandler: handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadStaticPackageAsync(Release(), path, null, CancellationToken.None));
        Assert.Equal(1, calls); Assert.False(File.Exists(path));
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(reply(request)); }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
