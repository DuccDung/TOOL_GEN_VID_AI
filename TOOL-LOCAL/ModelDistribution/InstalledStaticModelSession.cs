using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;
using TOOL_SHARED.Distribution.StaticModels;

namespace TOOL_LOCAL.ModelDistribution;

// Uses only installed, approved roots. No installer, provider readiness mutation,
// workspace model discovery, artifact downloader or user settings are composed here.
internal sealed class InstalledStaticModelSession : IInstalledStaticModelSession
{
    private readonly InstalledStaticModelOptions _options;
    private readonly FileStream _usage;
    private readonly StaticModelInstallReceipt _receipt;
    private IDisposable? _runtimeLease;
    private VietsubTranslationComponentStore? _qwen;
    private VietsubTranslationWorkerRuntimeProfile? _profile;
    private VietsubTranslationWorkerClient? _worker;
    private VietsubVoiceComponentStore? _piper;
    private HttpClient? _http;
    private bool _loaded;
    public InstalledStaticModelBinding Binding { get; private set; } = null!;

    private InstalledStaticModelSession(InstalledStaticModelOptions options, FileStream usage, StaticModelInstallReceipt receipt)
    { _options = options; _usage = usage; _receipt = receipt; }

    internal static async Task<IInstalledStaticModelSession> OpenAsync(InstalledStaticModelOptions options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!StaticModelRuntimePolicy.RequiredByRelease)
            throw new SetupException("installed_model_release_required", "Use the installed StaticArchive release.");
        StaticModelRuntimePolicy.EnsureNotRevoked();
        var usage = StaticModelRuntimePolicy.AcquireUsageLease(installing: false);
        InstalledStaticModelSession? session = null;
        try
        {
            var receipt = StaticModelRuntimePolicy.ReadReadyReceipt()
                ?? throw new SetupException("installed_model_receipt_invalid", "Complete Setup before qualification.");
            AssertSeparateOutput(options.OutputDirectory, AppContext.BaseDirectory, SystemSetupPaths.ComponentsRoot,
                SystemSetupPaths.MetadataRoot, receipt.CacheRoot);
            session = new(options, usage, receipt);
            if (options.Engine == "qwen") session.OpenQwen();
            else session.OpenPiper();
            var fingerprint = session.CurrentRuntimeFingerprint();
            var approved = receipt.Result.Components.Single(x => x.ComponentId == options.Engine).Fingerprint;
            if (!string.Equals(fingerprint, approved, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Installed runtime evidence differs from the committed Setup.");
            session.Binding = new(receipt.Result.OperationId, receipt.MachineFingerprint,
                receipt.ApplicationFingerprint, fingerprint, session._profile?.ProfileId);
            session.AssertUnchanged();
            return session;
        }
        catch
        {
            if (session is not null) await session.DisposeAsync(); else usage.Dispose();
            throw;
        }
    }

    internal static void AssertSeparateOutput(string output, params string[] productionRoots)
    {
        StaticModelRuntimePolicy.ValidateLocalPath(output);
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(output));
        foreach (var root in productionRoots)
        {
            var protectedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (string.Equals(candidate, protectedRoot, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(protectedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || protectedRoot.StartsWith(candidate + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("QA output must be separate from application, runtime, metadata and cache roots.");
        }
    }

    private void OpenQwen()
    {
        _http = new HttpClient(new NoDownloads());
        _qwen = new(VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km,
            componentsRoot: StaticModelRuntimePolicy.TranslationRoot, httpClient: _http,
            approvedLocalModelCandidates: [], allowUpstreamDownload: false);
        RequireExistingDirectory(_qwen.ComponentDirectory);
        _runtimeLease = new RuntimeUseGate(SystemSetupPaths.MetadataRoot, _qwen.ComponentDirectory).Acquire(exclusive: true);
        foreach (var profileId in new[] { VietsubTranslationWorkerProfiles.StandardProfileId, VietsubTranslationWorkerProfiles.LowMemoryProfileId })
        {
            var profile = VietsubTranslationWorkerProfiles.CreateRuntimeProfile(profileId, Environment.ProcessorCount);
            var inspection = _qwen.Inspect(requireProbe: true, checkResources: false, runtimeProfile: profile);
            if (inspection.ModelVerified && inspection.ProbeVerified) { _profile = profile; break; }
        }
        if (_profile is null) throw new InvalidDataException("The installed Qwen model or probe binding is invalid.");
        var resources = _qwen.EvaluateResources(_profile.ResourceRequirements);
        if (resources.IsBlocking) throw new SetupException("installed_model_resources_unavailable", "Native resource admission failed.");
        if (resources.RequiresConfirmation && (!_options.AcceptResourceWarning || _options.ConfirmedResourceProfile != _profile.ProfileId))
            throw new SetupException("installed_model_resource_confirmation_" + _profile.ProfileId,
                "Confirm the exact installed CPU profile before qualification.");
        if (_options.AcceptResourceWarning && _options.ConfirmedResourceProfile != _profile.ProfileId)
            throw new ArgumentException("Resource confirmation identifies another profile.");
        _worker = new(VietsubTranslationWorkerClientOptions.CreateDefault() with
        {
            DiagnosticDirectory = Path.Combine(_options.OutputDirectory, "worker-diagnostics"),
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["VIDEOMAKER_TRANSLATION_WORKER_TEST_MODE"] = null,
                [VietsubTranslationDiagnosticWorkspace.EnvironmentVariable] = null,
                ["PATH"] = Environment.SystemDirectory + ";" + Environment.GetEnvironmentVariable("SystemRoot")
            }
        });
    }

    private void OpenPiper()
    {
        var payload = ApprovedStaticModelBundle.DescribeCachedPayload(_receipt.CacheRoot);
        _piper = new(null, featureEnabled: true, httpHandler: new NoDownloads(), useUserComponentsRoot: true,
            requireUserComponentsRoot: true, allowUpstreamDownloads: false,
            offlineBundle: new PiperOfflineBundle(payload.PiperBundleDirectory, PiperOfflineBundle.ApprovedDefinition()));
        RequireExistingDirectory(_piper.ComponentDirectory);
        RequireExistingDirectory(_piper.RuntimeRoot);
        _runtimeLease = new RuntimeUseGate(SystemSetupPaths.MetadataRoot, _piper.ComponentDirectory).Acquire(exclusive: true);
        _ = _piper.RequireReady();
    }

    private static void RequireExistingDirectory(string path)
    {
        StaticModelRuntimePolicy.ValidateLocalPath(path);
        if (!Directory.Exists(path)) throw new InvalidDataException("Installed runtime is missing.");
    }

    public void AssertUnchanged()
    {
        StaticModelRuntimePolicy.EnsureNotRevoked();
        var current = StaticModelRuntimePolicy.ReadReadyReceipt();
        if (current is null || current.Result.OperationId != _receipt.Result.OperationId
            || current.MachineFingerprint != _receipt.MachineFingerprint
            || current.ApplicationFingerprint != _receipt.ApplicationFingerprint
            || !string.Equals(current.CacheRoot, _receipt.CacheRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installed ownership or commit evidence changed.");
        if (_qwen is not null)
        {
            var inspection = _qwen.Inspect(requireProbe: true, checkResources: false, runtimeProfile: _profile!);
            if (!inspection.ModelVerified || !inspection.ProbeVerified) throw new InvalidDataException("Installed Qwen evidence changed.");
        }
        else _ = _piper!.RequireReady();
        if (Binding is not null && !string.Equals(CurrentRuntimeFingerprint(), Binding.RuntimeFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installed runtime evidence changed during qualification.");
    }

    private string CurrentRuntimeFingerprint() => _profile is not null
        ? StaticModelComponentEvidence.Qwen(_profile.ProfileId) : StaticModelComponentEvidence.Piper(_piper!.RuntimeRoot);

    public async Task<InstalledStaticModelSample> RunIterationAsync(int iteration, string outputDirectory, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        if (_qwen is null) return await RunPiperAsync(iteration, outputDirectory, watch, token);
        if (!_loaded)
        {
            var definition = VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km;
            var loaded = await _worker!.LoadAsync(new(_qwen.ComponentsRoot, _qwen.ModelPath,
                definition.ComponentId, definition.EngineId, definition.EngineVersion, definition.ModelFileName,
                definition.ModelSizeBytes, definition.ModelSha256, _profile!.InferenceConfig,
                _profile.ResourceRequirements, _options.AcceptResourceWarning), null, token);
            if (!loaded.BackendIdentity.Contains("cpu", StringComparison.OrdinalIgnoreCase) || loaded.OffloadedLayers != 0
                || loaded.ConfigFingerprint != VietsubTranslationWorkerProtocol.ComputeConfigFingerprint(_profile.InferenceConfig))
                throw new InvalidDataException("The qualification worker did not use the approved CPU configuration.");
            _loaded = true;
        }
        var request = iteration % 2 == 1 ? QwenGgufVietsubTranslationProvider.CreateEnglishProbeRequest()
            : QwenGgufVietsubTranslationProvider.CreateChineseProbeRequest();
        var inference = await _worker!.InferAsync(new("QUALIFYING_INSTALLED",
            QwenGgufVietsubTranslationProvider.BuildPrompt(request), QwenGgufVietsubTranslationProvider.BuildJsonGrammar(request),
            Math.Min(_profile!.InferenceConfig.MaximumGeneratedTokens, 320), 12_000), null, token);
        var result = QwenGgufVietsubTranslationProvider.ParseResult(inference.RawOutput, request);
        if (iteration % 2 == 1) QwenGgufVietsubTranslationProvider.EnsureEnglishProbeResult(result);
        else QwenGgufVietsubTranslationProvider.EnsureChineseProbeResult(result);
        return new(iteration, watch.ElapsedMilliseconds, inference.Metrics.PrivateBytes);
    }

    private async Task<InstalledStaticModelSample> RunPiperAsync(int iteration, string outputDirectory, Stopwatch watch, CancellationToken token)
    {
        var paths = _piper!.RequireReady();
        var requestId = Guid.NewGuid().ToString("N");
        var requestPath = Path.Combine(outputDirectory, requestId + ".request.json");
        var outputPath = Path.Combine(outputDirectory, $"piper-{iteration:D3}.wav");
        StaticModelRuntimePolicy.ValidateLocalPath(requestPath);
        StaticModelRuntimePolicy.ValidateLocalPath(outputPath);
        if (File.Exists(outputPath)) throw new IOException("QA audio output already exists.");
        await using (var requestFile = new FileStream(requestPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(requestFile, new { protocolVersion = 1, requestId,
                modelPath = paths.ModelPath, configPath = paths.ConfigPath, volume = 1.0, lengthScale = 1.0,
                items = new[] { new { index = iteration, text = PiperFixture(iteration), outputPath } }
            }, InstalledStaticModelQualification.Json, token);
        try
        {
            var info = new ProcessStartInfo(paths.PythonPath) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = outputDirectory };
            foreach (var argument in new[] { "-X", "utf8", "-I", "-B", paths.WorkerPath, requestPath }) info.ArgumentList.Add(argument);
            info.Environment["PYTHONUTF8"] = "1"; info.Environment["PYTHONIOENCODING"] = "utf-8";
            info.Environment["PYTHONNOUSERSITE"] = "1"; info.Environment["TEMP"] = outputDirectory;
            info.Environment["TMP"] = outputDirectory;
            using var process = Process.Start(info) ?? throw new IOException("Could not start installed Piper.");
            using var tree = new PiperProcessTree(process);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var drainError = DrainAsync(process.StandardError, timeout.Token);
            var itemCompleted = false; var completed = false; var events = 0;
            while (await ReadBoundedLineAsync(process.StandardOutput, timeout.Token) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (++events > 256 || completed) throw new InvalidDataException("Invalid Piper event ordering.");
                var item = JsonSerializer.Deserialize<VietsubPiperWorkerEvent>(line, InstalledStaticModelQualification.Json);
                if (item is null || item.ProtocolVersion != 1 || item.RequestId != requestId)
                    throw new InvalidDataException("Invalid Piper response ownership.");
                if (item.Type == "item_completed")
                {
                    if (itemCompleted || item.Index != iteration) throw new InvalidDataException("Invalid Piper item.");
                    itemCompleted = true;
                }
                else if (item.Type == "completed")
                {
                    if (!itemCompleted) throw new InvalidDataException("Piper finished without audio.");
                    completed = true;
                }
                else if (item.Type is not ("started" or "model_loaded" or "item_started" or "progress"))
                    throw new InvalidDataException("Unexpected Piper response.");
            }
            await process.WaitForExitAsync(timeout.Token); await drainError;
            if (process.ExitCode != 0 || !completed) throw new InvalidDataException("Installed Piper failed qualification.");
            var wav = VietsubWavInspector.Inspect(outputPath, analyzeSilence: true);
            if (wav.Channels != 1 || wav.BitsPerSample != 16 || wav.SampleRate != 22050 || wav.SignalEndMilliseconds is null)
                throw new InvalidDataException("Unexpected Piper WAV format.");
            await using var audio = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(audio, token)).ToLowerInvariant();
            return new(iteration, watch.ElapsedMilliseconds, null, hash, wav.DurationMilliseconds, wav.SampleRate);
        }
        finally { File.Delete(requestPath); }
    }

    internal static string PiperFixture(int iteration)
    {
        string[] corpus = [
            "Xin chào! Chúc bạn một ngày vui vẻ.",
            "Hôm nay, chị đưa em đến trường; đường phố đông nhưng mọi người vẫn đi đúng phần đường.",
            "Lúc 7 giờ 30 phút, chuyến xe số 24 khởi hành. Giá vé là 150 nghìn đồng.",
            "Bạn đã kiểm tra tài liệu chưa? Vâng, tôi đã kiểm tra xong rồi!",
            "Trên cánh đồng, những bông lúa chín vàng nghiêng mình trong gió; phía xa, đàn chim đang trở về tổ khi mặt trời dần khuất sau rặng núi. Người nông dân thu dọn dụng cụ, chậm rãi bước qua con đường nhỏ và kể cho nhau nghe những câu chuyện trong ngày.",
            "Tiếng Việt có dấu: hòa bình, yêu thương, trách nhiệm, kỹ thuật và sức khỏe."
        ];
        if (iteration is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(iteration));
        return $"Bài số {iteration}. " + corpus[(iteration - 1) % corpus.Length];
    }

    private static async Task<string?> ReadBoundedLineAsync(StreamReader reader, CancellationToken token)
    {
        var text = new StringBuilder(); var one = new char[1];
        while (await reader.ReadAsync(one.AsMemory(), token) > 0)
        {
            if (one[0] == '\n') return text.ToString().TrimEnd('\r');
            if (text.Length >= 16 * 1024) throw new InvalidDataException("Piper event exceeded its bound.");
            text.Append(one[0]);
        }
        return text.Length == 0 ? null : text.ToString();
    }
    private static async Task DrainAsync(StreamReader reader, CancellationToken token)
    { var buffer = new char[4096]; while (await reader.ReadAsync(buffer.AsMemory(), token) != 0) { } }

    public async ValueTask DisposeAsync()
    {
        try { if (_worker is not null) await _worker.DisposeAsync(); }
        finally { _qwen?.Dispose(); _piper?.Dispose(); _http?.Dispose(); _runtimeLease?.Dispose(); _usage.Dispose(); }
    }
    private sealed class NoDownloads : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Network downloads are forbidden during installed qualification.");
    }
}
