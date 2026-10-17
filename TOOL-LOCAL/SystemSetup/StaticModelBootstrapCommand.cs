using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.ModelDistribution;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.ModelDistribution;

namespace TOOL_LOCAL.SystemSetup;

// Explicit production maintenance entry point. It does not load application settings,
// authenticate, connect to SQL, open a project, or permit upstream model downloads.
internal static class StaticModelBootstrapCommand
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly object OutputLock = new();
    private static long _lastProgressTimestamp;
    private static string? _lastProgressStage;
    internal static bool Matches(string[] args) => args.Length > 0 && args[0] is
        "--prepare-static-models" or "--check-static-prerequisites" or "--check-static-model-policy";

    internal static async Task<int> RunAsync(string[] args, Action? checkTrust = null)
    {
        if (args is ["--check-static-model-policy"])
        {
            var policy = InspectPolicy(checkTrust);
            WriteOutput(policy);
            return policy.ModelUseAllowed ? 0 : 2;
        }
        var operationId = Guid.NewGuid().ToString("N");
        string? resultPath = null;
        var results = new List<StaticModelComponentResult>();
        string? warningProfileId = null;
        StaticModelBootstrapResult? completed = null;
        try
        {
            if (args.Length > 0 && args[0] == "--check-static-prerequisites")
            {
                var preflightCacheRoot = StaticModelRuntimePolicy.DefaultCacheRoot;
                Options? preflightOptions = null;
                if (args.Length > 1)
                {
                    var preflight = ParseOptions(["--prepare-static-models", .. args.Skip(1)]);
                    operationId = preflight.OperationId;
                    resultPath = preflight.ResultPath;
                    preflightCacheRoot = preflight.CacheRoot;
                    preflightOptions = preflight;
                }
                using var preflightTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
                if (preflightOptions is not null) await WaitForParentJobAsync(preflightOptions, preflightTimeout.Token);
                (checkTrust ?? StaticModelRuntimePolicy.EnsureNotRevoked)();
                await InspectPrerequisitesAsync(results, preflightTimeout.Token);
                var ready = results.Count == 3 && results.All(x => x.Ready);
                var checkedResult = new StaticModelBootstrapResult(1, operationId, ApprovedStaticModelBundle.Current.BundleId,
                    ready, results, ready ? null : "static_model_prerequisites_failed",
                    RuntimeDiskRequiredBytes: ready ? EstimateRuntimeDiskRequiredBytes(preflightCacheRoot) : null);
                if (resultPath is not null) StaticModelRuntimePolicy.WriteAtomic(resultPath, checkedResult);
                WriteOutput(checkedResult);
                return ready ? 0 : 2;
            }
            var options = ParseOptions(args);
            operationId = options.OperationId;
            resultPath = options.ResultPath;
            // Validate command ownership before writing the gate or allocating large files.
            using var parent = Process.GetProcessById(options.ParentProcessId);
            var parentStarted = parent.StartTime.ToUniversalTime();
            if (parent.HasExited || parent.Id == Environment.ProcessId) throw new ArgumentException("Invalid setup owner.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromHours(2));
            await using var parentMonitor = new System.Threading.Timer(_ =>
            {
                try
                {
                    if (parent.HasExited || parent.StartTime.ToUniversalTime() != parentStarted) timeout.Cancel();
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
                { timeout.Cancel(); }
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            var token = timeout.Token;
            await WaitForParentJobAsync(options, token);
            (checkTrust ?? StaticModelRuntimePolicy.EnsureNotRevoked)();
            using var lease = StaticModelRuntimePolicy.AcquireUsageLease(installing: true);
            StaticModelRuntimePolicy.MarkPending(operationId);
            await InspectPrerequisitesAsync(results, token);
            if (results.Any(x => !x.Ready)) throw new SetupException("static_model_prerequisites_failed",
                "Thành phần đi kèm ứng dụng hoặc WebView2 chưa sẵn sàng.");
            if (VietsubTranslationWorkerProtocol.WorkerVersion != ApprovedStaticModelBundle.Current.RequiredWorkerVersion
                || VietsubTranslationWorkerProtocol.Version != ApprovedStaticModelBundle.Current.RequiredWorkerProtocolVersion)
                throw new SetupException("static_model_worker_incompatible", "Worker không tương thích bộ model của bản phát hành.");
            ApprovedStaticModelBundle.Current.ValidateDesktopCompatibility(Updates.DesktopBuildInfo.Version,
                Updates.DesktopBuildInfo.BuildNumber, VietsubTranslationWorkerProtocol.WorkerVersion, VietsubTranslationWorkerProtocol.Version);
            var resources = VietsubTranslationResourceGate.Evaluate(new WindowsVietsubTranslationMemoryProbe(),
                VietsubTranslationResourceRequirements.LowMemoryCpu);
            if (resources.IsBlocking)
                throw new SetupException("static_model_resources_unavailable", "Không thể chuẩn bị Qwen trên máy này.");
            if (resources.RequiresConfirmation && (!options.AcceptResourceWarning
                || options.ConfirmedResourceProfile != VietsubTranslationWorkerProfiles.LowMemoryProfileId))
            {
                warningProfileId = VietsubTranslationWorkerProfiles.LowMemoryProfileId;
                throw new SetupException("static_model_resource_warning", resources.Message +
                    " Xác nhận sẽ tiếp tục với profile CPU tiết kiệm RAM (" + VietsubTranslationWorkerProfiles.LowMemoryProfileId + ").");
            }
            StaticModelRuntimePolicy.EnsureNotRevoked();
            using var archive = new StaticModelArchiveStore();
            var payload = await archive.EnsurePayloadAsync(options.CacheRoot,
                new SetupProgress<StaticModelSetupProgress>(WriteOutput), token);
            StaticModelRuntimePolicy.EnsureNotRevoked();
            using var store = new VietsubTranslationComponentStore(VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km,
                componentsRoot: StaticModelRuntimePolicy.TranslationRoot,
                approvedLocalModelCandidates: [payload.QwenModelPath], allowUpstreamDownload: false);
            await using var qwen = new QwenGgufVietsubTranslationProvider(store);
            if (options.AcceptResourceWarning) qwen.TrySelectRuntimeProfile(options.ConfirmedResourceProfile!);
            try
            {
                await RunComponentAsync(new QwenSetupAdapter(qwen), options.AcceptResourceWarning
                    && options.ConfirmedResourceProfile == qwen.RuntimeProfileId, results, token,
                    () => StaticModelComponentEvidence.Qwen(qwen.RuntimeProfileId));
            }
            catch (VietsubTranslationException error) when (error.Code == VietsubTranslationErrorCodes.ResourceConfirmationRequired)
            {
                warningProfileId = qwen.RuntimeProfileId;
                throw;
            }
            // The payload contains Python and wheels; no machine PATH interpreter is used.
            using var voice = new VietsubVoiceComponentStore(null, featureEnabled: true,
                useUserComponentsRoot: true, requireUserComponentsRoot: true,
                offlineBundle: new PiperOfflineBundle(payload.PiperBundleDirectory, PiperOfflineBundle.ApprovedDefinition()),
                allowUpstreamDownloads: false);
            await RunComponentAsync(new PiperSetupAdapter(voice, true), false, results, token,
                () => StaticModelComponentEvidence.Piper(voice.RuntimeRoot));
            completed = new(1, operationId, payload.BundleId, results.Count == 5 && results.All(x => x.Ready), results);
            if (!StaticModelRuntimePolicy.HasAllRequiredComponents(completed))
                throw new SetupException("static_model_component_probe_failed", "Các thành phần bắt buộc chưa đạt kiểm tra.");
            token.ThrowIfCancellationRequested();
            StaticModelRuntimePolicy.EnsureNotRevoked();
            // Setup owns the remaining application transaction. Its pending barrier must
            // survive this child process until application receipt and rollback journal commit.
            StaticModelRuntimePolicy.Commit(completed, options.CacheRoot, keepPending: options.ParentJobReadyPath is not null);
            StaticModelRuntimePolicy.WriteAtomic(resultPath, completed);
            WriteOutput(completed);
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            var code = error switch
            {
                OperationCanceledException => "static_model_setup_cancelled",
                VietsubTranslationException translation when translation.Code == VietsubTranslationErrorCodes.ResourceConfirmationRequired
                    => "static_model_resource_warning",
                VietsubTranslationException translation => translation.Code,
                VietsubVoiceException voice => voice.Code,
                ModelDistributionException distribution => distribution.Code,
                SetupException setup => setup.Code,
                ArgumentException => "static_model_arguments_invalid",
                IOException => "static_model_files_unavailable",
                UnauthorizedAccessException => "static_model_access_denied",
                _ => "static_model_setup_failed"
            };
            completed = new(1, operationId, ApprovedStaticModelBundle.Current.BundleId, false, results, code,
                error is SetupException warning && warning.Code == "static_model_resource_warning" ? warning.Message
                    : "Cài model chưa hoàn tất. Dữ liệu tải hợp lệ được giữ lại để tiếp tục hoặc sửa cài đặt.",
                ResourceProfileId: code == "static_model_resource_warning" ? warningProfileId : null);
            if (resultPath is not null)
            {
                try { StaticModelRuntimePolicy.WriteAtomic(resultPath, completed); }
                catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
            WriteOutput(completed);
            return error is OperationCanceledException ? 3 : 2;
        }
    }

    internal sealed record PolicyReport(int SchemaVersion, bool Required, string BundleId, string ArchiveSha256,
        string WorkerVersion, int WorkerProtocolVersion, bool ModelUseAllowed, string? ErrorCode)
    {
        public string Version => Updates.DesktopBuildInfo.Version;
        public int BuildNumber => Updates.DesktopBuildInfo.BuildNumber;
        public string Channel => typeof(StaticModelBootstrapCommand).Assembly.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
            .SingleOrDefault(x => x.Key == "StaticModelReleaseChannel")?.Value ?? "Stable";
        public int DistributionProtocol => Updates.DesktopBuildInfo.DistributionProtocol;
        public string Profile => Updates.DesktopBuildInfo.Profile;
        public int DownloadPolicyVersion => TOOL_SHARED.Distribution.Updates.DesktopArtifactDownloadPolicy.PolicyVersion;
    }

    internal static PolicyReport InspectPolicy(Action? checkTrust = null)
    {
        string? errorCode = null;
        try { (checkTrust ?? StaticModelRuntimePolicy.EnsureNotRevoked)(); }
        catch (ModelDistributionException error) { errorCode = error.Code; }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        { errorCode = "static_model_trust_state_invalid"; }
        return new(1, StaticModelRuntimePolicy.RequiredByRelease, ApprovedStaticModelBundle.Current.BundleId,
            ApprovedStaticModelBundle.Current.ArchiveSha256, VietsubTranslationWorkerProtocol.WorkerVersion,
            VietsubTranslationWorkerProtocol.Version, errorCode is null, errorCode);
    }

    private static async Task WaitForParentJobAsync(Options options, CancellationToken token)
    {
        if (options.ParentJobReadyPath is null) return;
        using var parent = Process.GetProcessById(options.ParentProcessId);
        var parentStarted = parent.StartTime.ToUniversalTime();
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(15))
        {
            token.ThrowIfCancellationRequested();
            if (parent.HasExited || parent.StartTime.ToUniversalTime() != parentStarted)
                throw new OperationCanceledException("Setup owner exited.", token);
            StaticModelRuntimePolicy.AssertSafeFile(options.ParentJobReadyPath);
            if (File.Exists(options.ParentJobReadyPath))
            {
                if (new FileInfo(options.ParentJobReadyPath).Length > 128
                    || await File.ReadAllTextAsync(options.ParentJobReadyPath, token) != options.OperationId)
                    throw new ArgumentException("Invalid parent job handshake.");
                return;
            }
            await Task.Delay(100, token);
        }
        throw new SetupException("static_model_parent_job_unavailable", "Setup chưa xác nhận quyền quản lý tiến trình kiểm tra.");
    }

    private static long EstimateRuntimeDiskRequiredBytes(string cacheRoot)
    {
        var definition = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km;
        using var translation = new VietsubTranslationComponentStore(definition,
            componentsRoot: StaticModelRuntimePolicy.TranslationRoot, allowUpstreamDownload: false);
        var qwenBytes = translation.Inspect(requireProbe: false, checkResources: false).ModelVerified
            ? 0 : definition.MinimumFreeDiskBytes;
        var payload = ApprovedStaticModelBundle.DescribeCachedPayload(cacheRoot);
        var piperDefinition = PiperOfflineBundle.ApprovedDefinition();
        using var voice = new VietsubVoiceComponentStore(null, true, useUserComponentsRoot: true,
            requireUserComponentsRoot: true, allowUpstreamDownloads: false,
            offlineBundle: new PiperOfflineBundle(payload.PiperBundleDirectory, piperDefinition));
        var piperBytes = voice.GetStatus().Ready ? 0 : piperDefinition.MinimumFreeDiskBytes;
        return checked(qwenBytes + piperBytes + (qwenBytes + piperBytes > 0 ? 64L * 1024 * 1024 : 0));
    }

    private static async Task InspectPrerequisitesAsync(List<StaticModelComponentResult> results, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64 || !Environment.Is64BitProcess)
            throw new SetupException("desktop_platform_unsupported", "Bộ cài model yêu cầu Windows x64.");
        var webView = await StaticModelWebViewProbe.CheckAsync(token);
        results.Add(new("webview2", webView.IsReady,
            Fingerprint: webView.IsReady ? StaticModelComponentEvidence.WebView2() : null, ErrorCode: webView.ErrorCode));
        if (!webView.IsReady) return;
        var mediaPaths = new MediaToolPathResolver(new Configuration.MediaToolOptions()).Resolve();
        var media = new MediaToolPreflightService(mediaPaths, new ExternalProcessRunner(), TimeProvider.System);
        await RunComponentAsync(new MediaSetupAdapter(media, mediaPaths), false, results, token);
        await using var ocr = new PaddleVietsubOcrRecognizer();
        await RunComponentAsync(new OcrSetupAdapter(ocr, true), false, results, token);
        if (OcrNativeDependencies.InspectLoadedModules().Any(x => !x.AppLocal))
        {
            results.RemoveAll(x => x.ComponentId == "ocr");
            results.Add(new("ocr", false, ErrorCode: "static_model_ocr_native_not_app_local"));
        }
    }

    private static async Task RunComponentAsync(ISetupComponentAdapter adapter, bool resourceAccepted,
        List<StaticModelComponentResult> results, CancellationToken token, Func<string>? evidence = null)
    {
        WriteOutput(new StaticModelSetupProgress("PROBING", 0, 1, adapter.Component.Id));
        var result = await adapter.RunAsync(install: true, resourceAccepted,
            (stage, _, bytes, total) => WriteOutput(new StaticModelSetupProgress(stage, bytes ?? 0, total ?? 0, adapter.Component.Id)), token);
        var ready = result.State == "READY";
        if (ready && result.ErrorCode is not null)
            throw new SetupException("static_model_component_evidence_invalid", "Kết quả kiểm tra thành phần không nhất quán.");
        string? fingerprint = null;
        if (ready)
        {
            WriteOutput(new StaticModelSetupProgress("VERIFYING_EVIDENCE", 0, 1, result.Id));
            fingerprint = evidence?.Invoke() ?? result.Id switch
            {
                "media" => StaticModelComponentEvidence.Media(),
                "ocr" => StaticModelComponentEvidence.Ocr(),
                _ => throw new InvalidDataException("Missing component probe evidence.")
            };
            if (!StaticModelRuntimePolicy.IsSha256(fingerprint)) throw new InvalidDataException("Invalid component fingerprint.");
        }
        results.Add(new(result.Id, ready, Fingerprint: fingerprint, ErrorCode: result.ErrorCode));
        if (!ready) throw new SetupException(result.ErrorCode ?? "static_model_component_probe_failed", "Kiểm tra thành phần chưa đạt.");
    }

    private static void WriteOutput<T>(T value)
    {
        lock (OutputLock)
        {
            if (value is StaticModelSetupProgress progress)
            {
                var now = Stopwatch.GetTimestamp();
                var stageKey = progress.Stage + "|" + progress.Message;
                if (_lastProgressStage == stageKey && _lastProgressTimestamp != 0
                    && Stopwatch.GetElapsedTime(_lastProgressTimestamp, now) < TimeSpan.FromMilliseconds(150)) return;
                _lastProgressTimestamp = now;
                _lastProgressStage = stageKey;
            }
            var line = JsonSerializer.Serialize(value, Json);
            if (line.Length > 16 * 1024) throw new InvalidDataException("Bootstrap output exceeds protocol limit.");
            Console.WriteLine(line);
            Console.Out.Flush();
        }
    }

    internal sealed record Options(string OperationId, string ResultPath, string CacheRoot, int ParentProcessId,
        bool AcceptResourceWarning, string? ConfirmedResourceProfile, string? ParentJobReadyPath);
    internal static Options ParseOptions(string[] args)
    {
        if (args.Length < 1 || args[0] != "--prepare-static-models") throw new ArgumentException("Invalid bootstrap command.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var accept = false;
        for (var i = 1; i < args.Length; i++)
        {
            var key = args[i];
            if (key == "--accept-resource-warning")
            {
                if (accept) throw new ArgumentException("Duplicate resource confirmation.");
                accept = true;
                continue;
            }
            if (key is not ("--operation" or "--result" or "--cache-root" or "--parent-pid" or "--confirmed-resource-profile" or "--parent-job-ready")
                || ++i >= args.Length || !values.TryAdd(key, args[i])) throw new ArgumentException("Invalid bootstrap argument.");
        }
        var profile = values.GetValueOrDefault("--confirmed-resource-profile");
        var jobReady = values.GetValueOrDefault("--parent-job-ready");
        if (values.Count != (accept ? 5 : 4) + (jobReady is null ? 0 : 1) || accept != (profile is not null)
            || (profile is not null && profile is not (VietsubTranslationWorkerProfiles.StandardProfileId
                or VietsubTranslationWorkerProfiles.LowMemoryProfileId))
            || !Guid.TryParseExact(values.GetValueOrDefault("--operation"), "N", out var operation)
            || operation == Guid.Empty || !int.TryParse(values.GetValueOrDefault("--parent-pid"), out var parent) || parent <= 0)
            throw new ArgumentException("Bootstrap ownership is required.");
        var result = values["--result"];
        var cache = values["--cache-root"];
        StaticModelRuntimePolicy.ValidateLocalPath(result);
        StaticModelRuntimePolicy.ValidateLocalPath(cache);
        if (!result.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("A JSON result path is required.");
        if (jobReady is not null && !string.Equals(jobReady, result + ".jobready", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid parent job handshake path.");
        return new(operation.ToString("N"), Path.GetFullPath(result), Path.GetFullPath(cache), parent, accept, profile, jobReady);
    }
}
