using System.Diagnostics;
using TOOL_LOCAL.Vietsub.Translation;

namespace VideoMaker.Vietsub.Translation.Worker;

internal sealed class TranslationWorkerHost : IAsyncDisposable
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly TextWriter _error;
    private readonly ITranslationWorkerEngine _engine;
    private readonly string? _testMode;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _activeLock = new();
    private ActiveOperation? _active;
    private bool _disposed;

    public TranslationWorkerHost(
        Stream input,
        Stream output,
        TextWriter error,
        ITranslationWorkerEngine engine,
        string? testMode)
    {
        _input = input;
        _output = output;
        _error = error;
        _engine = engine;
        _testMode = testMode;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await WriteAsync(VietsubTranslationWorkerProtocol.Create(
            VietsubTranslationWorkerProtocol.Hello,
            "worker",
            new VietsubTranslationWorkerHello(
                VietsubTranslationWorkerProtocol.WorkerVersion,
                QwenTranslationWorkerEngine.EngineId,
                QwenTranslationWorkerEngine.EngineVersion,
                VietsubTranslationWorkerProtocol.Version,
                Environment.ProcessId)), cancellationToken);
        await _error.WriteLineAsync(
            $"worker_started:worker={VietsubTranslationWorkerProtocol.WorkerVersion}:engine={QwenTranslationWorkerEngine.EngineVersion}:protocol={VietsubTranslationWorkerProtocol.Version}");

        if (_testMode == "hang-after-hello")
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return;
        }

        if (_testMode == "hang-with-child")
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Worker process path is unavailable.");
            var childStart = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            childStart.Environment["VIDEOMAKER_TRANSLATION_WORKER_TEST_MODE"] = "hang-after-hello";
            using var child = Process.Start(childStart)
                ?? throw new InvalidOperationException("Unable to start worker test child.");
            await _error.WriteLineAsync($"child_pid:{child.Id}");
            await _error.FlushAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return;
        }

        if (_testMode == "stderr-large")
        {
            for (var index = 0; index < 4096; index++)
            {
                await _error.WriteLineAsync($"bounded-stderr-test-{index:D4}-{new string('x', 480)}");
            }
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            VietsubTranslationWorkerEnvelope? envelope;
            try
            {
                envelope = await VietsubTranslationWorkerFrameCodec
                    .ReadAsync(_input, cancellationToken)
                    .WaitAsync(IdleTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                await _error.WriteLineAsync("worker_idle_timeout");
                break;
            }
            if (envelope is null)
            {
                break;
            }

            if (_testMode == "crash-after-hello")
            {
                Environment.Exit(197);
            }

            if (_testMode == "malformed-after-hello")
            {
                await _output.WriteAsync(new byte[] { 1, 0, 0, 0, 0xff }, cancellationToken);
                await _output.FlushAsync(cancellationToken);
                return;
            }

            switch (envelope.Type)
            {
                case VietsubTranslationWorkerProtocol.Load:
                case VietsubTranslationWorkerProtocol.ProbeHardware:
                case VietsubTranslationWorkerProtocol.Infer:
                    StartOperation(envelope, cancellationToken);
                    break;
                case VietsubTranslationWorkerProtocol.Cancel:
                    CancelOperation(envelope.RequestId);
                    break;
                case VietsubTranslationWorkerProtocol.Shutdown:
                    await StopActiveAsync();
                    return;
                default:
                    await WriteErrorAsync(
                        envelope.RequestId,
                        VietsubTranslationErrorCodes.WorkerProtocolInvalid,
                        "Worker không hỗ trợ loại message đã nhận.",
                        retryable: false,
                        cancellationToken);
                    break;
            }
        }

        await StopActiveAsync();
    }

    private void StartOperation(
        VietsubTranslationWorkerEnvelope envelope,
        CancellationToken hostCancellationToken)
    {
        ActiveOperation operation;
        lock (_activeLock)
        {
            if (_active is { Task.IsCompleted: false, EngineCompleted: false })
            {
                _ = WriteErrorAsync(
                    envelope.RequestId,
                    VietsubTranslationErrorCodes.JobConflict,
                    "Worker chỉ xử lý một yêu cầu suy luận tại một thời điểm.",
                    retryable: true,
                    CancellationToken.None);
                return;
            }

            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(hostCancellationToken);
            operation = new ActiveOperation(envelope.RequestId, cancellation);
            _active = operation;
            operation.Task = HandleOperationAsync(envelope, operation, cancellation.Token);
        }

        _ = operation.Task.ContinueWith(
            _ =>
            {
                lock (_activeLock)
                {
                    if (ReferenceEquals(_active, operation))
                    {
                        _active = null;
                    }
                }

                operation.Cancellation.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task HandleOperationAsync(
        VietsubTranslationWorkerEnvelope envelope,
        ActiveOperation operation,
        CancellationToken cancellationToken)
    {
        try
        {
            object result = envelope.Type switch
            {
                VietsubTranslationWorkerProtocol.ProbeHardware => CudaHardwareProbe.Capture(),
                VietsubTranslationWorkerProtocol.Load => await _engine.LoadAsync(
                    VietsubTranslationWorkerProtocol.ReadPayload<VietsubTranslationWorkerLoadRequest>(envelope),
                    cancellationToken),
                VietsubTranslationWorkerProtocol.Infer => await _engine.InferAsync(
                    VietsubTranslationWorkerProtocol.ReadPayload<VietsubTranslationWorkerInferRequest>(envelope),
                    cancellationToken),
                _ => throw new VietsubTranslationWorkerProtocolException("Unsupported operation.")
            };
            operation.MarkEngineCompleted();
            if (result is VietsubTranslationWorkerLoadResult load)
            {
                await _error.WriteLineAsync(
                    $"load_succeeded:backend={load.BackendIdentity}:avx={load.AvxLevel}:native_sha256={load.NativeLibraryHash}:elapsed_ms={load.Metrics.ElapsedMilliseconds}:peak_ws={load.Metrics.PeakWorkingSetBytes}");
            }
            else if (result is VietsubTranslationWorkerInferResult inference)
            {
                var stage = envelope.Type == VietsubTranslationWorkerProtocol.Infer
                    ? VietsubTranslationWorkerProtocol.ReadPayload<VietsubTranslationWorkerInferRequest>(envelope).Stage
                    : "unknown";
                await _error.WriteLineAsync(
                    $"infer_succeeded:stage={stage}:elapsed_ms={inference.Metrics.ElapsedMilliseconds}:peak_ws={inference.Metrics.PeakWorkingSetBytes}");
            }
            await WriteAsync(VietsubTranslationWorkerProtocol.Create(
                VietsubTranslationWorkerProtocol.Result,
                envelope.RequestId,
                result), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            operation.MarkEngineCompleted();
            await WriteErrorAsync(
                envelope.RequestId,
                VietsubTranslationErrorCodes.ProcessFailed,
                "Yêu cầu worker đã được hủy.",
                retryable: true,
                CancellationToken.None);
        }
        catch (TranslationWorkerException exception)
        {
            operation.MarkEngineCompleted();
            await WriteErrorAsync(
                envelope.RequestId,
                exception.Code,
                exception.Message,
                exception.Retryable,
                CancellationToken.None);
        }
        catch (VietsubTranslationWorkerProtocolException exception)
        {
            operation.MarkEngineCompleted();
            await WriteErrorAsync(
                envelope.RequestId,
                VietsubTranslationErrorCodes.WorkerProtocolInvalid,
                exception.Message,
                retryable: false,
                CancellationToken.None);
        }
        catch (OutOfMemoryException)
        {
            operation.MarkEngineCompleted();
            await WriteErrorAsync(
                envelope.RequestId,
                VietsubTranslationErrorCodes.RuntimeOutOfMemory,
                "Worker không còn đủ bộ nhớ để hoàn tất yêu cầu.",
                retryable: false,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            operation.MarkEngineCompleted();
            _error.WriteLine($"operation_error:{exception.GetType().Name}");
            await WriteErrorAsync(
                envelope.RequestId,
                VietsubTranslationErrorCodes.ProcessFailed,
                "Worker gặp lỗi khi xử lý yêu cầu local.",
                retryable: true,
                CancellationToken.None);
        }
    }

    private void CancelOperation(string requestId)
    {
        lock (_activeLock)
        {
            if (_active is not null
                && string.Equals(_active.RequestId, requestId, StringComparison.Ordinal))
            {
                _active.Cancellation.Cancel();
            }
        }
    }

    private async Task StopActiveAsync()
    {
        ActiveOperation? operation;
        lock (_activeLock)
        {
            operation = _active;
            operation?.Cancellation.Cancel();
        }

        if (operation?.Task is not null)
        {
            try
            {
                await operation.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException)
            {
                // Native decode can ignore cancellation until a batch finishes. Never dispose its
                // context/weights or write gate concurrently. The isolated process owns these resources.
                Environment.Exit(70);
            }
        }
    }

    private Task WriteErrorAsync(
        string requestId,
        string code,
        string message,
        bool retryable,
        CancellationToken cancellationToken) => WriteAsync(
        VietsubTranslationWorkerProtocol.Create(
            VietsubTranslationWorkerProtocol.Error,
            requestId,
            errorCode: code,
            message: message,
            retryable: retryable),
        cancellationToken);

    private async Task WriteAsync(
        VietsubTranslationWorkerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await VietsubTranslationWorkerFrameCodec.WriteAsync(_output, envelope, cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopActiveAsync();
        _writeGate.Dispose();
    }

    private sealed class ActiveOperation(
        string requestId,
        CancellationTokenSource cancellation)
    {
        public string RequestId { get; } = requestId;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        public Task Task { get; set; } = Task.CompletedTask;

        public bool EngineCompleted => Volatile.Read(ref _engineCompleted) == 1;

        private int _engineCompleted;

        public void MarkEngineCompleted() => Volatile.Write(ref _engineCompleted, 1);
    }
}
