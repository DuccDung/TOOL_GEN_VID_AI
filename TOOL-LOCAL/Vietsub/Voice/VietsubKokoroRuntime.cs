using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TOOL_LOCAL.Vietsub.Voice;

internal sealed record VietsubKokoroRuntimePaths(
    string PythonPath, string WorkerPath, string OnnxPath, string ConfigPath,
    string VoicePackPath, string RequestDirectory);

internal sealed class VietsubKokoroRuntime(VietsubVoiceComponentStore models)
{
    private const int ProtocolVersion = 1;
    private const string RuntimeVersion = "kokoro-onnx-python-3.11.15-locked-v1";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _root = Path.Combine(Path.GetDirectoryName(models.ComponentDirectory)!,
        "kokoro-vietnamese");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string RuntimeRoot => Path.Combine(_root, "runtime", RuntimeVersion);
    private string PythonPath => Path.Combine(RuntimeRoot, ".venv", "Scripts", "python.exe");
    private string RequestDirectory => Path.Combine(_root, "requests");
    private string EnvironmentMarkerPath => Path.Combine(RuntimeRoot, ".installed.json");
    private static string WorkerPath => Path.Combine(AppContext.BaseDirectory, "workers", "kokoro_worker.py");
    private static string RequirementsPath => Path.Combine(AppContext.BaseDirectory, "workers", "kokoro-requirements.lock");
    private string VoiceMarkerPath(string voiceId) => Path.Combine(_root,
        ".ready-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(voiceId))) + ".json");

    internal VietsubVoiceRuntimeStatus GetStatus(string voiceId)
    {
        var voice = VietsubVoiceModelCatalog.Find(voiceId)
            ?? throw new VietsubVoiceException(VietsubVoiceErrorCodes.ModelNotApproved, "Giọng Kokoro không thuộc danh mục đã duyệt.");
        var assets = models.GetModelStatuses().Single(item => item.VoiceId == voiceId);
        var bytes = assets.InstalledBytes;
        if (assets.Status != "READY")
            return Status("NOT_INSTALLED", false, voiceId, bytes, VietsubVoiceErrorCodes.ModelInvalid,
                "Cần cài tài nguyên giọng Kokoro trước khi tạo audio.");
        try
        {
            if (!File.Exists(PythonPath) || !File.Exists(WorkerPath) || !File.Exists(RequirementsPath)
                || !EnvironmentMarkerMatches())
                return Status("NOT_INSTALLED", false, voiceId, bytes, VietsubVoiceErrorCodes.RuntimeNotInstalled,
                    "Runtime Kokoro chưa được cài và kiểm tra trên máy này.");
            return VoiceMarkerMatches(voice)
                ? Status("READY", true, voiceId, bytes, null, "Kokoro đã tạo và kiểm tra WAV bằng giọng này.")
                : Status("INVALID", false, voiceId, bytes, VietsubVoiceErrorCodes.RuntimeInvalid,
                    "Giọng Kokoro cần được probe lại trước khi tạo audio.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return Status("INVALID", false, voiceId, bytes, VietsubVoiceErrorCodes.RuntimeInvalid,
                "Không thể xác minh runtime Kokoro.");
        }
    }

    internal VietsubKokoroRuntimePaths RequireReady(string voiceId)
    {
        var status = GetStatus(voiceId);
        if (!status.Ready)
            throw new VietsubVoiceException(status.ErrorCode ?? VietsubVoiceErrorCodes.RuntimeInvalid, status.Message);
        var paths = models.RequireKokoroModel(voiceId);
        return new(PythonPath, WorkerPath, paths.OnnxPath, paths.ConfigPath,
            paths.VoicePackPath, RequestDirectory);
    }

    internal async Task<VietsubVoiceRuntimeStatus> InstallAsync(string voiceId,
        IProgress<VietsubVoiceModelInstallProgress>? progress, CancellationToken token)
    {
        _ = models.RequireKokoroModel(voiceId);
        await _gate.WaitAsync(token);
        try
        {
            if (GetStatus(voiceId).Ready) return GetStatus(voiceId);
            TOOL_LOCAL.SystemSetup.SystemSetupPaths.EnsureSafeDirectory(RuntimeRoot);
            TOOL_LOCAL.SystemSetup.SystemSetupPaths.EnsureSafeDirectory(RequestDirectory);
            if (!EnvironmentMarkerMatches())
            {
                EnsureDiskSpace();
                var uv = models.RequireUvInstaller();
                progress?.Report(new(voiceId, "PYTHON", 75, "Đang cài Python cô lập cho Kokoro.", 0, 1));
                await RunUvAsync(uv, ["venv", "--clear", "--python", "3.11.15",
                    "--managed-python", "--no-config", Path.Combine(RuntimeRoot, ".venv")], token);
                progress?.Report(new(voiceId, "PACKAGES", 82, "Đang cài dependency Kokoro đã khóa hash.", 0, 1));
                await RunUvAsync(uv, ["pip", "install", "--python", PythonPath,
                    "--only-binary", ":all:", "--require-hashes", "--no-config",
                    "--default-index", "https://pypi.org/simple",
                    "--requirements", RequirementsPath], token);
                await WriteEnvironmentMarkerAsync(token);
            }
            progress?.Report(new(voiceId, "PROBE", 95, "Đang thử tạo WAV bằng giọng đã chọn.", 0, 1));
            var paths = models.RequireKokoroModel(voiceId);
            var runtimePaths = new VietsubKokoroRuntimePaths(PythonPath, WorkerPath, paths.OnnxPath,
                paths.ConfigPath, paths.VoicePackPath, RequestDirectory);
            var output = Path.Combine(RequestDirectory, "probe-" + Guid.NewGuid().ToString("N") + ".partial.wav");
            try
            {
                await new VietsubKokoroVoiceSynthesizer(runtimePaths).SynthesizeIncrementallyAsync(
                    [new VietsubVoiceSynthesisItem(0, "probe", "Xin chào, đây là giọng đọc tiếng Việt.",
                        output)], _ => ValueTask.CompletedTask, token);
                var metadata = VietsubWavInspector.Inspect(output, analyzeSilence: false);
                if (metadata.SampleRate != 24000 || metadata.Channels != 1 || metadata.DurationMilliseconds < 300)
                    throw new VietsubVoiceException(VietsubVoiceErrorCodes.ResultInvalid, "Probe Kokoro không tạo WAV hợp lệ.");
                await WriteVoiceMarkerAsync(voiceId, token);
            }
            finally { TryDelete(output); }
            var status = GetStatus(voiceId);
            if (!status.Ready)
                throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInvalid, status.Message);
            progress?.Report(new(voiceId, "READY", 100, status.Message, 1, 1));
            return status;
        }
        finally { _gate.Release(); }
    }

