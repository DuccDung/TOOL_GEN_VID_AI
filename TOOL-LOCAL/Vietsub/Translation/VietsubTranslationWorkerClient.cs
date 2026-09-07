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
        Path.Combine(
            AppContext.BaseDirectory,
            "_translation_worker",
            "VideoMaker.Vietsub.TranslationWorker.exe"),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromMinutes(4),
        TimeSpan.FromMinutes(8),
        TimeSpan.FromSeconds(2));
}

internal sealed class VietsubTranslationWorkerClient : IAsyncDisposable
{
    private const int MaximumDiagnosticCharacters = 32 * 1024;
    private readonly VietsubTranslationWorkerClientOptions _options;
    private readonly IVietsubTranslationMemoryProbe _memoryProbe;
    private readonly VietsubTranslationResourceRequirements _resourceRequirements;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly ConcurrentDictionary<string, PendingRequest> _pending = new(StringComparer.Ordinal);
    private readonly BoundedTextBuffer _stderr = new(MaximumDiagnosticCharacters);
    private Process? _process;
    private Stream? _input;
    private Stream? _output;
    private Task? _responsePump;
    private Task? _stderrPump;
    private TaskCompletionSource<VietsubTranslationWorkerHello>? _hello;
    private string? _loadedConfigFingerprint;
    private bool _disposed;

    public VietsubTranslationWorkerClient(
        VietsubTranslationWorkerClientOptions? options = null,
        IVietsubTranslationMemoryProbe? memoryProbe = null,
        VietsubTranslationResourceRequirements? resourceRequirements = null)
    {
        _options = options ?? VietsubTranslationWorkerClientOptions.CreateDefault();
        _memoryProbe = memoryProbe ?? new WindowsVietsubTranslationMemoryProbe();
        _resourceRequirements = resourceRequirements ?? VietsubTranslationResourceRequirements.SafeCpuBaseline;
    }

    public string LastDiagnostics => _stderr.ToString();

    public string WorkerBinaryFingerprint
    {
        get
        {
            var executable = Path.GetFullPath(_options.WorkerExecutablePath);
            return VietsubTranslationWorkerProtocol.ComputeWorkerBinaryFingerprint(
                Path.GetDirectoryName(executable)!);
        }
    }

    public bool IsLoaded(string configFingerprint) =>
        IsWorkerAlive()
        && string.Equals(_loadedConfigFingerprint, configFingerprint, StringComparison.Ordinal);

    public async Task<VietsubTranslationWorkerLoadResult> LoadAsync(
        VietsubTranslationWorkerLoadRequest request,
        IProgress<VietsubTranslationWorkerProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = await SendAsync<VietsubTranslationWorkerLoadResult>(
            VietsubTranslationWorkerProtocol.Load,
            request,
            _options.LoadTimeout,
            progress,
            cancellationToken);
        _loadedConfigFingerprint = result.ConfigFingerprint;
        return result;
    }

    public Task<VietsubTranslationWorkerInferResult> InferAsync(
        VietsubTranslationWorkerInferRequest request,
        IProgress<VietsubTranslationWorkerProgress>? progress,
        CancellationToken cancellationToken) => SendAsync<VietsubTranslationWorkerInferResult>(
        VietsubTranslationWorkerProtocol.Infer,
        request,
        _options.InferenceTimeout,
        progress,
            cancellationToken);

    public Task ResetAsync() => StopWorkerAsync(kill: true);

