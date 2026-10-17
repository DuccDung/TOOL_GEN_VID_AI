using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using TOOL_LOCAL.ModelDistribution;
using TOOL_SHARED.Contracts.ModelDistribution;

namespace TOOL_TESTS.Updates;

public sealed class ResumableModelDownloaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-model-download-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly Uri Origin = new("https://models.invalid/");
    private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("model fixture with a checked prefix and suffix");
    private static readonly ModelDistributionArtifact Artifact = new("fixture", "fixture", "v1", "model.gguf",
        "artifacts/fixture/v1/model.gguf", Bytes.Length, Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant(),
        1, "1.0.0", 1);
    private string Destination => Path.Combine(_root, "model.gguf");

    [Fact]
    public async Task Interrupted_transfer_resumes_with_strong_validator_and_hashes_complete_file()
    {
        using var handler = new Handler((request, index) => index == 0
            ? Interrupted(request)
            : Remainder(request, 5));
        using var http = new HttpClient(handler);
        var result = await Downloader(http).DownloadAsync(Artifact, Destination, null, CancellationToken.None);
        Assert.Equal(Destination, result);
        Assert.Equal(Bytes, File.ReadAllBytes(result));
        Assert.Equal([null, 5L], handler.Offsets);
        Assert.Equal([null, "\"fixture-v1\""], handler.IfRanges);
        Assert.False(File.Exists(Destination + ".part"));
        Assert.False(File.Exists(Destination + ".part.receipt.json"));
    }

    [Fact]
    public async Task Full_200_after_range_restarts_instead_of_appending()
    {
        using var handler = new Handler((request, index) => index == 0 ? Interrupted(request) : Complete(request));
        using var http = new HttpClient(handler);
        await Downloader(http).DownloadAsync(Artifact, Destination, null, CancellationToken.None);
        Assert.Equal([null, 5L], handler.Offsets);
        Assert.Equal(Bytes, File.ReadAllBytes(Destination));
    }

    [Fact]
    public async Task Range_416_discards_incomplete_prefix_then_restarts()
    {
        using var handler = new Handler((request, index) => index switch
        {
            0 => Interrupted(request),
            1 => Response(request, HttpStatusCode.RequestedRangeNotSatisfiable, []),
            _ => Complete(request)
        });
        using var http = new HttpClient(handler);
        await Downloader(http).DownloadAsync(Artifact, Destination, null, CancellationToken.None);
        Assert.Equal([null, 5L, null], handler.Offsets);
        Assert.Equal(Bytes, File.ReadAllBytes(Destination));
    }

    [Fact]
    public async Task Weak_etag_never_authorizes_a_range_resume()
    {
        using var handler = new Handler((request, index) =>
        {
            if (index > 0) return Complete(request);
            var response = Interrupted(request);
            response.Headers.ETag = new EntityTagHeaderValue("\"fixture-v1\"", isWeak: true);
            return response;
        });
        using var http = new HttpClient(handler);
        await Downloader(http).DownloadAsync(Artifact, Destination, null, CancellationToken.None);
        Assert.Equal([null, null], handler.Offsets);
        Assert.Equal(Bytes, File.ReadAllBytes(Destination));
    }

    [Theory]
    [InlineData("offset")]
    [InlineData("total")]
    [InlineData("etag")]
    [InlineData("encoding")]
    public async Task Invalid_partial_response_is_rejected_and_cleared(string mutation)
    {
        using var handler = new Handler((request, index) =>
        {
            if (index == 0) return Interrupted(request);
            var response = Remainder(request, 5);
            switch (mutation)
            {
                case "offset": response.Content.Headers.ContentRange = new(4, Bytes.Length - 1, Bytes.Length); break;
                case "total": response.Content.Headers.ContentRange = new(5, Bytes.Length - 1, Bytes.Length + 1); break;
                case "etag": response.Headers.ETag = new("\"changed\""); break;
                default: response.Content.Headers.ContentEncoding.Add("gzip"); break;
            }
            return response;
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => Downloader(http).DownloadAsync(Artifact, Destination, null, CancellationToken.None));
        Assert.False(File.Exists(Destination));
        Assert.False(File.Exists(Destination + ".part"));
        Assert.False(File.Exists(Destination + ".part.receipt.json"));
    }

    [Fact]
    public async Task Oversized_body_without_content_length_stops_and_keeps_previous_file()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Destination, "previous file kept for recovery");
        using var handler = new Handler((request, _) =>
        {
            var response = Complete(request);
            response.Content = new StreamContent(new ChunkStream(Bytes.Concat(new byte[] { 1 }).ToArray()));
            return response;
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => Downloader(http).DownloadAsync(Artifact, Destination, null, CancellationToken.None));
        Assert.Equal("previous file kept for recovery", await File.ReadAllTextAsync(Destination));
        Assert.False(File.Exists(Destination + ".part"));
    }

    [Fact]
    public async Task Hash_mismatch_cleans_untrusted_files_without_overwriting_old_file()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Destination, "old model");
        var wrong = (byte[])Bytes.Clone();
        wrong[0] ^= 1;
        using var handler = new Handler((request, _) => Response(request, HttpStatusCode.OK, wrong));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => Downloader(http).DownloadAsync(Artifact, Destination, null, CancellationToken.None));
        Assert.Equal("old model", await File.ReadAllTextAsync(Destination));
        Assert.Single(handler.Offsets);
        Assert.False(File.Exists(Destination + ".part"));
        Assert.False(File.Exists(Destination + ".part.receipt.json"));
    }

    [Fact]
    public async Task Cancellation_preserves_prefix_and_next_invocation_can_resume()
    {
        using var cancel = new CancellationTokenSource();
        using var firstHandler = new Handler((request, _) =>
        {
            var response = Complete(request);
            response.Content = new StreamContent(new ChunkStream(Bytes[..5], onEnd: () => cancel.Cancel()));
            return response;
        });
        using var firstHttp = new HttpClient(firstHandler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Downloader(firstHttp).DownloadAsync(Artifact, Destination, null, cancel.Token));
        Assert.Equal(Bytes[..5], File.ReadAllBytes(Destination + ".part"));
        Assert.False(File.Exists(Destination));
        using var nextHandler = new Handler((request, _) => Remainder(request, 5));
        using var nextHttp = new HttpClient(nextHandler);
        await Downloader(nextHttp).DownloadAsync(Artifact, Destination, null, CancellationToken.None);
        Assert.Equal([5L], nextHandler.Offsets);
        Assert.Equal(Bytes, File.ReadAllBytes(Destination));
    }

    [Fact]
    public async Task Verified_cached_file_does_not_contact_network()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(Destination, Bytes);
        using var handler = new Handler((_, _) => throw new InvalidOperationException("Network should not be contacted."));
        using var http = new HttpClient(handler);
        await Downloader(http).DownloadAsync(Artifact, Destination, null, CancellationToken.None);
        Assert.Empty(handler.Offsets);
    }

    [Fact]
    public async Task Retry_exhaustion_never_promotes_partial_output()
    {
        using var handler = new Handler((request, _) => Response(request, HttpStatusCode.ServiceUnavailable, []));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => Downloader(http).DownloadAsync(Artifact, Destination, null, CancellationToken.None));
        Assert.Equal(4, handler.Offsets.Count);
        Assert.False(File.Exists(Destination));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "delta", 5000)]
    [InlineData(HttpStatusCode.TooManyRequests, "delta", 5000)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "date", 8000)]
    [InlineData(HttpStatusCode.TooManyRequests, "date", 8000)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "large-delta", 60000)]
    [InlineData(HttpStatusCode.TooManyRequests, "large-date", 60000)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "past-date", 300)]
    [InlineData(HttpStatusCode.TooManyRequests, "zero", 300)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "invalid", 300)]
    [InlineData(HttpStatusCode.TooManyRequests, "missing", 300)]
    public async Task Busy_and_rate_limited_responses_honor_bounded_retry_after_before_recovering(
        HttpStatusCode status, string header, int expectedMilliseconds)
    {
        var now = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
        var delays = new List<TimeSpan>();
        var errorBody = new DisposalTrackedContent();
        using var handler = new Handler((request, index) =>
        {
            if (index > 0) return Complete(request);
            var response = new HttpResponseMessage(status) { RequestMessage = request, Content = errorBody };
            response.Headers.RetryAfter = header switch
            {
                "delta" => new RetryConditionHeaderValue(TimeSpan.FromSeconds(5)),
                "date" => new RetryConditionHeaderValue(now.AddSeconds(8)),
                "large-delta" => new RetryConditionHeaderValue(TimeSpan.FromHours(12)),
                "large-date" => new RetryConditionHeaderValue(now.AddDays(1)),
                "past-date" => new RetryConditionHeaderValue(now.AddSeconds(-30)),
                "zero" => new RetryConditionHeaderValue(TimeSpan.Zero),
                _ => null
            };
            if (header == "invalid") response.Headers.TryAddWithoutValidation("Retry-After", "invalid fixture value");
            return response;
        });
        using var http = new HttpClient(handler);
        var downloader = new ResumableModelDownloader(http, Origin, "Stable", timeProvider: new FixedClock(now),
            delayAsync: (delay, token) =>
            {
                token.ThrowIfCancellationRequested();
                Assert.True(errorBody.Disposed);
                Assert.Single(handler.Offsets);
                delays.Add(delay);
                return Task.CompletedTask;
            });
        await downloader.DownloadAsync(Artifact, Destination, null, CancellationToken.None);
        Assert.Equal([TimeSpan.FromMilliseconds(expectedMilliseconds)], delays);
        Assert.Equal([null, null], handler.Offsets);
        Assert.Equal(Bytes, File.ReadAllBytes(Destination));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Repeated_busy_responses_stop_after_four_requests_and_three_capped_waits(HttpStatusCode status)
    {
        var delays = new List<TimeSpan>();
        using var handler = new Handler((request, _) =>
        {
            var response = Response(request, status, []);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromDays(1));
            return response;
        });
        using var http = new HttpClient(handler);
        var downloader = new ResumableModelDownloader(http, Origin, "Stable", delayAsync: (delay, token) =>
        {
            token.ThrowIfCancellationRequested();
            delays.Add(delay);
            return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<HttpRequestException>(() => downloader.DownloadAsync(Artifact, Destination, null, CancellationToken.None));
        Assert.Equal(4, handler.Offsets.Count);
        Assert.Equal(3, delays.Count);
        Assert.All(delays, delay => Assert.Equal(TimeSpan.FromSeconds(60), delay));
        Assert.Equal(TimeSpan.FromSeconds(180), TimeSpan.FromTicks(delays.Sum(delay => delay.Ticks)));
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task Cancellation_during_retry_after_preserves_receipt_and_resumable_prefix()
    {
        using var cancel = new CancellationTokenSource();
        var retryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorBody = new DisposalTrackedContent();
        using var handler = new Handler((request, index) =>
        {
            if (index == 0) return Interrupted(request);
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request, Content = errorBody };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
            return response;
        });
        using var http = new HttpClient(handler);
        var downloader = new ResumableModelDownloader(http, Origin, "Stable", delayAsync: (delay, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (delay != TimeSpan.FromSeconds(5)) return Task.CompletedTask;
            Assert.True(errorBody.Disposed);
            retryEntered.TrySetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        var download = downloader.DownloadAsync(Artifact, Destination, null, cancel.Token);
        await retryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(Bytes[..5], File.ReadAllBytes(Destination + ".part"));
        Assert.True(File.Exists(Destination + ".part.receipt.json"));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => download.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal([null, 5L], handler.Offsets);
        Assert.False(File.Exists(Destination));
        Assert.Equal(Bytes[..5], File.ReadAllBytes(Destination + ".part"));
        Assert.True(File.Exists(Destination + ".part.receipt.json"));

        using var resumedHandler = new Handler((request, _) => Remainder(request, 5));
        using var resumedHttp = new HttpClient(resumedHandler);
        await Downloader(resumedHttp).DownloadAsync(Artifact, Destination, null, CancellationToken.None);
        Assert.Equal([5L], resumedHandler.Offsets);
        Assert.Equal(["\"fixture-v1\""], resumedHandler.IfRanges);
        Assert.Equal(Bytes, File.ReadAllBytes(Destination));
    }

    [Fact]
    public async Task Busy_response_after_a_dropped_transfer_keeps_the_same_prefix_and_validator()
    {
        var delays = new List<TimeSpan>();
        using var handler = new Handler((request, index) =>
        {
            if (index == 0) return Interrupted(request);
            if (index > 1) return Remainder(request, 5);
            var response = Response(request, HttpStatusCode.TooManyRequests, []);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
            return response;
        });
        using var http = new HttpClient(handler);
        var downloader = new ResumableModelDownloader(http, Origin, "Stable", delayAsync: (delay, token) =>
        {
            token.ThrowIfCancellationRequested();
            delays.Add(delay);
            Assert.Equal(Bytes[..5], File.ReadAllBytes(Destination + ".part"));
            return Task.CompletedTask;
        });
        await downloader.DownloadAsync(Artifact, Destination, null, CancellationToken.None);
        Assert.Equal([TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(5)], delays);
        Assert.Equal([null, 5L, 5L], handler.Offsets);
        Assert.Equal([null, "\"fixture-v1\"", "\"fixture-v1\""], handler.IfRanges);
        Assert.Equal(Bytes, File.ReadAllBytes(Destination));
    }

    [Fact]
    public async Task Cross_origin_busy_response_is_rejected_before_any_wait_or_retry()
    {
        using var handler = new Handler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://unapproved.invalid/model.gguf"),
                Content = new ByteArrayContent([])
            };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
            return response;
        });
        using var http = new HttpClient(handler);
        var downloader = new ResumableModelDownloader(http, Origin, "Stable", delayAsync: (_, _) => throw new InvalidOperationException("No retry is allowed."));
        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(Artifact, Destination, null, CancellationToken.None));
        Assert.Single(handler.Offsets);
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task Stalled_stream_is_bounded_by_inactivity_timeout()
    {
        using var handler = new Handler((request, _) =>
        {
            var response = Complete(request);
            response.Content = new StreamContent(new StalledStream());
            return response;
        });
        using var http = new HttpClient(handler);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var downloader = new ResumableModelDownloader(http, Origin, "Stable", TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAsync<IOException>(() => downloader.DownloadAsync(Artifact, Destination, null, deadline.Token));
        Assert.Equal(4, handler.Offsets.Count);
        Assert.False(deadline.IsCancellationRequested);
        Assert.False(File.Exists(Destination));
    }

    [Fact]
    public async Task Truncated_body_at_eof_can_resume_with_the_next_response()
    {
        using var handler = new Handler((request, index) =>
        {
            if (index > 0) return Remainder(request, 5);
            var response = Complete(request);
            response.Content = new StreamContent(new ChunkStream(Bytes[..5]));
            return response;
        });
        using var http = new HttpClient(handler);
        await Downloader(http).DownloadAsync(Artifact, Destination, null, CancellationToken.None);
        Assert.Equal([null, 5L], handler.Offsets);
        Assert.Equal(Bytes, File.ReadAllBytes(Destination));
    }

    [Fact]
    public async Task Modified_prefix_is_detected_by_the_full_hash_after_resume()
    {
        var wrongPrefix = Bytes[..5];
        wrongPrefix[0] ^= 1;
        using var handler = new Handler((request, index) =>
        {
            if (index > 0) return Remainder(request, 5);
            var response = Complete(request);
            response.Content = new StreamContent(new ChunkStream(wrongPrefix, failAtEnd: true));
            return response;
        });
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => Downloader(http).DownloadAsync(Artifact, Destination, null, CancellationToken.None));
        Assert.False(File.Exists(Destination));
        Assert.False(File.Exists(Destination + ".part"));
        Assert.False(File.Exists(Destination + ".part.receipt.json"));
    }

    [Fact]
    public async Task Resume_receipt_from_another_origin_is_discarded()
    {
        using var cancel = new CancellationTokenSource();
        using var firstHandler = new Handler((request, _) =>
        {
            var response = Complete(request);
            response.Content = new StreamContent(new ChunkStream(Bytes[..5], onEnd: () => cancel.Cancel()));
            return response;
        });
        using var firstHttp = new HttpClient(firstHandler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Downloader(firstHttp).DownloadAsync(Artifact, Destination, null, cancel.Token));
        using var nextHandler = new Handler((request, _) => Complete(request));
        using var nextHttp = new HttpClient(nextHandler);
        await new ResumableModelDownloader(nextHttp, new Uri("https://second-models.invalid/"), "Stable")
            .DownloadAsync(Artifact, Destination, null, CancellationToken.None);
        Assert.Equal([null], nextHandler.Offsets);
        Assert.Equal(Bytes, File.ReadAllBytes(Destination));
    }

    private static ResumableModelDownloader Downloader(HttpClient http) => new(http, Origin, "Stable");

    private static HttpResponseMessage Complete(HttpRequestMessage request) => Response(request, HttpStatusCode.OK, Bytes);
    private static HttpResponseMessage Interrupted(HttpRequestMessage request)
    {
        var response = Complete(request);
        response.Content = new StreamContent(new ChunkStream(Bytes[..5], failAtEnd: true));
        return response;
    }
    private static HttpResponseMessage Remainder(HttpRequestMessage request, int offset)
    {
        var response = Response(request, HttpStatusCode.PartialContent, Bytes[offset..]);
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, Bytes.Length - 1, Bytes.Length);
        return response;
    }
    private static HttpResponseMessage Response(HttpRequestMessage request, HttpStatusCode status, byte[] bytes)
    {
        var response = new HttpResponseMessage(status) { RequestMessage = request, Content = new ByteArrayContent(bytes) };
        response.Headers.ETag = new EntityTagHeaderValue("\"fixture-v1\"");
        response.Content.Headers.ContentType = new("application/octet-stream");
        return response;
    }

    private sealed class Handler(Func<HttpRequestMessage, int, HttpResponseMessage> callback) : HttpMessageHandler
    {
        public List<long?> Offsets { get; } = [];
        public List<string?> IfRanges { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = Offsets.Count;
            Offsets.Add(request.Headers.Range?.Ranges.Single().From);
            IfRanges.Add(request.Headers.IfRange?.EntityTag?.ToString());
            return Task.FromResult(callback(request, index));
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class DisposalTrackedContent : HttpContent
    {
        public bool Disposed { get; private set; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;
        protected override bool TryComputeLength(out long length) { length = 0; return true; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class ChunkStream(byte[] bytes, bool failAtEnd = false, Action? onEnd = null) : Stream
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
            if (_position == bytes.Length)
            {
                onEnd?.Invoke();
                cancellationToken.ThrowIfCancellationRequested();
                if (failAtEnd) throw new IOException("Fixture stream interrupted.");
                return ValueTask.FromResult(0);
            }
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

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    public void Dispose()
    {
        var path = Path.GetFullPath(_root);
        if (!path.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Test cleanup escaped its temporary root.");
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
