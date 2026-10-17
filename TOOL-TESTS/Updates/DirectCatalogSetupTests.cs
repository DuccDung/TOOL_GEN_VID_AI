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

public sealed class DirectCatalogSetupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "direct-catalog-setup-" + Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly byte[] Payload = [80, 75, 3, 4, .. Enumerable.Range(0, 2048).Select(i => (byte)i)];
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static InstallerOptions Options(string version = "1.2.0") => new() { ServerBaseUrl = "https://api.invalid/", Profile = "Production",
        Channel = "Stable", ModelDeliveryMode = "StaticArchive", SetupMode = "SharedBootstrapper", BootstrapperVersion = version };
    internal static DesktopDirectReleaseResponse Release(string version = "1.2.0", int build = 3)
    {
        var hash = Hash(Payload); var file = DesktopDirectCatalogPolicy.FileName(version, build, hash);
        var setup = new DesktopSharedBootstrapperResponse(Guid.NewGuid(), "1.2.0", "Setup.exe", "/api/launcher-distribution/bootstrappers/fixture/download",
            100, new string('b', 64), "https://api.invalid/", "Production", "Stable", "win-x64", 3,
            ApprovedStaticModelBundle.Current.BundleId, "3,4");
        return new(Guid.NewGuid(), version, build, file, DesktopArtifactDownloadPolicy.ModelHostPackagePrefix + file, hash,
            "Production", "Stable", "win-x64", ApprovedStaticModelBundle.Current.BundleId, Guid.NewGuid(), DateTime.UtcNow,
            false, "release fixture", setup);
    }

    [Fact]
    public void DirectBindingAdvertisesBothCatalogsWithoutChangingApplicationProtocol()
    {
        Assert.Equal("3,4", Options().InspectSharedBootstrapper().SupportedCatalogProtocols);
        Assert.Equal(3, Options().DistributionProtocol);
        Assert.Equal("3", Options("1.1.0").InspectSharedBootstrapper().SupportedCatalogProtocols);
        LauncherInstallerService.ValidateDirectRelease(Release(), Options());
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateDirectRelease(Release(), Options("1.1.0")));
        var json = JsonSerializer.SerializeToElement(Release(), Json);
        Assert.False(json.TryGetProperty("sizeBytes", out _));
        Assert.False(json.TryGetProperty("sha256", out _));
        Assert.True(json.TryGetProperty("expectedSha256", out _));
    }

    [Fact]
    public async Task CatalogLookupUsesOnlyTheApiAndRefreshPinsRevisionAndExpectedHash()
    {
        var release = Release(); var calls = new List<Uri>();
        using var handler = new Handler(request =>
        {
            calls.Add(request.RequestUri!);
            return JsonResponse(request.RequestUri!.AbsolutePath.EndsWith("/versions")
                ? JsonSerializer.SerializeToUtf8Bytes(new DesktopDirectReleaseListResponse([release]), Json)
                : JsonSerializer.SerializeToUtf8Bytes(release, Json));
        });
        using var service = new LauncherInstallerService(Options(), metadataHandler: handler);
        Assert.Equal(release, Assert.Single(await service.GetDirectVersionsAsync(CancellationToken.None)));
        Assert.Equal(release, await service.GetDirectReleaseAsync(release, CancellationToken.None));
        Assert.All(calls, uri => Assert.Equal("api.invalid", uri.Host));
        Assert.Contains($"bootstrapperId={release.SharedBootstrapper.BootstrapperId:D}", calls[1].Query);
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateDirectSnapshot(release,
            release with { Revision = Guid.NewGuid() }, Options()));
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateDirectSnapshot(release,
            release with { SharedBootstrapper = release.SharedBootstrapper with { BootstrapperId = Guid.NewGuid() } }, Options()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadsExactExpectedHashWithKnownOrUnknownResponseLength(bool unknownLength)
    {
        Directory.CreateDirectory(_root); var path = Path.Combine(_root, "package.zip"); var release = Release();
        using var handler = new Handler(request =>
        {
            Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
            Assert.Null(request.Headers.Range); Assert.Equal("identity", request.Headers.AcceptEncoding.Single().Value);
            return ZipResponse(Payload, unknownLength);
        });
        using var service = new LauncherInstallerService(Options(), artifactHandler: handler);
        Assert.Equal(Payload.LongLength, await service.DownloadDirectPackageAsync(release.DownloadUrl, release.ExpectedSha256,
            release.Revision.ToString("D"), path, null, CancellationToken.None));
        Assert.Equal(Payload, await File.ReadAllBytesAsync(path)); Assert.False(File.Exists(path + ".part"));
        Assert.False(File.Exists(path + ".direct-receipt.json"));
    }

    [Fact]
    public async Task ResumeRequiresTheSameStrongEtagAndHashesTheEntireCombinedFile()
    {
        var release = Release(); var path = await SeedPartialAsync(release, 128);
        using var handler = new Handler(request =>
        {
            Assert.Equal(128, request.Headers.Range!.Ranges.Single().From);
            Assert.Equal("\"same-file\"", request.Headers.IfRange!.EntityTag!.ToString());
            var response = ZipResponse(Payload[128..]); response.StatusCode = HttpStatusCode.PartialContent;
            response.Headers.ETag = new EntityTagHeaderValue("\"same-file\"");
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(128, Payload.Length - 1, Payload.Length); return response;
        });
        using var service = new LauncherInstallerService(Options(), artifactHandler: handler);
        await service.DownloadDirectPackageAsync(release.DownloadUrl, release.ExpectedSha256, release.Revision.ToString("D"), path, null, CancellationToken.None);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task FullResponseToRangeRestartsInsteadOfAppending()
    {
        var release = Release(); var path = await SeedPartialAsync(release, 128);
        using var handler = new Handler(request => { Assert.NotNull(request.Headers.Range); return ZipResponse(Payload); });
        using var service = new LauncherInstallerService(Options(), artifactHandler: handler);
        await service.DownloadDirectPackageAsync(release.DownloadUrl, release.ExpectedSha256, release.Revision.ToString("D"), path, null, CancellationToken.None);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task RevisionChangeDiscardsAnOldPartBeforeSendingAnotherRequest()
    {
        var release = Release(); var path = await SeedPartialAsync(release, 128);
        using var handler = new Handler(request => { Assert.Null(request.Headers.Range); return ZipResponse(Payload); });
        using var service = new LauncherInstallerService(Options(), artifactHandler: handler);
        await service.DownloadDirectPackageAsync(release.DownloadUrl, release.ExpectedSha256, Guid.NewGuid().ToString("D"), path, null, CancellationToken.None);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("html")]
    [InlineData("encoding")]
    [InlineData("redirect")]
    [InlineData("oversize")]
    public async Task InvalidDownloadNeverPromotesAPackage(string mutation)
    {
        Directory.CreateDirectory(_root); var path = Path.Combine(_root, "package.zip"); var release = Release();
        using var handler = new Handler(_ =>
        {
            var response = ZipResponse(Payload);
            if (mutation == "html") response.Content.Headers.ContentType = new("text/html");
            if (mutation == "encoding") response.Content.Headers.ContentEncoding.Add("gzip");
            if (mutation == "redirect") { response.StatusCode = HttpStatusCode.Found; response.Headers.Location = new Uri("https://other.invalid/"); }
            if (mutation == "oversize") response.Content.Headers.ContentLength = DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes + 1;
            return response;
        });
        using var service = new LauncherInstallerService(Options(), artifactHandler: handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadDirectPackageAsync(release.DownloadUrl,
            mutation == "hash" ? new string('c', 64) : release.ExpectedSha256, release.Revision.ToString("D"), path, null, CancellationToken.None));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ChangedEtagOnPartialResponseIsRejectedWithoutPromotion()
    {
        var release = Release(); var path = await SeedPartialAsync(release, 128);
        using var handler = new Handler(_ =>
        {
            var response = ZipResponse(Payload[128..]); response.StatusCode = HttpStatusCode.PartialContent;
            response.Headers.ETag = new("\"changed-file\"");
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(128, Payload.Length - 1, Payload.Length); return response;
        });
        using var service = new LauncherInstallerService(Options(), artifactHandler: handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadDirectPackageAsync(release.DownloadUrl, release.ExpectedSha256,
            release.Revision.ToString("D"), path, null, CancellationToken.None));
        Assert.False(File.Exists(path)); Assert.Equal(128, new FileInfo(path + ".part").Length);
    }

    [Fact]
    public async Task ARangeNotSatisfiableResponseRestartsOnceWithAFullRequest()
    {
        var release = Release(); var path = await SeedPartialAsync(release, 128); var calls = 0;
        using var handler = new Handler(request =>
        {
            calls++;
            if (calls == 1) { Assert.NotNull(request.Headers.Range); return new(HttpStatusCode.RequestedRangeNotSatisfiable); }
            Assert.Null(request.Headers.Range); return ZipResponse(Payload);
        });
        using var service = new LauncherInstallerService(Options(), artifactHandler: handler);
        await service.DownloadDirectPackageAsync(release.DownloadUrl, release.ExpectedSha256, release.Revision.ToString("D"), path, null, CancellationToken.None);
        Assert.Equal(2, calls); Assert.Equal(Payload, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task CancellationKeepsTheOwnedPartialCacheAndDoesNotPromoteAnApplication()
    {
        Directory.CreateDirectory(_root); var path = Path.Combine(_root, "package.zip"); var release = Release();
        using var cancellation = new CancellationTokenSource();
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new CancelContent(Payload, cancellation),
            Headers = { ETag = new EntityTagHeaderValue("\"same-file\"") } });
        using var service = new LauncherInstallerService(Options(), artifactHandler: handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadDirectPackageAsync(release.DownloadUrl,
            release.ExpectedSha256, release.Revision.ToString("D"), path, null, cancellation.Token));
        Assert.False(File.Exists(path)); Assert.Equal(128, new FileInfo(path + ".part").Length);
        Assert.True(File.Exists(path + ".direct-receipt.json"));
    }

    [Fact]
    public async Task MaintenanceUsesTypedCatalogSchemaAndRejectsChangedOwnershipOrAck()
    {
        Directory.CreateDirectory(_root); var release = Release(); var now = DateTime.UtcNow;
        var request = new DesktopDirectMaintenanceRequest(2, Guid.NewGuid().ToString("N"), new string('d', 64), "update", release,
            _root, "1.1.0", 99, new string('e', 64), Environment.ProcessId + 1, now.Ticks, "S-1-5-21-fixture", CreatedAtUtc: now);
        DesktopMaintenanceProtocol.ValidateRequest(request, now);
        await using var stream = new MemoryStream(); await DesktopMaintenanceProtocol.WriteAsync(stream, request, CancellationToken.None);
        stream.Position = 0;
        Assert.Equal(request, Assert.IsType<DesktopDirectMaintenanceRequest>(await DesktopMaintenanceProtocol.ReadMaintenanceRequestAsync(stream, CancellationToken.None)));
        var ack = new DesktopMaintenanceAck(2, request.OperationId, request.Nonce, release.ReleaseId, release.ExpectedSha256,
            _root, 42, now.Ticks, request.UserSid, now.AddSeconds(30));
        DesktopMaintenanceProtocol.ValidateAck(request, ack, 42, now.Ticks, now);
        Assert.Throws<InvalidDataException>(() => DesktopMaintenanceProtocol.ValidateAck(request, ack with { SchemaVersion = 1 }, 42, now.Ticks, now));
        Assert.Throws<InvalidDataException>(() => DesktopMaintenanceProtocol.ValidateAck(request, ack with { PackageSha256 = new string('f', 64) }, 42, now.Ticks, now));
        Assert.Throws<InvalidDataException>(() => DesktopMaintenanceProtocol.ValidateRequest(request with { CurrentVersion = release.Version }, now));
        Assert.Throws<InvalidDataException>(() => SetupMaintenanceHandoff.ValidateDirectRequest(request,
            new MaintenanceInvocation("fixture-maintenance-pipe", new string('f', 64)), Options(), request.UserSid));
    }

    [Fact]
    public void RepairRequiresTheExactInstalledVersionBuildAndRealPackageHash()
    {
        var release = Release(); var now = DateTime.UtcNow;
        var request = new DesktopDirectMaintenanceRequest(2, Guid.NewGuid().ToString("N"), new string('d', 64), "repair", release,
            _root, release.Version, release.DeclaredBuildNumber, release.ExpectedSha256, Environment.ProcessId + 1, now.Ticks,
            "S-1-5-21-fixture", CreatedAtUtc: now);
        DesktopMaintenanceProtocol.ValidateRequest(request, now);
        Assert.Throws<InvalidDataException>(() => DesktopMaintenanceProtocol.ValidateRequest(request with { CurrentBuildNumber = 2 }, now));
        Assert.Throws<InvalidDataException>(() => DesktopMaintenanceProtocol.ValidateRequest(request with { CurrentPackageSha256 = new string('e', 64) }, now));
    }

    [Fact]
    public void StandaloneCatalogInstallRejectsDowngradeOrReplacementOfTheSameVersion()
    {
        var release = Release();
        LauncherInstallerService.ValidateDirectVersionTransition(release, release.Version, release.DeclaredBuildNumber, release.ExpectedSha256);
        LauncherInstallerService.ValidateDirectVersionTransition(release, "1.1.9", 999, new string('a', 64));
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateDirectVersionTransition(release, "1.3.0", 1, new string('a', 64)));
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateDirectVersionTransition(release, release.Version,
            release.DeclaredBuildNumber - 1, new string('a', 64)));
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateDirectVersionTransition(release, release.Version,
            release.DeclaredBuildNumber, new string('a', 64)));
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("changed-revision")]
    [InlineData("wrong-operation")]
    public async Task FinalApprovalFailureLeavesCommitClosedAndTheTransactionCanRestorePreviousFiles(string failure)
    {
        var release = Release(); var target = Path.Combine(_root, "target"); Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "app.dll"), "previous-application");
        var transaction = new StaticSetupTransaction(Path.Combine(_root, "transaction"), target);
        Directory.CreateDirectory(transaction.StageRoot);
        await File.WriteAllTextAsync(Path.Combine(transaction.StageRoot, "app.dll"), "candidate-application");
        var operation = Guid.NewGuid().ToString("N"); transaction.Begin(release.ExpectedSha256, operation);
        transaction.Snapshot(["app.dll"], ["app.dll"], CancellationToken.None); transaction.Apply(null, CancellationToken.None);
        Assert.Equal("candidate-application", await File.ReadAllTextAsync(Path.Combine(target, "app.dll")));
        using var handler = new Handler(_ => failure == "revoked" ? new(HttpStatusCode.NotFound)
            : JsonResponse(JsonSerializer.SerializeToUtf8Bytes(failure == "changed-revision" ? release with { Revision = Guid.NewGuid() } : release, Json)));
        using var service = new LauncherInstallerService(Options(), metadataHandler: handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RevalidateDirectCommitAsync(release, transaction,
            failure == "wrong-operation" ? Guid.NewGuid().ToString("N") : operation, CancellationToken.None));
        Assert.Equal("Probing", transaction.Current!.State); // No successful commit/READY was recorded.
        transaction.Recover();
        Assert.Equal("previous-application", await File.ReadAllTextAsync(Path.Combine(target, "app.dll")));
        Assert.Equal("RolledBack", transaction.Current!.State);
    }

    private async Task<string> SeedPartialAsync(DesktopDirectReleaseResponse release, int count)
    {
        Directory.CreateDirectory(_root); var path = Path.Combine(_root, "package.zip");
        await File.WriteAllBytesAsync(path + ".part", Payload[..count]);
        await File.WriteAllTextAsync(path + ".direct-receipt.json", JsonSerializer.Serialize(new { url = release.DownloadUrl,
            expectedSha256 = release.ExpectedSha256, catalogRevision = release.Revision.ToString("D"), totalBytes = Payload.LongLength, eTag = "\"same-file\"" }, Json));
        return path;
    }
    private static HttpResponseMessage JsonResponse(byte[] bytes) => new(HttpStatusCode.OK)
        { Content = new ByteArrayContent(bytes) { Headers = { ContentType = new("application/json") } } };
    private static HttpResponseMessage ZipResponse(byte[] bytes, bool unknownLength = false)
    {
        HttpContent content = unknownLength ? new UnknownLengthContent(bytes) : new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return new(HttpStatusCode.OK) { Content = content };
    }
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }
    private sealed class CancelContent : HttpContent
    {
        private readonly byte[] _bytes; private readonly CancellationTokenSource _cancellation;
        public CancelContent(byte[] bytes, CancellationTokenSource cancellation)
        { _bytes = bytes; _cancellation = cancellation; Headers.ContentType = new("application/zip"); }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();
        protected override bool TryComputeLength(out long length) { length = _bytes.LongLength; return true; }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new CancelAfterChunk(_bytes, _cancellation));
    }
    private sealed class CancelAfterChunk(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes, writable: false)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (Position > 0) { cancellation.Cancel(); return ValueTask.FromCanceled<int>(cancellation.Token); }
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 128)], token);
        }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(reply(request)); }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}
