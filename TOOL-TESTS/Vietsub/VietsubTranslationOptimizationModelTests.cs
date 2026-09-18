using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.Vietsub.Translation;
using Xunit.Abstractions;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubTranslationOptimizationModelTests(ITestOutputHelper output)
{
    private const string CorpusVersion = "translation-en-zh-2-6-12-v1";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    [TranslationOptimizationFact]
    [Trait("Category", "TranslationOptimizationModel")]
    public async Task Compare_executor_modes_and_GPU_layers_on_fixed_corpus()
    {
        var root = Required("VIDEOMAKER_TRANSLATION_COMPONENT_ROOT");
        var reportPath = Required("VIDEOMAKER_TRANSLATION_PERFORMANCE_OUTPUT");
        var cases = (Environment.GetEnvironmentVariable("VIDEOMAKER_TRANSLATION_PERFORMANCE_CASES")
            ?? "legacy:24,reuse:24,reuse:28,reuse:30,reuse:32").Split(',').Select(ParseCase).ToArray();
        var repeats = ReadCount("VIDEOMAKER_TRANSLATION_PERFORMANCE_REPEATS", 3, 10);
        var maximumTargetCues = Math.Max(2, ReadCount("VIDEOMAKER_TRANSLATION_PERFORMANCE_MAX_TARGETS", 12, 12));
        var corpus = new[] { "en", "zh" }.SelectMany(language => new[] { 2, 6, 12 }
            .Where(count => count <= maximumTargetCues)
            .Select(count => Fixture(language, count))).ToArray();
        var corpusHash = Hash(string.Join("\n", corpus.Select(QwenGgufVietsubTranslationProvider.BuildPrompt)));
        var reports = new List<CaseReport>();
        var failures = new List<string>();
        _ = VietsubTranslationCudaPack.Verify(VietsubTranslationCudaPack.DirectoryPath(root));
        var profile = VietsubTranslationWorkerProfiles.CreateStandardCpuRuntimeProfile(Environment.ProcessorCount);
        // Alternate order across rounds to expose temperature/order effects. Each case gets a fresh process.
        for (var round = 0; round < repeats; round++)
        foreach (var candidate in round % 2 == 0 ? cases : cases.Reverse())
        {
            await using var client = BenchmarkClient();
            var hardware = await client.ProbeHardwareAsync(default);
            var device = hardware.Devices.OrderByDescending(VietsubTranslationGpuPlanner.SelectLayers).FirstOrDefault();
            Assert.NotNull(device);
            await client.ResetAsync();
            var config = profile.InferenceConfig with { GpuLayerCount = candidate.Layers,
                GpuDeviceId = candidate.Layers > 0 ? device.Id : null, ExecutorMode = candidate.Mode };
            var report = new CaseReport(candidate.Label, round + 1, device, config, client.WorkerBinaryFingerprint);
            reports.Add(report);
            try
            {
                if (candidate.Layers > VietsubTranslationGpuPlanner.SelectLayers(device))
                {
                    report.Status = "Skipped";
                    report.ErrorCode = "INSUFFICIENT_FREE_VRAM";
                    continue;
                }
                await WaitForResourcesAsync(profile.ResourceRequirements);
                var cold = Stopwatch.StartNew();
                var load = await client.LoadAsync(LoadRequest(root, profile, config), null, default);
                report.ColdLoadMs = cold.Elapsed.TotalMilliseconds;
                Assert.Equal(candidate.Layers, load.OffloadedLayers);
                report.Backend = load.BackendIdentity;
                foreach (var scene in corpus)
                {
                    var result = await InferAsync(client, scene);
                    Validate(result, scene);
                    report.Scenes.Add(new(scene.SourceLanguageCode, scene.Cues.Count,
                        result.Metrics, Hash(result.RawOutput)));
                }
                // A -> other scenes/grammars/language -> A must be independent on the same backend.
                var again = await InferAsync(client, corpus[0]);
                Validate(again, corpus[0]);
                Assert.Equal(report.Scenes[0].OutputSha256, Hash(again.RawOutput));
                Assert.Equal(candidate.Mode == VietsubTranslationExecutorModes.Legacy, again.Metrics.ExecutorCreated);
                if (candidate.Mode == VietsubTranslationExecutorModes.Reuse)
                {
                    Assert.True(report.Scenes[0].Metrics.ExecutorCreated);
                    Assert.All(report.Scenes.Skip(1), s => Assert.False(s.Metrics.ExecutorCreated));
                }
                report.Status = "Passed";
            }
            catch (Exception e)
            {
                report.Status = "Failed";
                report.ErrorCode = e is VietsubTranslationException known ? known.Code : e.GetType().Name;
                failures.Add($"{candidate.Label}/round{round + 1}: {report.ErrorCode}");
            }
            finally
            {
                await client.ResetAsync();
                await WriteReportAsync(reportPath, corpusHash, maximumTargetCues, reports);
                output.WriteLine($"{report.Label}; round={report.Round}; status={report.Status}; "
                    + $"coldMs={report.ColdLoadMs:F0}; sceneMs={report.Scenes.Sum(s => s.Metrics.ElapsedMilliseconds)}; code={report.ErrorCode}");
            }
        }
        // Only the small-scene corpus qualifies the new production tiers. Structural
        // JSON validation alone did not catch meaning changes in the 12-cue corpus.
        var baseline = reports.FirstOrDefault(r => r.Label == "legacy:24" && r.Status == "Passed");
        if (baseline is not null)
        {
            foreach (var report in reports.Where(r => r.Status == "Passed" && r.Label.StartsWith("reuse:")))
            foreach (var scene in report.Scenes.Where(s => s.CueCount <= VietsubTranslationGpuPlanner.IntermediateLayerMaximumTargetCues))
            {
                var expected = baseline.Scenes.Single(s => s.Language == scene.Language && s.CueCount == scene.CueCount);
                if (scene.OutputSha256 == expected.OutputSha256) continue;
                report.Status = "Failed";
                report.ErrorCode = "QUALIFIED_CORPUS_OUTPUT_CHANGED";
                failures.Add($"{report.Label}/round{report.Round}: {report.ErrorCode}");
            }
        }
        await WriteReportAsync(reportPath, corpusHash, maximumTargetCues, reports);
        Assert.Empty(failures);
        Assert.Contains(reports, r => r.Status == "Passed");
    }

    [TranslationOptimizationFact]
    [Trait("Category", "TranslationOptimizationSoak")]
    public async Task Repeated_scenes_errors_and_reset_keep_inference_independent()
    {
        var root = Required("VIDEOMAKER_TRANSLATION_COMPONENT_ROOT");
        var reportPath = Required("VIDEOMAKER_TRANSLATION_PERFORMANCE_OUTPUT") + ".soak.json";
        var candidate = ParseCase(Environment.GetEnvironmentVariable("VIDEOMAKER_TRANSLATION_SOAK_CASE") ?? "reuse:32");
        var count = ReadCount("VIDEOMAKER_TRANSLATION_SOAK_SCENES", 100, 200);
        var profile = VietsubTranslationWorkerProfiles.CreateStandardCpuRuntimeProfile(Environment.ProcessorCount);
        _ = VietsubTranslationCudaPack.Verify(VietsubTranslationCudaPack.DirectoryPath(root));
        await using var client = BenchmarkClient();
        var hardware = await client.ProbeHardwareAsync(default);
        var device = hardware.Devices.OrderByDescending(VietsubTranslationGpuPlanner.SelectLayers).First();
        Assert.True(VietsubTranslationGpuPlanner.SelectLayers(device) >= candidate.Layers, "Insufficient free VRAM.");
        var config = profile.InferenceConfig with { GpuLayerCount = candidate.Layers,
            GpuDeviceId = candidate.Layers > 0 ? device.Id : null, ExecutorMode = candidate.Mode };
        var samples = new List<SceneReport>();
        var expected = new Dictionary<string, string>();
        try
        {
            await client.ResetAsync();
            await WaitForResourcesAsync(profile.ResourceRequirements);
            await client.LoadAsync(LoadRequest(root, profile, config), null, default);
            for (var i = 0; i < count; i++)
            {
                var scene = Fixture(i % 2 == 0 ? "en" : "zh", 2);
                var result = await InferAsync(client, scene);
                Validate(result, scene);
                var hash = Hash(result.RawOutput);
                if (expected.TryGetValue(scene.SourceLanguageCode, out var prior)) Assert.Equal(prior, hash);
                else expected.Add(scene.SourceLanguageCode, hash);
                samples.Add(new(scene.SourceLanguageCode, 2, result.Metrics, hash));
                if ((i + 1) % 10 == 0) output.WriteLine($"soak={i + 1}/{count}; layers={candidate.Layers}; privateBytes={result.Metrics.PrivateBytes}");
            }
            // A rejected output must not leave an active context/sampler for the next request.
            var first = Fixture("en", 2);
            var error = await Assert.ThrowsAsync<VietsubTranslationException>(() => client.InferAsync(
                InferRequest(first) with { MaximumOutputCharacters = 1 }, null, default));
            Assert.Equal(VietsubTranslationErrorCodes.ResultInvalid, error.Code);
            var recovered = await InferAsync(client, first);
            Assert.Equal(expected["en"], Hash(recovered.RawOutput));
            // Cancel during actual inference, then reset/load: session recovery must remain usable.
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.InferAsync(InferRequest(Fixture("zh", 12)), null, cancel.Token));
            await client.ResetAsync();
            await WaitForResourcesAsync(profile.ResourceRequirements);
            await client.LoadAsync(LoadRequest(root, profile, config), null, default);
            recovered = await InferAsync(client, first);
            Assert.Equal(expected["en"], Hash(recovered.RawOutput));
            // Compare medians after warm-up; tolerate bounded native allocator caching, not linear growth.
            if (count >= 30)
            {
                var early = samples.Skip(10).Take(10).Select(s => s.Metrics.PrivateBytes).Order().ElementAt(5);
                var late = samples.TakeLast(10).Select(s => s.Metrics.PrivateBytes).Order().ElementAt(5);
                Assert.True(late - early < 256L << 20, $"Private memory grew by {late - early} bytes.");
            }
        }
        finally
        {
            await client.ResetAsync();
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { CorpusVersion, config, samples }, Json));
        }
    }

    private static VietsubTranslationWorkerClient BenchmarkClient() => new(VietsubTranslationWorkerClientOptions.CreateDefault() with
    {
        EnvironmentVariables = new Dictionary<string, string?> { ["VIDEOMAKER_TRANSLATION_WORKER_BENCHMARK"] = "1" }
    });

    private static VietsubTranslationWorkerLoadRequest LoadRequest(string root, VietsubTranslationWorkerRuntimeProfile profile,
        VietsubTranslationWorkerInferenceConfig config)
    {
        var c = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km;
        return new(root, Path.Combine(root, c.ComponentId, c.EngineVersion, c.ModelFileName), c.ComponentId,
            c.EngineId, c.EngineVersion, c.ModelFileName, c.ModelSizeBytes, c.ModelSha256, config, profile.ResourceRequirements);
    }

    private static Task<VietsubTranslationWorkerInferResult> InferAsync(VietsubTranslationWorkerClient client, VietsubTranslationSceneRequest scene) =>
        client.InferAsync(InferRequest(scene), null, default);

    private static VietsubTranslationWorkerInferRequest InferRequest(VietsubTranslationSceneRequest scene) => new("BENCHMARK",
        QwenGgufVietsubTranslationProvider.BuildPrompt(scene), QwenGgufVietsubTranslationProvider.BuildJsonGrammar(scene),
        Math.Min(1024, Math.Clamp(128 + scene.Cues.Count * 96, 256, 1536)), 12000);

    private static void Validate(VietsubTranslationWorkerInferResult result, VietsubTranslationSceneRequest scene)
    {
        var parsed = QwenGgufVietsubTranslationProvider.ParseResult(result.RawOutput, scene);
        Assert.Equal(scene.Cues.Count, parsed.Items.Count);
        Assert.All(parsed.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.TranslatedText)));
        Assert.Contains(scene.SourceLanguageCode == "en" ? "cửa" : "thứ hai", parsed.Items[0].TranslatedText, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Metrics.FirstOutputMilliseconds);
    }

    private static VietsubTranslationSceneRequest Fixture(string language, int count)
    {
        string[] english = ["Could you open the door for me?", "Of course, I'll do it now.", "I will help you.",
            "Thank you for waiting for me.", "We need to leave at 8 o'clock.", "I will bring two bottles of water.",
            "Please close the window before we leave.", "I have already put the books on the table.",
            "Remember to call me when you arrive.", "I understand. Don't worry about me.",
            "It might rain this afternoon.", "Then I will take an umbrella."];
        string[] chinese = ["今天是星期一。", "对不起，路上堵车了。", "我会帮助你的。", "谢谢你等我。",
            "我们八点出发。", "我带两瓶水。", "走之前请关上窗户。", "我已经把书放在桌子上了。",
            "到了以后记得给我打电话。", "我知道了，别担心。", "今天下午可能下雨。", "那我带一把伞。"];
        var cues = Enumerable.Range(0, count).Select(i => new VietsubTranslationCueInput($"C{i + 1:D6}",
            Guid.Parse($"00000000-0000-0000-0000-{i + 1:D12}"), i, i * 3000, i * 3000 + 2800,
            i % 2 == 0 ? "older_sister" : "younger_brother", (language == "en" ? english : chinese)[i], true, 100)).ToArray();
        return new("Optimization fixture v1", language, "vi", "Hai chị em chuẩn bị đi ra ngoài.",
            "older_sister là chị, younger_brother là em trai; dùng xưng hô chị/em.", "Dịch tự nhiên, giữ số liệu.",
            [new(Guid.Parse("10000000-0000-0000-0000-000000000001"), "door", "cửa", null)], [], cues,
            VietsubTranslationPass.Translate, "Hội thoại liên tục trong gia đình.", new string('a', 64));
    }

    private static async Task WaitForResourcesAsync(VietsubTranslationResourceRequirements requirements)
    {
        var timer = Stopwatch.StartNew();
        var probe = new WindowsVietsubTranslationMemoryProbe();
        while (!VietsubTranslationResourceGate.Evaluate(probe, requirements).CanLoad && timer.Elapsed < TimeSpan.FromSeconds(30))
            await Task.Delay(1000);
        Assert.True(VietsubTranslationResourceGate.Evaluate(probe, requirements).CanLoad, "Insufficient RAM/commit; no warning bypass.");
    }

    private static (string Label, string Mode, int Layers) ParseCase(string value)
    {
        var parts = value.Split(':');
        var mode = parts[0] switch { "legacy" => VietsubTranslationExecutorModes.Legacy,
            "reuse" => VietsubTranslationExecutorModes.Reuse, "prefix" => VietsubTranslationExecutorModes.PrefixCache,
            _ => throw new ArgumentException("Unknown benchmark executor.") };
        if (parts.Length != 2 || !int.TryParse(parts[1], out var layers) || !VietsubTranslationGpuPlanner.IsApprovedLayerCount(layers))
            throw new ArgumentException("Unknown benchmark layer count.");
        return (value, mode, layers);
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"{name} is required.");
    private static int ReadCount(string name, int fallback, int maximum) => int.TryParse(Environment.GetEnvironmentVariable(name), out var n)
        ? Math.Clamp(n, 1, maximum) : fallback;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static Task WriteReportAsync(string path, string corpusHash, int maximumTargetCues, List<CaseReport> cases) => File.WriteAllTextAsync(path,
        JsonSerializer.Serialize(new { CorpusVersion, corpusHash, maximumTargetCues, modelHash = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km.ModelSha256,
            protocol = VietsubTranslationWorkerProtocol.Version, planner = VietsubTranslationGpuPlanner.PolicyVersion, cases }, Json));

    private sealed record SceneReport(string Language, int CueCount, VietsubTranslationWorkerMetrics Metrics, string OutputSha256);
    private sealed class CaseReport(string label, int round, VietsubTranslationGpuDevice device,
        VietsubTranslationWorkerInferenceConfig config, string workerFingerprint)
    {
        public string Label { get; } = label;
        public int Round { get; } = round;
        public VietsubTranslationGpuDevice Device { get; } = device;
        public VietsubTranslationWorkerInferenceConfig Config { get; } = config;
        public string WorkerFingerprint { get; } = workerFingerprint;
        public string Status { get; set; } = "Running";
        public string? ErrorCode { get; set; }
        public string? Backend { get; set; }
        public double ColdLoadMs { get; set; }
        public List<SceneReport> Scenes { get; } = [];
    }
}

internal sealed class TranslationOptimizationFactAttribute : FactAttribute
{
    public TranslationOptimizationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("VIDEOMAKER_RUN_TRANSLATION_PERFORMANCE_TESTS") != "1")
            Skip = "Requires explicit translation optimization opt-in, pinned model, verified CUDA pack and free RAM/VRAM.";
    }
}
