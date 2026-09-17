using System.Security.Cryptography;

namespace TOOL_LOCAL.Vietsub.Translation;

internal static class VietsubTranslationExecutionPolicies
{
    public const string Auto = "AUTO";
    public const string CpuOnly = "CPU_ONLY";

    public static string Validate(string? value) => value switch
    {
        null or CpuOnly => CpuOnly,
        Auto => Auto,
        _ => throw new ArgumentException("Chế độ xử lý dịch phải là AUTO hoặc CPU_ONLY.")
    };
}

internal sealed record VietsubTranslationGpuDevice(
    string Id, string Name, int Ordinal, string DriverVersion,
    ulong TotalBytes, ulong FreeBytes, int ComputeMajor);

internal sealed record VietsubTranslationHardwareResult(
    IReadOnlyList<VietsubTranslationGpuDevice> Devices, string? ErrorCode = null);

internal static class VietsubTranslationGpuPlanner
{
    public const string PolicyVersion = "qwen4b-cuda12-v1";
    // Q4 weights + F16 KV at context 4096 + compute buffers + desktop reserve.
    // These are admission estimates; actual allocation and inference must also succeed.
    public const ulong FixedReserveBytes = 1024UL * 1024 * 1024;
    public const ulong PerLayerBytes = 86UL * 1024 * 1024;
    public static int SelectLayers(VietsubTranslationGpuDevice device) =>
        device.ComputeMajor < 6 ? 0 : new[] { 36, 24, 12 }
            .FirstOrDefault(layers => RequiredBytes(layers) <= device.FreeBytes);
    public static ulong RequiredBytes(int layers) => FixedReserveBytes + (ulong)layers * PerLayerBytes;
    public static int ReduceLayers(int layers) => layers switch { 36 => 24, 24 => 12, _ => 0 };
    public static bool CanFallback(string code) => code is
        "TRANSLATION_GPU_UNAVAILABLE" or "TRANSLATION_GPU_MEMORY" or
        "TRANSLATION_GPU_DRIVER" or "TRANSLATION_GPU_QUERY_UNAVAILABLE" or
        "TRANSLATION_GPU_INTEGRITY" or "TRANSLATION_RUNTIME_PROBE_FAILED" or
        "TRANSLATION_EN_PROBE_FAILED" or "TRANSLATION_ZH_PROBE_FAILED" or
        "TRANSLATION_BACKEND_LOAD_FAILED" or "TRANSLATION_RUNTIME_OUT_OF_MEMORY" or
        "TRANSLATION_PROCESS_CRASHED" or "TRANSLATION_PROCESS_TIMEOUT";

    public static string FallbackMessage(string? code) => (code switch
    {
        "TRANSLATION_GPU_NOT_INSTALLED" => "Máy chưa có đầy đủ gói tăng tốc NVIDIA.",
        "TRANSLATION_GPU_UNAVAILABLE" => "Không tìm thấy GPU NVIDIA phù hợp hoặc GPU không còn khả dụng.",
        "TRANSLATION_GPU_DRIVER" => "Driver NVIDIA chưa hỗ trợ chế độ tăng tốc này.",
        "TRANSLATION_GPU_QUERY_UNAVAILABLE" => "Không thể kiểm tra tài nguyên GPU NVIDIA.",
        "TRANSLATION_GPU_MEMORY" or "TRANSLATION_RUNTIME_OUT_OF_MEMORY" => "GPU không đủ bộ nhớ để dịch ổn định.",
        "TRANSLATION_GPU_INTEGRITY" => "Gói tăng tốc NVIDIA không vượt qua kiểm tra toàn vẹn và sẽ không được sử dụng.",
        "TRANSLATION_RUNTIME_PROBE_FAILED" or "TRANSLATION_EN_PROBE_FAILED" or "TRANSLATION_ZH_PROBE_FAILED"
            => "GPU không vượt qua bài kiểm tra dịch thử.",
        "TRANSLATION_PROCESS_TIMEOUT" => "GPU không phản hồi trong thời gian cho phép.",
        _ => "Không thể sử dụng tăng tốc GPU cho lần dịch này."
    }) + " Ứng dụng tự chuyển sang dịch bằng CPU, giữ các câu đã dịch.";
}

internal static class VietsubTranslationCudaPack
{
    public const string Version = "llamasharp-0.27.0-cuda12-12.4-v1";
    public const string IntegrityError = "TRANSLATION_GPU_INTEGRITY";
    public static string DirectoryPath(string componentsRoot) => Path.Combine(componentsRoot, "acceleration", Version);

    public static readonly IReadOnlyDictionary<string, string> NativeHashes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["ggml-base.dll"] = "feb9c403470ece921524aea6f5654111355308d11e583aa947c83a2e10530c94",
        ["ggml-cuda.dll"] = "5849c65cf176c082dd807db0f8d1337eea5003141c2fb9168a2fc12843d15a9a",
        ["ggml.dll"] = "ca90a3b2cb72e93417f0efc8246b8885db7e4624ac78977313f0f9f0d2ae4fc8",
        ["llama.dll"] = "ab4acb25175f70113d726d3889dfd214e5708c26731000ee81b836e34ee70441",
        ["cudart64_12.dll"] = "d28e42265da7462162a54da6b7a99ea4fa2caf8139d862bb500db875d0b32dfc",
        ["cublas64_12.dll"] = "e40202fe4223c1cd2d2dce7beec59e1ed61c7801bd827309183be9b50e358f4c",
        ["cublasLt64_12.dll"] = "2a896460bef60ed57ef32b0875812f355a6984e671d638bb632f5e8c1d7a831f",
        ["LICENSE-NVIDIA.txt"] = "e2c71babfd18a8e69542dd7e9ca018f9caa438094001a58e6bc4d8c999bf0d07"
    };

    public static string CpuKernelHash(string avx) => avx switch
    {
        "avx2" => "5c579f09d7b4f782c534b03f5962ff82cdddf9374aa443175f9db3cbc86b7b3c",
        "avx" => "447502e055c0df2ffd7ceba6e800e1608029bd1d6c760e5a1b29b2f6d9671a72",
        "noavx" => "bb3d65715350df4b9f2d605367e7063e27c40146330e6ed9807598c41aec08a5",
        _ => throw new InvalidDataException("CPU kernel chưa được duyệt.")
    };

    public static string Verify(string directory)
    {
        if (Directory.EnumerateFileSystemEntries(directory).Any(path =>
            !NativeHashes.ContainsKey(Path.GetFileName(path)) || Directory.Exists(path)))
            throw new InvalidDataException("Thư mục CUDA chứa dependency ngoài allowlist.");
        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (name, expected) in NativeHashes.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var path = Path.Combine(directory, name);
            using var stream = File.OpenRead(path);
            var hash = SHA256.HashData(stream);
            if (!Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Thư viện tăng tốc sai SHA-256 đã ghim.");
            aggregate.AppendData(hash);
        }
        return Convert.ToHexString(aggregate.GetHashAndReset()).ToLowerInvariant();
    }
}
