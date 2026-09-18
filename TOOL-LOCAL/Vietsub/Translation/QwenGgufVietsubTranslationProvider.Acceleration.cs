using System.Text.Json;

namespace TOOL_LOCAL.Vietsub.Translation;

internal sealed record VietsubTranslationExecutionState(
    string? Backend = null, string? DeviceName = null, string? DeviceId = null,
    string? DriverVersion = null, int Layers = 0, bool CpuFallback = false,
    int GpuRetries = 0, string? FallbackCode = null, string? NativeFingerprint = null,
    string? PlannerPolicy = null);

internal sealed partial class QwenGgufVietsubTranslationProvider
{
    private string _executionPolicy = VietsubTranslationExecutionPolicies.CpuOnly;
    private VietsubTranslationWorkerInferenceConfig? _executionConfig;
    private VietsubTranslationExecutionState _executionState = new();
    private Func<VietsubTranslationExecutionState, CancellationToken, Task>? _saveExecution;
    private bool _executionSelected;
    private Guid? _executionJobId;
    private int? _maximumPlannedTargetCues;
    internal VietsubTranslationExecutionState? GetExecutionState(Guid jobId) =>
        _executionJobId == jobId ? _executionState : null;
    private string CudaDirectory => VietsubTranslationCudaPack.DirectoryPath(_componentStore.ComponentsRoot);
    private bool CudaInstalled => VietsubTranslationCudaPack.NativeHashes.Keys.All(n => File.Exists(Path.Combine(CudaDirectory, n)));

    internal async Task BeginExecutionAsync(string policy, VietsubTranslationExecutionState? previous,
        Func<VietsubTranslationExecutionState, CancellationToken, Task> saveExecution, CancellationToken ct,
        Guid? jobId = null, int? maximumPlannedTargetCues = null)
    {
        await _inferenceGate.WaitAsync(ct);
        try
        {
            _executionPolicy = VietsubTranslationExecutionPolicies.Validate(policy);
            if (maximumPlannedTargetCues is <= 0 or > 30)
                throw new ArgumentOutOfRangeException(nameof(maximumPlannedTargetCues));
            _maximumPlannedTargetCues = maximumPlannedTargetCues;
            if (previous is not null && (!VietsubTranslationGpuPlanner.IsApprovedLayerCount(previous.Layers)
                || previous.GpuRetries < 0))
                throw new VietsubTranslationException(VietsubTranslationErrorCodes.ContextInvalid,
                    "Trạng thái tăng tốc của job không hợp lệ.");
            _executionJobId = jobId;
            _executionState = previous ?? new();
            _saveExecution = saveExecution;
            _executionSelected = false;
            _executionConfig = null;
            _loadResult = null;
            await _workerClient.ResetAsync();
        }
        finally { _inferenceGate.Release(); }
    }

