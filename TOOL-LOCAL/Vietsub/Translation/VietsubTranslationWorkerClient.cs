using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace TOOL_LOCAL.Vietsub.Translation;

internal sealed record VietsubTranslationWorkerClientOptions(
    string WorkerExecutablePath,
    TimeSpan StartupTimeout,
    TimeSpan LoadTimeout,
    TimeSpan InferenceTimeout,
    TimeSpan CancelGracePeriod,
    IReadOnlyDictionary<string, string?>? EnvironmentVariables = null)
{
    public static VietsubTranslationWorkerClientOptions CreateDefault() => new(
        Path.Combine(AppContext.BaseDirectory, "_translation_worker", "VideoMaker.Vietsub.TranslationWorker.exe"),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromMinutes(4),
        TimeSpan.FromMinutes(8),
        TimeSpan.FromSeconds(2));
}

internal interface IVietsubTranslationWorkerClient : IAsyncDisposable
{
    string WorkerBinaryFingerprint { get; }
    bool IsLoaded(string configFingerprint);
    Task ResetAsync();
    Task<VietsubTranslationHardwareResult> ProbeHardwareAsync(CancellationToken cancellationToken);
    Task<VietsubTranslationWorkerLoadResult> LoadAsync(VietsubTranslationWorkerLoadRequest request,
        IProgress<VietsubTranslationWorkerProgress>? progress, CancellationToken cancellationToken);
    Task<VietsubTranslationWorkerInferResult> InferAsync(VietsubTranslationWorkerInferRequest request,
        IProgress<VietsubTranslationWorkerProgress>? progress, CancellationToken cancellationToken);
}

internal sealed class VietsubTranslationWorkerClient : IVietsubTranslationWorkerClient
{
    private const int MaximumDiagnosticCharacters = 32 * 1024;
    private readonly VietsubTranslationWorkerClientOptions _options;
    private readonly IVietsubTranslationMemoryProbe _memoryProbe;
    private readonly VietsubTranslationResourceRequirements _resourceRequirements;
    private readonly Func<Stream, Stream>? _responseStreamDecorator;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly object _disposeLock = new();
    private WorkerSession? _session;
    private string _lastDiagnostics = string.Empty;
    private volatile bool _disposed;
    private Task? _disposeTask;

    public VietsubTranslationWorkerClient(
        VietsubTranslationWorkerClientOptions? options = null,
        IVietsubTranslationMemoryProbe? memoryProbe = null,
        VietsubTranslationResourceRequirements? resourceRequirements = null,
        Func<Stream, Stream>? responseStreamDecorator = null)
    {
        _options = options ?? VietsubTranslationWorkerClientOptions.CreateDefault();
        _memoryProbe = memoryProbe ?? new WindowsVietsubTranslationMemoryProbe();
        _resourceRequirements = resourceRequirements ?? VietsubTranslationResourceRequirements.SafeCpuBaseline;
        _responseStreamDecorator = responseStreamDecorator;
    }

    public string LastDiagnostics => Volatile.Read(ref _session)?.Diagnostics.ToString()
        ?? Volatile.Read(ref _lastDiagnostics);

    public string WorkerBinaryFingerprint => VietsubTranslationWorkerProtocol.ComputeWorkerBinaryFingerprint(
        Path.GetDirectoryName(Path.GetFullPath(_options.WorkerExecutablePath))!);

    public bool IsLoaded(string configFingerprint)
    {
        var session = Volatile.Read(ref _session);
        return session is not null && session.IsUsable
            && string.Equals(session.LoadedConfigFingerprint, configFingerprint, StringComparison.Ordinal);
    }

    public Task<VietsubTranslationWorkerLoadResult> LoadAsync(
        VietsubTranslationWorkerLoadRequest request,
        IProgress<VietsubTranslationWorkerProgress>? progress,
        CancellationToken cancellationToken) => SendAsync<VietsubTranslationWorkerLoadResult>(
            VietsubTranslationWorkerProtocol.Load, request, _options.LoadTimeout, progress, cancellationToken);

    public Task<VietsubTranslationWorkerInferResult> InferAsync(
        VietsubTranslationWorkerInferRequest request,
        IProgress<VietsubTranslationWorkerProgress>? progress,
        CancellationToken cancellationToken) => SendAsync<VietsubTranslationWorkerInferResult>(
            VietsubTranslationWorkerProtocol.Infer, request, _options.InferenceTimeout, progress, cancellationToken);

    public Task ResetAsync()
    {
        ThrowIfDisposed();
        return StopWorkerAsync(kill: true);
    }

