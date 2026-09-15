using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_LOCAL.Vietsub.Voice;

internal sealed record VietsubPiperComponentPaths(
    string PythonPath,
    string WorkerPath,
    string ModelPath,
    string ConfigPath,
    string RequestDirectory);

internal sealed class VietsubVoiceComponentStore : IDisposable
{
    private const int ProtocolVersion = 1;
    internal const string RuntimeVersion = "piper-1.6.0-python-3.11.15-locked-v2";
    private const string UvVersion = "0.12.3";
    private const long UvArchiveSize = 19_013_455;
    private const string UvArchiveSha256 = "b23350c79e8ad0192b8124af13a0f17e8d4e4549524785e1aef389ae5a06990e";
    private const long UvExecutableSize = 48_024_064;
    private const string UvExecutableSha256 = "68a22cbab1674647bcda32120b214e6480f875414e3333f49f87ae99b4b0e0fa";
    internal const long ModelSize = 63_201_294;
    private const string ModelSha256 = "ec7c89e2c85f4d1edc24b6120c18aaf1bda614f06b511567eb9c7c0de15e2dab";
    private const long ConfigSize = 4_860;
    private const string ConfigSha256 = "fafb9da1354ed4b77c31af228ed41fb41cd825c14cffa105454b25e6ae751ee0";
    private static readonly Uri UvArchiveUri = new(
        "https://github.com/astral-sh/uv/releases/download/0.12.3/uv-x86_64-pc-windows-msvc.zip");
    private static readonly Uri ModelUri = new(
        "https://huggingface.co/rhasspy/piper-voices/resolve/ea046e8458f6acd997706d6e6066a022b42f6fb1/vi/vi_VN/vais1000/medium/vi_VN-vais1000-medium.onnx?download=true");
    private static readonly Uri ConfigUri = new(
        "https://huggingface.co/rhasspy/piper-voices/resolve/ea046e8458f6acd997706d6e6066a022b42f6fb1/vi/vi_VN/vais1000/medium/vi_VN-vais1000-medium.onnx.json?download=true");
    private static readonly HashSet<string> AllowedDownloadHosts = new(
        [
            "github.com",
            "objects.githubusercontent.com",
            "release-assets.githubusercontent.com",
            "huggingface.co",
            "cdn-lfs.hf.co",
            "cas-bridge.xethub.hf.co",
            "us.aws.cdn.hf.co"
        ],
        StringComparer.OrdinalIgnoreCase);
    private readonly bool _featureEnabled;
    private readonly string _componentRoot;
    private readonly bool _useVersionedRuntime;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, VerifiedFile> _verifiedFiles = new(StringComparer.OrdinalIgnoreCase);

