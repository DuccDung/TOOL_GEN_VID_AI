using System.Diagnostics;
using System.Text.Json;
using TOOL_LOCAL.Vietsub.Translation;
using Xunit.Abstractions;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubTranslationGpuModelTests(ITestOutputHelper output)
{
    [GpuModelFact]
    [Trait("Category", "LocalGpuModel")]
    public async Task Hardware_probe_reset_and_standard_GPU_inference_survive_three_cold_starts()
    {
        var root = Environment.GetEnvironmentVariable("VIDEOMAKER_TRANSLATION_COMPONENT_ROOT")
            ?? throw new InvalidOperationException("A component root is required for opt-in GPU tests.");
        _ = VietsubTranslationCudaPack.Verify(VietsubTranslationCudaPack.DirectoryPath(root));
        var component = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km;
        var profile = VietsubTranslationWorkerProfiles.CreateStandardCpuRuntimeProfile(Environment.ProcessorCount);
        await using var client = new VietsubTranslationWorkerClient();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var hardware = await client.ProbeHardwareAsync(default);
            var device = hardware.Devices.OrderByDescending(VietsubTranslationGpuPlanner.SelectLayers).FirstOrDefault();
            Assert.NotNull(device);
            var layers = VietsubTranslationGpuPlanner.SelectLayers(device);
            Assert.True(layers > 0, hardware.ErrorCode ?? "Not enough free VRAM.");
            await client.ResetAsync();
            try
            {
                var config = profile.InferenceConfig with { GpuLayerCount = layers, GpuDeviceId = device.Id };
                var load = await client.LoadAsync(new(root,
                    Path.Combine(root, component.ComponentId, component.EngineVersion, component.ModelFileName),
                    component.ComponentId, component.EngineId, component.EngineVersion, component.ModelFileName,
                    component.ModelSizeBytes, component.ModelSha256, config, profile.ResourceRequirements), null, default);
                Assert.Equal("cuda12", load.BackendIdentity);
                Assert.Equal(layers, load.OffloadedLayers);
                Assert.True(client.IsLoaded(load.ConfigFingerprint));
                foreach (var language in new[] { "en", "zh" })
                {
                    var fixture = Fixture(language);
                    var inference = await client.InferAsync(new("BENCHMARK",
                        QwenGgufVietsubTranslationProvider.BuildPrompt(fixture),
                        QwenGgufVietsubTranslationProvider.BuildJsonGrammar(fixture), 512, 12000), null, default);
                    var parsed = QwenGgufVietsubTranslationProvider.ParseResult(inference.RawOutput, fixture);
                    Assert.Equal(2, parsed.Items.Count);
                    Assert.All(parsed.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.TranslatedText)));
                    Assert.Contains(language == "en" ? "cửa" : "thứ hai", parsed.Items[0].TranslatedText, StringComparison.OrdinalIgnoreCase);
                    Assert.True(client.IsLoaded(load.ConfigFingerprint));
                    output.WriteLine($"cycle={cycle + 1}; backend={load.BackendIdentity}; layers={layers}; language={language}; inferenceMs={inference.Metrics.ElapsedMilliseconds}");
                }
            }
            catch { output.WriteLine(client.LastDiagnostics.Replace(root, "[component]", StringComparison.OrdinalIgnoreCase)); throw; }
            finally { await client.ResetAsync(); }
        }
    }

    [GpuNativeFact]
    [Trait("Category", "LocalGpuNative")]
    public async Task CUDA_dependencies_load_without_loading_any_model()
    {
        var root = Environment.GetEnvironmentVariable("VIDEOMAKER_TRANSLATION_COMPONENT_ROOT")
            ?? throw new InvalidOperationException("A component root is required.");
        var options = VietsubTranslationWorkerClientOptions.CreateDefault() with
        { EnvironmentVariables = new Dictionary<string, string?> { ["VIDEOMAKER_TRANSLATION_WORKER_TEST_MODE"] = "backend-preflight-cuda" } };
        await using var client = new VietsubTranslationWorkerClient(options);
        var component = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km;
        // This engine tests library loading only, so there is no model RAM requirement.
        var load = await client.LoadAsync(new(root, "unused", component.ComponentId, component.EngineId, component.EngineVersion,
            component.ModelFileName, component.ModelSizeBytes, component.ModelSha256,
            VietsubTranslationWorkerProfiles.CreateSafeCpuProfile(Environment.ProcessorCount), new(0, 0, 0)), null, default);
        Assert.Equal("cuda12", load.BackendIdentity);
        Assert.Equal(VietsubTranslationCudaPack.Verify(VietsubTranslationCudaPack.DirectoryPath(root)), load.NativeLibraryHash);
        output.WriteLine($"Verified native CUDA dependency set: {load.NativeLibraryHash}; model not loaded.");
    }

    [GpuModelFact]
    [Trait("Category", "LocalGpuModel")]
    public async Task Pinned_CUDA_pack_and_CPU_translate_identical_fixture_corpus()
    {
        var root = Environment.GetEnvironmentVariable("VIDEOMAKER_TRANSLATION_COMPONENT_ROOT")
            ?? throw new InvalidOperationException("A component root is required for opt-in GPU tests.");
        var allowWarning = Environment.GetEnvironmentVariable("VIDEOMAKER_GPU_ACCEPT_RESOURCE_WARNING") == "1";
        var repeats = int.TryParse(Environment.GetEnvironmentVariable("VIDEOMAKER_GPU_BENCHMARK_REPEATS"), out var n)
            ? Math.Clamp(n, 1, 30) : 3;
        await using var client = new VietsubTranslationWorkerClient();
        var hardware = await client.ProbeHardwareAsync(default);
        var device = hardware.Devices.OrderByDescending(VietsubTranslationGpuPlanner.SelectLayers).FirstOrDefault();
        Assert.NotNull(device);
        var layers = VietsubTranslationGpuPlanner.SelectLayers(device);
        Assert.True(layers > 0, hardware.ErrorCode ?? "Not enough free VRAM for approved profiles.");
        output.WriteLine($"gpu={device.Name}; driver={device.DriverVersion}; freeVRAM={device.FreeBytes}; layers={layers}");
        await new VietsubTranslationCudaInstaller().InstallAsync(root, null, default);
        var component = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km;
        var profile = VietsubTranslationWorkerProfiles.CreateLowMemoryCpuRuntimeProfile(Environment.ProcessorCount);
        var reports = new List<object>();
        foreach (var offload in new[] { 0, layers })
        {
            await client.ResetAsync();
            // Windows can report memory from the terminated backend briefly after exit.
            // Wait for normal admission; never turn a low-memory warning into consent.
            if (!allowWarning)
            {
                var releaseWait = Stopwatch.StartNew();
                var memory = new WindowsVietsubTranslationMemoryProbe();
                while (!VietsubTranslationResourceGate.Evaluate(memory, profile.ResourceRequirements).CanLoad
                    && releaseWait.Elapsed < TimeSpan.FromSeconds(30))
                    await Task.Delay(1000);
            }
            var config = profile.InferenceConfig with { GpuLayerCount = offload, GpuDeviceId = offload == 0 ? null : device.Id };
            var cold = Stopwatch.StartNew();
            VietsubTranslationWorkerLoadResult load;
            try
            {
                load = await client.LoadAsync(new(root,
                    Path.Combine(root, component.ComponentId, component.EngineVersion, component.ModelFileName),
                    component.ComponentId, component.EngineId, component.EngineVersion, component.ModelFileName,
                    component.ModelSizeBytes, component.ModelSha256, config, profile.ResourceRequirements, allowWarning), null, default);
            }
            catch { output.WriteLine(client.LastDiagnostics); throw; }
            cold.Stop();
            Assert.Equal(offload, load.OffloadedLayers);
            if (offload > 0) Assert.Equal("cuda12", load.BackendIdentity);
            var times = new List<long>();
            long peakRam = 0;
            ulong peakVram = 0;
            for (var repetition = 0; repetition < repeats; repetition++)
            foreach (var language in new[] { "en", "zh" })
            {
                var request = Fixture(language);
                var inference = await client.InferAsync(new("BENCHMARK",
                    QwenGgufVietsubTranslationProvider.BuildPrompt(request),
                    QwenGgufVietsubTranslationProvider.BuildJsonGrammar(request), 512, 12000), null, default);
                var parsed = QwenGgufVietsubTranslationProvider.ParseResult(inference.RawOutput, request);
                Assert.Equal(2, parsed.Items.Count);
                if (language == "en") Assert.Contains("cửa", parsed.Items[0].TranslatedText, StringComparison.OrdinalIgnoreCase);
                else Assert.Contains("thứ hai", parsed.Items[0].TranslatedText, StringComparison.OrdinalIgnoreCase);
                times.Add(inference.Metrics.ElapsedMilliseconds);
                peakRam = Math.Max(peakRam, inference.Metrics.PeakWorkingSetBytes);
                peakVram = Math.Max(peakVram, inference.Metrics.PeakDeviceUsedBytes);
                output.WriteLine($"backend={load.BackendIdentity}; lang={language}; run={repetition + 1}; sceneMs={times[^1]}");
            }
            var ordered = times.Order().ToArray();
            var report = new { backend = load.BackendIdentity, device = load.Device?.Name, layers = load.OffloadedLayers,
                modelSha256 = component.ModelSha256, config, coldLoadMs = cold.ElapsedMilliseconds,
                scenes = times.Count, totalInferenceMs = times.Sum(), p50Ms = ordered[(ordered.Length - 1) / 2],
                p95Ms = ordered[(int)Math.Ceiling(ordered.Length * .95) - 1], peakWorkerRamBytes = peakRam,
                sampledPeakDeviceUsedBytes = peakVram, // whole GPU incl. desktop; samples every 500 ms, not process-exact
                cuesPerSecond = times.Count * 2 * 1000d / times.Sum() };
            reports.Add(report);
            output.WriteLine(JsonSerializer.Serialize(report));
            if (Environment.GetEnvironmentVariable("VIDEOMAKER_GPU_BENCHMARK_OUTPUT") is { Length: > 0 } path)
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static VietsubTranslationSceneRequest Fixture(string language) => new("GPU acceptance fixture", language, "vi",
        "Hai chị em nói chuyện.", "older_sister là chị, younger_brother là em trai; dùng xưng hô chị/em.", "Dịch tự nhiên.",
        [new(Guid.NewGuid(), "door", "cửa", null)], [],
        [new("C000001", Guid.NewGuid(), 0, 0, 2000, "older_sister", language == "en" ? "Could you open the door for me?" : "今天是星期一。", true, 80),
         new("C000002", Guid.NewGuid(), 1, 2100, 4000, "younger_brother", language == "en" ? "Of course, I'll do it now." : "对不起，路上堵车了。", true, 80)],
        VietsubTranslationPass.Translate, "Hội thoại liên tục.", new string('a', 64));
}

internal sealed class GpuModelFactAttribute : FactAttribute
{
    public GpuModelFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VIDEOMAKER_RUN_GPU_MODEL_TESTS") != "1")
            Skip = "Requires explicit GPU model benchmark opt-in, pinned model and NVIDIA hardware.";
    }
}

internal sealed class GpuNativeFactAttribute : FactAttribute
{
    public GpuNativeFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VIDEOMAKER_RUN_GPU_NATIVE_TESTS") != "1")
            Skip = "Requires explicit native CUDA smoke opt-in and installed pinned CUDA pack.";
    }
}
