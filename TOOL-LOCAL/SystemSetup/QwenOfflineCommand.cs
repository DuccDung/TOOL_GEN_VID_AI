using System.Text.Json;
using System.Text.RegularExpressions;
using TOOL_LOCAL.Vietsub.Translation;

namespace TOOL_LOCAL.SystemSetup;

// Tests the model and worker belonging to this application, without login, SQL,
// developer caches or HTTP downloads. Readiness belongs only to this workspace.
internal static class QwenOfflineCommand
{
    internal static bool Matches(string[] args) => args.Length > 0 && args[0] == "--check-qwen-offline";

    internal static async Task<int> RunAsync(string[] args, string? bundleRoot = null)
    {
        var stage = "VALIDATING_ARGUMENTS";
        var completedIterations = 0;
        using var network = new BlockedDownloads();
        using var http = new HttpClient(network);
        var workerEnvironment = new Dictionary<string, string?>
        {
            ["VIDEOMAKER_TRANSLATION_WORKER_TEST_MODE"] = null,
            [VietsubTranslationDiagnosticWorkspace.EnvironmentVariable] = null,
            ["PATH"] = Environment.SystemDirectory + ";" + Environment.GetEnvironmentVariable("SystemRoot")
        };
        await using var worker = new VietsubTranslationWorkerClient(
            VietsubTranslationWorkerClientOptions.CreateDefault() with
            {
                WorkerExecutablePath = Path.Combine(bundleRoot ?? AppContext.BaseDirectory,
                    "_translation_worker", "VideoMaker.Vietsub.TranslationWorker.exe"),
                EnvironmentVariables = workerEnvironment
            });
        try
        {
            var options = ParseOptions(args);
            var workspace = options.Workspace;
            var componentRoot = VietsubTranslationDiagnosticWorkspace.Initialize(workspace);
            workerEnvironment[VietsubTranslationDiagnosticWorkspace.EnvironmentVariable] = componentRoot;

            var model = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km;
            var candidate = Path.Combine(bundleRoot ?? AppContext.BaseDirectory, "third_party", "translation",
                "runtime-test", model.ComponentId, model.EngineVersion, model.ModelFileName);
            using var store = new VietsubTranslationComponentStore(model, componentRoot,
                httpClient: http, approvedLocalModelCandidates: [candidate]);
            await using var provider = new QwenGgufVietsubTranslationProvider(store, worker);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(options.Iterations > 0 ? 120 : 25));
            var progress = new StageProgress(value => Volatile.Write(ref stage, value.Stage));
            await provider.InstallAsync(progress, timeout.Token, resourceWarningAccepted: options.AcceptResourceWarning);
            var privateMemory = new List<long>();
            for (var iteration = 0; iteration < options.Iterations; iteration++)
            {
                var chinese = iteration % 2 != 0;
                stage = chinese ? "STABILITY_ZH" : "STABILITY_EN";
                var request = (chinese ? QwenGgufVietsubTranslationProvider.CreateChineseProbeRequest()
                    : QwenGgufVietsubTranslationProvider.CreateEnglishProbeRequest())
                    with { ResourceWarningAccepted = options.AcceptResourceWarning };
                var result = await provider.TranslateAsync(request, timeout.Token);
                if (chinese) QwenGgufVietsubTranslationProvider.EnsureChineseProbeResult(result);
                else QwenGgufVietsubTranslationProvider.EnsureEnglishProbeResult(result);
                if (worker.LastMetrics is { } metrics) privateMemory.Add(metrics.PrivateBytes);
                completedIterations++;
            }
            // Use the same warm-up/window bound as the existing native soak test.
            long? memoryGrowth = privateMemory.Count >= 30
                ? Median(privateMemory.TakeLast(10)) - Median(privateMemory.Skip(10).Take(10)) : null;
            if (memoryGrowth >= 256L * 1024 * 1024)
                throw new VietsubTranslationException("TRANSLATION_STABILITY_MEMORY_GROWTH",
                    "Worker memory grew beyond the stability qualification limit.");
            var status = provider.GetRuntimeStatus();
            var ready = status.Ready && network.Attempts == 0;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                QwenOfflineReady = ready, Stage = ready ? "READY" : stage, status.ErrorCode,
                ModelSha256 = model.ModelSha256, WorkerVersion = VietsubTranslationWorkerProtocol.WorkerVersion,
                ProtocolVersion = VietsubTranslationWorkerProtocol.Version, provider.RuntimeProfileId,
                WorkerBinaryFingerprint = worker.WorkerBinaryFingerprint,
                NetworkDownloadsAllowed = false, NetworkAttempts = network.Attempts, ServerAccessChecked = false,
                CompletedIterations = completedIterations, RequestedIterations = options.Iterations,
                StabilityVerified = options.Iterations >= 30 && memoryGrowth is not null,
                PrivateMemoryGrowthBytes = memoryGrowth, LastMetrics = worker.LastMetrics
            }));
            return ready ? 0 : 2;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            // Never expose native stderr, exception messages, model text or local paths.
            var operationError = Regex.Match(worker.LastDiagnostics, @"operation_error:([A-Za-z0-9_]{1,80})");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                QwenOfflineReady = false, Stage = stage,
                ErrorCode = error is VietsubTranslationException translation ? translation.Code : "qwen_offline_diagnostics_failed",
                ErrorType = error.GetType().Name,
                WorkerErrorType = operationError.Success ? operationError.Groups[1].Value : null,
                DiagnosticId = worker.LastFailureReport?.DiagnosticId,
                WorkerExitCode = worker.LastFailureReport?.ExitCode,
                WorkerExitCodeHex = worker.LastFailureReport?.ExitCodeHex,
                WorkerStage = worker.LastFailureReport?.Stage,
                CompletedIterations = completedIterations,
                NetworkDownloadsAllowed = false, NetworkAttempts = network.Attempts, ServerAccessChecked = false
            }));
            return 2;
        }
    }

    internal sealed record Options(string Workspace, bool AcceptResourceWarning, int Iterations);

    internal static Options ParseOptions(string[] args)
    {
        if (args.Length < 3 || !Matches(args) || args[1] != "--workspace" || !Path.IsPathFullyQualified(args[2]))
            throw new ArgumentException("An explicit absolute diagnostic workspace is required.");
        var accept = false;
        var iterations = 0;
        var seenIterations = false;
        for (var i = 3; i < args.Length; i++)
        {
            if (args[i] == "--accept-resource-warning" && !accept) accept = true;
            else if (args[i] == "--iterations" && !seenIterations && ++i < args.Length
                && int.TryParse(args[i], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out iterations) && iterations is >= 0 and <= 100)
                seenIterations = true;
            else throw new ArgumentException("Invalid or repeated offline diagnostic option.");
        }
        return new(Path.GetFullPath(args[2]), accept, iterations);
    }

    private static long Median(IEnumerable<long> values)
    {
        var sorted = values.Order().ToArray();
        return sorted[sorted.Length / 2];
    }

    private sealed class StageProgress(Action<VietsubTranslationRuntimeInstallProgress> report)
        : IProgress<VietsubTranslationRuntimeInstallProgress>
    {
        public void Report(VietsubTranslationRuntimeInstallProgress value) => report(value);
    }

    private sealed class BlockedDownloads : HttpMessageHandler
    {
        private int _attempts;
        public int Attempts => Volatile.Read(ref _attempts);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            throw new HttpRequestException("Model downloads are disabled in offline diagnostics.");
        }
    }
}
