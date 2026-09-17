using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TOOL_LOCAL.Vietsub.Translation;

internal static class VietsubTranslationWorkerProtocol
{
    public const int Version = 2;
    public const int MaximumFrameBytes = 1024 * 1024;
    public const string WorkerVersion = "1.1.0";
    public const string ProbeHardware = "probeHardware";

    public const string Hello = "hello";
    public const string Load = "load";
    public const string Infer = "infer";
    public const string Cancel = "cancel";
    public const string Shutdown = "shutdown";
    public const string Progress = "progress";
    public const string Result = "result";
    public const string Error = "error";

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static VietsubTranslationWorkerEnvelope Create(
        string type,
        string requestId,
        object? payload = null,
        string? errorCode = null,
        string? message = null,
        bool retryable = false) => new(
            Version,
            type,
            requestId,
            payload is null ? null : JsonSerializer.SerializeToElement(payload, JsonOptions),
            errorCode,
            message,
            retryable);

    public static T ReadPayload<T>(VietsubTranslationWorkerEnvelope envelope)
    {
        if (envelope.Payload is null
            || envelope.Payload.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new VietsubTranslationWorkerProtocolException("Worker message is missing its payload.");
        }

        try
        {
            return envelope.Payload.Value.Deserialize<T>(JsonOptions)
                ?? throw new VietsubTranslationWorkerProtocolException("Worker payload is null.");
        }
        catch (JsonException exception)
        {
            throw new VietsubTranslationWorkerProtocolException("Worker payload schema is invalid.", exception);
        }
    }

    public static void ValidateEnvelope(VietsubTranslationWorkerEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.ProtocolVersion != Version)
        {
            throw new VietsubTranslationWorkerProtocolException(
                $"Unsupported worker protocol version {envelope.ProtocolVersion}.");
        }

        if (string.IsNullOrWhiteSpace(envelope.Type) || envelope.Type.Length > 32)
        {
            throw new VietsubTranslationWorkerProtocolException("Worker message type is invalid.");
        }

        if (string.IsNullOrWhiteSpace(envelope.RequestId)
            || envelope.RequestId.Length > 64
            || envelope.RequestId.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new VietsubTranslationWorkerProtocolException("Worker request ID is invalid.");
        }
    }

    public static string ComputeConfigFingerprint(VietsubTranslationWorkerInferenceConfig config)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(config, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static string ComputeWorkerBinaryFingerprint(string workerDirectory)
    {
        var directory = Path.GetFullPath(workerDirectory);
        var native = Path.Combine(directory, "runtimes", "win-x64", "native", VietsubTranslationWorkerProfiles.SelectAvxName());
        var files = new[]
        {
            Path.Combine(directory, "VideoMaker.Vietsub.TranslationWorker.exe"),
            Path.Combine(directory, "VideoMaker.Vietsub.TranslationWorker.dll"),
            Path.Combine(directory, "LLamaSharp.dll"),
            Path.Combine(native, "llama.dll"),
            Path.Combine(native, "ggml.dll"),
            Path.Combine(native, "ggml-base.dll"),
            Path.Combine(native, "ggml-cpu.dll")
        };
        if (files.Any(path => !File.Exists(path)))
        {
            return string.Empty;
        }

        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var path in files)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = SHA256.HashData(stream);
            aggregate.AppendData(hash);
        }

        return Convert.ToHexString(aggregate.GetHashAndReset()).ToLowerInvariant();
    }
}

internal static class VietsubTranslationWorkerFrameCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static async Task WriteAsync(
        Stream stream,
        VietsubTranslationWorkerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        VietsubTranslationWorkerProtocol.ValidateEnvelope(envelope);
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            envelope,
            VietsubTranslationWorkerProtocol.JsonOptions);
        if (payload.Length is <= 0 or > VietsubTranslationWorkerProtocol.MaximumFrameBytes)
        {
            throw new VietsubTranslationWorkerProtocolException("Worker frame exceeds the allowed size.");
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<VietsubTranslationWorkerEnvelope?> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[sizeof(int)];
        var firstRead = await stream.ReadAsync(header.AsMemory(0, 1), cancellationToken);
        if (firstRead == 0)
        {
            return null;
        }

        await ReadExactlyAsync(stream, header.AsMemory(1), cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > VietsubTranslationWorkerProtocol.MaximumFrameBytes)
        {
            throw new VietsubTranslationWorkerProtocolException("Worker frame length is invalid.");
        }

        var payload = new byte[length];
        await ReadExactlyAsync(stream, payload, cancellationToken);
        try
        {
            _ = StrictUtf8.GetString(payload);
            var envelope = JsonSerializer.Deserialize<VietsubTranslationWorkerEnvelope>(
                payload,
                VietsubTranslationWorkerProtocol.JsonOptions)
                ?? throw new VietsubTranslationWorkerProtocolException("Worker frame contains null JSON.");
            VietsubTranslationWorkerProtocol.ValidateEnvelope(envelope);
            return envelope;
        }
        catch (DecoderFallbackException exception)
        {
            throw new VietsubTranslationWorkerProtocolException("Worker frame is not valid UTF-8.", exception);
        }
        catch (JsonException exception)
        {
            throw new VietsubTranslationWorkerProtocolException("Worker frame is not valid JSON.", exception);
        }
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Worker stream ended in the middle of a frame.");
            }

            offset += read;
        }
    }
}

internal sealed record VietsubTranslationWorkerEnvelope(
    int ProtocolVersion,
    string Type,
    string RequestId,
    JsonElement? Payload,
    string? ErrorCode,
    string? Message,
    bool Retryable);

internal sealed record VietsubTranslationWorkerHello(
    string WorkerVersion,
    string EngineId,
    string EngineVersion,
    int ProtocolVersion,
    int ProcessId);

internal sealed record VietsubTranslationWorkerInferenceConfig(
    int ContextSize,
    int MaximumGeneratedTokens,
    int BatchSize,
    int UBatchSize,
    int Threads,
    int GpuLayerCount,
    bool UseMemoryMap,
    string AvxPolicy,
    string ProfileId,
    string PromptProfileId,
    string SamplingProfileId,
    string? GpuDeviceId = null);

internal sealed record VietsubTranslationWorkerRuntimeProfile(
    string ProfileId,
    int ResourcePolicyVersion,
    bool IsLowMemory,
    VietsubTranslationWorkerInferenceConfig InferenceConfig,
    VietsubTranslationResourceRequirements ResourceRequirements,
    int MaximumTargetCues,
    int MaximumContextCues,
    int MaximumSourceCharacters,
    int MaximumOutputCharacters);

internal static class VietsubTranslationWorkerProfiles
{
    public const int ResourcePolicyVersion = 1;
    public const string StandardProfileId = "qwen3-cpu-safe-v2";
    public const string LowMemoryProfileId = "qwen3-cpu-low-memory-v1";

    public static VietsubTranslationWorkerRuntimeProfile CreateStandardCpuRuntimeProfile(int processorCount) =>
        new(
            StandardProfileId,
            ResourcePolicyVersion,
            IsLowMemory: false,
            new VietsubTranslationWorkerInferenceConfig(
                ContextSize: 4_096,
                MaximumGeneratedTokens: 1_024,
                BatchSize: 256,
                UBatchSize: 64,
                Threads: Math.Clamp(processorCount - 2, 2, 8),
                GpuLayerCount: 0,
                UseMemoryMap: true,
                AvxPolicy: "best-supported-cpu-up-to-avx2",
                ProfileId: StandardProfileId,
                PromptProfileId: "qwen3-vietsub-context-v2-no-think",
                SamplingProfileId: "deterministic-gbnf-v1"),
            VietsubTranslationResourceRequirements.StandardCpu,
            MaximumTargetCues: 12,
            MaximumContextCues: 3,
            MaximumSourceCharacters: 6_000,
            MaximumOutputCharacters: 8_000);