    private async Task<T> SendAsync<T>(
        string type,
        object payload,
        TimeSpan timeout,
        IProgress<VietsubTranslationWorkerProgress>? progress,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken);
        var requestId = Guid.NewGuid().ToString("N");
        var pending = new PendingRequest(progress);
        try
        {
            var startupRequirements = payload is VietsubTranslationWorkerLoadRequest loadRequest
                ? loadRequest.ResourceRequirements
                : _resourceRequirements;
            var resourceWarningAccepted = payload is VietsubTranslationWorkerLoadRequest acceptedLoad
                && acceptedLoad.ResourceWarningAccepted;
            await EnsureStartedAsync(
                startupRequirements,
                resourceWarningAccepted,
                cancellationToken);
            if (!_pending.TryAdd(requestId, pending))
            {
                throw new InvalidOperationException("Worker request ID collision.");
            }

            await WriteAsync(
                VietsubTranslationWorkerProtocol.Create(type, requestId, payload),
                cancellationToken);
            VietsubTranslationWorkerEnvelope response;
            try
            {
                response = await pending.Completion.Task.WaitAsync(timeout, cancellationToken);
            }
            catch (TimeoutException exception)
            {
                await CancelAndStopIfNeededAsync(requestId, pending);
                throw new VietsubTranslationException(
                    VietsubTranslationErrorCodes.ProcessTimeout,
                    "Worker dịch local không phản hồi trong thời gian cho phép.",
                    retryable: true,
                    innerException: exception);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await CancelAndStopIfNeededAsync(requestId, pending);
                throw;
            }

            if (response.Type == VietsubTranslationWorkerProtocol.Error)
            {
                throw new VietsubTranslationException(
                    response.ErrorCode ?? VietsubTranslationErrorCodes.ProcessFailed,
                    response.Message ?? "Worker dịch local trả về lỗi.",
                    response.Retryable);
            }

            if (response.Type != VietsubTranslationWorkerProtocol.Result)
            {
                throw new VietsubTranslationException(
                    VietsubTranslationErrorCodes.WorkerProtocolInvalid,
                    "Worker trả về loại response không hợp lệ.");
            }

            return VietsubTranslationWorkerProtocol.ReadPayload<T>(response);
        }
        catch (VietsubTranslationWorkerProtocolException exception)
        {
            await StopWorkerAsync(kill: true);
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.WorkerProtocolInvalid,
                "Giao thức worker dịch local không hợp lệ.",
                retryable: false,
                innerException: exception);
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
            _operationGate.Release();
        }
    }

    private async Task EnsureStartedAsync(
        VietsubTranslationResourceRequirements resourceRequirements,
        bool resourceWarningAccepted,
        CancellationToken cancellationToken)
    {
        if (IsWorkerAlive())
        {
            return;
        }

        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (IsWorkerAlive())
            {
                return;
            }

            CleanupExitedProcess();
            var resources = VietsubTranslationResourceGate.Evaluate(_memoryProbe, resourceRequirements);
            if (!resources.CanLoad
                && !(resources.RequiresConfirmation && resourceWarningAccepted))
            {
                throw new VietsubTranslationException(
                    resources.RequiresConfirmation
                        ? VietsubTranslationErrorCodes.ResourceConfirmationRequired
                        : VietsubTranslationErrorCodes.RuntimeUnsupportedPlatform,
                    resources.Message,
                    retryable: false);
            }

            var executable = Path.GetFullPath(_options.WorkerExecutablePath);
            if (!File.Exists(executable))
            {
                throw new VietsubTranslationException(
                    VietsubTranslationErrorCodes.BackendLoadFailed,
                    "Thiếu translation worker trong bộ cài desktop; hãy sửa hoặc cập nhật ứng dụng.");
            }

            _stderr.Clear();
            _hello = new TaskCompletionSource<VietsubTranslationWorkerHello>(
                TaskCreationOptions.RunContinuationsAsynchronously);
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
            {
                foreach (var pair in _options.EnvironmentVariables)
                {
                    startInfo.Environment[pair.Key] = pair.Value;
                }
            }

            try
            {
                _process = Process.Start(startInfo)
                    ?? throw new VietsubTranslationException(
                        VietsubTranslationErrorCodes.BackendLoadFailed,
                        "Không thể khởi động translation worker.");
            }
            catch (Exception exception) when (exception is
                System.ComponentModel.Win32Exception or
                InvalidOperationException or
                IOException)
            {
                throw new VietsubTranslationException(
                    VietsubTranslationErrorCodes.BackendLoadFailed,
                    "Không thể khởi động translation worker trong bộ cài desktop.",
                    retryable: false,
                    innerException: exception);
            }
            _input = _process.StandardInput.BaseStream;
            _output = _process.StandardOutput.BaseStream;
            _responsePump = PumpResponsesAsync(_process, _output);
            _stderrPump = PumpStderrAsync(_process.StandardError);
            VietsubTranslationWorkerHello hello;
            try
            {
                hello = await _hello.Task.WaitAsync(_options.StartupTimeout, cancellationToken);
            }
            catch (TimeoutException exception)
            {
                await StopWorkerCoreAsync(kill: true);
                throw new VietsubTranslationException(
                    VietsubTranslationErrorCodes.ProcessTimeout,
                    "Translation worker không hoàn tất startup handshake.",
                    retryable: true,
                    innerException: exception);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await StopWorkerCoreAsync(kill: true);
                throw;
            }

            if (hello.ProtocolVersion != VietsubTranslationWorkerProtocol.Version
                || !string.Equals(hello.WorkerVersion, VietsubTranslationWorkerProtocol.WorkerVersion, StringComparison.Ordinal)
                || !string.Equals(hello.EngineId, VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km.EngineId, StringComparison.Ordinal)
                || !string.Equals(hello.EngineVersion, VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km.EngineVersion, StringComparison.Ordinal))
            {
                await StopWorkerCoreAsync(kill: true);
                throw new VietsubTranslationException(
                    VietsubTranslationErrorCodes.WorkerProtocolInvalid,
                    "Translation worker sai protocol, engine hoặc version đã pin.");
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task PumpResponsesAsync(Process process, Stream output)
    {
        Exception? failure = null;
        try
        {
            while (true)
            {
                var response = await VietsubTranslationWorkerFrameCodec.ReadAsync(output, CancellationToken.None);
                if (response is null)
                {
                    break;
                }

                if (response.Type == VietsubTranslationWorkerProtocol.Hello)
                {
                    _hello?.TrySetResult(
                        VietsubTranslationWorkerProtocol.ReadPayload<VietsubTranslationWorkerHello>(response));
                    continue;
                }

                if (!_pending.TryGetValue(response.RequestId, out var pending))
                {
                    continue;
                }

                if (response.Type == VietsubTranslationWorkerProtocol.Progress)
                {
                    pending.Progress?.Report(
                        VietsubTranslationWorkerProtocol.ReadPayload<VietsubTranslationWorkerProgress>(response));
                    continue;
                }

                pending.Completion.TrySetResult(response);
            }
        }
        catch (Exception exception) when (exception is
            IOException or
            EndOfStreamException or
            ObjectDisposedException or
            VietsubTranslationWorkerProtocolException)
        {
            failure = exception;
        }

        _loadedConfigFingerprint = null;
        var exitCode = TryGetExitCode(process);
        var code = failure is VietsubTranslationWorkerProtocolException
            ? VietsubTranslationErrorCodes.WorkerProtocolInvalid
            : VietsubTranslationErrorCodes.ProcessCrashed;
        var exceptionToReport = new VietsubTranslationException(
            code,
            exitCode is null
                ? "Translation worker đã thoát trước khi trả kết quả."
                : $"Translation worker đã thoát bất thường (exit code {exitCode}).",
            retryable: code == VietsubTranslationErrorCodes.ProcessCrashed,
            innerException: failure);
        _hello?.TrySetException(exceptionToReport);
        foreach (var pending in _pending.Values)
        {
            pending.Completion.TrySetException(exceptionToReport);
        }
    }

    private async Task PumpStderrAsync(StreamReader error)
    {
        var buffer = new char[1024];
        try
        {
            while (true)
            {
                var read = await error.ReadAsync(buffer);
                if (read == 0)
                {
                    break;
                }

                _stderr.Append(buffer.AsSpan(0, read));
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }

    private async Task CancelAndStopIfNeededAsync(string requestId, PendingRequest pending)
    {
        try
        {
            if (IsWorkerAlive())
            {
                await WriteAsync(
                    VietsubTranslationWorkerProtocol.Create(
                        VietsubTranslationWorkerProtocol.Cancel,
                        requestId),
                    CancellationToken.None);
                var completed = await Task.WhenAny(
                    pending.Completion.Task,
                    Task.Delay(_options.CancelGracePeriod));
                if (completed == pending.Completion.Task)
                {
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or VietsubTranslationWorkerProtocolException)
        {
        }

        await StopWorkerAsync(kill: true);
    }

    private async Task WriteAsync(
        VietsubTranslationWorkerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var input = _input ?? throw new VietsubTranslationException(
            VietsubTranslationErrorCodes.ProcessCrashed,
            "Translation worker chưa sẵn sàng.",
            retryable: true);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await VietsubTranslationWorkerFrameCodec.WriteAsync(input, envelope, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            throw new VietsubTranslationException(
                VietsubTranslationErrorCodes.ProcessCrashed,
                "Không thể gửi yêu cầu tới translation worker.",
                retryable: true,
                innerException: exception);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private Task StopWorkerAsync(bool kill) => WithLifecycleGateAsync(
        () => StopWorkerCoreAsync(kill));

    private async Task WithLifecycleGateAsync(Func<Task> action)
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task StopWorkerCoreAsync(bool kill)
    {
        var process = _process;
        if (process is null)
        {
            return;
        }

        if (!process.HasExited && !kill)
        {
            try
            {
                await WriteAsync(
                    VietsubTranslationWorkerProtocol.Create(
                        VietsubTranslationWorkerProtocol.Shutdown,
                        Guid.NewGuid().ToString("N")),
                    CancellationToken.None);
                await process.WaitForExitAsync().WaitAsync(_options.CancelGracePeriod);
            }
            catch (Exception exception) when (exception is
                IOException or
                ObjectDisposedException or
                TimeoutException or
                VietsubTranslationException or
                VietsubTranslationWorkerProtocolException)
            {
                kill = true;
            }
        }

        if (!process.HasExited && kill)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(_options.CancelGracePeriod);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
            {
            }
        }

        CleanupExitedProcess();
    }

    private bool IsWorkerAlive()
    {
        try
        {
            return _process is { HasExited: false };
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void CleanupExitedProcess()
    {
        _input?.Dispose();
        _output?.Dispose();
        _process?.Dispose();
        _input = null;
        _output = null;
        _process = null;
        _responsePump = null;
        _stderrPump = null;
        _hello = null;
        _loadedConfigFingerprint = null;
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.WaitForExit(500);
            }

            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopWorkerAsync(kill: false);
        _operationGate.Dispose();
        _writeGate.Dispose();
        _lifecycleGate.Dispose();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class PendingRequest(IProgress<VietsubTranslationWorkerProgress>? progress)
    {
        public IProgress<VietsubTranslationWorkerProgress>? Progress { get; } = progress;

        public TaskCompletionSource<VietsubTranslationWorkerEnvelope> Completion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BoundedTextBuffer(int capacity)
    {
        private readonly object _sync = new();
        private readonly StringBuilder _value = new(capacity);

        public void Append(ReadOnlySpan<char> value)
        {
            lock (_sync)
            {
                if (value.Length >= capacity)
                {
                    _value.Clear();
                    _value.Append(value[^capacity..]);
                    return;
                }

                var overflow = _value.Length + value.Length - capacity;
                if (overflow > 0)
                {
                    _value.Remove(0, overflow);
                }

                _value.Append(value);
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _value.Clear();
            }
        }

        public override string ToString()
        {
            lock (_sync)
            {
                return _value.ToString();
            }
        }
    }
}