    private bool EnvironmentMarkerMatches()
    {
        if (!File.Exists(EnvironmentMarkerPath) || !File.Exists(PythonPath)
            || !File.Exists(RequirementsPath)) return false;
        var marker = JsonSerializer.Deserialize<EnvironmentMarker>(File.ReadAllText(EnvironmentMarkerPath), Json);
        return marker is not null && marker.RuntimeVersion == RuntimeVersion
            && marker.MachineFingerprint == TOOL_LOCAL.SystemSetup.SystemSetupPaths.MachineFingerprint
            && marker.RequirementsSha256 == HashFile(RequirementsPath)
            && marker.PythonSha256 == HashFile(PythonPath);
    }

    private bool VoiceMarkerMatches(VietsubVoiceModelDefinition voice)
    {
        var path = VoiceMarkerPath(voice.VoiceId);
        if (!File.Exists(path)) return false;
        var marker = JsonSerializer.Deserialize<VoiceMarker>(File.ReadAllText(path), Json);
        return marker is not null && marker.ProtocolVersion == ProtocolVersion
            && marker.VoiceId == voice.VoiceId
            && marker.RuntimeVersion == RuntimeVersion
            && marker.MachineFingerprint == TOOL_LOCAL.SystemSetup.SystemSetupPaths.MachineFingerprint
            && marker.WorkerSha256 == HashFile(WorkerPath)
            && marker.RequirementsSha256 == HashFile(RequirementsPath)
            && marker.OnnxSha256 == VietsubVoiceModelCatalog.CoreModel.Sha256
            && marker.ConfigSha256 == VietsubVoiceModelCatalog.Config.Sha256
            && marker.VoicePackSha256 == voice.VoicePack.Sha256;
    }