    public VietsubVoiceComponentStore(VietsubAppPaths paths, bool featureEnabled, HttpMessageHandler? httpHandler = null,
        bool useUserComponentsRoot = false)
    {
        _featureEnabled = featureEnabled;
        _useVersionedRuntime = useUserComponentsRoot;
        var legacy = Path.Combine(paths.RootDirectory, "components", "voice", "piper");
        _componentRoot = !useUserComponentsRoot || Directory.Exists(legacy) ? legacy
            : Path.Combine(TOOL_LOCAL.SystemSetup.SystemSetupPaths.ComponentsRoot, "voice", "piper");
        httpHandler ??= new HttpClientHandler { AllowAutoRedirect = false };
        _httpClient = new HttpClient(httpHandler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        _httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd("VideoMaker-Vietsub/1.0");
    }

    public VietsubVoiceRuntimeStatus GetStatus()
    {
        if (!_featureEnabled)
        {
            return Status("DISABLED", false, "Tạo giọng local đang bị khóa bởi feature flag.", VietsubVoiceErrorCodes.FeatureDisabled);
        }

        try
        {
            var modelReady = IsVerifiedFile(ModelPath, ModelSize, ModelSha256);
            var configReady = IsVerifiedFile(ConfigPath, ConfigSize, ConfigSha256);
            var installedBytes = GetInstalledBytes();
            if (!modelReady || !configReady || !File.Exists(PythonPath) || !File.Exists(WorkerPath))
            {
                return Status(
                    installedBytes == 0 ? "NOT_INSTALLED" : "INVALID",
                    false,
                    "Piper local chưa được cài đầy đủ. Hãy cài hoặc sửa runtime trước khi tạo giọng.",
                    installedBytes == 0 ? VietsubVoiceErrorCodes.RuntimeNotInstalled : VietsubVoiceErrorCodes.RuntimeInvalid,
                    installedBytes);
            }

            if (!MarkerMatches())
            {
                return Status(
                    "INVALID",
                    false,
                    "Fingerprint runtime, worker hoặc model Piper không còn khớp.",
                    VietsubVoiceErrorCodes.RuntimeInvalid,
                    installedBytes);
            }

            return Status("READY", true, "Piper local đã sẵn sàng tạo giọng tiếng Việt.", null, installedBytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Status("INVALID", false, "Không thể xác minh runtime Piper local.", VietsubVoiceErrorCodes.RuntimeInvalid);
        }
    }

    internal string ComponentDirectory => _componentRoot;

    public async Task<VietsubVoiceRuntimeStatus> InstallAsync(
        IProgress<VietsubVoiceRuntimeInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!_featureEnabled)
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.FeatureDisabled, "Tạo giọng local đang bị khóa bởi feature flag.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (GetStatus().Ready) return GetStatus();
            // Never leave an old marker usable while Python/packages are being replaced.
            TryDelete(MarkerPath);
            EnsureDiskSpace(768L * 1024 * 1024);
            TOOL_LOCAL.SystemSetup.SystemSetupPaths.EnsureSafeDirectory(_componentRoot);
            Directory.CreateDirectory(ModelDirectory);
            Directory.CreateDirectory(RequestDirectory);

            await DownloadVerifiedAsync(UvArchiveUri, UvArchivePath, UvArchiveSize, UvArchiveSha256, "UV", 0, 20, progress, cancellationToken);
            ExtractVerifiedUv();
            progress?.Report(new("PYTHON", 25, "Đang cài Python 3.11 x64 cô lập cho Piper.", 0, 1));
            await RunUvAsync(
                BuildVenvArguments(Path.GetDirectoryName(Path.GetDirectoryName(PythonPath)!)!),
                cancellationToken);
            progress?.Report(new("PACKAGES", 45, "Đang cài Piper runtime đã khóa phiên bản.", 0, 1));
            await RunUvAsync(
                [
                    "pip", "install",
                    "--python", PythonPath,
                    "--only-binary", ":all:",
                    "--require-hashes", "--no-config", "--default-index", "https://pypi.org/simple",
                    "--requirements", RequirementsPath
                ],
                cancellationToken);

            await DownloadVerifiedAsync(ModelUri, ModelPath, ModelSize, ModelSha256, "MODEL", 55, 39, progress, cancellationToken);
            await DownloadVerifiedAsync(ConfigUri, ConfigPath, ConfigSize, ConfigSha256, "CONFIG", 94, 3, progress, cancellationToken);
            progress?.Report(new("PROBE", 98, "Đang kiểm tra kiến trúc, module và worker Piper.", 0, 1));
            await ProbeAsync(cancellationToken);
            await WriteMarkerAsync(cancellationToken);
            var status = GetStatus();
            if (!status.Ready)
            {
                throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInvalid, status.Message);
            }
            progress?.Report(new("READY", 100, status.Message, status.RequiredBytes, status.RequiredBytes));
            return status;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (VietsubVoiceException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new VietsubVoiceException(
                VietsubVoiceErrorCodes.RuntimeInstallFailed,
                "Không thể cài Piper local. Hãy kiểm tra mạng, dung lượng đĩa rồi thử lại.",
                innerException: exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public VietsubPiperComponentPaths RequireReady()
    {
        var status = GetStatus();
        if (!status.Ready)
        {
            throw new VietsubVoiceException(status.ErrorCode ?? VietsubVoiceErrorCodes.RuntimeInvalid, status.Message);
        }
        return new(PythonPath, WorkerPath, ModelPath, ConfigPath, RequestDirectory);
    }

    internal async Task<VietsubVoiceRuntimeStatus> VerifyAsync(CancellationToken token)
    {
        if (!_featureEnabled) return GetStatus();
        await _gate.WaitAsync(token);
        try
        {
            _verifiedFiles.Clear();
            if (!IsVerifiedFile(ModelPath, ModelSize, ModelSha256)
                || !IsVerifiedFile(ConfigPath, ConfigSize, ConfigSha256)
                || !File.Exists(PythonPath) || !File.Exists(WorkerPath)) return GetStatus();
            TryDelete(MarkerPath);
            await ProbeAsync(token);
            await WriteMarkerAsync(token);
            return GetStatus();
        }
        finally { _gate.Release(); }
    }

    private string RuntimeRoot => _useVersionedRuntime ? Path.Combine(_componentRoot, "runtime", RuntimeVersion)
        : Path.Combine(_componentRoot, "runtime");
    private string ModelDirectory => Path.Combine(_componentRoot, "model");
    private string PythonPath => Path.Combine(RuntimeRoot, ".venv", "Scripts", "python.exe");
    private string UvPath => Path.Combine(RuntimeRoot, "uv.exe");
    private string UvArchivePath => Path.Combine(RuntimeRoot, $"uv-{UvVersion}.zip");
    private string ModelPath => Path.Combine(ModelDirectory, "vi_VN-vais1000-medium.onnx");
    private string ConfigPath => Path.Combine(ModelDirectory, "vi_VN-vais1000-medium.onnx.json");
    private string MarkerPath => Path.Combine(_componentRoot, ".ready.json");
    private string RequestDirectory => Path.Combine(_componentRoot, "requests");
    private static string WorkerPath => Path.Combine(AppContext.BaseDirectory, "workers", "piper_worker.py");
    private static string RequirementsPath => Path.Combine(AppContext.BaseDirectory, "workers", "piper-requirements.lock");

    private async Task DownloadVerifiedAsync(
        Uri uri,
        string destination,
        long expectedSize,
        string expectedHash,
        string stage,
        double basePercent,
        double spanPercent,
        IProgress<VietsubVoiceRuntimeInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (IsVerifiedFile(destination, expectedSize, expectedHash)) return;
        if (uri.Scheme != Uri.UriSchemeHttps || !AllowedDownloadHosts.Contains(uri.Host))
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInstallFailed, "Nguồn tải component giọng đọc không được phép.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + ".partial";
        TryDelete(partial);
        try
        {
            using var response = await GetAllowedResponseAsync(uri, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long contentLength && contentLength != expectedSize)
            {
                throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInvalid, "Kích thước component Piper không đúng manifest.");
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(
                partial,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024];
            long processed = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hash.AppendData(buffer, 0, read);
                processed += read;
                if (processed > expectedSize)
                {
                    throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInvalid, "Component Piper vượt quá kích thước đã duyệt.");
                }
                progress?.Report(new(
                    stage,
                    Math.Min(basePercent + spanPercent, basePercent + processed * spanPercent / expectedSize),
                    stage == "MODEL" ? "Đang tải model giọng nữ tiếng Việt." : "Đang tải component Piper đã ký checksum.",
                    processed,
                    expectedSize));
            }
            await target.FlushAsync(cancellationToken);
            target.Flush(flushToDisk: true);
            var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (processed != expectedSize || !FixedHashEquals(actualHash, expectedHash))
            {
                throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInvalid, "Checksum component Piper không hợp lệ.");
            }
            target.Close();
            File.Move(partial, destination, overwrite: true);
            _verifiedFiles.TryRemove(destination, out _);
        }
        finally
        {
            TryDelete(partial);
        }
    }

