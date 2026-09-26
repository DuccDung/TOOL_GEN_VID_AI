using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_SHARED.Contracts.Organizations;

namespace TOOL_TESTS.SystemSetup;

public sealed class StartupSystemSetupWorkflowTests
{
    [Fact]
    public async Task Check_skips_components_with_current_machine_readiness()
    {
        var ready = new FakeAdapter("qwen", "READY");
        var unknown = new FakeAdapter("media", "UNKNOWN") {
            Action = (_, _, _, _) => Task.FromResult(Component("media", "READY")) };
        var disabled = new FakeAdapter("piper", "DISABLED");
        await using var fixture = new Fixture(ready, unknown, disabled);
        using var workflow = new StartupSystemSetupWorkflow(fixture.Coordinator);

        var snapshot = await workflow.CheckAsync(default);

        Assert.True(StartupSystemSetupWorkflow.IsReady(snapshot));
        Assert.Equal(0, ready.Calls);
        Assert.Equal(1, unknown.Calls);
        Assert.False(unknown.LastInstall);
        Assert.Equal(0, disabled.Calls);
    }

    [Fact]
    public async Task Missing_model_is_checked_then_installed_before_ready()
    {
        var model = new FakeAdapter("qwen", "NOT_INSTALLED") {
            Action = (install, _, _, _) => Task.FromResult(
                Component("qwen", install ? "READY" : "NOT_INSTALLED")) };
        await using var fixture = new Fixture(model);
        using var workflow = new StartupSystemSetupWorkflow(fixture.Coordinator);

        var checkedSnapshot = await workflow.CheckAsync(default);
        var installed = await workflow.InstallAsync(false, default);

        Assert.False(StartupSystemSetupWorkflow.IsReady(checkedSnapshot));
        Assert.True(StartupSystemSetupWorkflow.IsReady(installed.Snapshot));
        Assert.False(installed.RestartScheduled);
        Assert.Equal(2, model.Calls);
        Assert.True(model.LastInstall);
    }

    [Fact]
    public async Task Bundled_component_failure_starts_verified_application_repair()
    {
        var media = new FakeAdapter("media", "UNKNOWN", canInstall: false, canRepair: true) {
            Action = (_, _, _, _) => Task.FromResult(
                Component("media", "REPAIR_REQUIRED", canInstall: false, canRepair: true)) };
        await using var fixture = new Fixture(media);
        var repairCalls = 0;
        using var workflow = new StartupSystemSetupWorkflow(
            fixture.Coordinator,
            (_, _) =>
            {
                repairCalls++;
                return Task.CompletedTask;
            });

        var checkedSnapshot = await workflow.CheckAsync(default);
        var installed = await workflow.InstallAsync(false, default);

        Assert.True(StartupSystemSetupWorkflow.NeedsApplicationRepair(checkedSnapshot));
        Assert.True(installed.RestartScheduled);
        Assert.Equal(1, repairCalls);
        Assert.Equal(1, media.Calls);
    }

    [Fact]
    public async Task Qwen_resource_warning_requires_retry_bound_confirmation()
    {
        var warningSeen = false;
        var qwen = new FakeAdapter("qwen", "NOT_INSTALLED") {
            Action = (install, accepted, _, _) =>
            {
                warningSeen = accepted;
                return Task.FromResult(install && accepted
                    ? Component("qwen", "READY")
                    : Component(
                        "qwen",
                        "NEEDS_VERIFICATION",
                        errorCode: "TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED"));
            } };
        await using var fixture = new Fixture(qwen);
        using var workflow = new StartupSystemSetupWorkflow(fixture.Coordinator);

        var checkedSnapshot = await workflow.CheckAsync(default);
        var installed = await workflow.InstallAsync(true, default);

        Assert.True(StartupSystemSetupWorkflow.NeedsResourceConfirmation(checkedSnapshot));
        Assert.True(warningSeen);
        Assert.True(StartupSystemSetupWorkflow.IsReady(installed.Snapshot));
    }

    [Fact]
    public async Task Startup_gate_blocks_business_commands_until_all_required_components_are_ready()
    {
        var model = new FakeAdapter("qwen", "NOT_INSTALLED");
        await using var fixture = new Fixture(model);
        var gate = new StartupSystemSetupGate(fixture.Coordinator, required: true);

        Assert.True(gate.IsBlocking);
        Assert.True(StartupSystemSetupGate.IsAllowedCommand("app.ready"));
        Assert.True(StartupSystemSetupGate.IsAllowedCommand("system.setup.start"));
        Assert.True(StartupSystemSetupGate.IsAllowedCommand("media.tools.install"));
        Assert.True(StartupSystemSetupGate.IsAllowedCommand("tiktok.state.get"));
        Assert.False(StartupSystemSetupGate.IsAllowedCommand("tiktok.oauth.connect"));
        Assert.False(StartupSystemSetupGate.IsAllowedCommand("tiktok.publish.start"));
        Assert.False(StartupSystemSetupGate.IsAllowedCommand("generation.content"));

        using var workflow = new StartupSystemSetupWorkflow(fixture.Coordinator);
        var result = await workflow.InstallAsync(false, default);

        Assert.True(StartupSystemSetupWorkflow.IsReady(result.Snapshot));
        Assert.False(gate.IsBlocking);
    }

