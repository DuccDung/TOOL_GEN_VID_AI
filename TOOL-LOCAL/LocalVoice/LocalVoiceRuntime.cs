using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.Media;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_LOCAL.LocalVoice;

internal interface ILocalVoiceRuntime
{
    LocalVoiceRuntimeSummary GetStatus();
    Task InstallAsync(CancellationToken token);
    Task RunAsync(string action, string workDirectory, Action<string>? progress, CancellationToken token);
}

internal sealed class LocalVoiceRuntime(string workspaceRoot, bool featureEnabled, string? componentRootOverride = null,
    string? workerPathOverride = null) : ILocalVoiceRuntime
{
    private readonly string _root = componentRootOverride ?? Path.Combine(workspaceRoot, "components", "veo-local-voice", "v1");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string Worker => workerPathOverride ?? Path.Combine(AppContext.BaseDirectory, "workers", "voice_consistency_worker.py");
    private string Python => Path.Combine(_root, "venv", "Scripts", "python.exe");
    public bool FeatureEnabled => featureEnabled;
    internal long LastProcessPeakBytes { get; private set; }
    public LocalVoiceRuntimeSummary GetStatus()
    {
        if (!featureEnabled) return new("DISABLED", "Đồng nhất giọng Veo local đang tắt trong cấu hình desktop.");
        try
        {
            if (!File.Exists(Python) || !File.Exists(Path.Combine(_root, "manifest.json")) || !File.Exists(Worker))
                return new("NOT_INSTALLED", "Chưa cài runtime đồng nhất giọng trên máy này.");
            var fingerprint = Fingerprint();
            if (!File.Exists(Path.Combine(_root, "ready.json")) || File.ReadAllText(Path.Combine(_root, "ready.json")) != fingerprint)
                return new("INVALID", "Runtime cần cài/kiểm tra lại trước khi chạy.");
            return new("READY", "Runtime local đã probe; mỗi clip vẫn cần nghe và duyệt chất lượng.", fingerprint);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { return new("INVALID", "Không xác minh được component local."); }
    }

    private string Fingerprint() => LocalVoiceStore.Hash(File.ReadAllText(Path.Combine(_root, "manifest.json")) +
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Worker))));

    public async Task InstallAsync(CancellationToken token)
    {
        if (!featureEnabled) throw new InvalidOperationException("Tính năng đồng nhất giọng local đang tắt.");
        await _gate.WaitAsync(token);
        try
        {
            using var componentLock = AcquireComponentLock();
            var readyPath = Path.Combine(_root, "ready.json");
            if (File.Exists(readyPath)) File.Delete(readyPath);
            var installer = Path.Combine(AppContext.BaseDirectory, "workers", "install_voice_consistency.ps1");
            var runner = new ExternalProcessRunner();
            var result = await runner.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"),
                ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", installer, "-ComponentRoot", _root],
                TimeSpan.FromMinutes(60), token);
            if (result.ExitCode != 0) throw new InvalidOperationException("Không cài được runtime local. Kiểm tra mạng, dung lượng và thử lại.");
            await ProbeInstalledAsync(token);
        }
        finally { _gate.Release(); }
    }

    public async Task RunAsync(string action, string workDirectory, Action<string>? progress, CancellationToken token)
    {
        if (GetStatus().Status != "READY") throw new InvalidOperationException("Runtime local chưa sẵn sàng; hãy cài/kiểm tra component.");
        await _gate.WaitAsync(token);
        try { using var componentLock = AcquireComponentLock(); await ExecuteAsync(action, workDirectory, progress, token); }
        finally { _gate.Release(); }
    }

    private FileStream AcquireComponentLock()
    {
        Directory.CreateDirectory(_root);
        return new FileStream(Path.Combine(_root, "runtime.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    internal async Task ProbeInstalledAsync(CancellationToken token)
    {
        await ExecuteAsync("probe", _root, null, token);
        var readyPath = Path.Combine(_root, "ready.json");
        await File.WriteAllTextAsync(readyPath + ".part", Fingerprint(), token);
        File.Move(readyPath + ".part", readyPath, true);
    }

    private async Task ExecuteAsync(string action, string work, Action<string>? progress, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(20));
        await VerifyRuntimeAsync(timeout.Token);
        Directory.CreateDirectory(work);
        var requestPath = Path.Combine(work, Guid.NewGuid().ToString("N") + ".request.json");
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(new {
            protocolVersion = 1, action, componentRoot = _root, workDirectory = work, fingerprint = Fingerprint(),
            hostVerifiedManifestSha256 = await LocalVoiceStore.FileHashAsync(Path.Combine(_root, "manifest.json"), token)
        }), new UTF8Encoding(false), token);
        var info = new ProcessStartInfo(Python) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        info.ArgumentList.Add("-I"); info.ArgumentList.Add("-X"); info.ArgumentList.Add("utf8");
        info.ArgumentList.Add(Worker); info.ArgumentList.Add(requestPath);
        var retained = new[] { "SystemRoot", "WINDIR", "TEMP", "TMP", "PATH" }.ToDictionary(x => x, x => Environment.GetEnvironmentVariable(x));
        info.Environment.Clear();
        foreach (var entry in retained) if (entry.Value is not null) info.Environment[entry.Key] = entry.Value;
        info.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        info.Environment["TORCH_FORCE_NO_WEIGHTS_ONLY_LOAD"] = "1";
        info.Environment["MPLCONFIGDIR"] = Path.Combine(_root, "plot-cache");
        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
            using var cancellation = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            var errors = DrainAsync(process.StandardError, timeout.Token);
            var completed = false;
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                if (line.Length > 32768) throw new InvalidDataException("Worker event quá lớn.");
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.GetProperty("protocolVersion").GetInt32() != 1) throw new InvalidDataException("Worker protocol không khớp.");
                var kind = doc.RootElement.GetProperty("type").GetString();
                if (kind == "error") throw new InvalidDataException("Xử lý giọng local thất bại: " + doc.RootElement.GetProperty("code").GetString());
                if (kind == "progress") progress?.Invoke(doc.RootElement.GetProperty("stage").GetString()!);
                if (kind == "completed") completed = true;
            }
            await process.WaitForExitAsync(timeout.Token);
            var diagnostic = await errors;
            try { LastProcessPeakBytes = process.PeakWorkingSet64; } catch (InvalidOperationException) { LastProcessPeakBytes = 0; }
            if (process.ExitCode != 0 || !completed)
                throw new InvalidDataException($"Worker local kết thúc khi chưa hoàn tất (exit={process.ExitCode}, {diagnostic}).");
        }
        finally
        {
            try { if (process.Id != 0 && !process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } } catch (InvalidOperationException) { }
            if (File.Exists(requestPath)) File.Delete(requestPath);
        }
    }

    private async Task VerifyRuntimeAsync(CancellationToken token)
    {
        var root = Path.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "manifest.json"), token));
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var checkedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesToVerify = new List<(string Path, long Size, string Hash)>();
        foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var relative = entry.GetProperty("path").GetString()!;
            var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (Path.IsPathRooted(relative) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !paths.Add(relative))
                throw new InvalidDataException("Manifest runtime không hợp lệ.");
            // Normalize and validate before adding the Win32 extended-length prefix.
            if (OperatingSystem.IsWindows() && !path.StartsWith(@"\\?\", StringComparison.Ordinal))
                path = path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)!); directory is not null; directory = directory.Parent)
            {
                if (!checkedDirectories.Add(directory.FullName)) break;
                if ((File.GetAttributes(directory.FullName) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Component không được chứa liên kết thư mục.");
            }
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Component không được chứa liên kết file.");
            if (PinnedModels.TryGetValue(relative, out var pinned) && entry.GetProperty("sha256").GetString() != pinned)
                throw new InvalidDataException("Model không thuộc profile đã ghim.");
            filesToVerify.Add((path, entry.GetProperty("size").GetInt64(), entry.GetProperty("sha256").GetString()!));
        }
        foreach (var required in new[] { "venv/Scripts/python.exe", "checkpoint.pth", "config.json", "demucs/955717e8-8726e21a.th", "openvoice/openvoice/models.py" })
            if (!paths.Contains(required)) throw new InvalidDataException("Runtime thiếu thành phần bắt buộc.");
        // Bounded parallel reads keep full byte verification practical for thousands of small Python files.
        await Parallel.ForEachAsync(filesToVerify, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token },
            async (file, cancellation) => {
                if (new FileInfo(file.Path).Length != file.Size ||
                    !string.Equals(await LocalVoiceStore.FileHashAsync(file.Path, cancellation), file.Hash, StringComparison.Ordinal))
                    throw new InvalidDataException("Checksum runtime không khớp; hãy cài lại component.");
            });
    }

    private static readonly IReadOnlyDictionary<string, string> PinnedModels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["checkpoint.pth"] = "9652c27e92b6b2a91632590ac9962ef7ae2b712e5c5b7f4c34ec55ee2b37ab9e",
        ["config.json"] = "9dfff60350b8c63f2c664efd92a61b2516efb22671466960f0e5dfebd881fa47",
        ["demucs/955717e8-8726e21a.th"] = "8726e21a993978c7ba086d3872e7608d7d5bfca646ca4aca459ffda844faa8b4"
    };

    private static async Task<string> DrainAsync(StreamReader reader, CancellationToken token)
    {
        var buffer = new char[4096];
        var tail = ""; var code = "no_safe_diagnostic";
        while (await reader.ReadAsync(buffer.AsMemory(), token) is var count && count > 0)
        {
            var value = tail + new string(buffer, 0, count);
            if (value.Contains("OMP: Error #15", StringComparison.Ordinal)) code = "openmp_conflict";
            if (value.Contains("bad_alloc", StringComparison.OrdinalIgnoreCase) || value.Contains("not enough memory", StringComparison.OrdinalIgnoreCase))
                code = "insufficient_memory";
            tail = value.Length > 128 ? value[^128..] : value;
        }
        return code; // Never return raw stderr, paths, content or environment values.
    }
}
