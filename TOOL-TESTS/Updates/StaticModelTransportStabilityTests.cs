using System.Net;
using System.Net.Http.Headers;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.StaticModels;

namespace TOOL_TESTS.Updates;

public sealed class StaticModelTransportStabilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-static-transport-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Repeated_transient_responses_stop_after_four_attempts_and_three_bounded_delays(int status)
    {
        var fixture = StaticArchiveFixture.Create();
        var delays = new List<TimeSpan>();
        using var handler = fixture.Handler((request, _) =>
        {
            var response = StaticArchiveFixture.Response(request, (HttpStatusCode)status, []);
            response.Headers.RetryAfter = new(TimeSpan.FromDays(3));
            return response;
        });
        using var http = new HttpClient(handler);
        using var store = new StaticModelArchiveStore(http, fixture.Descriptor, (_, _) => { },
            (delay, _) => { delays.Add(delay); return Task.CompletedTask; });

        await Assert.ThrowsAsync<HttpRequestException>(() => store.EnsurePayloadAsync(_root, null, CancellationToken.None));

        Assert.Equal(4, handler.Count);
        Assert.Equal(Enumerable.Repeat(TimeSpan.FromSeconds(60), 3), delays);
        AssertNoPromotedFiles(fixture);
    }

    [Fact]
    public async Task Cancellation_during_retry_after_prevents_the_next_request()
    {
        var fixture = StaticArchiveFixture.Create();
        using var cancellation = new CancellationTokenSource();
        using var handler = fixture.Handler((request, _) =>
            StaticArchiveFixture.Response(request, HttpStatusCode.ServiceUnavailable, []));
        using var http = new HttpClient(handler);
        using var store = new StaticModelArchiveStore(http, fixture.Descriptor, (_, _) => { },
            (_, token) => { cancellation.Cancel(); return Task.Delay(Timeout.InfiniteTimeSpan, token); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.EnsurePayloadAsync(_root, null, cancellation.Token));

        Assert.Equal(1, handler.Count);
        AssertNoPromotedFiles(fixture);
    }

    [Fact]
    public async Task Stalled_headers_time_out_with_bounded_retries_and_do_not_create_a_payload()
    {
        var fixture = StaticArchiveFixture.Create();
        using var handler = new AsyncHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        });
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var store = new StaticModelArchiveStore(http, fixture.Descriptor, (_, _) => { },
            (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }, TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<HttpRequestException>(() => store.EnsurePayloadAsync(_root, null, CancellationToken.None));

        Assert.Equal(4, handler.Count);
        AssertNoPromotedFiles(fixture);
    }

    [Fact]
    public async Task Stalled_body_retains_only_the_received_prefix_and_resumes_each_bounded_attempt()
    {
        var fixture = StaticArchiveFixture.Create();
        using var handler = fixture.Handler((request, _) =>
        {
            var offset = request.Headers.Range?.Ranges.Single().From ?? 0;
            var response = StaticArchiveFixture.Response(request,
                offset == 0 ? HttpStatusCode.OK : HttpStatusCode.PartialContent, []);
            response.Content = new StreamContent(new TimedReadStream(fixture.Archive[(int)offset..], 5, TimeSpan.Zero, stallAfterFirst: true));
            response.Content.Headers.ContentType = new("application/zip");
            response.Content.Headers.ContentLength = fixture.Archive.LongLength - offset;
            if (offset > 0) response.Content.Headers.ContentRange = new(offset, fixture.Archive.LongLength - 1, fixture.Archive.LongLength);
            return response;
        });
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var store = new StaticModelArchiveStore(http, fixture.Descriptor, (_, _) => { },
            (_, _) => Task.CompletedTask, TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<IOException>(() => store.EnsurePayloadAsync(_root, null, CancellationToken.None));

        Assert.Equal(new long?[] { null, 5, 10, 15 }, handler.Offsets);
        Assert.Equal(fixture.Archive[..20], await File.ReadAllBytesAsync(fixture.ArchivePath(_root) + ".part"));
        AssertNoPromotedFiles(fixture);
    }

    [Fact]
    public async Task Healthy_stream_can_outlive_the_header_timeout_while_each_read_keeps_progressing()
    {
        var fixture = StaticArchiveFixture.Create();
        var chunks = 0;
        using var handler = fixture.Handler((request, _) =>
        {
            var response = StaticArchiveFixture.Response(request, HttpStatusCode.OK, []);
            response.Content = new StreamContent(new TimedReadStream(fixture.Archive,
                (fixture.Archive.Length + 7) / 8, TimeSpan.FromMilliseconds(100)));
            response.Content.Headers.ContentType = new("application/zip");
            response.Content.Headers.ContentLength = fixture.Archive.LongLength;
            return response;
        });
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var store = new StaticModelArchiveStore(http, fixture.Descriptor, (_, _) => { },
            (_, _) => Task.CompletedTask, TimeSpan.FromMilliseconds(400));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await store.EnsurePayloadAsync(_root, new InlineProgress(value =>
        {
            if (value.Stage == StaticModelSetupStages.Downloading) chunks++;
        }), CancellationToken.None);

        Assert.True(watch.Elapsed > TimeSpan.FromMilliseconds(400));
        Assert.True(chunks >= 8);
        Assert.Equal(1, handler.Count);
        Assert.Equal(fixture.Archive, await File.ReadAllBytesAsync(fixture.ArchivePath(_root)));
        Assert.True(File.Exists(result.QwenModelPath));
    }

    [Fact]
    public async Task Repeated_416_responses_do_not_loop_forever_or_promote_a_nonexistent_archive()
    {
        var fixture = StaticArchiveFixture.Create();
        using var handler = fixture.Handler((request, _) =>
            StaticArchiveFixture.Response(request, HttpStatusCode.RequestedRangeNotSatisfiable, []));
        using var http = new HttpClient(handler);
        using var store = fixture.Store(http);

        await Assert.ThrowsAsync<HttpRequestException>(() => store.EnsurePayloadAsync(_root, null, CancellationToken.None));

        Assert.Equal(4, handler.Count);
        Assert.All(handler.Offsets, value => Assert.Null(value));
        AssertNoPromotedFiles(fixture);
    }

    [Fact]
    public async Task Expired_retry_after_date_keeps_a_positive_bounded_backoff()
    {
        var fixture = StaticArchiveFixture.Create();
        var delays = new List<TimeSpan>();
        using var handler = fixture.Handler((request, index) =>
        {
            var response = StaticArchiveFixture.Response(request, index == 0 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK,
                index == 0 ? [] : fixture.Archive);
            if (index == 0) response.Headers.RetryAfter = new(DateTimeOffset.UtcNow.AddDays(-1));
            return response;
        });
        using var http = new HttpClient(handler);
        using var store = new StaticModelArchiveStore(http, fixture.Descriptor, (_, _) => { },
            (delay, _) => { delays.Add(delay); return Task.CompletedTask; });

        await store.EnsurePayloadAsync(_root, null, CancellationToken.None);

        Assert.Equal([TimeSpan.FromMilliseconds(500)], delays);
        Assert.Equal(2, handler.Count);
    }

    [Fact]
    public async Task Running_out_of_disk_before_a_retry_keeps_the_prefix_and_prevents_the_next_outbound_request()
    {
        var fixture = StaticArchiveFixture.Create();
        var checks = 0;
        using var handler = fixture.Handler((request, _) =>
        {
            var response = StaticArchiveFixture.Response(request, HttpStatusCode.OK, []);
            response.Content = new StreamContent(new TimedReadStream(fixture.Archive, 5, TimeSpan.Zero, stallAfterFirst: true));
            response.Content.Headers.ContentType = new("application/zip");
            response.Content.Headers.ContentLength = fixture.Archive.LongLength;
            return response;
        });
        using var http = new HttpClient(handler);
        using var store = new StaticModelArchiveStore(http, fixture.Descriptor,
            (_, _) => { if (++checks == 3) throw new IOException("fixture volume full"); },
            (_, _) => Task.CompletedTask, TimeSpan.FromMilliseconds(100));

        var error = await Assert.ThrowsAsync<IOException>(() => store.EnsurePayloadAsync(_root, null, CancellationToken.None));

        Assert.Equal("fixture volume full", error.Message);
        Assert.Equal(1, handler.Count);
        Assert.Equal(fixture.Archive[..5], await File.ReadAllBytesAsync(fixture.ArchivePath(_root) + ".part"));
        AssertNoPromotedFiles(fixture);
    }

    [Fact]
    public async Task Cancelling_before_final_hash_keeps_complete_part_for_hash_verified_retry_without_network()
    {
        var fixture = StaticArchiveFixture.Create();
        using var cancellation = new CancellationTokenSource();
        using (var handler = fixture.Handler())
        using (var http = new HttpClient(handler))
        using (var store = fixture.Store(http))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.EnsurePayloadAsync(_root,
                new InlineProgress(value =>
                {
                    if (value.Stage == StaticModelSetupStages.Verifying &&
                        File.Exists(fixture.ArchivePath(_root) + ".part")) cancellation.Cancel();
                }), cancellation.Token));
            Assert.Equal(1, handler.Count);
        }
        AssertNoPromotedFiles(fixture);
        Assert.Equal(fixture.Archive, await File.ReadAllBytesAsync(fixture.ArchivePath(_root) + ".part"));

        using var retryHandler = fixture.Handler((_, _) => throw new InvalidOperationException("No outbound allowed on retry."));
        using var retryHttp = new HttpClient(retryHandler);
        using var retryStore = fixture.Store(retryHttp);
        await retryStore.EnsurePayloadAsync(_root, null, CancellationToken.None);

        Assert.Equal(0, retryHandler.Count);
        Assert.False(File.Exists(fixture.ArchivePath(_root) + ".part"));
        Assert.True(Directory.Exists(fixture.PayloadPath(_root)));
    }

    private void AssertNoPromotedFiles(StaticArchiveFixture fixture)
    {
        Assert.False(File.Exists(fixture.ArchivePath(_root)));
        Assert.False(Directory.Exists(fixture.PayloadPath(_root)));
    }

    private sealed class InlineProgress(Action<StaticModelSetupProgress> report) : IProgress<StaticModelSetupProgress>
    { public void Report(StaticModelSetupProgress value) => report(value); }

    private sealed class AsyncHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        internal int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Count++; return send(request, token); }
    }

    private sealed class TimedReadStream(byte[] bytes, int chunkSize, TimeSpan delay, bool stallAfterFirst = false) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (stallAfterFirst && _position > 0) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            if (_position == bytes.Length) return 0;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
            var count = Math.Min(Math.Min(chunkSize, buffer.Length), bytes.Length - _position);
            bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public void Dispose() => StaticArchiveFixture.DeleteTemp(_root);
}