    private async Task<VietsubTranslationSceneResult> TranslateWithAccelerationAsync(
        VietsubTranslationSceneRequest request, CancellationToken ct)
    {
        var targetCount = request.Cues.Count(cue => cue.IsTarget);
        var qualificationTargets = Math.Max(targetCount, _maximumPlannedTargetCues ?? targetCount);
        if (!_executionSelected)
        {
            _executionSelected = true;
            _executionConfig = _runtimeProfile.InferenceConfig;
            if (_executionState.GpuRetries >= VietsubTranslationGpuPlanner.MaximumGpuFailures)
                await SaveExecutionAsync(_executionState with { CpuFallback = true, Backend = "cpu", Layers = 0,
                    DeviceId = null, DeviceName = null, DriverVersion = null, NativeFingerprint = null }, ct);
            if (_executionPolicy == VietsubTranslationExecutionPolicies.Auto && !_executionState.CpuFallback)
            {
                try
                {
                    if (!CudaInstalled)
                    {
                        await SaveExecutionAsync(_executionState with { CpuFallback = true, Backend = "cpu", Layers = 0,
                            DeviceId = null, DeviceName = null, DriverVersion = null, NativeFingerprint = null,
                            FallbackCode = "TRANSLATION_GPU_NOT_INSTALLED" }, ct);
                    }
                    else
                    {
                        var hardware = await _workerClient.ProbeHardwareAsync(ct);
                        var maximumLayers = _executionState.Layers > 0 ? _executionState.Layers
                            : _executionState.GpuRetries > 0 ? 0 : 36;
                        var device = hardware.Devices
                            .Where(d => _executionState.DeviceId is null || d.Id == _executionState.DeviceId)
                            .OrderByDescending(d => VietsubTranslationGpuPlanner.SelectLayersForScene(d, maximumLayers, qualificationTargets)).FirstOrDefault();
                        var layers = device is null ? 0 : VietsubTranslationGpuPlanner.SelectLayersForScene(device, maximumLayers, qualificationTargets);
                        if (layers > 0)
                        {
                            _executionConfig = _runtimeProfile.InferenceConfig with { GpuLayerCount = layers, GpuDeviceId = device!.Id };
                            // Save admission before loading: even an interrupted first load must not raise the cap on resume.
                            await SaveExecutionAsync(_executionState with { Layers = layers, DeviceId = device.Id,
                                PlannerPolicy = VietsubTranslationGpuPlanner.PolicyVersion }, ct);
                        }
                        else await SaveExecutionAsync(_executionState with { CpuFallback = true, Backend = "cpu", Layers = 0,
                            DeviceId = null, DeviceName = null, DriverVersion = null, NativeFingerprint = null,
                            FallbackCode = hardware.ErrorCode ?? (device is null ? "TRANSLATION_GPU_UNAVAILABLE" : "TRANSLATION_GPU_MEMORY") }, ct);
                    }
                }
                catch (VietsubTranslationException e) when (VietsubTranslationGpuPlanner.CanFallback(e.Code))
                {
                    await _workerClient.ResetAsync();
                    await SaveExecutionAsync(_executionState with { CpuFallback = true, Backend = "cpu", Layers = 0,
                        DeviceId = null, DeviceName = null, DriverVersion = null, NativeFingerprint = null, FallbackCode = e.Code }, ct);
                }
            }
        }

        if (!VietsubTranslationGpuPlanner.IsQualifiedForScene(_executionConfig!.GpuLayerCount, targetCount))
        {
            await _workerClient.ResetAsync();
            _loadResult = null;
            _executionConfig = _executionConfig with { GpuLayerCount = 24 };
            // This is a quality cap, not a failed GPU attempt. Persist before loading the
            // conservative tier and never raise it again within this job (including resume).
            await SaveExecutionAsync(_executionState with { Backend = null, Layers = 24,
                NativeFingerprint = null, FallbackCode = "TRANSLATION_GPU_QUALITY_CAP",
                PlannerPolicy = VietsubTranslationGpuPlanner.PolicyVersion }, ct);
        }

        while (true)
        {
            try
            {
                await EnsureWorkerLoadedAsync(request.ResourceWarningAccepted, ct);
                if (_executionConfig!.GpuLayerCount > 0) await EnsureCudaProbeAsync(null, ct);
                await SaveExecutionAsync(_executionState with
                {
                    Backend = _loadResult?.BackendIdentity,
                    DeviceName = _loadResult?.Device?.Name,
                    DeviceId = _loadResult?.Device?.Id,
                    DriverVersion = _loadResult?.Device?.DriverVersion,
                    Layers = _loadResult?.OffloadedLayers ?? 0,
                    NativeFingerprint = _loadResult?.NativeLibraryHash
                }, ct);
                return await TranslateCoreAsync(request, "TRANSLATING", ct);
            }
            catch (VietsubTranslationException e) when (_executionConfig!.GpuLayerCount > 0
                && _executionPolicy == VietsubTranslationExecutionPolicies.Auto && VietsubTranslationGpuPlanner.CanFallback(e.Code))
            {
                var lower = _executionState.GpuRetries + 1 < VietsubTranslationGpuPlanner.MaximumGpuFailures
                    && e.Code is "TRANSLATION_GPU_MEMORY" or "TRANSLATION_RUNTIME_OUT_OF_MEMORY"
                    ? VietsubTranslationGpuPlanner.ReduceLayers(_executionConfig.GpuLayerCount) : 0;
                var deviceId = _executionConfig.GpuDeviceId;
                await _workerClient.ResetAsync();
                _loadResult = null;
                // Persist before retry. Resume after app restart must not oscillate back to GPU.
                await SaveExecutionAsync(_executionState with
                { CpuFallback = lower == 0, GpuRetries = _executionState.GpuRetries + 1, FallbackCode = e.Code,
                    Backend = lower == 0 ? "cpu" : null, Layers = lower, DeviceId = lower > 0 ? deviceId : null, DeviceName = null,
                    DriverVersion = null, NativeFingerprint = null, PlannerPolicy = VietsubTranslationGpuPlanner.PolicyVersion }, ct);
                if (lower > 0)
                {
                    lower = await SelectLowerLayersAsync(lower, deviceId!, ct);
                    await SaveExecutionAsync(_executionState with { CpuFallback = lower == 0, Layers = lower,
                        Backend = lower == 0 ? "cpu" : null, DeviceId = lower > 0 ? deviceId : null }, ct);
                }
                _executionConfig = _runtimeProfile.InferenceConfig with
                { GpuLayerCount = lower, GpuDeviceId = lower > 0 ? deviceId : null };
            }
        }
    }

