using System.Diagnostics;
using TOOL_LOCAL.Vietsub.Translation;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubTranslationWorkerLifecycleTests
{
    [Fact]
    public async Task Shutdown_does_not_dispose_native_resources_while_inference_ignores_cancellation()
    {
        var start = new ProcessStartInfo(VietsubTranslationWorkerClientOptions.CreateDefault().WorkerExecutablePath)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment[TestMode] = "ignore-infer-cancel";
        using var worker = Process.Start(start)!;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var input = worker.StandardInput.BaseStream;
        var stdout = worker.StandardOutput.BaseStream;
        try
        {
            Assert.Equal(VietsubTranslationWorkerProtocol.Hello,
                (await VietsubTranslationWorkerFrameCodec.ReadAsync(stdout, deadline.Token))!.Type);
            await VietsubTranslationWorkerFrameCodec.WriteAsync(input,
                VietsubTranslationWorkerProtocol.Create(VietsubTranslationWorkerProtocol.Infer, "infer",
                    new VietsubTranslationWorkerInferRequest("TEST", "fixture", "root ::= \"[]\"", 32, 100)), deadline.Token);
            while (await worker.StandardError.ReadLineAsync(deadline.Token) is { } line)
                if (line == "test_inference_entered") break;
            var diagnostics = worker.StandardError.ReadToEndAsync(deadline.Token);
            await VietsubTranslationWorkerFrameCodec.WriteAsync(input,
                VietsubTranslationWorkerProtocol.Create(VietsubTranslationWorkerProtocol.Shutdown, "shutdown"), deadline.Token);
            await worker.WaitForExitAsync(deadline.Token);
            Assert.Equal(70, worker.ExitCode);
            Assert.DoesNotContain("test_unsafe_engine_dispose", await diagnostics);
        }
        finally
        {
            if (!worker.HasExited) { worker.Kill(entireProcessTree: true); await worker.WaitForExitAsync(); }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reset_drains_old_stdout_before_starting_next_worker(bool malformedTail)
    {
        DelayedEndStream? oldOutput = null;
        var starts = 0;
        await using var client = CreateClient(new() { [TestMode] = "echo" }, stream =>
            Interlocked.Increment(ref starts) == 1
                ? oldOutput = new DelayedEndStream(stream, malformedTail)
                : stream);
        var first = await client.LoadAsync(Request(), null, default);
        Assert.True(client.IsLoaded(first.ConfigFingerprint));
        var reset = client.ResetAsync();
        Task<VietsubTranslationWorkerLoadResult>? next = null;
        try
        {
            await oldOutput!.EndReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            next = client.LoadAsync(Request(), null, default);
            // The old pipe is deliberately held open. This timeout only checks that
            // Reset cannot complete or let another worker start before the barrier.
            await Task.WhenAny(reset, Task.Delay(100));
            Assert.False(reset.IsCompleted);
            Assert.Equal(1, Volatile.Read(ref starts));
        }
        finally
        {
            oldOutput!.Release();
            await reset.WaitAsync(TimeSpan.FromSeconds(5));
            if (next is not null)
            {
                try { await next.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (Exception) { /* Observed here; asserted below on the successful test path. */ }
            }
        }
        var loaded = await next!;
        Assert.True(client.IsLoaded(loaded.ConfigFingerprint));
        var result = await client.InferAsync(new("TEST", "fixture", "root ::= \"[]\"", 32, 100), null, default);
        Assert.Equal("[]", result.RawOutput);
        Assert.Equal(2, starts);
    }

    [Fact]
    public async Task Reset_cancels_active_request_and_next_worker_remains_usable()
    {
        var modes = new Dictionary<string, string?> { [TestMode] = "hang-after-hello" };
        await using var client = CreateClient(modes);
        var load = client.LoadAsync(Request(), null, default);
        await WaitForWorkerAsync(client);
        await client.ResetAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
        modes[TestMode] = "echo";
        var next = await client.LoadAsync(Request(), null, default);
        Assert.True(client.IsLoaded(next.ConfigFingerprint));
    }

    [Fact]
    public async Task Dispose_cancels_active_request_without_disposing_its_semaphore_early()
    {
        var client = CreateClient(new() { [TestMode] = "hang-after-hello" });
        var load = client.LoadAsync(Request(), null, default);
        try
        {
            await WaitForWorkerAsync(client);
            var queued = client.LoadAsync(Request(), null, default);
            var firstDispose = client.DisposeAsync().AsTask();
            var secondDispose = client.DisposeAsync().AsTask();
            await Task.WhenAll(firstDispose, secondDispose).WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
            var queuedError = await Record.ExceptionAsync(() => queued);
            Assert.True(queuedError is OperationCanceledException or ObjectDisposedException,
                $"Unexpected queued request result: {queuedError?.GetType().Name ?? "success"}");
            await Assert.ThrowsAsync<ObjectDisposedException>(() => client.LoadAsync(Request(), null, default));
        }
        finally { await client.DisposeAsync(); }
    }

    [Fact]
    public async Task Dispose_during_startup_unblocks_the_handshake_and_drains_the_reader()
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = CreateClient(new() { [TestMode] = "echo" }, stream => new HeldHandshakeStream(stream, held));
        var load = client.LoadAsync(Request(), null, default);
        try
        {
            await held.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(6));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => load);
        }
        finally { await client.DisposeAsync(); }
    }

    [Fact]
    public async Task Crash_is_reported_once_and_next_worker_can_load()
    {
        var modes = new Dictionary<string, string?> { [TestMode] = "crash-after-hello" };
        await using var client = CreateClient(modes);
        var error = await Assert.ThrowsAsync<VietsubTranslationException>(() => client.LoadAsync(Request(), null, default));
        Assert.Equal(VietsubTranslationErrorCodes.ProcessCrashed, error.Code);
        modes[TestMode] = "echo";
        var next = await client.LoadAsync(Request(), null, default);
        Assert.True(client.IsLoaded(next.ConfigFingerprint));
    }

    private const string TestMode = "VIDEOMAKER_TRANSLATION_WORKER_TEST_MODE";

    private static VietsubTranslationWorkerClient CreateClient(Dictionary<string, string?> modes,
        Func<Stream, Stream>? decorator = null) => new(
            new(Path.Combine(AppContext.BaseDirectory, "_translation_worker", "VideoMaker.Vietsub.TranslationWorker.exe"),
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), modes),
            resourceRequirements: new(0, 0, 0), responseStreamDecorator: decorator);

    private static VietsubTranslationWorkerLoadRequest Request()
    {
        var component = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km;
        return new(Path.GetTempPath(), "fixture.gguf", "fixture", component.EngineId, component.EngineVersion,
            "fixture.gguf", 1, new string('0', 64),
            VietsubTranslationWorkerProfiles.CreateSafeCpuProfile(Environment.ProcessorCount), new(0, 0, 0));
    }

    private static async Task WaitForWorkerAsync(VietsubTranslationWorkerClient client)
    {
        var timeout = Stopwatch.StartNew();
        while (!client.LastDiagnostics.Contains("worker_started:", StringComparison.Ordinal))
        {
            Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(5), "Worker did not start.");
            await Task.Delay(10);
        }
    }

    private sealed class DelayedEndStream(Stream inner, bool malformedTail) : Stream
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly byte[] _tail = malformedTail ? [1, 0, 0, 0, 0xff] : [];
        private int _tailOffset;
        public TaskCompletionSource EndReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => _release.TrySetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await inner.ReadAsync(buffer, cancellationToken);
            if (count != 0) return count;
            EndReached.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            var remaining = Math.Min(buffer.Length, _tail.Length - _tailOffset);
            _tail.AsMemory(_tailOffset, remaining).CopyTo(buffer);
            _tailOffset += remaining;
            return remaining;
        }

        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class HeldHandshakeStream(Stream inner, TaskCompletionSource held) : Stream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await inner.ReadAsync(buffer, cancellationToken);
            held.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return count;
        }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
