using System.ComponentModel;

namespace TOOL_LOCAL.Vietsub.Voice;

internal enum PiperInstallPhase
{
    Unknown,
    Validate,
    Extract,
    Promote,
    Python,
    Packages,
    Probe,
    Ready
}

// Safe local diagnostics only: never include a path, arguments, stdout, stderr or model input.
internal sealed record PiperInstallFailureDetails(int SchemaVersion, string Phase, string FailureKind,
    int? ExitCode, int? NativeErrorCode, int? HResult, int RuntimeRootCharacters,
    int EmbeddedPythonPathCharacters, int EnvironmentPathCharacters);

internal static class PiperInstallDiagnostics
{
    private const string DetailsKey = "VideoMaker.Piper.InstallFailure.v1";
    internal const int OutputDrainBufferCharacters = 4096;

    internal static PiperInstallFailureDetails? GetDetails(Exception error)
    {
        for (var depth = 0; error is not null && depth < 8; depth++, error = error.InnerException!)
            if (error.Data[DetailsKey] is PiperInstallFailureDetails details) return details;
        return null;
    }

    internal static VietsubVoiceException ProcessExited(PiperInstallPhase phase, int exitCode, string runtimeRoot)
    {
        var failure = new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInstallFailed,
            $"Không thể chuẩn bị Piper ở bước {PhaseName(phase)} (mã tiến trình {exitCode}).");
        failure.Data[DetailsKey] = Details(phase, "PROCESS_EXIT", runtimeRoot, exitCode: exitCode);
        return failure;
    }

    internal static VietsubVoiceException Normalize(PiperInstallPhase phase, Exception error, string runtimeRoot)
    {
        // Preserve the code and any precise phase/exit code already recorded by the process caller.
        if (error is VietsubVoiceException voice)
        {
            if (GetDetails(voice) is null)
                voice.Data[DetailsKey] = Details(phase, "COMPONENT_REJECTED", runtimeRoot, hResult: voice.HResult);
            return voice;
        }
        var native = error is Win32Exception win32 ? win32.NativeErrorCode
            : error is PathTooLongException ? 206 : (int?)null;
        var isPathLimit = error is PathTooLongException || native == 206;
        var kind = isPathLimit ? "PATH_TOO_LONG" : error is Win32Exception ? "PROCESS_START_FAILED"
            : error is UnauthorizedAccessException ? "ACCESS_DENIED" : "FILE_IO_FAILED";
        var message = isPathLimit
            ? $"Piper gặp lỗi giới hạn đường dẫn ở bước {PhaseName(phase)}. Cần kiểm tra cấu hình thư mục runtime."
            : $"Không thể chuẩn bị Piper ở bước {PhaseName(phase)}. Hãy kiểm tra quyền ghi và bộ ứng dụng.";
        var failure = new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInstallFailed, message);
        // Do not retain the original exception: native/IO messages can contain private paths or process output.
        failure.Data[DetailsKey] = Details(phase, kind, runtimeRoot, nativeError: native, hResult: error.HResult);
        return failure;
    }

    internal static async Task DrainOutputAsync(TextReader reader, CancellationToken token)
    {
        var buffer = new char[OutputDrainBufferCharacters];
        while (await reader.ReadAsync(buffer.AsMemory(), token) != 0) { }
    }

    private static PiperInstallFailureDetails Details(PiperInstallPhase phase, string kind, string runtimeRoot,
        int? exitCode = null, int? nativeError = null, int? hResult = null) => new(1, PhaseName(phase), kind,
        exitCode, nativeError, hResult, runtimeRoot.Length,
        Path.Combine(runtimeRoot, "python", "python.exe").Length, Path.Combine(runtimeRoot, ".venv").Length);

    private static string PhaseName(PiperInstallPhase phase) => Enum.IsDefined(phase)
        ? phase.ToString().ToUpperInvariant() : "UNKNOWN";
}