    private async Task<int> SelectLowerLayersAsync(int maximumLayers, string deviceId, CancellationToken ct)
    {
        try
        {
            var hardware = await _workerClient.ProbeHardwareAsync(ct);
            var device = hardware.Devices.SingleOrDefault(d => d.Id == deviceId);
            return device is null ? 0 : VietsubTranslationGpuPlanner.SelectLayers(device, maximumLayers);
        }
        catch (VietsubTranslationException e) when (VietsubTranslationGpuPlanner.CanFallback(e.Code)) { return 0; }
    }

    private async Task SaveExecutionAsync(VietsubTranslationExecutionState state, CancellationToken ct)
    {
        if (state == _executionState) return;
        _executionState = state;
        if (_saveExecution is not null) await _saveExecution(state, ct);
    }

    internal async Task InstallAccelerationAsync(IProgress<VietsubTranslationRuntimeInstallProgress>? progress,
        CancellationToken ct, bool warningAccepted)
    {
        var status = GetRuntimeStatus();
        if (!status.Ready) throw new VietsubTranslationException(VietsubTranslationErrorCodes.ModelNotReady,
            "Hãy cài/kiểm tra engine CPU trước khi cài tăng tốc NVIDIA.");
        await _installGate.WaitAsync(ct);
        try
        {
            await _inferenceGate.WaitAsync(ct);
            Volatile.Write(ref _busy, 1);
            try
            {
                await _workerClient.ResetAsync();
                _loadResult = null;
                var hardware = await _workerClient.ProbeHardwareAsync(ct);
                var device = hardware.Devices.OrderByDescending(VietsubTranslationGpuPlanner.SelectLayers).FirstOrDefault();
                if (device is null || VietsubTranslationGpuPlanner.SelectLayers(device) == 0)
                    throw new VietsubTranslationException(hardware.ErrorCode ?? "TRANSLATION_GPU_MEMORY",
                        "Chưa tìm thấy NVIDIA CUDA phù hợp hoặc VRAM trống chưa đủ. CPU vẫn sử dụng được.");
                await new VietsubTranslationCudaInstaller().InstallAsync(_componentStore.ComponentsRoot, progress, ct);
                EnsureResourcesAvailable(warningAccepted);
                _executionConfig = _runtimeProfile.InferenceConfig with
                { GpuLayerCount = VietsubTranslationGpuPlanner.SelectLayers(device), GpuDeviceId = device.Id };
                for (var failures = 0; ; failures++)
                {
                    try
                    {
                        await EnsureWorkerLoadedAsync(warningAccepted, ct);
                        await EnsureCudaProbeAsync(progress, ct, force: true);
                        break;
                    }
                    catch (VietsubTranslationException e) when (failures + 1 < VietsubTranslationGpuPlanner.MaximumGpuFailures
                        && e.Code is "TRANSLATION_GPU_MEMORY" or "TRANSLATION_RUNTIME_OUT_OF_MEMORY")
                    {
                        await _workerClient.ResetAsync();
                        _loadResult = null;
                        var lower = VietsubTranslationGpuPlanner.ReduceLayers(_executionConfig.GpuLayerCount);
                        if (lower > 0) lower = await SelectLowerLayersAsync(lower, device.Id, ct);
                        if (lower == 0) throw;
                        _executionConfig = _runtimeProfile.InferenceConfig with { GpuLayerCount = lower, GpuDeviceId = device.Id };
                    }
                }
                _executionState = new(_loadResult!.BackendIdentity, device.Name, device.Id, device.DriverVersion,
                    _loadResult.OffloadedLayers, NativeFingerprint: _loadResult.NativeLibraryHash,
                    PlannerPolicy: VietsubTranslationGpuPlanner.PolicyVersion);
                progress?.Report(new("READY", 100, $"Tăng tốc NVIDIA đã vượt probe Anh/Trung → Việt trên {device.Name}.", 1, 1));
            }
            catch (InvalidDataException e)
            { throw new VietsubTranslationException(VietsubTranslationCudaPack.IntegrityError,
                "Gói tăng tốc sai checksum hoặc nội dung; hãy cài lại.", innerException: e); }
            catch (Exception e) when (e is IOException or HttpRequestException)
            { throw new VietsubTranslationException(VietsubTranslationErrorCodes.RuntimeDownloadFailed,
                "Không thể tải/cài gói tăng tốc NVIDIA. Hãy kiểm tra mạng và dung lượng ổ đĩa.", retryable: true, innerException: e); }
            finally
            {
                await _workerClient.ResetAsync();
                _loadResult = null;
                _executionConfig = null;
                _executionSelected = false;
                _saveExecution = null;
                Volatile.Write(ref _busy, 0);
                _inferenceGate.Release();
            }
        }
        finally { _installGate.Release(); }
    }