    [Fact]
    public async Task Startup_gate_is_disabled_for_a_role_that_cannot_manage_setup()
    {
        var model = new FakeAdapter("qwen", "NOT_INSTALLED");
        await using var fixture = new Fixture(model);
        var gate = new StartupSystemSetupGate(fixture.Coordinator, required: false);

        Assert.False(gate.IsBlocking);
        Assert.Equal(0, model.Calls);
    }

    [Fact]
    public async Task InstallingOnlyPiperDoesNotRepairOcrOrOpenTheBusinessGate()
    {
        var ocr = new FakeAdapter("ocr", "REPAIR_REQUIRED", canInstall: false);
        var piper = new FakeAdapter("piper", "NOT_INSTALLED");
        await using var fixture = new Fixture(ocr, piper);
        var gate = new StartupSystemSetupGate(fixture.Coordinator, required: true);
        var snapshot = fixture.Coordinator.GetSnapshot();
        var completed = new TaskCompletionSource<SetupSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Coordinator.Changed += (type, value) => { if (type == "system.setup.completed") completed.TrySetResult(value); };
        await fixture.Coordinator.StartAsync(new(Guid.NewGuid(), snapshot.OrganizationId!.Value,
            snapshot.ContextGeneration, ["piper"]), "start", _ => { }, default);
        var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("READY", result.Components.Single(x => x.Id == "piper").State);
        Assert.Equal("REPAIR_REQUIRED", result.Components.Single(x => x.Id == "ocr").State);
        Assert.Equal(0, ocr.Calls);
        Assert.True(result.Operation!.AllSelectedReady);
        Assert.False(result.Operation.AllRequiredReady);
        Assert.True(gate.IsBlocking);
    }

    private static SetupComponent Component(
        string id,
        string state,
        bool canInstall = true,
        bool canRepair = true,
        string? errorCode = null) =>
        new(
            id,
            id,
            "1",
            state,
            state,
            canInstall,
            true,
            canRepair,
            ErrorCode: errorCode,
            ResourceProfileId: id == "qwen" ? "standard" : null);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "vm-startup-setup-" + Guid.NewGuid().ToString("N"));

        public Fixture(params FakeAdapter[] adapters)
        {
            Context = new FakeContext();
            Coordinator = new SystemSetupCoordinator(
                new SystemSetupAuthorizer(Context),
                adapters,
                new RuntimeUseGate(_root),
                new SystemSetupJournal(_root),
                () => true);
        }

        public FakeContext Context { get; }

        public SystemSetupCoordinator Coordinator { get; }

        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync();
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class FakeAdapter(
        string id,
        string initialState,
        bool canInstall = true,
        bool canRepair = true) : ISetupComponentAdapter, ISetupComponentStatusInspector
    {
        private SetupComponent _current = Component(id, initialState, canInstall, canRepair);

        public SetupComponent Component => _current;

        public int Calls { get; private set; }

        public bool LastInstall { get; private set; }

        public Func<bool, bool, Action<string, double?, long?, long?>, CancellationToken, Task<SetupComponent>>? Action { get; init; }

        public SetupComponent Inspect() => _current;

        public async Task<SetupComponent> RunAsync(
            bool install,
            bool resourceWarningAccepted,
            Action<string, double?, long?, long?> progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastInstall = install;
            _current = Action is null
                ? Component(id, "READY", canInstall, canRepair)
                : await Action(install, resourceWarningAccepted, progress, cancellationToken);
            return _current;
        }
    }

    private sealed class FakeContext : IVietsubLocalAccessContext
    {
        public string? CurrentUserId => "user";

        public Guid? SelectedOrganizationId { get; } = Guid.NewGuid();

        public Task EnsureSessionAndLicenseAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<OrganizationSummaryResponse>> GetOrganizationsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<OrganizationSummaryResponse>>([
                new(
                    SelectedOrganizationId!.Value,
                    "ORG",
                    "Organization",
                    OrganizationRoles.Member,
                    "Active",
                    0,
                    0,
                    0,
                    0,
                    "USD",
                    DateTime.UtcNow,
                    DateTime.UtcNow.AddDays(1))
            ]);
    }
}
