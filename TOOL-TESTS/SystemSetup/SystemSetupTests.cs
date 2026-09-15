using System.Collections.Concurrent;
using System.Text.Json;
using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_SHARED.Contracts.Organizations;

namespace TOOL_TESTS.SystemSetup;

public sealed class SystemSetupTests
{
    [Fact]
    [Trait("Category", "WebView2Integration")]
    public async Task WebView2_UsesRealSetupBridgeWithoutAProject()
    {
        await using var fixture = new Fixture();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browserExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var form = new Form { Width = 640, Height = 480, ShowInTaskbar = false, Opacity = 0 };
            using var web = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
            form.Controls.Add(web);
            using var bridge = new SystemSetupBridge(fixture.Coordinator, json => {
                if (!form.IsDisposed && form.IsHandleCreated)
                    form.BeginInvoke(() => web.CoreWebView2?.PostWebMessageAsJson(json));
            });
            form.Shown += async (_, _) => {
                try
                {
                    var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(
                        userDataFolder: Path.Combine(fixture.Root, "webview"));
                    environment.BrowserProcessExited += (_, _) => browserExited.TrySetResult();
                    await web.EnsureCoreWebView2Async(environment);
                    web.CoreWebView2.WebMessageReceived += async (_, message) => {
                        try
                        {
                            var json = message.TryGetWebMessageAsString();
                            if (json.StartsWith("fixture-error:", StringComparison.Ordinal)) throw new InvalidOperationException(json);
                            if (json == "fixture-completed")
                            {
                                web.Dispose();
                                // The browser exit event is delivered on this STA; keep its message loop alive until it arrives.
                                await browserExited.Task.WaitAsync(TimeSpan.FromSeconds(10));
                                completed.TrySetResult(); form.Close(); return;
                            }
                            if (!await bridge.TryHandleAsync(json, default)) throw new InvalidOperationException("Setup message not handled.");
                        }
                        catch (Exception e) { completed.TrySetException(e); form.Close(); }
                    };
                    web.NavigateToString("""
                        <!doctype html><html><body>Setup fixture<script>
                        const host=window.chrome.webview; let accepted=false;
                        window.onerror=(message)=>host.postMessage('fixture-error:'+message);
                        host.addEventListener('message', event => {
                          const message=event.data;
                          if(message.type==='system.setup.status') {
                            const state=message.payload;
                            host.postMessage(JSON.stringify({type:'system.setup.start',requestId:'install',payload:{
                              operationId:'dc01e2ee-a78e-4bba-8923-798da627debb',expectedOrganizationId:state.organizationId,
                              contextGeneration:state.contextGeneration,componentIds:['qwen']}}));
                          }
                          if(message.type==='system.setup.accepted') accepted=true;
                          if(message.type==='operation.error') host.postMessage('fixture-error:'+message.error.code);
                          if(message.type==='system.setup.completed' && accepted && message.payload.operation.allSelectedReady)
                            host.postMessage('fixture-completed');
                        });
                        host.postMessage(JSON.stringify({type:'system.setup.get',requestId:'get'}));
                        </script></body></html>
                        """);
                }
                catch (Exception e) { completed.TrySetException(e); form.Close(); }
            };
            using var timeout = new System.Windows.Forms.Timer { Interval = 25000 };
            timeout.Tick += (_, _) => { completed.TrySetException(new TimeoutException("WebView2 setup fixture timed out.")); form.Close(); };
            timeout.Start();
            Application.Run(form);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, fixture.Adapter.Calls);
    }

    [Fact]
    public async Task Getter_IsCachedAndNeverRunsAdapters()
    {
        await using var fixture = new Fixture();
        for (var i = 0; i < 20; i++) Assert.Equal("UNKNOWN", fixture.Coordinator.GetSnapshot().Components[0].State);
        Assert.Equal(0, fixture.Adapter.Calls);
        Assert.Equal(0, fixture.Context.Checks);
    }

    [Theory]
    [InlineData("Viewer", "Active")]
    [InlineData("Member", "Disabled")]
    [InlineData("unknown", "Active")]
    public async Task Authorization_RejectsBeforeAdapterOrLease(string role, string status)
    {
        await using var fixture = new Fixture();
        fixture.Context.Role = role; fixture.Context.Status = status;
        await Assert.ThrowsAsync<SetupException>(() => fixture.Start());
        Assert.Equal(0, fixture.Adapter.Calls);
        Assert.Null(fixture.Coordinator.GetSnapshot().Operation);
        using var lease = fixture.Gate.Acquire(true);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("OrganizationAdmin")]
    [InlineData("BillingManager")]
    [InlineData("Member")]
    public async Task Setup_WorksWithoutProjectForAllowedMember(string role)
    {
        await using var fixture = new Fixture();
        fixture.Context.Role = role;
        await fixture.Start();
        var done = await fixture.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Completed", done.Operation!.State);
        Assert.True(done.Operation.AllSelectedReady);
        Assert.Equal(1, fixture.Adapter.Calls);
    }

    [Fact]
    public async Task StaleContext_IsRejectedBeforeRemoteChecks()
    {
        await using var fixture = new Fixture();
        var request = fixture.Request();
        fixture.Context.SelectedOrganizationId = Guid.NewGuid();
        await Assert.ThrowsAsync<SetupException>(() => fixture.Coordinator.StartAsync(request, "start", _ => { }, default));
        Assert.Equal(0, fixture.Context.Checks);
        Assert.Equal(0, fixture.Adapter.Calls);
    }

    [Fact]
    public async Task Check_DoesNotInstallMissingComponent()
    {
        await using var fixture = new Fixture();
        fixture.Adapter.Action = (install, _, _, _) => Task.FromResult(fixture.Adapter.Component with {
            State = install ? "READY" : "NOT_INSTALLED" });
        await fixture.Start("check");
        var done = await fixture.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("NOT_INSTALLED", done.Components[0].State);
        Assert.False(fixture.Adapter.LastInstall);
        Assert.Equal("Failed", done.Operation!.State);
    }

    [Fact]
    public async Task DuplicateOperation_ReplaysButChangedPayloadConflicts()
    {
        await using var fixture = new Fixture(block: true);
        var request = fixture.Request();
        await fixture.Coordinator.StartAsync(request, "start", _ => { }, default);
        await fixture.Adapter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Coordinator.StartAsync(request, "start", _ => { }, default);
        var error = await Assert.ThrowsAsync<SetupException>(() => fixture.Coordinator.StartAsync(
            request with { ResourceWarningAccepted = true }, "start", _ => { }, default));
        Assert.Equal("system_setup_conflict", error.Code);
        Assert.Equal(1, fixture.Adapter.Calls);
        fixture.Coordinator.Cancel(request.OperationId);
        await fixture.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReplayOfOldOperation_CannotOverrideNewerSnapshotRevision()
    {
        await using var fixture = new Fixture();
        var first = fixture.Request();
        await fixture.Coordinator.StartAsync(first, "start", _ => { }, default);
        await fixture.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Adapter.Action = async (_, _, _, token) => {
            await Task.Delay(Timeout.Infinite, token); return fixture.Adapter.Component;
        };
        await fixture.Start();
        var current = fixture.Coordinator.GetSnapshot();
        SetupSnapshot? replay = null;
        await fixture.Coordinator.StartAsync(first, "start", value => replay = value, default);
        Assert.NotNull(replay);
        Assert.Equal(first.OperationId, replay.Operation!.OperationId);
        Assert.True(replay.Revision < current.Revision);
        Assert.Equal(current.Operation!.OperationId, fixture.Coordinator.GetSnapshot().Operation!.OperationId);
    }

    [Fact]
    public async Task Cancel_AfterLicenseExpiry_ReleasesLeaseAndEndsOperation()
    {
        await using var fixture = new Fixture(block: true);
        await fixture.Start();
        await fixture.Adapter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Context.Valid = false;
        var id = fixture.Coordinator.GetSnapshot().Operation!.OperationId;
        fixture.Coordinator.Cancel(id);
        fixture.Coordinator.Cancel(id);
        var terminal = await fixture.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Cancelled", terminal.Operation!.State);
        using var lease = fixture.Gate.Acquire(true);
    }

    [Fact]
    public async Task NativeMonitor_CancelsExpiredLicense()
    {
        await using var fixture = new Fixture(block: true);
        await fixture.Start();
        await fixture.Adapter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Context.Valid = false;
        var terminal = await fixture.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Cancelled", terminal.Operation!.State);
    }

    [Fact]
    public async Task OrganizationChange_HidesOldOperationAndCallbacks()
    {
        await using var fixture = new Fixture(block: true);
        await fixture.Start();
        await fixture.Adapter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var before = fixture.Coordinator.GetSnapshot();
        fixture.Context.SelectedOrganizationId = Guid.NewGuid();
        var after = fixture.Coordinator.GetSnapshot();
        Assert.NotEqual(before.ContextGeneration, after.ContextGeneration);
        Assert.Null(after.Operation);
        await fixture.Coordinator.DisposeAsync();
        fixture.Disposed = true;
        Assert.False(fixture.Terminal.Task.IsCompleted);
    }

    [Fact]
    public async Task Admission_PreventsBothDirectionsAcrossGateInstances()
    {
        await using var fixture = new Fixture(block: true);
        var otherProcessGate = new RuntimeUseGate(fixture.Root);
        using (otherProcessGate.Acquire(false))
        {
            await Assert.ThrowsAsync<SetupException>(() => fixture.Start());
            Assert.Equal(0, fixture.Adapter.Calls);
        }
        await fixture.Start();
        await fixture.Adapter.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<SetupException>(() => otherProcessGate.Acquire(false));
        Assert.Throws<SetupException>(() => otherProcessGate.Acquire(true));
    }

    [Fact]
    public async Task SharedLegacyRoot_CannotBeWrittenFromAnotherWindowsProfile()
    {
        await using var fixture = new Fixture();
        var sharedModel = Path.Combine(fixture.Root, "model");
        var first = new RuntimeUseGate(Path.Combine(fixture.Root, "profile-a"), sharedModel);
        var second = new RuntimeUseGate(Path.Combine(fixture.Root, "profile-b"), sharedModel);
        using (first.Acquire(false)) Assert.Throws<SetupException>(() => second.Acquire(true));
        using (first.Acquire(true)) Assert.Throws<SetupException>(() => second.Acquire(false));
        using var released = second.Acquire(true);
    }

    [Fact]
    public async Task PartialSuccess_RetainsReadyAndPiperIsNotImplicitlyReady()
    {
        var piper = new FakeAdapter("piper") { Action = (_, _, _, _) => throw new IOException("SECRET RAW PATH") };
        await using var fixture = new Fixture(extra: piper);
        await fixture.Start(ids: ["qwen", "piper"]);
        var done = await fixture.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("PartiallyCompleted", done.Operation!.State);
        Assert.False(done.Operation.AllSelectedReady);
        Assert.False(done.Operation.AllRequiredReady);
        Assert.Equal("READY", done.Components[0].State);
        Assert.DoesNotContain("SECRET", JsonSerializer.Serialize(done));
        using var lease = fixture.Gate.Acquire(true);
    }

    [Fact]
    public async Task UnselectedPiper_DoesNotBecomeReady()
    {
        await using var fixture = new Fixture(extra: new FakeAdapter("piper"));
        await fixture.Start();
        var done = await fixture.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(done.Operation!.AllSelectedReady);
        Assert.False(done.Operation.AllRequiredReady);
        Assert.Equal("UNKNOWN", done.Components.Single(c => c.Id == "piper").State);
    }

    [Fact]
    public async Task Progress_IsOrdered_AndCannotArriveAfterTerminal()
    {
        await using var fixture = new Fixture();
        Action<string, double?, long?, long?>? callback = null;
        fixture.Adapter.Action = (_, _, p, _) => {
            callback = p; p("DOWNLOAD", 10, 10, 100); p("PROBE", 90, 100, 100);
            return Task.FromResult(fixture.Adapter.Component with { State = "READY" });
        };
        var received = new ConcurrentQueue<long>();
        fixture.Coordinator.Changed += (_, value) => received.Enqueue(value.Operation!.Sequence);
        await fixture.Start();
        await fixture.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var sequence = fixture.Coordinator.GetSnapshot().Operation!.Sequence;
        callback!("OLD", 20, null, null);
        Assert.Equal(sequence, fixture.Coordinator.GetSnapshot().Operation!.Sequence);
        Assert.Equal(received.Order(), received);
    }

    [Fact]
    public async Task Journal_RestoresInterruptedButNeverReadiness()
    {
        await using var fixture = new Fixture();
        var journal = new SystemSetupJournal(fixture.Root);
        var id = Guid.NewGuid();
        journal.Write("user", fixture.Context.SelectedOrganizationId!.Value,
            new(id, "start", "Running", ["qwen"], 12, AllSelectedReady: true, AllRequiredReady: true));
        var restored = journal.Read("user", fixture.Context.SelectedOrganizationId.Value)!;
        Assert.Equal("Interrupted", restored.State);
        Assert.False(restored.AllRequiredReady);
        Assert.False(restored.AllSelectedReady);
        Assert.Null(journal.Read("other-user", fixture.Context.SelectedOrganizationId.Value));
    }

    [Fact]
    public async Task ResourceConfirmation_IsBoundToPreviousOperationAndNativeProfile()
    {
        await using var fixture = new Fixture();
        fixture.Adapter.Action = (_, accepted, _, _) => accepted
            ? Task.FromResult(fixture.Adapter.Component with { State = "READY" })
            : throw new TOOL_LOCAL.Vietsub.Translation.VietsubTranslationException(
                "TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED", "Resource warning");
        await fixture.Start();
        var previous = await fixture.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var request = fixture.Request() with { PreviousOperationId = previous.Operation!.OperationId,
            ResourceWarningAccepted = true, ConfirmedResourceProfileId = "wrong-profile" };
        var rejected = await Assert.ThrowsAsync<SetupException>(() => fixture.Coordinator.StartAsync(request, "retry", _ => { }, default));
        Assert.Equal("system_setup_confirmation_invalid", rejected.Code);
        Assert.Equal(1, fixture.Adapter.Calls);
        var done = new TaskCompletionSource<SetupSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Coordinator.Changed += (type, state) => { if (type == "system.setup.completed") done.TrySetResult(state); };
        await fixture.Coordinator.StartAsync(request with { ConfirmedResourceProfileId = "standard" }, "retry", _ => { }, default);
        var retried = await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Completed", retried.Operation!.State);
        Assert.Equal(2, fixture.Adapter.Calls);
    }

    [Theory]
    [InlineData("system.setup.start", "\"componentIds\":[\"unknown\"]")]
    [InlineData("system.setup.start", "\"componentIds\":[\"qwen\"],\"url\":\"https://evil.invalid\"")]
    [InlineData("system.setup.start", "\"componentIds\":[\"qwen\"],\"path\":\"C:/model\"")]
    [InlineData("system.setup.start", "\"componentIds\":[]")]
    [InlineData("system.setup.start", "\"componentIds\":[\"qwen\",\"qwen\"]")]
    public async Task Bridge_RejectsInvalidPayloadBeforeSideEffects(string type, string fields)
    {
        await using var fixture = new Fixture();
        var messages = new List<string>();
        using var bridge = new SystemSetupBridge(fixture.Coordinator, messages.Add);
        var request = fixture.Request();
        var payload = $"{{\"operationId\":\"{request.OperationId}\",\"expectedOrganizationId\":\"{request.ExpectedOrganizationId}\",\"contextGeneration\":\"{request.ContextGeneration}\",{fields}}}";
        Assert.True(await bridge.TryHandleAsync($"{{\"type\":\"{type}\",\"requestId\":\"request\",\"payload\":{payload}}}", default));
        Assert.Contains("operation.error", Assert.Single(messages));
        Assert.Equal(0, fixture.Adapter.Calls);
    }

    [Fact]
    public async Task Bridge_AcknowledgesBeforeProgress_AndReconnectRestoresTerminal()
    {
        await using var fixture = new Fixture();
        var messages = new ConcurrentQueue<string>();
        using var bridge = new SystemSetupBridge(fixture.Coordinator, messages.Enqueue);
        var json = JsonSerializer.Serialize(new { type = "system.setup.start", requestId = "start", payload = fixture.Request() },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(await bridge.TryHandleAsync(json, default));
        await fixture.Terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("system.setup.accepted", messages.First());
        await bridge.TryHandleAsync("{\"type\":\"system.setup.get\",\"requestId\":\"get\"}", default);
        Assert.Contains("Completed", messages.Last());
        Assert.False(await bridge.TryHandleAsync("{\"type\":\"system.setup.execute\",\"requestId\":\"bad\"}", default));
    }

    [Fact]
    public async Task Bridge_marks_startup_snapshots_and_only_exits_for_an_empty_valid_request()
    {
        await using var fixture = new Fixture();
        var messages = new List<string>();
        var exitCalls = 0;
        using var bridge = new SystemSetupBridge(
            fixture.Coordinator,
            messages.Add,
            () => exitCalls++,
            startupRequired: true);

        Assert.True(await bridge.TryHandleAsync(
            "{\"type\":\"system.setup.get\",\"requestId\":\"get\"}", default));
        using (var status = JsonDocument.Parse(Assert.Single(messages)))
        {
            Assert.True(status.RootElement.GetProperty("payload").GetProperty("startupRequired").GetBoolean());
        }

        messages.Clear();
        Assert.True(await bridge.TryHandleAsync(
            "{\"type\":\"system.setup.exit\",\"requestId\":\"bad\",\"payload\":{\"force\":true}}", default));
        Assert.Equal(0, exitCalls);
        Assert.Contains("operation.error", Assert.Single(messages));

        messages.Clear();
        Assert.True(await bridge.TryHandleAsync(
            "{\"type\":\"system.setup.exit\",\"requestId\":\"exit\"}", default));
        Assert.Equal(1, exitCalls);
        Assert.Empty(messages);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "vm-setup-" + Guid.NewGuid().ToString("N"));
        public FakeContext Context { get; } = new();
        public FakeAdapter Adapter { get; } = new("qwen");
        public RuntimeUseGate Gate { get; }
        public SystemSetupCoordinator Coordinator { get; }
        public TaskCompletionSource<SetupSnapshot> Terminal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed;
        public Fixture(bool block = false, FakeAdapter? extra = null)
        {
            if (block) Adapter.Action = async (_, _, _, token) => {
                await Task.Delay(Timeout.Infinite, token); return Adapter.Component;
            };
            Gate = new(Root);
            Coordinator = new(new(Context), extra is null ? [Adapter] : [Adapter, extra], Gate, new(Root), () => Context.Valid);
            Coordinator.Changed += (type, snapshot) => { if (type == "system.setup.completed") Terminal.TrySetResult(snapshot); };
        }
        public SystemSetupRequest Request(string[]? ids = null) => new(Guid.NewGuid(), Context.SelectedOrganizationId!.Value,
            Coordinator.GetSnapshot().ContextGeneration, ids ?? ["qwen"]);
        public Task Start(string mode = "start", string[]? ids = null) => Coordinator.StartAsync(Request(ids), mode, _ => { }, default);
        public async ValueTask DisposeAsync()
        {
            if (!Disposed) await Coordinator.DisposeAsync();
            try { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
            catch (IOException) { /* Preserve a locked browser fixture for diagnosis; never kill unrelated browser processes. */ }
        }
    }
    private sealed class FakeAdapter(string id) : ISetupComponentAdapter
    {
        public SetupComponent Component => new(id, id, "1", "UNKNOWN", "unknown", true, true, true, ResourceProfileId: "standard");
        public int Calls;
        public bool LastInstall;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<bool, bool, Action<string, double?, long?, long?>, CancellationToken, Task<SetupComponent>>? Action;
        public Task<SetupComponent> RunAsync(bool install, bool warning, Action<string, double?, long?, long?> progress, CancellationToken token)
        {
            Calls++; LastInstall = install; Entered.TrySetResult();
            return Action?.Invoke(install, warning, progress, token) ?? Task.FromResult(Component with { State = "READY" });
        }
    }
    private sealed class FakeContext : IVietsubLocalAccessContext
    {
        public string? CurrentUserId { get; set; } = "user";
        public Guid? SelectedOrganizationId { get; set; } = Guid.NewGuid();
        public string Role = "Member";
        public string Status = "Active";
        public bool Valid = true;
        public int Checks;
        public Task EnsureSessionAndLicenseAsync(CancellationToken token)
        {
            Checks++;
            if (!Valid) throw new SetupException("expired", "expired");
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<OrganizationSummaryResponse>> GetOrganizationsAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<OrganizationSummaryResponse>>([new(SelectedOrganizationId!.Value,
                "ORG", "Organization", Role, Status, 0, 0, 0, 0, "USD", DateTime.UtcNow, DateTime.UtcNow.AddDays(1))]);
    }
}