    private async Task EnsureCudaProbeAsync(IProgress<VietsubTranslationRuntimeInstallProgress>? progress,
        CancellationToken ct, bool force = false)
    {
        var load = _loadResult!;
        var proof = new CudaProof(1, TOOL_LOCAL.SystemSetup.SystemSetupPaths.MachineFingerprint,
            VietsubTranslationWorkerProtocol.WorkerVersion, VietsubTranslationWorkerProtocol.Version,
            _workerClient.WorkerBinaryFingerprint, VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km.ModelSha256,
            VietsubTranslationGpuPlanner.PolicyVersion, load.ConfigFingerprint, load.BackendIdentity,
            load.NativeLibraryHash, load.Device!.Id, load.Device.DriverVersion, load.OffloadedLayers);
        var path = Path.Combine(_componentStore.ComponentDirectory, $"probe-cuda-{_runtimeProfile.ProfileId}-{load.OffloadedLayers}.json");
        if (!force)
        {
            try { if (File.Exists(path) && new FileInfo(path).Length <= 16 * 1024
                && JsonSerializer.Deserialize<CudaProof>(File.ReadAllText(path)) == proof) return; }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        }
        await ProbeCoreAsync(progress, ct);
        var part = path + $".{Guid.NewGuid():N}.part";
        try
        {
            await File.WriteAllTextAsync(part, JsonSerializer.Serialize(proof), ct);
            ct.ThrowIfCancellationRequested();
            File.Move(part, path, overwrite: true);
        }
        finally { if (File.Exists(part)) File.Delete(part); }
    }

    private sealed record CudaProof(int Schema, string Machine, string WorkerVersion, int Protocol,
        string WorkerFingerprint, string ModelHash, string Planner, string Config, string Backend,
        string NativeFingerprint, string DeviceId, string Driver, int Layers);
}
