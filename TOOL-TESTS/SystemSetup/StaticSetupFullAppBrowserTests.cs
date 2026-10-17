using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_SHARED.Contracts.Organizations;
using TOOL_TESTS.Updates;

namespace TOOL_TESTS.SystemSetup;

/// <summary>Full production App/hook/transport and native Setup bridge/coordinator/gate decision.
/// Session, component work and business execution are isolated fixtures; no account or model is installed.</summary>
[Collection(NativeWindowsCollection.Name)]
public sealed class StaticSetupFullAppBrowserTests
{
    [Fact]
    [Trait("Category", "StaticSetupBrowser")]
    public async Task Full_App_uses_real_native_Setup_routes_and_keeps_gates_during_faults_context_and_overlays()
    {
        var root = Path.Combine(Path.GetTempPath(), "vm-full-app-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        Form? activeForm = null; Process? browser = null; Thread? thread = null;
        try
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "TOOL_GEN_POST_VIDEO.slnx"))) repository = repository.Parent;
            Assert.NotNull(repository);
            var webRoot = Path.Combine(root, "web");
            var buildStart = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(repository.FullName, "TOOL-LOCAL", "Web"),
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "node_modules/vite/bin/vite.js", "build", "--config", "tests/static-setup-browser/vite.config.mjs", "--outDir", webRoot }) buildStart.ArgumentList.Add(argument);
            using (var build = Process.Start(buildStart)!)
            {
                var stdout = build.StandardOutput.ReadToEndAsync(); var stderr = build.StandardError.ReadToEndAsync();
                try { await build.WaitForExitAsync(timeout.Token); } catch { if (!build.HasExited) build.Kill(true); throw; }
                Assert.True(build.ExitCode == 0, (await stdout) + (await stderr));
            }
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            thread = new Thread(() =>
            {
                using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                    Location = new Point(-10000, -10000), ClientSize = new Size(1280, 800) };
                activeForm = form;
                using var webView = new WebView2 { Dock = DockStyle.Fill }; form.Controls.Add(webView);
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(root, "browser-profile"));
                        await webView.EnsureCoreWebView2Async(environment);
                        var core = webView.CoreWebView2; browser = Process.GetProcessById((int)core.BrowserProcessId);
                        core.SetVirtualHostNameToFolderMapping("app.local", webRoot, CoreWebView2HostResourceAccessKind.DenyCors);
                        // No request can escape the local virtual host, including via image/font fallbacks.
                        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                        core.WebResourceRequested += (_, args) =>
                        {
                            if (Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) && uri.Host == "app.local") return;
                            args.Response = environment.CreateWebResourceResponse(new MemoryStream(), 403, "Fixture forbids external requests", "Content-Type: text/plain");
                        };
                        await using var host = new ControlledHost(core, webView, Path.Combine(root, "native"));
                        core.WebMessageReceived += host.OnMessage;
                        core.Navigate("https://app.local/full-app.html");
                        await WaitAsync(() => host.PendingGets.Count > 0, host, timeout.Token);
                        await WaitScriptAsync(core, "window.inspectFullSetup && window.inspectFullSetup().setupVisible", host, timeout.Token);
                        await AssertState(core, mainInert: true, setupVisible: true);
                        Assert.DoesNotContain(host.Requests, request => request.Type == "vietsub.state.get");
                        await core.ExecuteScriptAsync("window.probeBlockedHostCommand()");
                        await WaitAsync(() => host.BlockedBusinessCommands == 1, host, timeout.Token);
                        Assert.Equal(0, host.BusinessHandlerCalls);

                        await host.ReleaseFirstGetAsync(malformed: true);
                        await WaitScriptAsync(core, "window.inspectFullSetup().alerts.includes('Trạng thái Setup chưa đầy đủ')", host, timeout.Token);
                        await AssertState(core, true, true);
                        await core.ExecuteScriptAsync("window.clickFullSetupButton('Tải lại trạng thái')");
                        await WaitAsync(() => host.Coordinator.GetSnapshot().Operation?.State == "Failed", host, timeout.Token);
                        await WaitScriptAsync(core, "window.inspectFullSetup().primaryEnabled", host, timeout.Token);

                        // Actual mutation is accepted once, while all ACK/progress/GET replies are lost.
                        host.DropSetupResponses = true;
                        await core.ExecuteScriptAsync("window.clickFullSetupPrimary()");
                        await WaitAsync(() => host.Adapters.Sum(adapter => adapter.InstallCalls) == 1, host, timeout.Token);
                        var operation = host.LastMutation!;
                        await WaitScriptAsync(core, "window.inspectFullSetup().alerts.includes('Chưa nhận xác nhận Setup')", host, timeout.Token);
                        await AssertState(core, true, true);
                        host.DropSetupResponses = false;
                        await core.ExecuteScriptAsync("window.clickFullSetupButton('Đối soát trạng thái')");
                        await WaitAsync(() => host.Mutations.Count(request => request.OperationId == operation.OperationId) == 2, host, timeout.Token);
                        Assert.Equal(1, host.Adapters.Sum(adapter => adapter.InstallCalls));
                        var replays = host.Mutations.Where(request => request.OperationId == operation.OperationId).ToArray();
                        Assert.Equal(replays[0].Payload, replays[1].Payload);
                        host.ReleaseInstall();
                        await WaitAsync(() => StartupSystemSetupWorkflow.IsReady(host.Coordinator.GetSnapshot()), host, timeout.Token);
                        await WaitScriptAsync(core, "!window.inspectFullSetup().setupVisible && !window.inspectFullSetup().mainInert", host, timeout.Token);
                        Assert.All(host.Adapters, adapter => Assert.Equal(1, adapter.InstallCalls));
                        await WaitAsync(() => host.Requests.Any(request => request.Type == "vietsub.state.get"), host, timeout.Token);

                        // A bad correlated GET after READY must quarantine the old readiness.
                        host.MalformedNextGet = true;
                        await WaitScriptAsync(core, "window.inspectFullSetup().alerts.includes('Trạng thái Setup chưa đầy đủ')", host, timeout.Token);
                        await AssertState(core, true, true);
                        var oldSnapshot = host.Coordinator.GetSnapshot() with { StartupRequired = true };
                        var oldRequest = host.Requests.Last(request => request.Type == "system.setup.get");
                        await host.SwitchContextAsync();
                        await WaitAsync(() => host.PendingGets.Count > 0, host, timeout.Token);
                        host.Post(new { type = "system.setup.completed", payload = oldSnapshot });
                        host.Post(new { type = "operation.error", requestId = oldRequest.RequestId,
                            error = new { code = "fixture_old_context", message = "OLD CONTEXT ERROR MUST BE IGNORED" } });
                        await AssertState(core, true, true);
                        Assert.DoesNotContain("OLD CONTEXT ERROR MUST BE IGNORED", await core.ExecuteScriptAsync("document.body.textContent"));
                        await host.ReleaseFirstGetAsync(malformed: false);
                        await WaitAsync(() => host.Coordinator.GetSnapshot().Operation?.State == "Failed", host, timeout.Token);
                        await WaitScriptAsync(core, "window.inspectFullSetup().primaryEnabled", host, timeout.Token);

                        host.Post(new { type = "update.available", payload = new { isUpdateAvailable = true, isMandatory = false, release = new {
                            releaseId = "fixture-update", productName = "VideoMaker", version = "9.9.9", buildNumber = 9,
                            channel = "Development", platform = "win-x64", publishedAtUtc = "2026-10-17T00:00:00Z",
                            fileName = "fixture.zip", downloadUrl = "/fixture.zip", sizeBytes = 1, sha256 = new string('a', 64) } } });
                        await WaitScriptAsync(core, "window.inspectFullSetup().updateVisible", host, timeout.Token);
                        using (var state = JsonDocument.Parse(await core.ExecuteScriptAsync("window.inspectFullSetup()")))
                        { Assert.Equal(1, state.RootElement.GetProperty("dialogCount").GetInt32()); Assert.True(state.RootElement.GetProperty("mainInert").GetBoolean()); Assert.False(state.RootElement.GetProperty("setupVisible").GetBoolean()); }
                        await core.ExecuteScriptAsync("window.clickFullSetupButton('Để sau')");
                        await WaitScriptAsync(core, "window.inspectFullSetup().setupVisible", host, timeout.Token);
                        host.Post(new { type = "operation.error", requestId = "fixture-service-error",
                            error = new { code = "provider_temporarily_unavailable", message = "Fixture service unavailable" } });
                        await WaitScriptAsync(core, "window.inspectFullSetup().serviceErrorVisible", host, timeout.Token);
                        using (var state = JsonDocument.Parse(await core.ExecuteScriptAsync("window.inspectFullSetup()")))
                        { Assert.Equal(1, state.RootElement.GetProperty("dialogCount").GetInt32()); Assert.True(state.RootElement.GetProperty("mainInert").GetBoolean()); }
                        Assert.Equal(0, host.BusinessHandlerCalls);
                        await host.WriteEvidenceAsync(Environment.GetEnvironmentVariable("VIDEOMAKER_STATIC_SETUP_ARTIFACTS"), core);
                        core.WebMessageReceived -= host.OnMessage;
                        completion.TrySetResult();
                    }
                    catch (Exception error) { completion.TrySetException(error); }
                    finally { form.Close(); }
                };
                Application.Run(form);
            }) { IsBackground = true, Name = "Full App Setup Native Bridge Regression" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            await completion.Task.WaitAsync(timeout.Token);
        }
        finally
        {
            if (activeForm is { IsDisposed: false, IsHandleCreated: true })
            { try { activeForm.BeginInvoke(new Action(activeForm.Close)); } catch (InvalidOperationException) { } }
            if (thread is not null) Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
            if (browser is not null) { if (!browser.HasExited && !browser.WaitForExit(5000)) browser.Kill(true); browser.Dispose(); }
            StaticArchiveFixture.DeleteTemp(root);
        }
    }

    private static async Task AssertState(CoreWebView2 core, bool mainInert, bool setupVisible)
    {
        using var state = JsonDocument.Parse(await core.ExecuteScriptAsync("window.inspectFullSetup()"));
        Assert.True(state.RootElement.GetProperty("appPresent").GetBoolean());
        Assert.Equal(mainInert, state.RootElement.GetProperty("mainInert").GetBoolean());
        Assert.Equal(mainInert, state.RootElement.GetProperty("sidebarInert").GetBoolean());
        Assert.Equal(setupVisible, state.RootElement.GetProperty("setupVisible").GetBoolean());
    }
    private static async Task WaitScriptAsync(CoreWebView2 core, string expression, ControlledHost host, CancellationToken token)
    { while (await core.ExecuteScriptAsync(expression) != "true") { host.ThrowIfFailed(); await Task.Delay(30, token); } }
    private static async Task WaitAsync(Func<bool> predicate, ControlledHost host, CancellationToken token)
    { while (!predicate()) { host.ThrowIfFailed(); await Task.Delay(20, token); } }

    private sealed record Request(string Type, string RequestId, string Json, string? OperationId, string? Payload);
    private sealed class ControlledHost : IAsyncDisposable
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private readonly CoreWebView2 _core; private readonly Control _owner; private readonly string _root;
        private readonly Context _context = new(); private SystemSetupBridge _bridge = null!;
        private object _gateCarrier = null!; private readonly MethodInfo _gateDecision = typeof(TOOL_LOCAL.Form1).GetMethod("ShouldBlockForStartupSetup", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private TaskCompletionSource _installRelease = NewCompletion(); private Exception? _failure;
        public readonly ConcurrentQueue<Request> Requests = new(); public readonly ConcurrentQueue<Request> Mutations = new();
        public readonly ConcurrentQueue<Request> PendingGets = new(); public readonly ConcurrentQueue<string> Trace = new();
        public SystemSetupCoordinator Coordinator { get; private set; } = null!;
        public Adapter[] Adapters { get; private set; } = [];
        public bool HoldGets { get; private set; } = true; public bool DropSetupResponses; public bool MalformedNextGet;
        public int BlockedBusinessCommands; public int BusinessHandlerCalls;
        public Request? LastMutation => Mutations.LastOrDefault();
        public ControlledHost(CoreWebView2 core, Control owner, string root) { _core = core; _owner = owner; _root = root; CreateCoordinator(); }
        private static TaskCompletionSource NewCompletion() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        private void CreateCoordinator()
        {
            _installRelease = NewCompletion();
            Adapters = new[] { "media", "ocr", "qwen", "piper" }.Select(id => new Adapter(id, () => _installRelease.Task)).ToArray();
            var localRoot = Path.Combine(_root, _context.CurrentUserId!);
            Coordinator = new(new SystemSetupAuthorizer(_context), Adapters, new RuntimeUseGate(localRoot), new SystemSetupJournal(localRoot), () => true);
            _bridge = new(Coordinator, PostRaw, startupRequired: true);
            // Exercise the actual Form1 routing decision without constructing its account,
            // media log, WebView, SQL or provider composition in this isolated test.
            _gateCarrier = RuntimeHelpers.GetUninitializedObject(typeof(TOOL_LOCAL.Form1));
            GC.SuppressFinalize(_gateCarrier);
            typeof(TOOL_LOCAL.Form1).GetField("_startupSystemSetupGate", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(_gateCarrier, new StartupSystemSetupGate(Coordinator, required: true));
        }
        public async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                var json = args.TryGetWebMessageAsString(); using var document = JsonDocument.Parse(json);
                var root = document.RootElement; var type = root.GetProperty("type").GetString()!;
                var requestId = root.GetProperty("requestId").GetString()!;
                var payload = root.TryGetProperty("payload", out var value) ? value.GetRawText() : null;
                var operation = value.ValueKind == JsonValueKind.Object && value.TryGetProperty("operationId", out var id) ? id.GetString() : null;
                var request = new Request(type, requestId, json, operation, payload); Requests.Enqueue(request); Trace.Enqueue("request:" + type + ":" + operation);
                if (type == "app.ready") { SendDashboard(); return; }
                if (type == "system.setup.get" && HoldGets) { PendingGets.Enqueue(request); return; }
                if (type is "system.setup.start" or "system.setup.retry") Mutations.Enqueue(request);
                if (type == "system.setup.get" && MalformedNextGet)
                { MalformedNextGet = false; SendMalformed(request); return; }
                if (await _bridge.TryHandleAsync(json, CancellationToken.None)) return;
                if (type == "generation.content")
                {
                    var arguments = new object?[] { json, null };
                    var blocked = (bool)_gateDecision.Invoke(_gateCarrier, arguments)!;
                    Assert.Equal(requestId, arguments[1]);
                    if (blocked) Interlocked.Increment(ref BlockedBusinessCommands); else Interlocked.Increment(ref BusinessHandlerCalls);
                    Post(new { type = "fixture.host.gate", requestId, payload = new { blocked } });
                }
                // Other App getters are recorded but never routed to network or business services.
            }
            catch (Exception error) { _failure = error; }
        }
        public async Task ReleaseFirstGetAsync(bool malformed)
        {
            Assert.True(PendingGets.TryDequeue(out var request)); HoldGets = false;
            if (malformed) SendMalformed(request!); else await _bridge.TryHandleAsync(request!.Json, CancellationToken.None);
        }
        private void SendMalformed(Request request)
        { Post(new { type = "system.setup.status", requestId = request.RequestId, payload = new { contextGeneration = Coordinator.GetSnapshot().ContextGeneration,
            organizationId = _context.SelectedOrganizationId, startupRequired = true, components = Array.Empty<object>() } }); }
        public void ReleaseInstall() => _installRelease.TrySetResult();
        public async Task SwitchContextAsync()
        {
            _bridge.Dispose(); await Coordinator.DisposeAsync(); while (PendingGets.TryDequeue(out _)) { }
            _context.User = "fixture-user-b"; _context.Organization = Guid.NewGuid(); HoldGets = true;
            CreateCoordinator(); SendDashboard();
        }
        public void Post<T>(T value) => PostRaw(JsonSerializer.Serialize(value, Json));
        private void PostRaw(string json)
        {
            using var parsed = JsonDocument.Parse(json); var type = parsed.RootElement.GetProperty("type").GetString()!;
            if (DropSetupResponses && type.StartsWith("system.setup.", StringComparison.Ordinal)) { Trace.Enqueue("dropped:" + type); return; }
            Trace.Enqueue("response:" + type);
            if (_owner.InvokeRequired) _owner.BeginInvoke(new Action(() => { if (!_owner.IsDisposed) _core.PostWebMessageAsJson(json); }));
            else _core.PostWebMessageAsJson(json);
        }
        private void SendDashboard() => Post(new { type = "dashboard.state", payload = new {
            profile = new { userId = _context.User, email = "fixture@example.test", displayName = _context.User, accountStatus = "Active", roles = Array.Empty<string>() },
            organizations = new[] { new { organizationId = _context.Organization, code = "FIXTURE", name = "Isolated fixture", role = "Member", status = "Active",
                monthlyBudgetLimit = 0, reservedCost = 0, actualCost = 0, remainingBudget = 0, currencyCode = "USD", periodStartsAtUtc = "2026-10-01T00:00:00Z", periodEndsAtUtc = "2026-11-01T00:00:00Z" } },
            selectedOrganizationId = _context.Organization, projects = Array.Empty<object>(), selectedProject = (object?)null, models = Array.Empty<object>(), assetLibrary = (object?)null,
            providerStatus = new { openAiReady = false, klingReady = false, videoReady = false }, mediaTools = new { ready = false, message = "Fixture", checkedAtUtc = "" },
            generationRunning = false, features = new { vietsubEnabled = true, speechSynchronizationEnabled = false, tikTokEnabled = false }, sceneFirstFrames = Array.Empty<object>(),
            license = new { hasActiveLicense = true, currentDeviceActivated = true, maxActivatedDevices = 1, activeDeviceCount = 1,
                offlineGraceHours = 0, serverTimeUtc = "2026-10-17T00:00:00Z", heartbeatIntervalSeconds = 300, leaseExpiresAtUtc = "2026-10-18T00:00:00Z", accessState = "Active" }
        } });
        public void ThrowIfFailed() { if (_failure is not null) throw new InvalidOperationException("Controlled native bridge failed.", _failure); }
        public async Task WriteEvidenceAsync(string? root, CoreWebView2 core)
        {
            ThrowIfFailed(); if (string.IsNullOrWhiteSpace(root)) return; Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "full-app-native-bridge.json"), JsonSerializer.Serialize(new {
                fixtureVersion = 2, fullProductionApp = true, realNativeSetupBridge = true, productionForm1GateDecision = true,
                fakeComponentAdapters = true, fakeSession = true, accountOrModelInstallation = false,
                blockedBusinessCommands = BlockedBusinessCommands, businessHandlerCalls = BusinessHandlerCalls,
                trace = Trace.ToArray(), requests = Requests.Select(request => new { request.Type, request.RequestId, request.OperationId }).ToArray()
            }, Json));
            await using var image = File.Create(Path.Combine(root, "full-app-final-overlay.png")); await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
        }
        public async ValueTask DisposeAsync() { _bridge.Dispose(); await Coordinator.DisposeAsync(); }
    }
    private sealed class Adapter(string id, Func<Task> release) : ISetupComponentAdapter, ISetupComponentStatusInspector
    {
        private SetupComponent _component = new(id, id, "fixture", "UNKNOWN", "Isolated fixture", true, true, true);
        public int InstallCalls; public SetupComponent Component => _component; public SetupComponent Inspect() => _component;
        public async Task<SetupComponent> RunAsync(bool install, bool accepted, Action<string, double?, long?, long?> progress, CancellationToken token)
        {
            if (install) { Interlocked.Increment(ref InstallCalls); await release().WaitAsync(token); }
            return _component = _component with { State = install ? "READY" : "NEEDS_VERIFICATION", Message = install ? "Fixture READY" : "Fixture needs verification" };
        }
    }
    private sealed class Context : IVietsubLocalAccessContext
    {
        public string User = "fixture-user-a"; public Guid Organization = Guid.NewGuid();
        public string? CurrentUserId => User; public Guid? SelectedOrganizationId => Organization;
        public Task EnsureSessionAndLicenseAsync(CancellationToken token) => Task.CompletedTask;
        public Task<IReadOnlyList<OrganizationSummaryResponse>> GetOrganizationsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<OrganizationSummaryResponse>>([
            new(Organization, "FIXTURE", "Isolated fixture", OrganizationRoles.Member, "Active", 0, 0, 0, 0, "USD", DateTime.UtcNow, DateTime.UtcNow.AddDays(1))]);
    }
}