    public static VietsubTranslationWorkerRuntimeProfile CreateLowMemoryCpuRuntimeProfile(int processorCount) =>
        new(
            LowMemoryProfileId,
            ResourcePolicyVersion,
            IsLowMemory: true,
            new VietsubTranslationWorkerInferenceConfig(
                ContextSize: 4_096,
                MaximumGeneratedTokens: 768,
                BatchSize: 128,
                UBatchSize: 64,
                Threads: Math.Clamp(processorCount - 2, 2, 4),
                GpuLayerCount: 0,
                UseMemoryMap: true,
                AvxPolicy: "best-supported-cpu-up-to-avx2",
                ProfileId: LowMemoryProfileId,
                PromptProfileId: "qwen3-vietsub-context-v2-no-think",
                SamplingProfileId: "deterministic-gbnf-v1"),
            VietsubTranslationResourceRequirements.LowMemoryCpu,
            MaximumTargetCues: 6,
            MaximumContextCues: 2,
            MaximumSourceCharacters: 3_000,
            MaximumOutputCharacters: 4_000);

    public static VietsubTranslationWorkerRuntimeProfile CreateRuntimeProfile(
        string profileId,
        int processorCount) => profileId switch
        {
            StandardProfileId => CreateStandardCpuRuntimeProfile(processorCount),
            LowMemoryProfileId => CreateLowMemoryCpuRuntimeProfile(processorCount),
            _ => throw new ArgumentOutOfRangeException(nameof(profileId), "Translation runtime profile is not approved.")
        };

    public static VietsubTranslationWorkerInferenceConfig CreateSafeCpuProfile(int processorCount) =>
        CreateStandardCpuRuntimeProfile(processorCount).InferenceConfig;

    public static string SelectAvxName() => SelectAvxName(
        System.Runtime.Intrinsics.X86.Avx2.IsSupported,
        System.Runtime.Intrinsics.X86.Avx.IsSupported);

    public static string SelectAvxName(bool avx2Supported, bool avxSupported) => avx2Supported
        ? "avx2"
        : avxSupported
            ? "avx"
            : "noavx";
}

internal sealed record VietsubTranslationWorkerLoadRequest(
    string ComponentRoot,
    string ModelPath,
    string ComponentId,
    string EngineId,
    string EngineVersion,
    string ModelFileName,
    long ModelSizeBytes,
    string ModelSha256,
    VietsubTranslationWorkerInferenceConfig Config,
    VietsubTranslationResourceRequirements ResourceRequirements,
    bool ResourceWarningAccepted = false);

internal sealed record VietsubTranslationWorkerLoadResult(
    string BackendIdentity,
    string AvxLevel,
    string NativeLibraryHash,
    string ConfigFingerprint,
    VietsubTranslationWorkerMetrics Metrics,
    VietsubTranslationGpuDevice? Device = null,
    int OffloadedLayers = 0);

internal sealed record VietsubTranslationWorkerInferRequest(
    string Stage,
    string Prompt,
    string Grammar,
    int MaximumGeneratedTokens,
    int MaximumOutputCharacters);

internal sealed record VietsubTranslationWorkerInferResult(
    string RawOutput,
    VietsubTranslationWorkerMetrics Metrics);

internal sealed record VietsubTranslationWorkerProgress(
    string Stage,
    double Percent,
    string Message);

internal sealed record VietsubTranslationWorkerMetrics(
    long ElapsedMilliseconds,
    long WorkingSetBytes,
    long PrivateBytes,
    long PeakWorkingSetBytes,
    ulong AvailablePhysicalBytes,
    ulong AvailableCommitBytes,
    ulong PeakDeviceUsedBytes = 0);

internal sealed class VietsubTranslationWorkerProtocolException(
    string message,
    Exception? innerException = null) : Exception(message, innerException);
