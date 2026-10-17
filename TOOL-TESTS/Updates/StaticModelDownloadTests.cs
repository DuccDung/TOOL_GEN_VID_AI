using System.Net;
using System.Net.Http.Headers;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.ModelDistribution;
using TOOL_SHARED.Distribution.StaticModels;

namespace TOOL_TESTS.Updates;

public sealed class StaticModelDownloadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-static-dl-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Interrupted_transfer_uses_strong_etag_range_and_checks_the_whole_archive()
    {
        var fixture = StaticArchiveFixture.Create();
        using var handler = fixture.Handler((request, index) => index == 0 ? Interrupted(fixture, request) : Remainder(fixture, request));
        using var http = new HttpClient(handler);
        using var store = fixture.Store(http);
        var result = await store.EnsurePayloadAsync(_root, null, CancellationToken.None);
        Assert.Equal([null, 5L], handler.Offsets);
        Assert.Equal([null, "\"fixture-v1\""], handler.IfRanges);
        Assert.Equal(fixture.BytesFor(ApprovedModelArtifactPins.QwenArtifactId), await File.ReadAllBytesAsync(result.QwenModelPath));
        Assert.False(File.Exists(fixture.ArchivePath(_root) + ".part"));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(416)]
    public async Task Server_ignoring_range_or_rejecting_offset_restarts_from_zero(int status)
    {
        var fixture = StaticArchiveFixture.Create();
        using var handler = fixture.Handler((request, index) => index switch
        {
            0 => Interrupted(fixture, request),
            1 when status == 416 => StaticArchiveFixture.Response(request, HttpStatusCode.RequestedRangeNotSatisfiable, []),
            _ => StaticArchiveFixture.Response(request, HttpStatusCode.OK, fixture.Archive)
        });
        using var http = new HttpClient(handler);
        using var store = fixture.Store(http);
        await store.EnsurePayloadAsync(_root, null, CancellationToken.None);
        Assert.Equal(status == 416 ? [null, 5L, null] : new long?[] { null, 5 }, handler.Offsets);
        Assert.Equal(fixture.Archive, await File.ReadAllBytesAsync(fixture.ArchivePath(_root)));
    }

    [Theory]
    [InlineData("weak")]
    [InlineData("missing")]
    public async Task Unusable_etag_restarts_without_range(string etag)
    {
        var fixture = StaticArchiveFixture.Create();
        using var handler = fixture.Handler((request, index) =>
        {
            if (index > 0) return StaticArchiveFixture.Response(request, HttpStatusCode.OK, fixture.Archive);
            var response = Interrupted(fixture, request);
            response.Headers.ETag = etag == "weak" ? new("\"fixture-v1\"", isWeak: true) : null;
            return response;
        });
        using var http = new HttpClient(handler);
        using var store = fixture.Store(http);
        await store.EnsurePayloadAsync(_root, null, CancellationToken.None);
        Assert.Equal([null, null], handler.Offsets);
    }

    [Theory]
    [InlineData("offset")]
    [InlineData("total")]
    [InlineData("etag")]
    [InlineData("length")]
    [InlineData("encoding")]
    public async Task Invalid_resume_headers_discard_the_untrusted_prefix(string mutation)
    {
        var fixture = StaticArchiveFixture.Create();
        using var handler = fixture.Handler((request, index) =>
        {
            if (index == 0) return Interrupted(fixture, request);
            var response = Remainder(fixture, request);
            switch (mutation)
            {
                case "offset": response.Content.Headers.ContentRange = new(4, fixture.Archive.Length - 1, fixture.Archive.Length); break;
                case "total": response.Content.Headers.ContentRange = new(5, fixture.Archive.Length - 1, fixture.Archive.Length + 1); break;
                case "etag": response.Headers.ETag = new("\"new-version\""); break;
                case "length": response.Content.Headers.ContentLength = 1; break;
                case "encoding": response.Content.Headers.ContentEncoding.Add("gzip"); break;
            }
            return response;
        });
        using var http = new HttpClient(handler);
        using var store = fixture.Store(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsurePayloadAsync(_root, null, CancellationToken.None));
        Assert.False(File.Exists(fixture.ArchivePath(_root) + ".part"));
        Assert.False(File.Exists(fixture.ArchivePath(_root) + ".part.receipt.json"));
    }

    [Fact]
    public async Task Cancelled_operation_keeps_a_valid_prefix_for_a_new_process_to_resume()
    {
        var fixture = StaticArchiveFixture.Create();
        using var cancelled = new CancellationTokenSource();
        using (var handler = fixture.Handler((request, _) => Interrupted(fixture, request)))
        using (var http = new HttpClient(handler))
        using (var store = fixture.Store(http))
        {
            var progress = new InlineProgress(value => { if (value.Stage == StaticModelSetupStages.Downloading) cancelled.Cancel(); });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.EnsurePayloadAsync(_root, progress, cancelled.Token));
        }
        Assert.Equal(5, new FileInfo(fixture.ArchivePath(_root) + ".part").Length);
        using var nextHandler = fixture.Handler((request, _) => Remainder(fixture, request));
        using var nextHttp = new HttpClient(nextHandler);
        using var nextStore = fixture.Store(nextHttp);
        await nextStore.EnsurePayloadAsync(_root, null, CancellationToken.None);
        Assert.Equal([5L], nextHandler.Offsets);
        Assert.Equal(["\"fixture-v1\""], nextHandler.IfRanges);
    }

    [Theory]
    [InlineData("url")]
    [InlineData("hash")]
    public async Task Receipt_from_another_origin_or_release_is_not_resumed(string mutation)
    {
        var fixture = StaticArchiveFixture.Create();
        var part = fixture.ArchivePath(_root) + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(part)!);
        await File.WriteAllBytesAsync(part, fixture.Archive[..5]);
        await File.WriteAllTextAsync(part + ".receipt.json", System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            url = mutation == "url" ? "https://other.invalid/models/fixture.zip" : fixture.Descriptor.ArchiveUrl.AbsoluteUri,
            sizeBytes = fixture.Archive.LongLength,
            sha256 = mutation == "hash" ? new string('0', 64) : fixture.Descriptor.ArchiveSha256,
            eTag = "\"fixture-v1\""
        }));
        using var handler = fixture.Handler();
        using var http = new HttpClient(handler);
        using var store = fixture.Store(http);
        await store.EnsurePayloadAsync(_root, null, CancellationToken.None);
        Assert.Equal([null], handler.Offsets);
    }

    [Theory]
    [InlineData("mime")]
    [InlineData("redirect")]
    [InlineData("response-origin")]
    [InlineData("hash")]
    [InlineData("overflow")]
    [InlineData("404")]
    public async Task Unexpected_server_content_is_rejected_without_promoting_or_retrying(string mutation)
    {
        var fixture = StaticArchiveFixture.Create();
        using var handler = fixture.Handler((request, _) =>
        {
            var body = fixture.Archive.ToArray();
            if (mutation == "hash") body[body.Length / 2] ^= 1;
            if (mutation == "overflow") body = [.. body, 1];
            var response = StaticArchiveFixture.Response(request, mutation switch
            {
                "redirect" => HttpStatusCode.TemporaryRedirect,
                "404" => HttpStatusCode.NotFound,
                _ => HttpStatusCode.OK
            }, body);
            if (mutation == "mime") response.Content.Headers.ContentType = new("text/html");
            if (mutation == "redirect") response.Headers.Location = new("https://other.invalid/changed.zip");
            if (mutation == "response-origin") response.RequestMessage = new(HttpMethod.Get, "https://other.invalid/changed.zip");
            if (mutation == "overflow") response.Content.Headers.ContentLength = null;
            return response;
        });
        using var http = new HttpClient(handler);
        using var store = fixture.Store(http);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsurePayloadAsync(_root, null, CancellationToken.None));
        Assert.Equal(1, handler.Count);
        Assert.False(File.Exists(fixture.ArchivePath(_root)));
        Assert.False(File.Exists(fixture.ArchivePath(_root) + ".part"));
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Transient_status_honors_bounded_retry_after_before_retrying(int status)
    {
        var fixture = StaticArchiveFixture.Create();
        var delays = new List<TimeSpan>();
        using var handler = fixture.Handler((request, index) =>
        {
            var response = StaticArchiveFixture.Response(request, index == 0 ? (HttpStatusCode)status : HttpStatusCode.OK,
                index == 0 ? [] : fixture.Archive);
            if (index == 0) response.Headers.RetryAfter = new(TimeSpan.FromMinutes(5));
            return response;
        });
        using var http = new HttpClient(handler);
        using var store = new StaticModelArchiveStore(http, fixture.Descriptor, checkDisk: (_, _) => { },
            delayAsync: (delay, _) => { delays.Add(delay); return Task.CompletedTask; });
        await store.EnsurePayloadAsync(_root, null, CancellationToken.None);
        Assert.Equal([TimeSpan.FromSeconds(60)], delays);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task Completed_part_is_hash_checked_and_promoted_without_outbound()
    {
        var fixture = StaticArchiveFixture.Create();
        var part = fixture.ArchivePath(_root) + ".part";
        Directory.CreateDirectory(Path.GetDirectoryName(part)!);
        await File.WriteAllBytesAsync(part, fixture.Archive);
        await File.WriteAllTextAsync(part + ".receipt.json", System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1, url = fixture.Descriptor.ArchiveUrl.AbsoluteUri,
            sizeBytes = fixture.Archive.LongLength, sha256 = fixture.Descriptor.ArchiveSha256, eTag = "\"fixture-v1\""
        }));
        using var handler = fixture.Handler();
        using var http = new HttpClient(handler);
        using var store = fixture.Store(http);
        await store.EnsurePayloadAsync(_root, null, CancellationToken.None);
        Assert.Equal(0, handler.Count);
        Assert.False(File.Exists(part));
    }

    [Fact]
    public void Incompatible_desktop_or_worker_is_rejected_before_using_a_bundle()
    {
        var descriptor = ApprovedStaticModelBundle.Current;
        descriptor.ValidateDesktopCompatibility("1.0.0", 1, "1.2.1", 3);
        descriptor.ValidateDesktopCompatibility("2.0.0", 1, "1.2.1", 3);
        Assert.Throws<ModelDistributionException>(() => descriptor.ValidateDesktopCompatibility("0.9.0", 1, "1.2.1", 3));
        Assert.Throws<ModelDistributionException>(() => descriptor.ValidateDesktopCompatibility("1.0.0", 0, "1.2.1", 3));
        Assert.Throws<ModelDistributionException>(() => descriptor.ValidateDesktopCompatibility("1.0.0", 1, "1.2.0", 3));
        Assert.Throws<ModelDistributionException>(() => descriptor.ValidateDesktopCompatibility("1.0.0", 1, "1.2.1", 2));
    }

    private static HttpResponseMessage Interrupted(StaticArchiveFixture fixture, HttpRequestMessage request)
    {
        var response = StaticArchiveFixture.Response(request, HttpStatusCode.OK, fixture.Archive);
        response.Content = new StreamContent(new PrefixStream(fixture.Archive[..5]));
        response.Content.Headers.ContentType = new("application/zip");
        response.Content.Headers.ContentLength = fixture.Archive.Length;
        return response;
    }

    private static HttpResponseMessage Remainder(StaticArchiveFixture fixture, HttpRequestMessage request)
    {
        var response = StaticArchiveFixture.Response(request, HttpStatusCode.PartialContent, fixture.Archive[5..]);
        response.Content.Headers.ContentRange = new(5, fixture.Archive.Length - 1, fixture.Archive.Length);
        return response;
    }

    private sealed class InlineProgress(Action<StaticModelSetupProgress> report) : IProgress<StaticModelSetupProgress>
    { public void Report(StaticModelSetupProgress value) => report(value); }

    private sealed class PrefixStream(byte[] bytes) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_position == bytes.Length) throw new IOException("fixture connection interrupted");
            var count = Math.Min(buffer.Length, bytes.Length - _position);
            bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return ValueTask.FromResult(count);
        }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public void Dispose() => StaticArchiveFixture.DeleteTemp(_root);
}