    public Task<VietsubTranslationHardwareResult> ProbeHardwareAsync(CancellationToken cancellationToken) =>
        SendAsync<VietsubTranslationHardwareResult>(VietsubTranslationWorkerProtocol.ProbeHardware,
            new { }, TimeSpan.FromSeconds(10), null, cancellationToken);

    private async Task<T> SendAsync<T>(
        string type, object payload, TimeSpan timeout,
        IProgress<VietsubTranslationWorkerProgress>? progress, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _disposeCancellation.Token);
        var token = operationCancellation.Token;
        await _operationGate.WaitAsync(token).ConfigureAwait(false);
        var requestId = Guid.NewGuid().ToString("N");
        var pending = new PendingRequest(progress);
        WorkerSession? session = null;
        try
        {
            ThrowIfDisposed();
            var requirements = type == VietsubTranslationWorkerProtocol.ProbeHardware
                ? new VietsubTranslationResourceRequirements(0, 0, 0)
                : payload is VietsubTranslationWorkerLoadRequest load ? load.ResourceRequirements : _resourceRequirements;
            session = await EnsureStartedAsync(requirements,
                payload is VietsubTranslationWorkerLoadRequest accepted && accepted.ResourceWarningAccepted,
                token, (payload as VietsubTranslationWorkerLoadRequest)?.Config.GpuDeviceId).ConfigureAwait(false);
            if (type == VietsubTranslationWorkerProtocol.Load) session.LoadedConfigFingerprint = null;
            session.Register(requestId, pending);
            await WriteAsync(session, VietsubTranslationWorkerProtocol.Create(type, requestId, payload), token)
                .ConfigureAwait(false);
            VietsubTranslationWorkerEnvelope response;
            try
            {
                response = await pending.Completion.Task.WaitAsync(timeout, token).ConfigureAwait(false);
            }
            catch (TimeoutException exception)
            {
                await CancelAndStopIfNeededAsync(session, requestId, pending).ConfigureAwait(false);
                throw new VietsubTranslationException(VietsubTranslationErrorCodes.ProcessTimeout,
                    "Worker dịch local không phản hồi trong thời gian cho phép.", retryable: true, innerException: exception);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                await CancelAndStopIfNeededAsync(session, requestId, pending).ConfigureAwait(false);
                throw;
            }

            if (response.Type == VietsubTranslationWorkerProtocol.Error)
                throw new VietsubTranslationException(
                    response.ErrorCode ?? VietsubTranslationErrorCodes.ProcessFailed,
                    response.Message ?? "Worker dịch local trả về lỗi.", response.Retryable);
            if (response.Type != VietsubTranslationWorkerProtocol.Result)
                throw new VietsubTranslationWorkerProtocolException("Worker trả về loại response không hợp lệ.");

            var result = VietsubTranslationWorkerProtocol.ReadPayload<T>(response);
            // A load completion belongs to this session, even if Reset runs concurrently.
            if (result is VietsubTranslationWorkerLoadResult loaded && session.IsUsable)
                session.LoadedConfigFingerprint = loaded.ConfigFingerprint;
            return result;
        }
        catch (VietsubTranslationWorkerProtocolException exception)
        {
            await StopWorkerAsync(kill: true, expected: session).ConfigureAwait(false);
            throw new VietsubTranslationException(VietsubTranslationErrorCodes.WorkerProtocolInvalid,
                "Giao thức worker dịch local không hợp lệ.", retryable: false, innerException: exception);
        }
        finally
        {
            session?.Pending.TryRemove(requestId, out _);
            _operationGate.Release();
        }
    }

    private async Task<WorkerSession> EnsureStartedAsync(
        VietsubTranslationResourceRequirements requirements, bool resourceWarningAccepted,
        CancellationToken cancellationToken, string? gpuDeviceId)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_session is { IsUsable: true } current) return current;
            await StopWorkerCoreAsync(kill: true).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            var resources = VietsubTranslationResourceGate.Evaluate(_memoryProbe, requirements);
            if (!resources.CanLoad && !(resources.RequiresConfirmation && resourceWarningAccepted))
                throw new VietsubTranslationException(resources.RequiresConfirmation
                        ? VietsubTranslationErrorCodes.ResourceConfirmationRequired
                        : VietsubTranslationErrorCodes.RuntimeUnsupportedPlatform,
                    resources.Message, retryable: false);
            var executable = Path.GetFullPath(_options.WorkerExecutablePath);
            if (!File.Exists(executable))
                throw new VietsubTranslationException(VietsubTranslationErrorCodes.BackendLoadFailed,
                    "Thiếu translation worker trong bộ cài desktop; hãy sửa hoặc cập nhật ứng dụng.");

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            };
            if (_options.EnvironmentVariables is not null)
                foreach (var pair in _options.EnvironmentVariables) startInfo.Environment[pair.Key] = pair.Value;
            startInfo.Environment.Remove("CUDA_VISIBLE_DEVICES");
            startInfo.Environment["CUDA_DEVICE_ORDER"] = "PCI_BUS_ID";
            if (gpuDeviceId is not null) startInfo.Environment["CUDA_VISIBLE_DEVICES"] = gpuDeviceId;

            Process process;
            try
            {
                process = Process.Start(startInfo) ?? throw new InvalidOperationException("Worker process was not created.");
            }
            catch (Exception exception) when (exception is
                System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
            {
                throw new VietsubTranslationException(VietsubTranslationErrorCodes.BackendLoadFailed,
                    "Không thể khởi động translation worker trong bộ cài desktop.", retryable: false, innerException: exception);
            }

            var session = new WorkerSession(process);
            Volatile.Write(ref _session, session);
            try
            {
                session.Output = _responseStreamDecorator?.Invoke(session.Output) ?? session.Output;
                session.ResponsePump = PumpResponsesAsync(session);
                session.StderrPump = PumpStderrAsync(session);
                var hello = await session.Hello.Task.WaitAsync(_options.StartupTimeout, cancellationToken).ConfigureAwait(false);
                if (hello.ProtocolVersion != VietsubTranslationWorkerProtocol.Version
                    || !string.Equals(hello.WorkerVersion, VietsubTranslationWorkerProtocol.WorkerVersion, StringComparison.Ordinal)
                    || !string.Equals(hello.EngineId, VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km.EngineId, StringComparison.Ordinal)
                    || !string.Equals(hello.EngineVersion, VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km.EngineVersion, StringComparison.Ordinal))
                    throw new VietsubTranslationException(VietsubTranslationErrorCodes.WorkerProtocolInvalid,
                        "Translation worker sai protocol, engine hoặc version đã pin.");
                return session;
            }
            catch (TimeoutException exception)
            {
                await StopWorkerCoreAsync(kill: true).ConfigureAwait(false);
                throw new VietsubTranslationException(VietsubTranslationErrorCodes.ProcessTimeout,
                    "Translation worker không hoàn tất startup handshake.", retryable: true, innerException: exception);
            }
            catch
            {
                await StopWorkerCoreAsync(kill: true).ConfigureAwait(false);
                throw;
            }
        }
        finally { _lifecycleGate.Release(); }
    }

    private static async Task PumpResponsesAsync(WorkerSession session)
    {
        Exception? failure = null;
        try
        {
            while (true)
            {
                var response = await VietsubTranslationWorkerFrameCodec.ReadAsync(session.Output, session.ReaderCancellation.Token)
                    .ConfigureAwait(false);
                if (response is null || session.IsStopping) break;
                if (response.Type == VietsubTranslationWorkerProtocol.Hello)
                {
                    session.Hello.TrySetResult(VietsubTranslationWorkerProtocol.ReadPayload<VietsubTranslationWorkerHello>(response));
                    continue;
                }
                if (!session.Pending.TryGetValue(response.RequestId, out var pending)) continue;
                if (response.Type == VietsubTranslationWorkerProtocol.Progress)
                {
                    pending.Progress?.Report(VietsubTranslationWorkerProtocol.ReadPayload<VietsubTranslationWorkerProgress>(response));
                    continue;
                }
                pending.Completion.TrySetResult(response);
            }
        }
        catch (OperationCanceledException) when (session.ReaderCancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is
            IOException or ObjectDisposedException or VietsubTranslationWorkerProtocolException)
        {
            failure = exception;
        }

        session.LoadedConfigFingerprint = null;
        if (session.IsStopping) return;
        var exitCode = await ReadExitCodeAsync(session.Process).ConfigureAwait(false);
        if (session.IsStopping) return;
        var code = failure is VietsubTranslationWorkerProtocolException
            ? VietsubTranslationErrorCodes.WorkerProtocolInvalid : VietsubTranslationErrorCodes.ProcessCrashed;
        session.Diagnostics.Append($"client_worker_exit:pid={session.ProcessId}:intentional=false:code={exitCode?.ToString() ?? "unknown"}\n");
        session.Terminate(new VietsubTranslationException(code,
            exitCode is null ? "Translation worker đã thoát trước khi trả kết quả."
                : $"Translation worker đã thoát bất thường (exit code {exitCode}).",
            retryable: code == VietsubTranslationErrorCodes.ProcessCrashed, innerException: failure));
    }

    private static async Task PumpStderrAsync(WorkerSession session)
    {
        var buffer = new char[1024];
        try
        {
            while (true)
            {
                var read = await session.Error.ReadAsync(buffer.AsMemory(), session.ReaderCancellation.Token).ConfigureAwait(false);
                if (read == 0) break;
                session.Diagnostics.Append(buffer.AsSpan(0, read));
            }
        }
        catch (OperationCanceledException) when (session.ReaderCancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException) { }
    }

    private async Task CancelAndStopIfNeededAsync(WorkerSession session, string requestId, PendingRequest pending)
    {
        try
        {
            if (session.IsUsable)
            {
                using var deadline = new CancellationTokenSource(_options.CancelGracePeriod);
                await WriteAsync(session, VietsubTranslationWorkerProtocol.Create(VietsubTranslationWorkerProtocol.Cancel, requestId),
                    deadline.Token).ConfigureAwait(false);
                if (await Task.WhenAny(pending.Completion.Task, Task.Delay(_options.CancelGracePeriod)).ConfigureAwait(false)
                    == pending.Completion.Task) return;
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException
            or OperationCanceledException or VietsubTranslationException or VietsubTranslationWorkerProtocolException) { }
        await StopWorkerAsync(kill: true, expected: session).ConfigureAwait(false);
    }

    private static async Task WriteAsync(WorkerSession session, VietsubTranslationWorkerEnvelope envelope,
        CancellationToken cancellationToken, bool shutdown = false)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            shutdown ? CancellationToken.None : session.StopCancellation.Token);
        await session.WriteGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            await VietsubTranslationWorkerFrameCodec.WriteAsync(session.Input, envelope, linked.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            session.StopCancellation.Token.ThrowIfCancellationRequested();
            throw new VietsubTranslationException(VietsubTranslationErrorCodes.ProcessCrashed,
                "Không thể gửi yêu cầu tới translation worker.", retryable: true, innerException: exception);
        }
        finally { session.WriteGate.Release(); }
    }

    private async Task StopWorkerAsync(bool kill, WorkerSession? expected = null)
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (expected is null || ReferenceEquals(expected, _session))
                await StopWorkerCoreAsync(kill).ConfigureAwait(false);
        }
        finally { _lifecycleGate.Release(); }
    }

    // Called only while holding the lifecycle gate. Pumps never acquire this gate.
    private async Task StopWorkerCoreAsync(bool kill)
    {
        var session = _session;
        if (session is null) return;
        session.RequestStop();
        if (session.IsProcessAlive && !kill)
        {
            try
            {
                using var deadline = new CancellationTokenSource(_options.CancelGracePeriod);
                await WriteAsync(session, VietsubTranslationWorkerProtocol.Create(
                    VietsubTranslationWorkerProtocol.Shutdown, Guid.NewGuid().ToString("N")), deadline.Token, shutdown: true)
                    .ConfigureAwait(false);
                await session.Process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException
                or OperationCanceledException or InvalidOperationException
                or VietsubTranslationException or VietsubTranslationWorkerProtocolException)
            {
                kill = true;
            }
        }

        if (session.IsProcessAlive && kill)
        {
            try
            {
                session.Process.Kill(entireProcessTree: true);
                await session.Process.WaitForExitAsync().WaitAsync(_options.CancelGracePeriod).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or System.ComponentModel.Win32Exception or TimeoutException)
            {
                if (session.IsProcessAlive)
                    throw new VietsubTranslationException(VietsubTranslationErrorCodes.ProcessTimeout,
                        "Chưa dừng được worker cũ; hãy thử lại trước khi bắt đầu worker mới.", true, exception);
            }
        }

        // Keep the session attached until all readers have finished. If a pipe gets
        // stuck, cancel/close it and wait once more; never silently abandon its task.
        var pumps = Task.WhenAll(session.ResponsePump, session.StderrPump);
        try { await pumps.WaitAsync(_options.CancelGracePeriod).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            session.ReaderCancellation.Cancel();
            session.Output.Dispose();
            session.Error.Dispose();
            try { await pumps.WaitAsync(_options.CancelGracePeriod).ConfigureAwait(false); }
            catch (TimeoutException exception)
            {
                throw new VietsubTranslationException(VietsubTranslationErrorCodes.ProcessTimeout,
                    "Chưa đóng được kênh giao tiếp của worker cũ; hãy thử lại.", true, exception);
            }
        }

        session.Diagnostics.Append($"client_worker_cleanup:pid={session.ProcessId}:code={session.Process.ExitCode}\n");
        Volatile.Write(ref _lastDiagnostics, session.Diagnostics.ToString());
        await session.WriteGate.WaitAsync().ConfigureAwait(false);
        try
        {
            session.Input.Dispose();
            session.Output.Dispose();
            session.Error.Dispose();
            session.Process.Dispose();
            session.ReaderCancellation.Dispose();
            // StopCancellation and managed semaphores remain valid for callers
            // already unwinding this session. They have no allocated wait handles.
            Volatile.Write(ref _session, null);
        }
        finally { session.WriteGate.Release(); }
    }

    private static async Task<int?> ReadExitCodeAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException) { return null; }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            if (_disposeTask is not null) return new ValueTask(_disposeTask);
            _disposed = true;
            _disposeCancellation.Cancel();
            _disposeTask = DisposeCoreAsync();
            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await StopWorkerAsync(kill: false).ConfigureAwait(false);
        await _operationGate.WaitAsync().ConfigureAwait(false);
        _operationGate.Release();
        // Do not dispose gates underneath queued callers that still must release
        // them after observing disposal/cancellation.
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class WorkerSession
    {
        private Exception? _terminalFailure;
        private int _stopping;
        public WorkerSession(Process process)
        {
            Process = process;
            ProcessId = process.Id;
            Input = process.StandardInput.BaseStream;
            Output = process.StandardOutput.BaseStream;
            Error = process.StandardError;
        }

        public Process Process { get; }
        public int ProcessId { get; }
        public Stream Input { get; }
        public Stream Output { get; set; }
        public StreamReader Error { get; }
        public Task ResponsePump { get; set; } = Task.CompletedTask;
        public Task StderrPump { get; set; } = Task.CompletedTask;
        public CancellationTokenSource ReaderCancellation { get; } = new();
        public CancellationTokenSource StopCancellation { get; } = new();
        public SemaphoreSlim WriteGate { get; } = new(1, 1);
        public ConcurrentDictionary<string, PendingRequest> Pending { get; } = new(StringComparer.Ordinal);
        public TaskCompletionSource<VietsubTranslationWorkerHello> Hello { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public BoundedTextBuffer Diagnostics { get; } = new(MaximumDiagnosticCharacters);
        public string? LoadedConfigFingerprint;
        public bool IsStopping => Volatile.Read(ref _stopping) != 0;
        public bool IsUsable => !IsStopping && Volatile.Read(ref _terminalFailure) is null && IsProcessAlive;
        public bool IsProcessAlive
        {
            get
            {
                try { return !Process.HasExited; }
                catch (InvalidOperationException) { return false; }
            }
        }

        public void Register(string requestId, PendingRequest pending)
        {
            if (!Pending.TryAdd(requestId, pending)) throw new InvalidOperationException("Worker request ID collision.");
            // Register-before-check closes the EOF/reset vs request-registration race.
            if (Volatile.Read(ref _terminalFailure) is { } failure) Complete(pending.Completion, failure);
        }

        public void RequestStop()
        {
            Interlocked.Exchange(ref _stopping, 1);
            Terminate(new OperationCanceledException("Phiên worker đã được chủ động dừng."));
            StopCancellation.Cancel();
        }

        public void Terminate(Exception failure)
        {
            if (Interlocked.CompareExchange(ref _terminalFailure, failure, null) is not null) return;
            LoadedConfigFingerprint = null;
            Complete(Hello, failure);
            foreach (var pending in Pending.Values) Complete(pending.Completion, failure);
        }

        private static void Complete<T>(TaskCompletionSource<T> completion, Exception failure)
        {
            if (failure is OperationCanceledException) completion.TrySetCanceled();
            else completion.TrySetException(failure);
        }
    }

    private sealed class PendingRequest(IProgress<VietsubTranslationWorkerProgress>? progress)
    {
        public IProgress<VietsubTranslationWorkerProgress>? Progress { get; } = progress;
        public TaskCompletionSource<VietsubTranslationWorkerEnvelope> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BoundedTextBuffer(int capacity)
    {
        private readonly object _sync = new();
        private readonly StringBuilder _value = new(capacity);

        public void Append(ReadOnlySpan<char> value)
        {
            lock (_sync)
            {
                if (value.Length >= capacity) { _value.Clear(); _value.Append(value[^capacity..]); return; }
                var overflow = _value.Length + value.Length - capacity;
                if (overflow > 0) _value.Remove(0, overflow);
                _value.Append(value);
            }
        }

        public override string ToString() { lock (_sync) return _value.ToString(); }
    }
}
