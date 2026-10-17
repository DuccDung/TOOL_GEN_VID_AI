using System.Runtime.InteropServices;
using TOOL_LOCAL.SystemSetup;

namespace TOOL_LOCAL.Vietsub.Voice;

internal sealed partial class VietsubVoiceComponentStore
{
    private PiperOfflineBundle? _offlineBundle;
    internal bool CanDownloadDistributionPayload => _distribution?.CanDownload == true || _staticBundleSource is not null;
    internal bool DistributionPayloadBlocked
    {
        get
        {
            try { EnsureDistributionNotRevoked(); return false; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or VietsubVoiceException)
            { return true; }
        }
    }
    internal bool HasOfflineBundle(out string? errorCode)
    {
        errorCode = null;
        return _offlineBundle is not null && _offlineBundle.Available(out errorCode);
    }
    internal long OfflineDiskBytes => _offlineBundle?.Definition.MinimumFreeDiskBytes ?? 768L * 1024 * 1024;
    internal bool UsesOfflineBundle => _offlineBundle is not null;

    private FileStream AcquireInstallLease()
    {
        SystemSetupPaths.EnsureSafeDirectory(_componentRoot);
        var path = Path.Combine(_componentRoot, ".piper-install.lock");
        PiperOfflineBundle.AssertSafePath(path);
        try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error)
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeBusy,
                "Piper đang được chuẩn bị hoặc kiểm tra ở cửa sổ khác. Hãy đợi rồi thử lại.", innerException: error);
        }
    }

    private void RequireOfflinePlatform()
    {
        if (!_featureEnabled)
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.FeatureDisabled, "Tạo giọng local đang bị khóa bởi feature flag.");
        if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64 || !Environment.Is64BitProcess)
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.RuntimeUnsupported, "Giọng Việt offline yêu cầu Windows x64.");
        EnsureDistributionNotRevoked();
    }

    private void EnsureDistributionNotRevoked()
    {
        try
        {
            if (!_allowUpstreamDownloads) TOOL_LOCAL.ModelDistribution.StaticModelRuntimePolicy.EnsureNotRevoked();
            var definition = _offlineBundle?.Definition ?? PiperOfflineBundle.ApprovedDefinition();
            if (_distribution is not null &&
                (_distribution.IsKnownRevoked(definition.ArchiveSha256) ||
                 _distribution.IsKnownRevoked(definition.ManifestSha256)))
                throw new VietsubVoiceException(VietsubVoiceErrorCodes.BundleInvalid, "Gói giọng Việt này đã bị thu hồi.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
            or TOOL_SHARED.Distribution.ModelDistribution.ModelDistributionException)
        {
            throw new VietsubVoiceException(VietsubVoiceErrorCodes.BundleInvalid,
                "Không thể xác minh trạng thái tin cậy của gói giọng Việt.", innerException: error);
        }
    }

    private async Task<VietsubVoiceRuntimeStatus> InstallOfflineAsync(
        IProgress<VietsubVoiceRuntimeInstallProgress>? progress, CancellationToken token)
    {
        RequireOfflinePlatform();
        if (_staticBundleSource is not null && !_offlineBundle!.Available(out _))
            _offlineBundle = await _staticBundleSource(progress, token);
        if (_distribution?.CanDownload == true && !_offlineBundle!.Available(out _))
        {
            var reporter = new SetupProgress<TOOL_LOCAL.ModelDistribution.ModelDownloadProgress>(p =>
                progress?.Report(new(p.Stage, p.TotalBytes > 0 ? p.BytesProcessed * 100d / p.TotalBytes : 0,
                    "Đang tải gói giọng Việt từ kho đã xác thực.", p.BytesProcessed, p.TotalBytes)));
            _offlineBundle = await _distribution.PreparePiperBundleAsync(reporter, token);
        }
        await _gate.WaitAsync(token);
        string? stage = null;
        var backup = RuntimeRoot + ".previous";
        FileStream? lease = null;
        var replacing = false;
        var phase = PiperInstallPhase.Validate;
        try
        {
            lease = AcquireInstallLease();
            _offlineBundle!.ClearVerificationCache();
            _verifiedFiles.Clear();
            RecoverInterruptedOfflineReplacement(backup);
            if (GetStatus().Ready) return GetStatus();
            _offlineBundle.VerifyArchive(force: true);
            _offlineBundle.VerifyWorker(WorkerPath, RequirementsPath);
            CleanInterruptedOfflineStages();
            EnsureDiskSpace(OfflineDiskBytes);
            var parent = Path.GetDirectoryName(RuntimeRoot)!;
            SystemSetupPaths.EnsureSafeDirectory(parent);
            stage = RuntimeRoot + ".stage-" + Guid.NewGuid().ToString("N");
            progress?.Report(new("EXTRACT", 0, "Đang chuẩn bị giọng Việt từ bộ ứng dụng.", 0, 1));
            phase = PiperInstallPhase.Extract;
            await _offlineBundle.ExtractAsync(stage, percent => progress?.Report(new("EXTRACT", percent * .45,
                "Đang xác minh và giải nén gói giọng Việt.", 0, 1)), token);
            token.ThrowIfCancellationRequested();
            phase = PiperInstallPhase.Promote;
            // Keep the existing venv at a backup path, but never execute it there. On
            // failure it is restored to its original absolute path before being used.
            PiperOfflineBundle.AssertSafePath(backup);
            replacing = true;
            if (Directory.Exists(RuntimeRoot)) Directory.Move(RuntimeRoot, backup);
            PiperOfflineBundle.AssertSafePath(stage);
            PiperOfflineBundle.AssertSafePath(RuntimeRoot);
            Directory.Move(stage, RuntimeRoot);
            stage = null;
            progress?.Report(new("PYTHON", 48, "Đang tạo môi trường giọng Việt trên máy này.", 0, 1));
            phase = PiperInstallPhase.Python;
            // A venv embeds absolute paths, so create it AFTER moving the verified payload.
            await RunUvAsync(["venv", "--python", Path.Combine(RuntimeRoot, "python", "python.exe"),
                "--no-python-downloads", "--offline", "--no-config", Path.Combine(RuntimeRoot, ".venv")], token, phase);
            progress?.Report(new("PACKAGES", 60, "Đang cài thư viện giọng Việt từ gói đi kèm.", 0, 1));
            phase = PiperInstallPhase.Packages;
            await RunUvAsync(["pip", "install", "--python", PythonPath, "--no-index",
                "--find-links", Path.Combine(RuntimeRoot, "wheels"), "--only-binary", ":all:", "--require-hashes",
                "--no-python-downloads", "--offline", "--no-config", "--link-mode", "copy",
                "--requirements", RequirementsPath], token, phase);
            // uv's interpreter discovery can write bytecode. Workers run with -B and use
            // the verified source files, never an unverified cached .pyc.
            foreach (var file in PiperOfflineBundle.EnumerateSafeFiles(RuntimeRoot)
                .Where(file => Path.GetExtension(file) is ".pyc" or ".pyo").ToArray()) File.Delete(file);
            if (!_offlineBundle.VerifyInstalled(RuntimeRoot, force: true) || !OfflineVenvMatches())
                throw PiperOfflineBundle.Invalid("Thư viện Piper sau cài đặt không khớp gói đã duyệt.");
            progress?.Report(new("PROBE", 92, "Đang tạo và kiểm tra âm thanh tiếng Việt.", 0, 1));
            phase = PiperInstallPhase.Probe;
            EnsureDistributionNotRevoked();
            await ProbeAsync(token);
            EnsureDistributionNotRevoked();
            phase = PiperInstallPhase.Ready;
            await WriteMarkerAsync(token);
            var status = GetStatus();
            if (!status.Ready) throw new VietsubVoiceException(status.ErrorCode!, status.Message);
            replacing = false;
            PiperOfflineBundle.DeleteOwnedDirectory(backup, parent);
            progress?.Report(new("READY", 100, status.Message, status.RequiredBytes, status.RequiredBytes));
            return status;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
            or System.ComponentModel.Win32Exception or VietsubVoiceException)
        {
            throw PiperInstallDiagnostics.Normalize(phase, error, RuntimeRoot);
        }
        finally
        {
            try
            {
                if (stage is not null) PiperOfflineBundle.DeleteOwnedDirectory(stage, Path.GetDirectoryName(RuntimeRoot)!);
                if (replacing)
                {
                    TryDelete(MarkerPath);
                    if (Directory.Exists(backup))
                    {
                        var parent = Path.GetDirectoryName(RuntimeRoot)!;
                        PiperOfflineBundle.DeleteOwnedDirectory(RuntimeRoot, parent);
                        PiperOfflineBundle.AssertSafePath(backup);
                        Directory.Move(backup, RuntimeRoot);
                    }
                }
            }
            finally { lease?.Dispose(); _gate.Release(); }
        }
    }

    private void RecoverInterruptedOfflineReplacement(string backup)
    {
        var parent = Path.GetDirectoryName(RuntimeRoot)!;
        PiperOfflineBundle.AssertSafePath(backup);
        if (!Directory.Exists(backup)) return;
        if (GetStatus().Ready)
        {
            PiperOfflineBundle.DeleteOwnedDirectory(backup, parent);
            return;
        }
        PiperOfflineBundle.DeleteOwnedDirectory(RuntimeRoot, parent);
        PiperOfflineBundle.AssertSafePath(RuntimeRoot);
        Directory.Move(backup, RuntimeRoot);
        _verifiedFiles.Clear();
    }

    private void CleanInterruptedOfflineStages()
    {
        var parent = Path.GetDirectoryName(RuntimeRoot)!;
        PiperOfflineBundle.AssertSafePath(parent);
        if (!Directory.Exists(parent)) return;
        var prefix = Path.GetFileName(RuntimeRoot) + ".stage-";
        foreach (var path in Directory.EnumerateDirectories(parent, prefix + "*"))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith(prefix, StringComparison.Ordinal)
                && Guid.TryParseExact(name[prefix.Length..], "N", out _))
                PiperOfflineBundle.DeleteOwnedDirectory(path, parent);
        }
    }

    private bool OfflineVenvMatches()
    {
        if (_offlineBundle is null) return true;
        var config = Path.Combine(RuntimeRoot, ".venv", "pyvenv.cfg");
        PiperOfflineBundle.AssertSafePath(config);
        if (!File.Exists(config) || new FileInfo(config).Length > 16_384) return false;
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(config))
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2 || !fields.TryAdd(parts[0].Trim(), parts[1].Trim())) return false;
        }
        return fields.GetValueOrDefault("include-system-site-packages") == "false"
            && fields.Count == 5 && fields.GetValueOrDefault("uv") == "0.12.3"
            && string.Equals(fields.GetValueOrDefault("home"), Path.Combine(RuntimeRoot, "python"), StringComparison.OrdinalIgnoreCase)
            && fields.GetValueOrDefault("version_info") == "3.11.15"
            && fields.GetValueOrDefault("implementation") == "CPython";
    }

    private async Task<VietsubVoiceRuntimeStatus> VerifyOfflineAsync(CancellationToken token)
    {
        if (!_featureEnabled) return GetStatus();
        RequireOfflinePlatform();
        if (!Directory.Exists(RuntimeRoot)) return GetStatus();
        await _gate.WaitAsync(token);
        var phase = PiperInstallPhase.Validate;
        try
        {
            using var lease = AcquireInstallLease();
            _verifiedFiles.Clear();
            _offlineBundle!.ClearVerificationCache();
            if (!Directory.Exists(RuntimeRoot)) return GetStatus();
            PiperOfflineBundle.AssertSafePath(MarkerPath);
            TryDelete(MarkerPath);
            _offlineBundle.VerifyWorker(WorkerPath, RequirementsPath);
            if (!_offlineBundle.VerifyInstalled(RuntimeRoot, force: true) || !OfflineVenvMatches()) return GetStatus();
            phase = PiperInstallPhase.Probe;
            EnsureDistributionNotRevoked();
            await ProbeAsync(token);
            EnsureDistributionNotRevoked();
            phase = PiperInstallPhase.Ready;
            await WriteMarkerAsync(token);
            return GetStatus();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or System.ComponentModel.Win32Exception or VietsubVoiceException)
        {
            throw PiperInstallDiagnostics.Normalize(phase, error, RuntimeRoot);
        }
        finally { _gate.Release(); }
    }
}
