using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Controllers;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.Payments;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_TESTS.Payments;

public sealed class AdminLicensePlanComposerTests
{
    [Fact]
    public async Task LocalTranslationPublishesAndEditsWithoutAnyPoolAndReplaysSafely()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.OrganizationPoolOrganizations.Remove(f.Link); f.Db.OrganizationPools.Remove(f.Pool);
        f.Db.Organizations.Remove(f.Organization); f.Readiness.Ready = false;
        await f.Db.SaveChangesAsync();
        var request = f.Request with { OrganizationPoolId = null, ProductScope = "vietsub", VietsubCloudEnabled = false, MemberMonthlyAiBudget = 25 };
        var created = await f.Service.CreateAsync(request, "admin", default);
        Assert.Null(created.OrganizationPoolId);
        Assert.True(created.Plan.IsActive); Assert.True(created.Plan.IsPublic);
        Assert.True(LicenseProductPolicy.IsLocalOnly(created.Plan.FeatureFlagsJson));
        Assert.True((await f.Service.CreateAsync(request, "admin", default)).Replayed);
        var editor = await f.Service.GetEditorAsync(created.Plan.LicensePlanId, default);
        var edited = await f.Service.CreateAsync(request with { CreationId = Guid.NewGuid(), PlanId = created.Plan.LicensePlanId,
            Name = "Local đã sửa", ProductScope = null, VietsubCloudEnabled = null,
            ExpectedUpdatedAtUtc = editor.Plan.UpdatedAtUtc, ExpectedMappingUpdatedAtUtc = editor.MappingUpdatedAtUtc }, "admin", default);
        Assert.Null(edited.OrganizationPoolId);
        Assert.Equal("Local đã sửa", edited.Plan.Name);
        Assert.Empty(await f.Db.LicensePlanOrganizationPools.ToArrayAsync());
        Assert.False(await f.Db.OrganizationSeatAssignments.AnyAsync());
    }

    [Fact]
    public async Task LocalIgnoresStalePoolButStillRequiresSepayToPublish()
    {
        await using var f = await Fixture.CreateAsync();
        var request = f.Request with { OrganizationPoolId = Guid.NewGuid(), ProductScope = "vietsub", VietsubCloudEnabled = false };
        f.Payments.Enabled = false;
        Assert.Equal("payments_unavailable", (await Assert.ThrowsAsync<AccountApiException>(() =>
            f.Service.CreateAsync(request, "admin", default))).Code);
        Assert.Empty(await f.Db.LicensePlans.ToArrayAsync());
        f.Payments.Enabled = true;
        Assert.Null((await f.Service.CreateAsync(request, "admin", default)).OrganizationPoolId);
        Assert.Empty(await f.Db.LicensePlanOrganizationPools.ToArrayAsync());
    }

    [Fact]
    public async Task CloudProfileCannotPublishWhenCloudIsDisabledAndNoPartialPlanIsCreated()
    {
        await using var f = await Fixture.CreateAsync();
        Assert.Equal("organization_not_ready", (await Assert.ThrowsAsync<AccountApiException>(() =>
            f.Service.CreateAsync(f.Request with { ProductScope="vietsub", VietsubCloudEnabled=true },"admin",default))).Code);
        Assert.Empty(await f.Db.LicensePlans.ToArrayAsync());
        Assert.Empty(await f.Db.LicensePlanOrganizationPools.ToArrayAsync());
    }

    [Fact]
    public async Task ExistingProductRightsCannotBeChangedThroughEditing()
    {
        await using var f = await Fixture.CreateAsync();
        var created = await f.Service.CreateAsync(f.Request,"admin",default);
        var editor = await f.Service.GetEditorAsync(created.Plan.LicensePlanId,default);
        var edit = f.Request with { CreationId=Guid.NewGuid(),PlanId=created.Plan.LicensePlanId,
            ExpectedUpdatedAtUtc=editor.Plan.UpdatedAtUtc,ExpectedMappingUpdatedAtUtc=editor.MappingUpdatedAtUtc,
            ProductScope="vietsub",VietsubCloudEnabled=false };
        Assert.Equal("license_plan_entitlements_immutable",(await Assert.ThrowsAsync<AccountApiException>(() =>
            f.Service.CreateAsync(edit,"admin",default))).Code);
        Assert.Equal(created.Plan.FeatureFlagsJson,(await f.Db.LicensePlans.AsNoTracking().SingleAsync()).FeatureFlagsJson);
    }
    [Fact]
    public async Task FirstPlanCanPublishAndReplayWithoutDuplicateOrReservingSeats()
    {
        await using var f = await Fixture.CreateAsync();
        var option = Assert.Single((await f.Service.GetSetupAsync(default)).Groups);
        Assert.True(option.Ready);
        Assert.Equal(10, option.AvailableSeats);
        Assert.Empty(await f.Db.LicensePlanOrganizationPools.ToListAsync());
        var first = await f.Service.CreateAsync(f.Request, "admin", default);
        var replay = await f.Service.CreateAsync(f.Request, "admin", default);
        Assert.True(first.Plan.IsPublic);
        Assert.True(first.Plan.IsActive);
        Assert.False(first.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal(first.Plan.LicensePlanId, replay.Plan.LicensePlanId);
        Assert.Single(await f.Db.LicensePlans.ToListAsync());
        Assert.Equal(first.Plan.LicensePlanId, (await f.Db.LicensePlanOrganizationPools.SingleAsync()).LicensePlanId);
        Assert.Equal(2, await f.Db.AccountAuditLogs.CountAsync());
        Assert.Equal(0, (await f.Db.OrganizationPoolOrganizations.SingleAsync()).ReservedSeatCount);
        Assert.Equal(300, (await f.Db.Organizations.SingleAsync()).MonthlyBudgetLimit);
    }

    [Fact]
    public async Task DraftNeedsNoGroupOrPaymentsAndCannotBeGrantedOrSold()
    {
        await using var f = await Fixture.CreateAsync();
        f.Payments.Enabled = false;
        var result = await f.Service.CreateAsync(f.Request with
            { Publish = false, OrganizationPoolId = null, DurationDays = null, PriceVnd = null }, "admin", default);
        Assert.False(result.Plan.IsActive);
        Assert.False(result.Plan.IsPublic);
        Assert.False(result.Published);
        Assert.Empty(await f.Db.LicensePlanOrganizationPools.ToListAsync());
    }

    [Theory]
    [InlineData("missing", "license_plan_pool_not_configured")]
    [InlineData("inactive", "organization_pool_inactive")]
    [InlineData("no-organizations", "pool_organization_missing")]
    [InlineData("auto-disabled", "organization_capacity_unavailable")]
    [InlineData("stored-not-ready", "organization_not_ready")]
    [InlineData("runtime-not-ready", "organization_not_ready")]
    [InlineData("full", "organization_capacity_unavailable")]
    [InlineData("organization-inactive", "organization_capacity_unavailable")]
    [InlineData("payments-disabled", "payments_unavailable")]
    public async Task PublishingRejectsEachBlockerWithoutPartialPlan(string condition, string code)
    {
        await using var f = await Fixture.CreateAsync();
        var request = f.Request;
        if (condition == "missing") request = request with { OrganizationPoolId = null };
        if (condition == "inactive") f.Pool.Status = "Inactive";
        if (condition == "no-organizations") f.Db.OrganizationPoolOrganizations.Remove(f.Link);
        if (condition == "auto-disabled") f.Link.IsAutoAssignmentEnabled = false;
        if (condition == "stored-not-ready") f.Link.IsReady = false;
        if (condition == "runtime-not-ready") f.Readiness.Ready = false;
        if (condition == "full") f.Link.ActiveSeatCount = 10;
        if (condition == "organization-inactive") f.Organization.Status = "Suspended";
        if (condition == "payments-disabled") f.Payments.Enabled = false;
        await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.CreateAsync(request, "admin", default));
        Assert.Equal(code, error.Code);
        Assert.Empty(await f.Db.LicensePlans.AsNoTracking().ToListAsync());
        Assert.Empty(await f.Db.LicensePlanOrganizationPools.AsNoTracking().ToListAsync());
        Assert.Empty(await f.Db.AccountAuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task RuntimeIsCheckedAgainAfterSetupAndSuccessfulReplaySurvivesDisabledPayments()
    {
        await using var f = await Fixture.CreateAsync();
        Assert.True(Assert.Single((await f.Service.GetSetupAsync(default)).Groups).Ready);
        f.Readiness.Ready = false;
        var error = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.CreateAsync(f.Request, "admin", default));
        Assert.Equal("organization_not_ready", error.Code);
        f.Readiness.Ready = true;
        var saved = await f.Service.CreateAsync(f.Request, "admin", default);
        f.Payments.Enabled = false; f.Readiness.Ready = false;
        var recovered = await f.Service.CreateAsync(f.Request, "admin", default);
        Assert.True(recovered.Replayed);
        Assert.Equal(saved.Plan.LicensePlanId, recovered.Plan.LicensePlanId);
    }

    [Fact]
    public async Task ChangedPayloadOrDifferentAdminCannotReplayOperation()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Service.CreateAsync(f.Request, "admin", default);
        foreach (var changed in new[] { f.Request with { Name = "Tên khác" }, f.Request with { Publish = false } })
            Assert.Equal("idempotency_key_conflict", (await Assert.ThrowsAsync<AccountApiException>(() =>
                f.Service.CreateAsync(changed, "admin", default))).Code);
        Assert.Equal("idempotency_key_conflict", (await Assert.ThrowsAsync<AccountApiException>(() =>
            f.Service.CreateAsync(f.Request, "other-admin", default))).Code);
        Assert.Single(await f.Db.LicensePlans.ToListAsync());
    }

    [Fact]
    public async Task MappingFailureRollsBackPlanAndBothAuditRecords()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER FailMapping BEFORE INSERT ON LicensePlanOrganizationPools
            BEGIN SELECT RAISE(ABORT, 'synthetic mapping failure'); END;
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Service.CreateAsync(f.Request, "admin", default));
        Assert.Empty(await f.Db.LicensePlans.AsNoTracking().ToListAsync());
        Assert.Empty(await f.Db.LicensePlanOrganizationPools.AsNoTracking().ToListAsync());
        Assert.Empty(await f.Db.AccountAuditLogs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task CloningPreservesEntitlementsWithoutModifyingSource()
    {
        await using var f = await Fixture.CreateAsync();
        var source = await new AdminLicenseService(f.Db, TimeProvider.System).CreatePlanAsync(
            new("source", "Gói nguồn", null, 3, 2, 0, 90, """{"customEntitlement":false}""", true), "admin", default);
        var copy = await f.Service.CreateAsync(f.Request with { SourcePlanId = source.LicensePlanId, MaxConcurrentSessions = 2 }, "admin", default);
        using var flags = JsonDocument.Parse(copy.Plan.FeatureFlagsJson!);
        Assert.False(flags.RootElement.GetProperty("customEntitlement").GetBoolean());
        Assert.Equal(2, copy.Plan.MaxConcurrentSessions);
        var original = await f.Db.LicensePlans.AsNoTracking().SingleAsync(x => x.LicensePlanId == source.LicensePlanId);
        Assert.Equal(90, original.DefaultDurationDays);
        Assert.Equal("Gói nguồn", original.Name);
    }

    [Theory]
    [InlineData(null, 30)]
    [InlineData(70000, null)]
    [InlineData(0, 30)]
    public async Task InvalidSalesFieldsNeverPersist(int? price, int? days)
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.CreateAsync(
            f.Request with { PriceVnd = price, DurationDays = days }, "admin", default));
        Assert.Empty(await f.Db.LicensePlans.ToListAsync());
    }

    [Fact]
    public void EndpointIsAdminOnlyAndContractRejectsCredentialOrUnknownFields()
    {
        var authorization = Assert.Single(typeof(AdminLicensePlanComposerController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("Admin", authorization.Roles);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ComposeLicensePlanRequest>(
            """{"CreationId":"00000000-0000-0000-0000-000000000001","Name":"Gói","ProviderKey":"fake"}"""));
    }

    [Fact]
    public async Task EditUsesCurrentPlanAndMappingAndReplaysWithoutChangingCodeOrEntitlements()
    {
        await using var f = await Fixture.CreateAsync();
        var created = await f.Service.CreateAsync(f.Request, "admin", default);
        var editor = await f.Service.GetEditorAsync(created.Plan.LicensePlanId, default);
        Assert.Equal(f.Pool.OrganizationPoolId, editor.OrganizationPoolId);
        var update = f.Request with { CreationId = Guid.NewGuid(), PlanId = created.Plan.LicensePlanId,
            ExpectedUpdatedAtUtc = editor.Plan.UpdatedAtUtc, ExpectedMappingUpdatedAtUtc = editor.MappingUpdatedAtUtc,
            Name = "Gói đã sửa", IsActive = true, MemberMonthlyAiBudget = 15 };
        var saved = await f.Service.CreateAsync(update, "admin", default);
        var replay = await f.Service.CreateAsync(update, "admin", default);
        Assert.Equal(created.Plan.LicensePlanId, saved.Plan.LicensePlanId);
        Assert.Equal(created.Plan.PlanCode, saved.Plan.PlanCode);
        Assert.Equal(created.Plan.FeatureFlagsJson, saved.Plan.FeatureFlagsJson);
        Assert.True(replay.Replayed);
        Assert.Single(await f.Db.LicensePlans.ToListAsync());
        Assert.Equal(15, (await f.Db.LicensePlanOrganizationPools.SingleAsync()).DefaultMemberMonthlyBudgetLimit);
        Assert.Equal(4, await f.Db.AccountAuditLogs.CountAsync());
        Assert.Equal("idempotency_key_conflict", (await Assert.ThrowsAsync<AccountApiException>(() =>
            f.Service.CreateAsync(update with { Name = "Nội dung khác" }, "admin", default))).Code);
        Assert.Equal("idempotency_key_conflict", (await Assert.ThrowsAsync<AccountApiException>(() =>
            f.Service.CreateAsync(update, "other-admin", default))).Code);
    }

    [Fact]
    public async Task EditRejectsChangedVersionAndUnavailableSalesWithoutUpdatingPlan()
    {
        await using var f = await Fixture.CreateAsync();
        var created = await f.Service.CreateAsync(f.Request, "admin", default);
        var update = f.Request with { CreationId = Guid.NewGuid(), PlanId = created.Plan.LicensePlanId,
            ExpectedUpdatedAtUtc = created.Plan.UpdatedAtUtc.AddDays(-1), ExpectedMappingUpdatedAtUtc = (await f.Service.GetEditorAsync(created.Plan.LicensePlanId, default)).MappingUpdatedAtUtc,
            Name = "Không được lưu" };
        Assert.Equal("license_plan_changed", (await Assert.ThrowsAsync<AccountApiException>(() =>
            f.Service.CreateAsync(update, "admin", default))).Code);
        f.Readiness.Ready = false;
        Assert.Equal("organization_not_ready", (await Assert.ThrowsAsync<AccountApiException>(() =>
            f.Service.CreateAsync(update with { ExpectedUpdatedAtUtc = created.Plan.UpdatedAtUtc }, "admin", default))).Code);
        Assert.Equal(created.Plan.Name, (await f.Db.LicensePlans.AsNoTracking().SingleAsync()).Name);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task EditCanKeepPrivateGrantPauseSalesOrDraftWithoutPayments(bool publish, bool active)
    {
        await using var f = await Fixture.CreateAsync();
        var created = await f.Service.CreateAsync(f.Request, "admin", default);
        f.Payments.Enabled = false;
        var saved = await f.Service.CreateAsync(f.Request with { CreationId = Guid.NewGuid(), PlanId = created.Plan.LicensePlanId,
            ExpectedUpdatedAtUtc = created.Plan.UpdatedAtUtc, ExpectedMappingUpdatedAtUtc = (await f.Service.GetEditorAsync(created.Plan.LicensePlanId, default)).MappingUpdatedAtUtc,
            Publish = publish, IsActive = active }, "admin", default);
        Assert.Equal(publish, saved.Plan.IsPublic);
        Assert.Equal(active, saved.Plan.IsActive);
        Assert.Equal(created.Plan.LicensePlanId, saved.Plan.LicensePlanId);
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("Reserved")]
    [InlineData("Scheduled")]
    public async Task EditCannotRemoveMappingWhileCustomerSeatsStillExist(string status)
    {
        await using var f = await Fixture.CreateAsync();
        var created = await f.Service.CreateAsync(f.Request, "admin", default);
        var editor = await f.Service.GetEditorAsync(created.Plan.LicensePlanId, default);
        await f.Db.Database.ExecuteSqlRawAsync("INSERT INTO OrganizationSeatAssignments VALUES ({0},{1},{2})", Guid.NewGuid(), created.Plan.LicensePlanId, status);
        var error = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.CreateAsync(f.Request with
            { CreationId = Guid.NewGuid(), PlanId = created.Plan.LicensePlanId, ExpectedUpdatedAtUtc = created.Plan.UpdatedAtUtc,
                ExpectedMappingUpdatedAtUtc = editor.MappingUpdatedAtUtc,
                OrganizationPoolId = null, Publish = false }, "admin", default));
        Assert.Equal("license_plan_pool_in_use", error.Code);
        Assert.Single(await f.Db.LicensePlanOrganizationPools.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task EditMappingFailureRollsBackPlanAndAudits()
    {
        await using var f = await Fixture.CreateAsync();
        var created = await f.Service.CreateAsync(f.Request, "admin", default);
        var editor = await f.Service.GetEditorAsync(created.Plan.LicensePlanId, default);
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER FailEdit BEFORE UPDATE ON LicensePlanOrganizationPools BEGIN SELECT RAISE(ABORT, 'synthetic edit failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Service.CreateAsync(f.Request with { CreationId = Guid.NewGuid(),
            PlanId = created.Plan.LicensePlanId, ExpectedUpdatedAtUtc = created.Plan.UpdatedAtUtc,
            ExpectedMappingUpdatedAtUtc = editor.MappingUpdatedAtUtc,
            Name = "Không lưu một phần", MemberMonthlyAiBudget = 4 }, "admin", default));
        Assert.Equal(created.Plan.Name, (await f.Db.LicensePlans.AsNoTracking().SingleAsync()).Name);
        Assert.Equal(2, await f.Db.AccountAuditLogs.CountAsync());
    }

    [Fact]
    public void OptionalEditFieldsDoNotChangeOriginalCreationHashPayload()
    {
        var json = JsonSerializer.Serialize(new ComposeLicensePlanRequest(Guid.NewGuid(), "Gói", 30, 50000, 1, null, false), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("planId", json);
        Assert.DoesNotContain("expectedUpdatedAtUtc", json);
        Assert.DoesNotContain("isActive", json);
    }

    [Fact]
    public async Task EditRejectsMappingChangedByAnotherAdmin()
    {
        await using var f = await Fixture.CreateAsync();
        var created = await f.Service.CreateAsync(f.Request, "admin", default);
        var editor = await f.Service.GetEditorAsync(created.Plan.LicensePlanId, default);
        var mapping = await f.Db.LicensePlanOrganizationPools.SingleAsync();
        mapping.UpdatedAtUtc = mapping.UpdatedAtUtc.AddSeconds(1);
        mapping.DefaultMemberMonthlyBudgetLimit = 22;
        await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.CreateAsync(f.Request with
            { CreationId = Guid.NewGuid(), PlanId = created.Plan.LicensePlanId, ExpectedUpdatedAtUtc = created.Plan.UpdatedAtUtc,
                ExpectedMappingUpdatedAtUtc = editor.MappingUpdatedAtUtc, Name = "Không ghi đè" }, "admin", default));
        Assert.Equal("license_plan_mapping_changed", error.Code);
        Assert.Equal(22, (await f.Db.LicensePlanOrganizationPools.AsNoTracking().SingleAsync()).DefaultMemberMonthlyBudgetLimit);
    }

    [Fact]
    public async Task CreationReplayFindsOriginalPlanAfterLegacyApiRenamesItsCode()
    {
        await using var f = await Fixture.CreateAsync();
        var created = await f.Service.CreateAsync(f.Request, "admin", default);
        var plan = await f.Db.LicensePlans.SingleAsync();
        plan.PlanCode = "renamed-by-compatible-api";
        await f.Db.SaveChangesAsync();
        var replay = await f.Service.CreateAsync(f.Request, "admin", default);
        Assert.True(replay.Replayed);
        Assert.Equal(created.Plan.LicensePlanId, replay.Plan.LicensePlanId);
        Assert.Single(await f.Db.LicensePlans.ToListAsync());
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Paid")]
    public async Task EditingDefaultQuotaWithOpenPaymentIsRejectedBeforePlanOrMappingChanges(string status)
    {
        await using var f = await Fixture.CreateAsync();
        var created = await f.Service.CreateAsync(f.Request, "admin", default);
        var editor = await f.Service.GetEditorAsync(created.Plan.LicensePlanId, default);
        await f.Db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO LicensePayments (LicensePaymentId, LicensePlanId, Status, ExpiresAtUtc) VALUES ({Guid.NewGuid()}, {created.Plan.LicensePlanId}, {status}, {DateTime.UtcNow.AddHours(1)})");
        var edit = f.Request with { CreationId = Guid.NewGuid(), PlanId = created.Plan.LicensePlanId,
            ExpectedUpdatedAtUtc = editor.Plan.UpdatedAtUtc, ExpectedMappingUpdatedAtUtc = editor.MappingUpdatedAtUtc,
            MemberMonthlyAiBudget = 30, Name = "Không ghi đè" };
        var error = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.CreateAsync(edit, "admin", default));
        Assert.Equal("license_plan_quota_in_use", error.Code);
        Assert.Null((await f.Db.LicensePlanOrganizationPools.AsNoTracking().SingleAsync()).DefaultMemberMonthlyBudgetLimit);
        Assert.Equal(created.Plan.Name, (await f.Db.LicensePlans.AsNoTracking().SingleAsync()).Name);
    }

    private sealed class Readiness : IOrganizationProvisioningReadinessEvaluator
    {
        public bool Ready { get; set; } = true;
        public Task<OrganizationProvisioningReadiness> EvaluateAsync(Guid organizationId, CancellationToken ct) =>
            Task.FromResult(new OrganizationProvisioningReadiness(Ready, Ready ? "Sẵn sàng." : "OpenAI text/credential/rate chưa sẵn sàng."));
    }
    private sealed class Payments : ISepayPaymentConfiguration
    {
        public bool Enabled { get; set; } = true;
        public Task<SepayPaymentOptions> GetOptionsAsync(CancellationToken ct) => Task.FromResult(new SepayPaymentOptions
            { Enabled = Enabled, ReceiverBankCode = "TEST", ReceiverAccountNumber = "11111111", ReceiverAccountName = "TEST HOLDER" });
    }
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public AccountDbContext Db { get; }
        public Readiness Readiness { get; } = new();
        public Payments Payments { get; } = new();
        public OrganizationPool Pool { get; } = new() { OrganizationPoolId = Guid.NewGuid(), Code = "test-pool", Name = "Nhóm thử", Status = "Active" };
        public Organization Organization { get; } = new() { OrganizationId = Guid.NewGuid(), Code = "test-org", Name = "Tổ chức thử", Status = "Active", MonthlyBudgetLimit = 300, CreatedByUserId = "admin" };
        public OrganizationPoolOrganization Link { get; }
        public ComposeLicensePlanRequest Request { get; }
        public AdminLicensePlanComposerService Service { get; }
        private Fixture(SqliteConnection connection)
        {
            this.connection = connection;
            Db = new(new DbContextOptionsBuilder<AccountDbContext>().UseSqlite(connection).Options);
            Link = new() { OrganizationPoolId = Pool.OrganizationPoolId, OrganizationId = Organization.OrganizationId, SeatCapacity = 10, IsAutoAssignmentEnabled = true, IsReady = true };
            Request = new(Guid.NewGuid(), "Gói 30 ngày", 30, 70000, 1, Pool.OrganizationPoolId, true);
            Service = new(Db, new AdminLicenseService(Db, TimeProvider.System), Readiness, Payments, TimeProvider.System);
        }
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var f = new Fixture(connection);
            await f.Db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE LicensePlans (LicensePlanId TEXT PRIMARY KEY, PlanCode TEXT NOT NULL UNIQUE, Name TEXT NOT NULL,
                Description TEXT, MaxActivatedDevices INTEGER NOT NULL, OfflineGraceHours INTEGER NOT NULL, DefaultDurationDays INTEGER,
                FeatureFlagsJson TEXT, SalePriceVnd TEXT, IsPublic INTEGER NOT NULL, DisplayOrder INTEGER NOT NULL,
                MarketingFeaturesJson TEXT, IsActive INTEGER NOT NULL, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL, RowVersion BLOB NOT NULL DEFAULT X'01');
                CREATE TABLE AccountAuditLogs (AccountAuditLogId INTEGER PRIMARY KEY AUTOINCREMENT, UserId TEXT, EventType TEXT NOT NULL,
                Succeeded INTEGER NOT NULL, IpAddress TEXT, UserAgent TEXT, CorrelationId TEXT, DetailsJson TEXT, OccurredAtUtc TEXT NOT NULL);
                CREATE TABLE OrganizationPools (OrganizationPoolId TEXT PRIMARY KEY, Code TEXT NOT NULL, Name TEXT NOT NULL,
                AllocationStrategy TEXT NOT NULL, Status TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL, RowVersion BLOB NOT NULL DEFAULT X'01');
                CREATE TABLE Organizations (OrganizationId TEXT PRIMARY KEY, Code TEXT NOT NULL, Name TEXT NOT NULL, Status TEXT NOT NULL,
                MonthlyBudgetLimit TEXT NOT NULL, CurrencyCode TEXT NOT NULL, CreatedByUserId TEXT NOT NULL,
                CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL, RowVersion BLOB NOT NULL DEFAULT X'01');
                CREATE TABLE OrganizationPoolOrganizations (OrganizationPoolId TEXT NOT NULL, OrganizationId TEXT NOT NULL,
                SeatCapacity INTEGER NOT NULL, ActiveSeatCount INTEGER NOT NULL, ReservedSeatCount INTEGER NOT NULL, Priority INTEGER NOT NULL,
                IsAutoAssignmentEnabled INTEGER NOT NULL, IsReady INTEGER NOT NULL, ReadinessMessage TEXT, CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL, RowVersion BLOB NOT NULL DEFAULT X'01', PRIMARY KEY (OrganizationPoolId, OrganizationId));
                CREATE TABLE LicensePlanOrganizationPools (LicensePlanId TEXT PRIMARY KEY REFERENCES LicensePlans(LicensePlanId),
                OrganizationPoolId TEXT NOT NULL REFERENCES OrganizationPools(OrganizationPoolId), DefaultMemberMonthlyBudgetLimit TEXT,
                IsActive INTEGER NOT NULL, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL, RowVersion BLOB NOT NULL DEFAULT X'01');
                CREATE TABLE OrganizationSeatAssignments (OrganizationSeatAssignmentId TEXT PRIMARY KEY, LicensePlanId TEXT NOT NULL, Status TEXT NOT NULL);
                CREATE TABLE LicensePayments (LicensePaymentId TEXT PRIMARY KEY, LicensePlanId TEXT NOT NULL, Status TEXT NOT NULL, ExpiresAtUtc TEXT NOT NULL, FulfilledAtUtc TEXT);
                """);
            f.Db.OrganizationPools.Add(f.Pool); f.Db.Organizations.Add(f.Organization); f.Db.OrganizationPoolOrganizations.Add(f.Link);
            await f.Db.SaveChangesAsync();
            return f;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
