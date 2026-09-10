using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Domain.Providers;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.Vietsub.Data;
using TOOL_SERVER.Vietsub.Translation;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubCloudServerTests
{
    [Fact]
    public async Task Start_ReplaysIdenticalSnapshotAndRejectsNewConcurrentIntent()
    {
        await using var f = await Fixture.Create();
        var first = await f.Start(); var again = await f.Start();
        Assert.Equal(first.JobId, again.JobId);
        Assert.DoesNotContain("Hello", (await f.Db.CloudTranslationJobs.SingleAsync()).ProtectedInput);
        Assert.Equal("idempotency_key_conflict", (await Assert.ThrowsAsync<AccountApiException>(() => f.Service.StartAsync(f.Project,
            f.Input with { Context = "changed" }, f.Who, default))).Code);
        Assert.Equal("CLOUD_JOB_ACTIVE", (await Assert.ThrowsAsync<AccountApiException>(() => f.Service.StartAsync(f.Project,
            f.Input with { ClientOperationId = Guid.NewGuid() }, f.Who, default))).Code);
        Assert.Equal(0, f.Provider.Calls); Assert.Empty(f.Budget.Reserved);
    }

    [Theory]
    [InlineData("disabled", "CLOUD_DISABLED")]
    [InlineData("pricing", "pricing_not_configured")]
    [InlineData("zero-budget", "organization_budget_exceeded")]
    [InlineData("access", "organization_role_denied")]
    [InlineData("session", "CLOUD_ACCESS_REQUIRED")]
    [InlineData("ownership", "vietsub_project_not_found")]
    public async Task Start_DeniesBeforeOutbound(string scenario, string code)
    {
        await using var f = await Fixture.Create();
        if (scenario == "disabled") f.Options.Enabled = false;
        if (scenario == "pricing") { f.Video.CostRates.RemoveRange(f.Video.CostRates); await f.Video.SaveChangesAsync(); }
        if (scenario == "zero-budget") f.Budget.Remaining = 0;
        if (scenario == "access") f.Access.Deny = true;
        if (scenario == "session") { (await f.Accounts.UserSessions.SingleAsync()).Status = "Revoked"; await f.Accounts.SaveChangesAsync(); }
        if (scenario == "ownership") { (await f.Db.Projects.SingleAsync()).CreatedByUserId = "another"; await f.Db.SaveChangesAsync(); }
        Assert.Equal(code, (await Assert.ThrowsAsync<AccountApiException>(() => f.Start())).Code);
        Assert.Empty(await f.Db.CloudTranslationJobs.ToArrayAsync()); Assert.Empty(f.Budget.Reserved); Assert.Equal(0, f.Provider.Calls);
    }

    [Fact]
    public async Task Worker_SettlesEachBatchOnceAndReturnsOrderedPagedResults()
    {
        await using var f = await Fixture.Create(25); var job = await f.Start();
        for (var i = 0; i < 3; i++) Assert.True(await f.Service.ProcessOneAsync(default));
        var done = await f.Service.GetAsync(f.Project, f.Org, job.JobId, f.Who, default);
        Assert.Equal("COMPLETED", done.Status); Assert.Equal(25, done.CompletedCues);
        Assert.Equal(3, f.Provider.Calls); Assert.Equal(3, f.Budget.Settlements.Count);
        var result = await f.Service.ResultsAsync(f.Project, f.Org, job.JobId, 0, f.Who, default);
        Assert.Equal(f.Input.Cues.Select(x => x.CueId), result.Items.Select(x => x.CueId)); Assert.False(result.HasMore);
        Assert.Equal(3, await f.Db.CloudTranslationAttempts.CountAsync());
        Assert.False(await f.Service.ProcessOneAsync(default));
        await f.Service.ControlAsync(f.Project, f.Org, job.JobId, "ack", f.Who, default);
        Assert.Equal(410, (await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ResultsAsync(f.Project, f.Org, job.JobId, 0, f.Who, default))).StatusCode);
        await f.Service.CleanupAsync(default);
        Assert.Null((await f.Db.CloudTranslationJobs.SingleAsync()).ProtectedInput);
        Assert.All(await f.Db.CloudTranslationBatches.ToArrayAsync(), x => Assert.Null(x.ProtectedResult));
    }

    [Fact]
    public async Task Cleanup_DoesNotLetOldEmptyJobsStarvePayloadExpiry()
    {
        await using var f = await Fixture.Create(); await f.Start();
        var expiring = await f.Db.CloudTranslationJobs.SingleAsync();
        expiring.Active = false; expiring.Status = "FAILED"; expiring.FinishedAtUtc = DateTime.UtcNow.AddHours(-25);
        expiring.ResultExpiresAtUtc = DateTime.UtcNow.AddDays(6);
        for (var i = 0; i < 110; i++) f.Db.CloudTranslationJobs.Add(new() { Id = Guid.NewGuid(), OrganizationId = f.Org,
            ProjectId = f.Project, UserId = f.Who.UserId, ClientOperationId = Guid.NewGuid(), Active = false, Status = "COMPLETED",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-6), UpdatedAtUtc = DateTime.UtcNow.AddDays(-6), FinishedAtUtc = DateTime.UtcNow.AddDays(-6) });
        await f.Db.SaveChangesAsync();
        await f.Service.CleanupAsync(default);
        Assert.Null((await f.Db.CloudTranslationJobs.SingleAsync(x => x.Id == expiring.Id)).ProtectedInput);
        Assert.Equal(111, await f.Db.CloudTranslationJobs.CountAsync());
    }

    [Fact]
    public async Task EmptyResultPageDuringRunning_DoesNotPretendAnotherPageIsReady()
    {
        await using var f = await Fixture.Create(); var job = await f.Start();
        var page = await f.Service.ResultsAsync(f.Project, f.Org, job.JobId, 0, f.Who, default);
        Assert.Empty(page.Items); Assert.False(page.HasMore); Assert.Equal(0, page.NextCursor);
    }

    [Fact]
    public async Task UnexpectedFailureAfterDispatch_LeavesReconcileableUnknownBatch()
    {
        await using var f = await Fixture.Create(); await f.Start(); f.Provider.UnexpectedFailure = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ProcessOneAsync(default));
        Assert.Equal("UNKNOWN", (await f.Db.CloudTranslationJobs.SingleAsync()).Status);
        Assert.Equal("UNKNOWN", (await f.Db.CloudTranslationBatches.SingleAsync()).Status);
        Assert.Equal("UNKNOWN", (await f.Db.CloudTranslationAttempts.SingleAsync()).Status);
        Assert.False(await f.Service.ProcessOneAsync(default)); Assert.Empty(f.Budget.Settlements);
    }

    [Fact]
    public async Task LostResponse_HoldsReservationAndExplicitReconciliationAllowsRetryWithNewAttempt()
    {
        await using var f = await Fixture.Create(); var job = await f.Start();
        f.Provider.Fail = true; await f.Service.ProcessOneAsync(default);
        Assert.Equal("UNKNOWN", (await f.Service.GetAsync(f.Project, f.Org, job.JobId, f.Who, default)).Status);
        Assert.False(await f.Service.ProcessOneAsync(default)); Assert.Empty(f.Budget.Settlements); Assert.Empty(f.Budget.Released);
        Assert.Equal("CLOUD_UNKNOWN", (await Assert.ThrowsAsync<AccountApiException>(() =>
            f.Service.ControlAsync(f.Project, f.Org, job.JobId, "retry", f.Who, default))).Code);
        var requestId = (await f.Db.CloudTranslationBatches.SingleAsync()).RequestId;
        var evidence = new VietsubCloudReconcileRequest(requestId, "CONFIRMED_USAGE", "INC-123", 120, 60, "resp_evidence");
        Assert.Equal(403, (await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ReconcileAsync(job.JobId, evidence, f.Who, default))).StatusCode);
        f.Accounts.Roles.Add(new IdentityRole("Admin") { Id = "admin-role" });
        f.Accounts.UserRoles.Add(new IdentityUserRole<string> { UserId = f.Who.UserId, RoleId = "admin-role" });
        await f.Accounts.SaveChangesAsync();
        var reconciled = await f.Service.ReconcileAsync(job.JobId, evidence, f.Who, default);
        Assert.True(reconciled.Settled); Assert.Equal("FAILED", reconciled.Status);
        await f.Service.ReconcileAsync(job.JobId, evidence, f.Who, default);
        Assert.Single(f.Budget.Settlements);
        f.Provider.Fail = false;
        await f.Service.ControlAsync(f.Project, f.Org, job.JobId, "retry", f.Who, default);
        await f.Service.ProcessOneAsync(default);
        Assert.Equal(2, f.Provider.Calls); Assert.Equal(2, await f.Db.CloudTranslationAttempts.CountAsync());
        Assert.NotEqual(requestId, (await f.Db.CloudTranslationBatches.SingleAsync()).RequestId);
    }

    [Fact]
    public async Task SettlementFailure_RecoversDurableResultWithoutProviderResubmission()
    {
        await using var f = await Fixture.Create(); var job = await f.Start(); f.Budget.FailSettlementOnce = true;
        await Assert.ThrowsAsync<IOException>(() => f.Service.ProcessOneAsync(default));
        Assert.Equal(1, f.Provider.Calls); Assert.Empty(f.Budget.Settlements);
        await f.Service.ProcessOneAsync(default);
        Assert.Equal(1, f.Provider.Calls); Assert.Single(f.Budget.Settlements);
        Assert.Equal("COMPLETED", (await f.Service.GetAsync(f.Project, f.Org, job.JobId, f.Who, default)).Status);
    }

    [Fact]
    public async Task PauseAndDisable_PreventNewOutboundWhileCompletedResultsRemainReadable()
    {
        await using var f = await Fixture.Create(13); var job = await f.Start();
        await f.Service.ProcessOneAsync(default);
        await f.Service.ControlAsync(f.Project, f.Org, job.JobId, "pause", f.Who, default);
        Assert.False(await f.Service.ProcessOneAsync(default)); Assert.Equal(1, f.Provider.Calls);
        await f.Service.ControlAsync(f.Project, f.Org, job.JobId, "resume", f.Who, default);
        f.Options.Enabled = false;
        await f.Service.ProcessOneAsync(default);
        Assert.Equal(1, f.Provider.Calls);
        Assert.Equal("BLOCKED", (await f.Service.GetAsync(f.Project, f.Org, job.JobId, f.Who, default)).Status);
        Assert.Equal(12, (await f.Service.ResultsAsync(f.Project, f.Org, job.JobId, 0, f.Who, default)).Items.Count);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public VietsubDbContext Db = null!;
        public AccountDbContext Accounts = new(new DbContextOptionsBuilder<AccountDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public ProviderAdminDbContext Providers = new(new DbContextOptionsBuilder<ProviderAdminDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public AiGovernanceDbContext Governance = new(new DbContextOptionsBuilder<AiGovernanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public VideoFactoryDbContext Video = new(new DbContextOptionsBuilder<VideoFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public VietsubCloudTranslationService Service = null!;
        public VietsubCloudTranslationOptions Options = new() { Enabled = true, ModelCode = "fixture-model" };
        public FakeProvider Provider = new(); public FakeBudget Budget = new(); public FakeAccess Access = new();
        public Guid Project = Guid.NewGuid(); public Guid Org = Guid.NewGuid();
        public CloudAccess Who = new("cloud-owner", Guid.NewGuid(), Guid.NewGuid());
        public VietsubCloudStartRequest Input = null!;
        public Task<VietsubCloudJobResponse> Start() => Service.StartAsync(Project, Input, Who, default);
        public static async Task<Fixture> Create(int cues = 1)
        {
            var f = new Fixture(); await f.connection.OpenAsync();
            f.Db = new(new DbContextOptionsBuilder<VietsubDbContext>().UseSqlite(f.connection).Options);
            var schema = f.Db.Database.GenerateCreateScript().Replace("\"RowVersion\" BLOB NOT NULL", "\"RowVersion\" BLOB NOT NULL DEFAULT X''");
            await f.Db.Database.ExecuteSqlRawAsync(schema);
            var now = DateTime.UtcNow;
            var providerId = Guid.NewGuid(); var modelId = Guid.NewGuid(); var credentialId = Guid.NewGuid();
            f.Db.Projects.Add(new() { ProjectId = f.Project, OrganizationId = f.Org, CreatedByUserId = f.Who.UserId,
                Name = "Fixture", CreatedAtUtc = now, UpdatedAtUtc = now });
            await f.Db.SaveChangesAsync();
            var user = new ApplicationUser { Id = f.Who.UserId, UserName = "fixture", AccountStatus = "Active" };
            var device = new RegisteredDevice { DeviceId = f.Who.DeviceId, UserId = user.Id, User = user, DeviceName = "fixture" };
            f.Accounts.UserSessions.Add(new UserSession { SessionId = f.Who.SessionId, UserId = user.Id, User = user,
                DeviceId = device.DeviceId, Device = device, AbsoluteExpiresAtUtc = now.AddDays(1) });
            await f.Accounts.SaveChangesAsync();
            f.Providers.Providers.Add(new AiProvider { ProviderId = providerId, ProviderCode = "openai", DisplayName = "fixture", IsEnabled = true,
                Models = [new AiProviderModel { ProviderModelId = modelId, ModelCode = f.Options.ModelCode, DisplayName = "fixture",
                    Modality = "Text", IsEnabled = true, CapabilitiesJson = "{\"structuredOutput\":true}" }] });
            await f.Providers.SaveChangesAsync();
            f.Governance.Organizations.Add(new Organization { OrganizationId = f.Org, Name = "fixture", Code = "fixture", CreatedByUserId = user.Id, MonthlyBudgetLimit = 100 });
            f.Governance.OrganizationProviderCredentials.Add(new() { OrganizationProviderCredentialId = credentialId,
                OrganizationId = f.Org, ProviderId = providerId, Name = "fixture", EncryptedPayload = "fixture-protected", SecretHint = "fixture", CreatedByUserId = user.Id });
            await f.Governance.SaveChangesAsync();
            foreach (var type in new[] { "InputToken", "OutputToken" })
                f.Video.CostRates.Add(new() { CostRateId = Guid.NewGuid(), ProviderModelId = modelId, UsageType = type,
                    Unit = "MillionTokens", UnitPrice = 1, CurrencyCode = "USD", IsActive = true, EffectiveFromUtc = now.AddDays(-1) });
            await f.Video.SaveChangesAsync();
            f.Input = VietsubCloudProviderTests.Request(cues) with { OrganizationId = f.Org };
            f.Service = new(f.Db, f.Governance, f.Providers, f.Video, f.Accounts, f.Access,
                new FakeResolver(new(providerId, modelId, credentialId, "openai", f.Options.ModelCode,
                    new Uri("https://api.openai.com/v1/"), "Bearer", null, "fixture-key")), f.Budget, f.Provider,
                new EphemeralDataProtectionProvider(), Microsoft.Extensions.Options.Options.Create(f.Options), TimeProvider.System);
            return f;
        }
        public async ValueTask DisposeAsync()
        { await Db.DisposeAsync(); await connection.DisposeAsync(); await Accounts.DisposeAsync(); await Providers.DisposeAsync(); await Governance.DisposeAsync(); await Video.DisposeAsync(); }
    }
    private sealed class FakeAccess : IGenerationAccessService
    {
        public bool Deny;
        public Task<GenerationAccessContext> RequireAsync(string userId, Guid deviceId, Guid? org, Guid? project, CancellationToken ct)
        { Assert.Null(project); if (Deny) throw new AccountApiException(403, "organization_role_denied", "denied"); return Task.FromResult(new GenerationAccessContext(org!.Value, "fixture", "Member", null)); }
    }
    private sealed class FakeResolver(ProviderRuntimeConfiguration runtime) : IProviderRuntimeResolver
    {
        public Task<ProviderRuntimeConfiguration> ResolveAsync(Guid org, string provider, string modality, Guid? credential, CancellationToken ct) => Task.FromResult(runtime);
        public Task<GenerationProviderStatusResponse> GetStatusAsync(Guid org, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class FakeProvider : IOpenAiSubtitleTranslationClient
    {
        public int Calls; public bool Fail, UnexpectedFailure;
        public Task<SubtitleProviderResult> TranslateAsync(ProviderRuntimeConfiguration p, VietsubCloudStartRequest s,
            IReadOnlyList<VietsubCloudCue> cues, int outputCap, string user, CancellationToken ct)
        {
            Calls++; if (Fail) throw new HttpRequestException("fixture connection lost");
            if (UnexpectedFailure) throw new InvalidOperationException("fixture unexpected error after dispatch");
            return Task.FromResult(new SubtitleProviderResult(cues.Where(x => x.IsTarget).Select(x => new VietsubCloudCueResult(x.CueId, x.InputFingerprint, "Xin chào", [])).ToArray(), 100, 60, "resp_fixture", null));
        }
    }
    private sealed class FakeBudget : IAiBudgetService
    {
        public decimal Remaining = 100; public bool FailSettlementOnce;
        public Dictionary<Guid, Guid> Reserved = []; public Dictionary<Guid, decimal> Settlements = []; public HashSet<Guid> Released = [];
        public Task<BudgetSnapshot> GetSnapshotAsync(Guid org, CancellationToken ct) => Task.FromResult(new BudgetSnapshot(Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddDays(30), Remaining, 0, 0, Remaining, "USD"));
        public Task<BudgetReservationResult> ReserveVietsubAsync(Guid org, string user, Guid project, Guid request, string key, string provider, string model, decimal amount, CancellationToken ct)
        { if (!Reserved.TryGetValue(request, out var id)) Reserved[request] = id = Guid.NewGuid(); return Task.FromResult(new BudgetReservationResult(id, Guid.NewGuid(), amount, "USD")); }
        public Task<BudgetReservationResult> ReserveAsync(Guid org, string user, Guid project, Guid request, string key, string provider, string model, decimal amount, CancellationToken ct) => throw new Xunit.Sdk.XunitException("Vietsub must not reserve against a video project.");
        public Task SettleAsync(Guid id, decimal actual, Guid? credential, object? usage, object? rate, CancellationToken ct)
        { if (FailSettlementOnce) { FailSettlementOnce = false; throw new IOException("fixture settlement interruption"); } Settlements.TryAdd(id, actual); return Task.CompletedTask; }
        public Task ReleaseAsync(Guid id, CancellationToken ct) { Released.Add(id); return Task.CompletedTask; }
    }
}
