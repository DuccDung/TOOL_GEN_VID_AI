using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using TOOL_LOCAL.Updates;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_TESTS.Updates;

public sealed class NewStandaloneSetupDownloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-standalone-exe-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Payload = [77, 90, .. Enumerable.Range(0, 2048).Select(x => (byte)x)];
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
    private static DesktopStandaloneReleaseResponse Release(string? hash = null)
    {
        hash ??= Hash(Payload);
        var url = DesktopArtifactDownloadPolicy.ModelHostPackagePrefix + "Setup-1.3.0-win-x64-" + hash + ".exe";
        var setup = DesktopStandaloneCatalogPolicy.ParseSetupUrl(new Uri("https://api.example.test/"), url);
        return StandaloneDesktopDistributionTests.Release("9.0.0") with { Setup = setup };
    }

    [Theory]
    [InlineData("html")]
    [InlineData("encoding")]
    [InlineData("redirect")]
    [InlineData("oversize")]
    [InlineData("empty-length")]
    public async Task Unsafe_download_headers_fail_before_writing_or_promoting_a_Setup(string mutation)
    {
        Directory.CreateDirectory(_root); var destination = Path.Combine(_root, "Setup.exe"); var release = Release();
        var calls = 0;
        using var handler = new Handler(request =>
        {
            calls++; Assert.Equal(release.Setup!.DownloadUrl, request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
            var response = Response(Payload);
            if (mutation == "html") response.Content.Headers.ContentType = new("text/html");
            if (mutation == "encoding") response.Content.Headers.ContentEncoding.Add("gzip");
            if (mutation == "redirect") { response.StatusCode = HttpStatusCode.Found; response.Headers.Location = new("https://outside.invalid/Setup.exe"); }
            if (mutation == "oversize") response.Content.Headers.ContentLength = DesktopArtifactDownloadPolicy.MaximumSetupArtifactBytes + 1;
            if (mutation == "empty-length") response.Content.Headers.ContentLength = 0;
            return response;
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => Download(release, destination, handler));
        Assert.Equal(1, calls); Assert.False(File.Exists(destination)); Assert.False(File.Exists(destination + ".part"));
    }

    [Fact]
    public async Task A_truncated_response_never_promotes_the_partial_executable()
    {
        Directory.CreateDirectory(_root); var destination = Path.Combine(_root, "Setup.exe"); var release = Release();
        using var handler = new Handler(_ => { var response = Response(Payload[..128]); response.Content.Headers.ContentLength = Payload.Length; return response; });
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Download(release, destination, handler));
        Assert.Contains("checksum", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(destination)); Assert.Equal(128, new FileInfo(destination + ".part").Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Declared_digest_is_verified_before_attempting_to_read_PE_metadata_for_known_and_unknown_lengths(bool unknownLength)
    {
        Directory.CreateDirectory(_root); var destination = Path.Combine(_root, "Setup.exe"); var release = Release(new string('f', 64));
        using var handler = new Handler(_ => Response(Payload, unknownLength));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Download(release, destination, handler));
        Assert.Equal("Setup checksum does not match its declared filename.", error.Message);
        Assert.False(File.Exists(destination)); Assert.Equal(Payload, await File.ReadAllBytesAsync(destination + ".part"));
    }

    [Fact]
    public async Task An_unknown_response_length_with_valid_bytes_and_digest_reaches_native_binding_validation_but_cannot_promote_an_unbound_PE()
    {
        Directory.CreateDirectory(_root); var destination = Path.Combine(_root, "Setup.exe"); var release = Release();
        using var handler = new Handler(_ => Response(Payload, unknownLength: true));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => Download(release, destination, handler));
        Assert.Equal("Setup thiếu metadata API/profile đóng trong binary.", error.Message);
        Assert.False(File.Exists(destination)); Assert.Equal(Payload, await File.ReadAllBytesAsync(destination + ".part"));
    }

    [Fact]
    public async Task Cancellation_during_body_transfer_leaves_no_promoted_or_executed_Setup()
    {
        Directory.CreateDirectory(_root); var destination = Path.Combine(_root, "Setup.exe"); var release = Release();
        using var cancellation = new CancellationTokenSource();
        using var handler = new Handler(_ => new(HttpStatusCode.OK) { Content = new CancelContent(Payload, cancellation) });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Download(release, destination, handler, cancellation.Token));
        Assert.False(File.Exists(destination)); Assert.Equal(128, new FileInfo(destination + ".part").Length);
    }

    [Fact]
    public async Task Caller_cannot_swap_the_Setup_pointer_behind_the_validated_release_before_network()
    {
        Directory.CreateDirectory(_root); var destination = Path.Combine(_root, "Setup.exe"); var release = Release(); var calls = 0;
        using var handler = new Handler(_ => { calls++; return Response(Payload); });
        var changed = release.Setup! with { Sha256 = new string('f', 64) };
        await Assert.ThrowsAsync<InvalidDataException>(() => DesktopInstalledMaintenanceService.DownloadStandaloneSetupAsync(
            new Uri(release.Setup!.DownloadUrl), changed, release, destination, null, default, handler));
        Assert.Equal(0, calls); Assert.Empty(Directory.GetFiles(_root));
    }

    [Fact]
    public void A_real_older_Setup_PE_cannot_impersonate_a_new_Setup_merely_by_renaming_its_hash_bound_filename()
    {
        Directory.CreateDirectory(_root); var path = Path.Combine(_root, "Setup.exe");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "DistributionResources", "DistributionResourcesA.exe"), path);
        var release = Release(Hash(File.ReadAllBytes(path)));
        DesktopStandaloneCatalogPolicy.ValidateRelease(release);
        Assert.Throws<InvalidDataException>(() => DesktopInstalledMaintenanceService.ValidateStandaloneSetupBinding(path, release.Setup!, release));
    }

    private static Task Download(DesktopStandaloneReleaseResponse release, string destination, HttpMessageHandler handler, CancellationToken token = default) =>
        DesktopInstalledMaintenanceService.DownloadStandaloneSetupAsync(new Uri(release.Setup!.DownloadUrl), release.Setup,
            release, destination, null, token, handler);
    private static HttpResponseMessage Response(byte[] bytes, bool unknownLength = false)
    {
        HttpContent content = unknownLength ? new UnknownLengthContent(bytes) : new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new(HttpStatusCode.OK) { Content = content };
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(response(request)); }
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }
    private sealed class CancelContent : HttpContent
    {
        private readonly byte[] _bytes; private readonly CancellationTokenSource _cancellation;
        public CancelContent(byte[] bytes, CancellationTokenSource cancellation)
        { _bytes = bytes; _cancellation = cancellation; Headers.ContentType = new("application/octet-stream"); }
        protected override bool TryComputeLength(out long length) { length = _bytes.Length; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();
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
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
