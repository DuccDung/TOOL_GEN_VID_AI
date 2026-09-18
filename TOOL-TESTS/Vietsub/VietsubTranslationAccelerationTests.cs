using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using TOOL_LOCAL.Vietsub.Translation;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubTranslationAccelerationTests
{
    [Theory]
    [InlineData(1024, 0)]
    [InlineData(2100, 12)]
    [InlineData(3259, 24)]
    [InlineData(3500, 28)]
    [InlineData(3700, 30)]
    [InlineData(3962, 32)]
    [InlineData(6000, 36)]
    public void Planner_reserves_desktop_memory_and_limits_approved_offload(int freeMiB, int layers)
    {
        var device = Device with { FreeBytes = (ulong)freeMiB << 20, TotalBytes = 8UL << 30 };
        Assert.Equal(layers, VietsubTranslationGpuPlanner.SelectLayers(device));
        Assert.Equal(0, VietsubTranslationGpuPlanner.SelectLayers(device with { ComputeMajor = 5 }));
    }

    [Theory]
    [InlineData(12, 0)]
    [InlineData(24, 12)]
    [InlineData(28, 24)]
    [InlineData(30, 28)]
    [InlineData(32, 30)]
    [InlineData(36, 32)]
    public void Planner_checks_exact_free_memory_boundaries_and_resume_cap(int layers, int previous)
    {
        var threshold = VietsubTranslationGpuPlanner.RequiredBytes(layers);
        var device = Device with { TotalBytes = 8UL << 30, FreeBytes = threshold - 1 };
        Assert.Equal(previous, VietsubTranslationGpuPlanner.SelectLayers(device));
        Assert.Equal(layers, VietsubTranslationGpuPlanner.SelectLayers(device with { FreeBytes = threshold }));
        Assert.Equal(layers, VietsubTranslationGpuPlanner.SelectLayers(device with { FreeBytes = threshold + 1 }));
        Assert.Equal(previous, VietsubTranslationGpuPlanner.SelectLayers(device with { FreeBytes = 8UL << 30 }, previous));
        Assert.False(VietsubTranslationGpuPlanner.IsApprovedLayerCount(31));
        Assert.Throws<ArgumentOutOfRangeException>(() => VietsubTranslationGpuPlanner.RequiredBytes(-1));
    }

    [Fact]
    public void Planner_does_not_trust_free_memory_exceeding_physical_VRAM()
    {
        Assert.Equal(32, VietsubTranslationGpuPlanner.SelectLayers(Device with { FreeBytes = 8UL << 30 }));
    }

    [Theory]
    [InlineData(2, 32)]
    [InlineData(6, 32)]
    [InlineData(7, 24)]
    [InlineData(12, 24)]
    public void Intermediate_tiers_are_only_qualified_for_small_target_groups(int targets, int expected)
    {
        var device = Device with { FreeBytes = 3962UL << 20 };
        Assert.Equal(expected, VietsubTranslationGpuPlanner.SelectLayersForScene(device, 36, targets));
        Assert.Equal(36, VietsubTranslationGpuPlanner.SelectLayersForScene(
            device with { FreeBytes = 6UL << 30, TotalBytes = 8UL << 30 }, 36, targets));
    }

    [Fact]
    public async Task Large_first_scene_uses_existing_tier_without_attempting_unqualified_GPU_levels()
    {
        await using var fixture = new Fixture();
        fixture.Worker.HardwareDevice = Device with { FreeBytes = 3962UL << 20 };
        fixture.Worker.TargetCount = 12;
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (_, _) => Task.CompletedTask, default);
        await fixture.Provider.TranslateAsync(Request(12), default);
        Assert.Equal(new[] { 24 }, fixture.Worker.Loads);
    }

    [Fact]
    public async Task Job_with_a_later_large_scene_avoids_an_unnecessary_high_tier_load()
    {
        await using var fixture = new Fixture();
        fixture.Worker.HardwareDevice = Device with { FreeBytes = 3962UL << 20 };
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (_, _) => Task.CompletedTask, default,
            maximumPlannedTargetCues: 12);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(new[] { 24 }, fixture.Worker.Loads);
    }

    [Theory]
    [InlineData(28)]
    [InlineData(30)]
    [InlineData(32)]
    public async Task Growing_scene_downgrades_before_inference_and_resume_keeps_quality_cap(int initialLayers)
    {
        await using var fixture = new Fixture();
        fixture.Worker.HardwareDevice = Device with { FreeBytes = VietsubTranslationGpuPlanner.RequiredBytes(initialLayers) };
        VietsubTranslationExecutionState? saved = null;
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (s, _) => { saved = s; return Task.CompletedTask; }, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        fixture.Worker.TargetCount = 12;
        await fixture.Provider.TranslateAsync(Request(12), default);
        Assert.Equal(new[] { initialLayers, 24 }, fixture.Worker.Loads);
        Assert.Equal(24, saved!.Layers);
        Assert.Equal(0, saved.GpuRetries);
        Assert.False(saved.CpuFallback);
        Assert.Equal("TRANSLATION_GPU_QUALITY_CAP", saved.FallbackCode);
        fixture.Worker.TargetCount = 1;
        await fixture.Provider.BeginExecutionAsync("AUTO", saved, (_, _) => Task.CompletedTask, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(new[] { initialLayers, 24, 24 }, fixture.Worker.Loads);
    }

    [Theory]
    [InlineData(28)]
    [InlineData(30)]
    [InlineData(32)]
    [InlineData(36)]
    public async Task High_layers_have_bounded_OOM_fallback_and_persist_before_retry(int layers)
    {
        await using var fixture = new Fixture();
        fixture.Worker.HardwareDevice = Device with { TotalBytes = 8UL << 30,
            FreeBytes = VietsubTranslationGpuPlanner.RequiredBytes(layers) };
        fixture.Worker.LoadError = n => n > 0 ? "TRANSLATION_GPU_MEMORY" : null;
        var states = new List<VietsubTranslationExecutionState>();
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (s, _) => { states.Add(s); return Task.CompletedTask; }, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(new[] { layers, 24, 12, 0 }, fixture.Worker.Loads);
        Assert.Equal(new[] { layers, 24, 12, 0 }, states.Select(s => s.Layers).Distinct());
        Assert.Equal(3, states[^1].GpuRetries);
        Assert.True(states[^1].CpuFallback);
        Assert.All(states, s => Assert.Equal(VietsubTranslationGpuPlanner.PolicyVersion, s.PlannerPolicy));
        await fixture.Provider.BeginExecutionAsync("AUTO", states[^1], (_, _) => Task.CompletedTask, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(0, fixture.Worker.Loads[^1]);
    }

    [Theory]
    [InlineData(24, 0)]
    [InlineData(28, 0)]
    [InlineData(30, 0)]
    [InlineData(24, 1)]
    [InlineData(12, 2)]
    public async Task Resume_never_raises_previously_selected_layer_count(int layers, int failures)
    {
        await using var fixture = new Fixture();
        fixture.Worker.HardwareDevice = Device with { FreeBytes = 3962UL << 20 };
        // Previous releases persisted no PlannerPolicy field.
        var previous = JsonSerializer.Deserialize<VietsubTranslationExecutionState>(
            JsonSerializer.Serialize(new { Layers = layers, GpuRetries = failures, DeviceId = Device.Id }));
        Assert.Null(previous!.PlannerPolicy);
        await fixture.Provider.BeginExecutionAsync("AUTO", previous, (_, _) => Task.CompletedTask, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(new[] { layers }, fixture.Worker.Loads);
    }

    [Fact]
    public async Task Retry_rechecks_free_VRAM_after_stopping_worker_and_skips_unavailable_level()
    {
        await using var fixture = new Fixture();
        fixture.Worker.HardwareDevice = Device with { FreeBytes = 3962UL << 20 };
        fixture.Worker.LoadError = n =>
        {
            if (n != 32) return null;
            fixture.Worker.HardwareDevice = Device with { FreeBytes = 2100UL << 20 };
            return "TRANSLATION_GPU_MEMORY";
        };
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (_, _) => Task.CompletedTask, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(new[] { 32, 12 }, fixture.Worker.Loads);
    }

    [Fact]
    public async Task Resume_with_missing_device_or_exhausted_budget_goes_to_CPU()
    {
        await using var fixture = new Fixture();
        await fixture.Provider.BeginExecutionAsync("AUTO", new(Layers: 24, GpuRetries: 3), (_, _) => Task.CompletedTask, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(0, fixture.Worker.HardwareCalls);
        await fixture.Provider.BeginExecutionAsync("AUTO", new(Layers: 24, DeviceId: "missing-device"), (_, _) => Task.CompletedTask, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(new[] { 0, 0 }, fixture.Worker.Loads);
    }

    [Fact]
    public async Task Cancellation_after_retry_checkpoint_does_not_resume_failed_high_layers()
    {
        await using var fixture = new Fixture();
        fixture.Worker.HardwareDevice = Device with { FreeBytes = 3962UL << 20 };
        fixture.Worker.LoadError = n => n == 32 ? "TRANSLATION_GPU_MEMORY" : null;
        VietsubTranslationExecutionState? checkpoint = null;
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (s, _) =>
        {
            checkpoint = s;
            if (s.GpuRetries == 1) throw new OperationCanceledException();
            return Task.CompletedTask;
        }, default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Provider.TranslateAsync(Request(), default));
        Assert.Equal(24, checkpoint!.Layers);
        await fixture.Provider.BeginExecutionAsync("AUTO", checkpoint, (_, _) => Task.CompletedTask, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(new[] { 32, 24 }, fixture.Worker.Loads);
    }

    [Fact]
    public async Task Auto_retries_lower_layers_once_then_persists_CPU_for_resume()
    {
        await using var fixture = new Fixture();
        fixture.Worker.LoadError = layers => layers > 0 ? "TRANSLATION_GPU_MEMORY" : null;
        VietsubTranslationExecutionState? saved = null;
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (state, _) => { saved = state; return Task.CompletedTask; }, default);
        var result = await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Single(result.Items);
        Assert.Equal(new[] { 24, 12, 0 }, fixture.Worker.Loads);
        Assert.True(saved!.CpuFallback);
        Assert.Equal(2, saved.GpuRetries);
        Assert.True(fixture.Provider.GetRuntimeStatus().Ready); // CPU proof survives GPU trouble.
        var probeCount = fixture.Worker.HardwareCalls;
        await fixture.Provider.BeginExecutionAsync("AUTO", saved, (_, _) => Task.CompletedTask, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(probeCount, fixture.Worker.HardwareCalls);
        Assert.Equal(0, fixture.Worker.Loads.Last());
    }

    [Fact]
    public async Task GPU_crash_retries_only_the_pending_scene_with_same_prompt_on_CPU()
    {
        await using var fixture = new Fixture();
        fixture.Worker.InferenceError = "TRANSLATION_PROCESS_CRASHED";
        VietsubTranslationExecutionState? saved = null;
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (s, _) => { saved = s; return Task.CompletedTask; }, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(new[] { 24, 0 }, fixture.Worker.Loads);
        Assert.True(saved!.CpuFallback);
        Assert.Equal(1, fixture.Worker.HardwareCalls);
        Assert.Single(fixture.Worker.Prompts.Distinct());
    }

    [Theory]
    [InlineData("TRANSLATION_RUNTIME_INVALID")]
    [InlineData("TRANSLATION_WORKER_PROTOCOL_INVALID")]
    public async Task Integrity_or_protocol_failure_never_silently_falls_back(string error)
    {
        await using var fixture = new Fixture();
        fixture.Worker.LoadError = _ => error;
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (_, _) => Task.CompletedTask, default);
        var failure = await Assert.ThrowsAsync<VietsubTranslationException>(() => fixture.Provider.TranslateAsync(Request(), default));
        Assert.Equal(error, failure.Code);
        Assert.Equal(new[] { 24 }, fixture.Worker.Loads);
    }

    [Fact]
    public async Task CPU_only_does_not_probe_GPU_and_Auto_without_device_uses_CPU()
    {
        await using var fixture = new Fixture();
        await fixture.Provider.BeginExecutionAsync("CPU_ONLY", null, (_, _) => Task.CompletedTask, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(0, fixture.Worker.HardwareCalls);
        fixture.Worker.NoDevice = true;
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (_, _) => Task.CompletedTask, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(new[] { 0, 0 }, fixture.Worker.Loads);
    }

    [Fact]
    public async Task Auto_without_acceleration_pack_reports_reason_and_continues_on_CPU()
    {
        await using var fixture = new Fixture();
        File.Delete(Path.Combine(fixture.CudaPath, "llama.dll"));
        VietsubTranslationExecutionState? saved = null;
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (s, _) => { saved = s; return Task.CompletedTask; }, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(new[] { 0 }, fixture.Worker.Loads);
        Assert.Equal(0, fixture.Worker.HardwareCalls);
        Assert.True(saved!.CpuFallback);
        Assert.Equal("TRANSLATION_GPU_NOT_INSTALLED", saved.FallbackCode);
        Assert.Contains("chưa có", fixture.Provider.GetRuntimeStatus().FallbackMessage);
    }

    [Theory]
    [InlineData("TRANSLATION_GPU_INTEGRITY")]
    [InlineData("TRANSLATION_BACKEND_LOAD_FAILED")]
    public async Task Unusable_GPU_dependencies_are_rejected_and_CPU_proof_is_preserved(string error)
    {
        await using var fixture = new Fixture();
        fixture.Worker.LoadError = layers => layers > 0 ? error : null;
        VietsubTranslationExecutionState? saved = null;
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (s, _) => { saved = s; return Task.CompletedTask; }, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(new[] { 24, 0 }, fixture.Worker.Loads);
        Assert.True(saved!.CpuFallback);
        Assert.Equal(error, saved.FallbackCode);
        Assert.Null(saved.DeviceName);
        Assert.True(fixture.Provider.GetRuntimeStatus().Ready);
    }

    [Theory]
    [InlineData("TRANSLATION_RUNTIME_PROBE_FAILED")]
    [InlineData("TRANSLATION_EN_PROBE_FAILED")]
    [InlineData("TRANSLATION_ZH_PROBE_FAILED")]
    public async Task Failed_GPU_probe_falls_back_before_translating_user_cues(string error)
    {
        await using var fixture = new Fixture();
        fixture.Worker.ProbeError = error;
        VietsubTranslationExecutionState? saved = null;
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (s, _) => { saved = s; return Task.CompletedTask; }, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(new[] { 24, 0 }, fixture.Worker.Loads);
        Assert.Single(fixture.Worker.Prompts);
        Assert.Equal(error, saved!.FallbackCode);
        Assert.True(saved.CpuFallback);
        Assert.True(fixture.Provider.GetRuntimeStatus().Ready);
    }

    [Fact]
    public async Task Healthy_GPU_is_checked_once_and_used_without_CPU_fallback()
    {
        await using var fixture = new Fixture();
        VietsubTranslationExecutionState? saved = null;
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (s, _) => { saved = s; return Task.CompletedTask; }, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(new[] { 24 }, fixture.Worker.Loads);
        Assert.Equal(1, fixture.Worker.HardwareCalls);
        Assert.Equal("cuda12", saved!.Backend);
        Assert.False(saved.CpuFallback);
        Assert.Null(saved.FallbackCode);
    }

    [Theory]
    [InlineData("layers")]
    [InlineData("driver")]
    [InlineData("worker")]
    public async Task Probe_proof_is_invalidated_by_layer_driver_or_worker_change(string change)
    {
        await using var fixture = new Fixture();
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (_, _) => Task.CompletedTask, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(3, fixture.Worker.ProbeCalls);
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (_, _) => Task.CompletedTask, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(3, fixture.Worker.ProbeCalls); // Matching proof is reusable.
        if (change == "layers") fixture.Worker.HardwareDevice = Device with { FreeBytes = 3500UL << 20 };
        if (change == "driver") fixture.Worker.HardwareDevice = Device with { DriverVersion = "new-driver" };
        if (change == "worker") fixture.Worker.WorkerBinaryFingerprint = "changed-worker";
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (_, _) => Task.CompletedTask, default);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.Equal(6, fixture.Worker.ProbeCalls);
    }

    [Fact]
    public async Task Cancellation_never_launches_CPU_retry()
    {
        await using var fixture = new Fixture();
        fixture.Worker.Cancel = true;
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (_, _) => Task.CompletedTask, default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Provider.TranslateAsync(Request(), default));
        Assert.Equal(new[] { 24 }, fixture.Worker.Loads);
    }

    [Fact]
    public async Task Read_only_status_does_not_start_worker_and_corrupt_pack_is_rejected()
    {
        await using var fixture = new Fixture();
        Assert.True(fixture.Provider.GetRuntimeStatus().Ready);
        Assert.Empty(fixture.Worker.Loads);
        Assert.Equal(0, fixture.Worker.HardwareCalls);
        Assert.Throws<InvalidDataException>(() => VietsubTranslationCudaPack.Verify(fixture.CudaPath));
    }

    [Fact]
    public async Task Worker_reports_corrupt_CUDA_pack_as_integrity_failure_before_native_load()
    {
        await using var fixture = new Fixture();
        var options = VietsubTranslationWorkerClientOptions.CreateDefault() with
        { EnvironmentVariables = new Dictionary<string, string?> { ["VIDEOMAKER_TRANSLATION_WORKER_TEST_MODE"] = "backend-preflight-cuda" } };
        await using var client = new VietsubTranslationWorkerClient(options);
        var component = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km;
        var error = await Assert.ThrowsAsync<VietsubTranslationException>(() => client.LoadAsync(new(
            fixture.Root, "unused", component.ComponentId, component.EngineId, component.EngineVersion,
            component.ModelFileName, component.ModelSizeBytes, component.ModelSha256,
            VietsubTranslationWorkerProfiles.CreateSafeCpuProfile(Environment.ProcessorCount), new(0, 0, 0)), null, default));
        Assert.Equal(VietsubTranslationCudaPack.IntegrityError, error.Code);
        // Explicit repair may replace a corrupt installation, and still honours cancellation.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new VietsubTranslationCudaInstaller().InstallAsync(fixture.Root, null, cancelled.Token));
    }

    [Fact]
    public async Task Execution_notifications_belong_only_to_the_current_job()
    {
        await using var fixture = new Fixture();
        var jobId = Guid.NewGuid();
        await fixture.Provider.BeginExecutionAsync("CPU_ONLY", null, (_, _) => Task.CompletedTask, default, jobId);
        await fixture.Provider.TranslateAsync(Request(), default);
        Assert.NotNull(fixture.Provider.GetExecutionState(jobId));
        Assert.Null(fixture.Provider.GetExecutionState(Guid.NewGuid()));
        var nextJob = Guid.NewGuid();
        await fixture.Provider.BeginExecutionAsync("AUTO", null, (_, _) => Task.CompletedTask, default, nextJob);
        Assert.Null(fixture.Provider.GetExecutionState(jobId));
        Assert.Null(fixture.Provider.GetExecutionState(nextJob)!.Backend);
    }

    [Fact]
    public void New_job_snapshots_execution_policy_and_old_jobs_stay_CPU()
    {
        var settings = new VietsubTranslationSettingsSnapshot("en", "vi", "CONTEXTUAL_REQUIRED", 3, 12, 8000, 18, "", "", "", []);
        var job = new VietsubTranslationJobParameters(4, "CONTINUE", Guid.NewGuid(), 1, "engine", "1",
            new string('a', 64), settings, VietsubTranslationWorkerProfiles.StandardProfileId, ExecutionPolicy: "AUTO");
        Assert.Equal("AUTO", VietsubTranslationJobParameters.Parse(job.ToJson()).ExecutionPolicy);
        var legacy = VietsubTranslationJobParameters.Parse((job with { StrategyVersion = 3 }).ToJson());
        Assert.Equal("CPU_ONLY", legacy.ExecutionPolicy);
        Assert.Equal(job.ConfigurationFingerprint, legacy.ConfigurationFingerprint);
        Assert.Throws<VietsubTranslationException>(() => VietsubTranslationJobParameters.Parse((job with { ExecutionPolicy = "from-dom" }).ToJson()));
    }

    [Fact]
    public void Bridge_rejects_native_paths_and_layer_overrides_from_DOM()
    {
        var json = "{\"runMode\":\"CONTINUE\",\"expectedTrackId\":\"00000000-0000-0000-0000-000000000001\",\"expectedTrackRevision\":1,\"gpuLayerCount\":99}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<VietsubStartTranslationInput>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<VietsubInstallTranslationRuntimeInput>(
            "{\"installAcceleration\":true,\"nativePath\":\"C:/untrusted.dll\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task Installer_checks_archive_hash_before_extracting_and_ignores_non_allowlisted_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), "vm-cuda-zip", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archive = Path.Combine(root, "fixture.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(zip.CreateEntry("../escaped.dll").Open());
                writer.Write("untrusted");
            }
            var output = Path.Combine(root, "extracted");
            Directory.CreateDirectory(output);
            var definition = new VietsubTranslationCudaInstaller.Archive("unused", new FileInfo(archive).Length,
                new string('0', 64), "approved/");
            await Assert.ThrowsAsync<InvalidDataException>(() => VietsubTranslationCudaInstaller.ExtractVerifiedAsync(archive, definition, output, default));
            Assert.Empty(Directory.EnumerateFileSystemEntries(output));
            definition = definition with { Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))) };
            await VietsubTranslationCudaInstaller.ExtractVerifiedAsync(archive, definition, output, default);
            Assert.False(File.Exists(Path.Combine(root, "escaped.dll")));
            Assert.Empty(Directory.EnumerateFileSystemEntries(output));
        }
        finally { Directory.Delete(root, true); }
    }

    internal static readonly VietsubTranslationGpuDevice Device = new(
        "GPU-00000000-0000-0000-0000-000000000000", "Fixture NVIDIA", 0, "fixture-driver", 4UL << 30, 3259UL << 20, 8);
    private static VietsubTranslationSceneRequest Request(int count = 1) => new("fixture", "en", "vi", "", "", "", [], [],
        Enumerable.Range(0, count).Select(i => new VietsubTranslationCueInput($"C{i + 1:D6}",
            Guid.NewGuid(), i, i * 1000, i * 1000 + 1000, "speaker", "Hello.", true, 40)).ToArray(),
        VietsubTranslationPass.Translate, "", new string('a', 64));

    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "vm-gpu", Guid.NewGuid().ToString("N"));
        public FakeWorker Worker { get; } = new();
        public QwenGgufVietsubTranslationProvider Provider { get; }
        public string CudaPath => VietsubTranslationCudaPack.DirectoryPath(Root);
        public Fixture()
        {
            var bytes = Encoding.UTF8.GetBytes("test-model");
            var component = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km with
            { ModelSizeBytes = bytes.Length, ModelSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), MinimumRamBytes = 1 };
            var store = new VietsubTranslationComponentStore(component, Root, memoryProbe: new Memory());
            Directory.CreateDirectory(store.ComponentDirectory);
            File.WriteAllBytes(store.ModelPath, bytes);
            var avx = VietsubTranslationWorkerProfiles.SelectAvxName();
            var level = avx == "noavx" ? "None" : avx == "avx2" ? "Avx2" : "Avx";
            var workerDir = Path.Combine(AppContext.BaseDirectory, "_translation_worker");
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(workerDir,
                "runtimes", "win-x64", "native", avx, "llama.dll")))).ToLowerInvariant();
            store.MarkProbeVerified(new(VietsubTranslationWorkerProtocol.WorkerVersion, VietsubTranslationWorkerProtocol.Version,
                VietsubTranslationWorkerProtocol.ComputeWorkerBinaryFingerprint(workerDir), "cpu-" + level.ToLowerInvariant(), level, hash,
                VietsubTranslationWorkerProtocol.ComputeConfigFingerprint(VietsubTranslationWorkerProfiles.CreateSafeCpuProfile(Environment.ProcessorCount))));
            Directory.CreateDirectory(CudaPath);
            foreach (var name in VietsubTranslationCudaPack.NativeHashes.Keys) File.WriteAllText(Path.Combine(CudaPath, name), "fake");
            Provider = new(store, Worker);
        }
        public async ValueTask DisposeAsync() { await Provider.DisposeAsync(); Directory.Delete(Root, true); }
    }

    private sealed class Memory : IVietsubTranslationMemoryProbe
    {
        public bool TryCapture(out VietsubTranslationMemorySnapshot s, out string? error)
        { s = new(16UL << 30, 12UL << 30, 32UL << 30, 24UL << 30, 20, DateTime.UtcNow); error = null; return true; }
    }

    private sealed class FakeWorker : IVietsubTranslationWorkerClient
    {
        public string WorkerBinaryFingerprint { get; set; } = "fixture";
        public List<int> Loads { get; } = [];
        public List<string> Prompts { get; } = [];
        public Func<int, string?>? LoadError { get; set; }
        public string? InferenceError { get; set; }
        public string? ProbeError { get; set; }
        public bool Cancel { get; set; }
        public bool NoDevice { get; set; }
        public VietsubTranslationGpuDevice HardwareDevice { get; set; } = Device;
        public int HardwareCalls { get; private set; }
        public int ProbeCalls { get; private set; }
        public int TargetCount { get; set; } = 1;
        private string? _fingerprint;
        private int _layers;
        public bool IsLoaded(string configFingerprint) => _fingerprint == configFingerprint;
        public Task ResetAsync() { _fingerprint = null; return Task.CompletedTask; }
        public Task<VietsubTranslationHardwareResult> ProbeHardwareAsync(CancellationToken ct)
        { HardwareCalls++; return Task.FromResult(new VietsubTranslationHardwareResult(NoDevice ? [] : [HardwareDevice])); }
        public Task<VietsubTranslationWorkerLoadResult> LoadAsync(VietsubTranslationWorkerLoadRequest request,
            IProgress<VietsubTranslationWorkerProgress>? progress, CancellationToken ct)
        {
            Assert.Null(_fingerprint); // Backend changes always restart the process.
            _layers = request.Config.GpuLayerCount;
            Loads.Add(_layers);
            if (Cancel) throw new OperationCanceledException();
            if (LoadError?.Invoke(_layers) is { } code) throw new VietsubTranslationException(code, "fixture");
            _fingerprint = VietsubTranslationWorkerProtocol.ComputeConfigFingerprint(request.Config);
            return Task.FromResult(new VietsubTranslationWorkerLoadResult(_layers > 0 ? "cuda12" : "cpu-avx2", "Avx2",
                "fixture", _fingerprint, new(1, 1, 1, 1, 1, 1), _layers > 0 ? HardwareDevice : null, _layers));
        }
        public Task<VietsubTranslationWorkerInferResult> InferAsync(VietsubTranslationWorkerInferRequest request,
            IProgress<VietsubTranslationWorkerProgress>? progress, CancellationToken ct)
        {
            if (request.Stage.StartsWith("PROBING", StringComparison.Ordinal)) ProbeCalls++;
            if (_layers > 0 && request.Stage.StartsWith("PROBING", StringComparison.Ordinal) && ProbeError is { } probeError)
                throw new VietsubTranslationException(probeError, "fixture");
            if (request.Stage == "TRANSLATING")
            {
                Prompts.Add(request.Prompt);
                if (_layers > 0 && InferenceError is { } code) throw new VietsubTranslationException(code, "fixture");
            }
            var raw = request.Stage switch
            {
                "PROBING_EN" => "[{\"cueAlias\":\"C000001\",\"translatedText\":\"Chị nhờ em mở cửa.\"},{\"cueAlias\":\"C000002\",\"translatedText\":\"Em làm ngay.\"}]",
                "PROBING_ZH" => "[{\"cueAlias\":\"C000001\",\"translatedText\":\"Hôm nay thứ hai.\"},{\"cueAlias\":\"C000002\",\"translatedText\":\"Xin lỗi, kẹt xe.\"}]",
                "TRANSLATING" => JsonSerializer.Serialize(Enumerable.Range(1, TargetCount)
                    .Select(i => new { cueAlias = $"C{i:D6}", translatedText = "Xin chào." })),
                _ => "[{\"cueAlias\":\"C000001\",\"translatedText\":\"Xin chào.\"}]"
            };
            return Task.FromResult(new VietsubTranslationWorkerInferResult(raw, new(1, 1, 1, 1, 1, 1)));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