    private async Task WriteEnvironmentMarkerAsync(CancellationToken token)
    {
        var marker = new EnvironmentMarker(RuntimeVersion,
            TOOL_LOCAL.SystemSetup.SystemSetupPaths.MachineFingerprint,
            HashFile(RequirementsPath), HashFile(PythonPath));
        await WriteAtomicAsync(EnvironmentMarkerPath, marker, token);
    }

    private async Task WriteVoiceMarkerAsync(string voiceId, CancellationToken token)
    {
        var voice = VietsubVoiceModelCatalog.Find(voiceId)!;
        var marker = new VoiceMarker(ProtocolVersion, RuntimeVersion, voiceId,
            TOOL_LOCAL.SystemSetup.SystemSetupPaths.MachineFingerprint,
            HashFile(WorkerPath), HashFile(RequirementsPath),
            VietsubVoiceModelCatalog.CoreModel.Sha256,
            VietsubVoiceModelCatalog.Config.Sha256, voice.VoicePack.Sha256);
        await WriteAtomicAsync(VoiceMarkerPath(voiceId), marker, token);
    }

    private static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken token)
    {
        var partial = path + ".part";
        try
        {
            await File.WriteAllTextAsync(partial, JsonSerializer.Serialize(value, Json), token);
            File.Move(partial, path, overwrite: true);
        }
        finally { TryDelete(partial); }
    }

    private async Task RunUvAsync(string uv, IReadOnlyList<string> arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo {
            FileName = uv, WorkingDirectory = RuntimeRoot, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var key in start.Environment.Keys.Where(key =>
            key.StartsWith("UV_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("PIP_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("PYTHON", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        start.Environment["UV_CACHE_DIR"] = Path.Combine(RuntimeRoot, "cache");
        start.Environment["UV_PYTHON_INSTALL_DIR"] = Path.Combine(RuntimeRoot, "cpython");
        start.Environment["UV_PYTHON_NO_REGISTRY"] = "1";
        start.Environment["UV_NO_PROGRESS"] = "1";
        start.Environment["UV_NO_CONFIG"] = "1";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        using var process = Process.Start(start)
            ?? throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInstallFailed,
                "Không thể khởi động trình cài Kokoro.");
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        _ = await output;
        _ = await error; // Never surface package output or local paths.
        if (process.ExitCode != 0)
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInstallFailed,
                "Cài runtime Kokoro thất bại. Hãy kiểm tra mạng và dung lượng đĩa.");
    }

    private void EnsureDiskSpace()
    {
        var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(RuntimeRoot))!);
        if (drive.AvailableFreeSpace < 4L * 1024 * 1024 * 1024)
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInstallFailed,
                "Ổ đĩa cần ít nhất 4 GiB trống để cài runtime Kokoro.");
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static VietsubVoiceRuntimeStatus Status(string state, bool ready, string voiceId,
        long bytes, string? code, string message) =>
        new(state, ready, VietsubVoiceEngines.Kokoro, VietsubVoiceCatalog.KokoroEngineVersion,
            VietsubVoiceModelCatalog.ModelId, VietsubVoiceModelCatalog.Revision, voiceId, bytes,
            VietsubVoiceModelCatalog.CoreModel.Size, message, code);

    private sealed record EnvironmentMarker(string RuntimeVersion, string MachineFingerprint,
        string RequirementsSha256, string PythonSha256);
    private sealed record VoiceMarker(int ProtocolVersion, string RuntimeVersion, string VoiceId,
        string MachineFingerprint, string WorkerSha256, string RequirementsSha256,
        string OnnxSha256, string ConfigSha256, string VoicePackSha256);
}