    private async Task<HttpResponseMessage> GetAllowedResponseAsync(Uri initialUri, CancellationToken cancellationToken)
    {
        var current = initialUri;
        for (var redirectCount = 0; redirectCount <= 5; redirectCount++)
        {
            EnsureAllowedDownloadUri(current);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!IsRedirect(response.StatusCode)) return response;

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || redirectCount == 5)
            {
                throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInstallFailed, "Chuỗi chuyển hướng component giọng đọc không hợp lệ.");
            }
            current = location.IsAbsoluteUri ? location : new Uri(current, location);
        }
        throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInstallFailed, "Component giọng đọc chuyển hướng quá nhiều lần.");
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => (int)statusCode is 301 or 302 or 303 or 307 or 308;

    private static void EnsureAllowedDownloadUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !AllowedDownloadHosts.Contains(uri.IdnHost))
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInstallFailed, "Nguồn tải component giọng đọc không được phép.");
        }
    }

    private void ExtractVerifiedUv()
    {
        if (IsVerifiedFile(UvPath, UvExecutableSize, UvExecutableSha256)) return;
        Directory.CreateDirectory(RuntimeRoot);
        using var archive = ZipFile.OpenRead(UvArchivePath);
        var entry = archive.Entries.SingleOrDefault(item =>
            string.Equals(Path.GetFileName(item.FullName), "uv.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Gói runtime Piper thiếu uv.exe.");
        var partial = UvPath + ".partial";
        TryDelete(partial);
        try
        {
            using var source = entry.Open();
            using var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(output);
            output.Flush(flushToDisk: true);
            output.Close();
            if (!IsVerifiedFile(partial, UvExecutableSize, UvExecutableSha256))
            {
                throw new InvalidDataException("Checksum uv.exe không hợp lệ.");
            }
            File.Move(partial, UvPath, overwrite: true);
        }
        finally
        {
            TryDelete(partial);
        }
    }

    internal static IReadOnlyList<string> BuildVenvArguments(string environmentPath) =>
        ["venv", "--clear", "--python", "3.11.15", "--managed-python", "--no-config", environmentPath];

    private async Task RunUvAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = UvPath,
            WorkingDirectory = RuntimeRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        foreach (var key in startInfo.Environment.Keys.Where(key => key.StartsWith("UV_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("PIP_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("PYTHON", StringComparison.OrdinalIgnoreCase)).ToArray())
            startInfo.Environment.Remove(key);
        startInfo.Environment["UV_CACHE_DIR"] = Path.Combine(RuntimeRoot, "cache");
        startInfo.Environment["UV_PYTHON_INSTALL_DIR"] = Path.Combine(RuntimeRoot, "cpython");
        startInfo.Environment["UV_PYTHON_NO_REGISTRY"] = "1";
        startInfo.Environment["UV_NO_PROGRESS"] = "1";
        startInfo.Environment["UV_NO_CONFIG"] = "1";
        startInfo.Environment["UV_DEFAULT_INDEX"] = "https://pypi.org/simple";
        using var installTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        installTimeout.CancelAfter(TimeSpan.FromMinutes(20));
        cancellationToken = installTimeout.Token;
        using var process = Process.Start(startInfo)
            ?? throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInstallFailed, "Không thể khởi động trình cài Piper.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            throw;
        }
        _ = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
        {
            throw new VietsubVoiceException(
                VietsubVoiceErrorCodes.RuntimeInstallFailed,
                "Cài Piper runtime thất bại: " + LastLine(error));
        }
    }

    private async Task ProbeAsync(CancellationToken cancellationToken)
    {
        if (!IsVerifiedFile(ModelPath, ModelSize, ModelSha256)
            || !IsVerifiedFile(ConfigPath, ConfigSize, ConfigSha256)
            || !File.Exists(WorkerPath))
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInvalid, "Thiếu model, config hoặc worker Piper đã duyệt.");
        }
        Directory.CreateDirectory(RequestDirectory);
        var requestId = "probe-" + Guid.NewGuid().ToString("N");
        var requestPath = Path.Combine(RequestDirectory, requestId + ".json");
        var outputPath = Path.Combine(RequestDirectory, requestId + ".partial.wav");
        var request = new
        {
            protocolVersion = ProtocolVersion,
            requestId,
            modelPath = ModelPath,
            configPath = ConfigPath,
            volume = 1.0,
            lengthScale = 1.0,
            items = new[] { new { index = 0, text = "Xin chào, đây là kiểm tra giọng Việt.", outputPath } }
        };
        await File.WriteAllTextAsync(
            requestPath,
            JsonSerializer.Serialize(request),
            new UTF8Encoding(false),
            cancellationToken);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = PythonPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.ArgumentList.Add("-X");
            startInfo.ArgumentList.Add("utf8");
            startInfo.ArgumentList.Add("-I");
            startInfo.ArgumentList.Add(WorkerPath);
            startInfo.ArgumentList.Add(requestPath);
            startInfo.Environment["PYTHONUTF8"] = "1";
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            startInfo.Environment["PYTHONNOUSERSITE"] = "1";
            using var process = Process.Start(startInfo)
                ?? throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInvalid, "Không thể probe Piper runtime.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Kill(process);
                throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInvalid, "Probe Piper runtime quá thời gian cho phép.");
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                throw;
            }
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0 || !ProbeProtocolIsValid(output, requestId))
            {
                throw new VietsubVoiceException(
                    VietsubVoiceErrorCodes.RuntimeInvalid,
                    "Probe Piper runtime/model/worker thất bại: " + LastLine(error));
            }
            _ = VietsubWavInspector.Inspect(outputPath, analyzeSilence: false);
        }
        finally
        {
            TryDelete(requestPath);
            TryDelete(outputPath);
        }
    }

    private static bool ProbeProtocolIsValid(string output, string requestId)
    {
        var itemCompleted = false;
        var completed = false;
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Length > 16 * 1024) return false;
            try
            {
                var workerEvent = JsonSerializer.Deserialize<ProbeWorkerEvent>(line, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (workerEvent is null
                    || workerEvent.ProtocolVersion != ProtocolVersion
                    || !string.Equals(workerEvent.RequestId, requestId, StringComparison.Ordinal))
                {
                    return false;
                }
                itemCompleted |= workerEvent.Type == "item_completed" && workerEvent.Index == 0;
                completed |= workerEvent.Type == "completed";
            }
            catch (JsonException)
            {
                return false;
            }
        }
        return itemCompleted && completed;
    }

    private async Task WriteMarkerAsync(CancellationToken cancellationToken)
    {
        var marker = new ReadyMarker(
            ProtocolVersion,
            RuntimeVersion,
            Sha256File(WorkerPath),
            ModelSha256,
            ConfigSha256,
            TOOL_LOCAL.SystemSetup.SystemSetupPaths.MachineFingerprint,
            Sha256File(RequirementsPath), Sha256File(PythonPath));
        var partial = MarkerPath + ".partial";
        await File.WriteAllTextAsync(partial, JsonSerializer.Serialize(marker), cancellationToken);
        File.Move(partial, MarkerPath, overwrite: true);
    }

    private bool MarkerMatches()
    {
        if (!File.Exists(MarkerPath)) return false;
        var marker = JsonSerializer.Deserialize<ReadyMarker>(File.ReadAllText(MarkerPath));
        return marker is not null
            && marker.ProtocolVersion == ProtocolVersion
            && marker.RuntimeVersion == RuntimeVersion
            && marker.MachineFingerprint == TOOL_LOCAL.SystemSetup.SystemSetupPaths.MachineFingerprint
            && File.Exists(RequirementsPath) && File.Exists(PythonPath)
            && marker.RequirementsSha256 == Sha256File(RequirementsPath)
            && marker.PythonSha256 == Sha256File(PythonPath)
            && FixedHashEquals(marker.WorkerSha256, Sha256File(WorkerPath))
            && FixedHashEquals(marker.ModelSha256, ModelSha256)
            && FixedHashEquals(marker.ConfigSha256, ConfigSha256);
    }

    private bool IsVerifiedFile(string path, long expectedSize, string expectedHash)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != expectedSize)
        {
            _verifiedFiles.TryRemove(path, out _);
            return false;
        }
        if (_verifiedFiles.TryGetValue(path, out var cached)
            && cached.Length == file.Length
            && cached.LastWriteAtUtc == file.LastWriteTimeUtc)
        {
            return cached.Valid;
        }
        using var stream = file.OpenRead();
        var valid = CryptographicOperations.FixedTimeEquals(SHA256.HashData(stream), Convert.FromHexString(expectedHash));
        _verifiedFiles[path] = new(file.Length, file.LastWriteTimeUtc, valid);
        return valid;
    }

    private long GetInstalledBytes() => new[] { ModelPath, ConfigPath }
        .Where(File.Exists)
        .Sum(path => new FileInfo(path).Length);

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static bool FixedHashEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private VietsubVoiceRuntimeStatus Status(
        string status,
        bool ready,
        string message,
        string? errorCode,
        long? installedBytes = null) => new(
            status,
            ready,
            VietsubVoiceEngines.Piper,
            VietsubVoiceCatalog.PiperEngineVersion,
            VietsubVoiceCatalog.PiperModelId,
            VietsubVoiceCatalog.PiperModelVersion,
            VietsubVoiceCatalog.PiperVoiceId,
            installedBytes ?? GetInstalledBytes(),
            ModelSize + ConfigSize,
            message,
            errorCode);

    private void EnsureDiskSpace(long requiredBytes)
    {
        var root = Path.GetPathRoot(_componentRoot);
        if (root is null) return;
        var drive = new DriveInfo(root);
        if (drive.IsReady && drive.AvailableFreeSpace < requiredBytes)
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeInstallFailed, "Ổ đĩa không đủ dung lượng để cài Piper local.");
        }
    }

    private static string LastLine(string value) => value
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .LastOrDefault() is { } line
            ? line[..Math.Min(300, line.Length)]
            : "không có thông tin lỗi.";

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _gate.Dispose();
        _httpClient.Dispose();
    }

    private sealed record ReadyMarker(
        int ProtocolVersion,
        string RuntimeVersion,
        string WorkerSha256,
        string ModelSha256,
        string ConfigSha256,
        string? MachineFingerprint = null,
        string? RequirementsSha256 = null,
        string? PythonSha256 = null);

    private sealed record ProbeWorkerEvent(int ProtocolVersion, string RequestId, string Type, int? Index);

    private sealed record VerifiedFile(long Length, DateTime LastWriteAtUtc, bool Valid);
}
