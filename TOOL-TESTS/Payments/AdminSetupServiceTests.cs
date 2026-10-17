using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Domain.Providers;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.Payments;
using TOOL_SERVER.Providers;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_TESTS.Payments;

public sealed class AdminSetupServiceTests
{
    [Fact]
    public async Task FirstPlanPublishesAtomicallyAndReplaySurvivesChangedReadiness()
    {
        await using var f = await Fixture.CreateAsync();
        var d = await f.PreparedAsync();
        var check = await f.Service.ReadinessAsync(d.DraftId, "admin", default);
        Assert.True(check.Ready);
        var request = new AdminSetupPublishRequest(Guid.NewGuid(), d.Revision, true, check.Fingerprint);
        var saved = await f.Service.PublishAsync(d.DraftId, request, "admin", default);
        Assert.True(saved.Plan!.IsPublic);
        Assert.True(saved.Plan.IsActive);
        Assert.True((await f.Db.OrganizationPoolOrganizations.SingleAsync()).IsAutoAssignmentEnabled);
        Assert.Equal("Active", (await f.Db.OrganizationPools.SingleAsync()).Status);
        Assert.Empty(await f.Db.OrganizationSeatAssignments.ToListAsync());
        f.Payments.Enabled = false;
        var replay = await f.Service.PublishAsync(d.DraftId, request, "admin", default);
        Assert.True(replay.Operation.Replayed);
        Assert.Equal(saved.Plan.LicensePlanId, replay.Plan!.LicensePlanId);
        Assert.Single(await f.Db.LicensePlans.ToListAsync());
        Assert.Single(await f.Db.LicensePlanOrganizationPools.ToListAsync());
    }

    [Fact]
    public async Task DraftAndReadonlyChecksDoNotActivateGroupOrCreateBudgetPeriodEvenOnNewMonth()
    {
        await using var f = await Fixture.CreateAsync(); var d = await f.PreparedAsync();
        for (var n = 0; n < 2; n++)
        {
            Assert.True((await f.Service.ReadinessAsync(d.DraftId, "admin", default)).Ready);
            await f.Service.BootstrapAsync("admin", default);
            Assert.Empty(await f.Db.OrganizationBudgetPeriods.ToListAsync());
            Assert.False((await f.Db.OrganizationPoolOrganizations.SingleAsync()).IsAutoAssignmentEnabled);
            Assert.Equal("Inactive", (await f.Db.OrganizationPools.SingleAsync()).Status);
            f.Clock.Utc = f.Clock.Utc.AddMonths(1);
        }
        Assert.Empty(await f.Db.LicensePlans.ToListAsync());
    }

    [Fact]
    public async Task FailureInsideComposerRollsBackActivationPlanMappingAndReceipt()
    {
        await using var f = await Fixture.CreateAsync(); var d = await f.PreparedAsync();
        var check = await f.Service.ReadinessAsync(d.DraftId, "admin", default);
        f.Legacy.Ready = false;
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.PublishAsync(d.DraftId,
            new(id, d.Revision, true, check.Fingerprint), "admin", default));
        Assert.Equal("Inactive", (await f.Db.OrganizationPools.AsNoTracking().SingleAsync()).Status);
        Assert.False((await f.Db.OrganizationPoolOrganizations.AsNoTracking().SingleAsync()).IsAutoAssignmentEnabled);
        Assert.Empty(await f.Db.LicensePlans.ToListAsync());
        Assert.Empty(await f.Db.LicensePlanOrganizationPools.ToListAsync());
        Assert.False(await f.Db.AdminSetupOperations.AnyAsync(x => x.OperationId == id));
    }

    [Fact]
    public async Task MissingActualDefaultImagePriceAndZeroMemberBudgetFailClosed()
    {
        await using var f = await Fixture.CreateAsync(); var d = await f.PreparedAsync();
        var form = d.Form with { MemberBudget = 0 };
        d = await f.Service.SaveDraftAsync(d.DraftId, new(d.Revision, 3, form), "admin", default);
        var image = await f.Db.ProviderModels.SingleAsync(x => x.Modality == "Image");
        f.Db.CostRates.RemoveRange(await f.Db.CostRates.Where(x => x.ProviderModelId == image.ProviderModelId).ToArrayAsync());
        await f.Db.SaveChangesAsync();
        var check = await f.Service.ReadinessAsync(d.DraftId, "admin", default);
        Assert.False(check.Ready); Assert.False(check.AiReady);
        Assert.Contains(check.Checks, x => x.Code == "pricing_Image_InputToken" && !x.Ready);
        Assert.Contains(check.Checks, x => x.Code == "member_budget" && !x.Ready);
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.PublishAsync(d.DraftId, new(Guid.NewGuid(), d.Revision, true, check.Fingerprint), "admin", default));
        Assert.Empty(await f.Db.LicensePlans.ToListAsync());
    }

    [Fact]
    public async Task StaleTabOtherActorAndViewerAreRejectedBeforeProbe()
    {
        await using var f = await Fixture.CreateAsync(); var d = await f.PreparedAsync();
        var changed = await f.Service.SaveDraftAsync(d.DraftId, new(d.Revision, 2, d.Form), "admin", default);
        var stale = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.SaveDraftAsync(d.DraftId, new(d.Revision, 3, d.Form), "admin", default));
        Assert.Equal("draft_changed", stale.Code);
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.GetDraftAsync(d.DraftId, "other", default));
        var member = await f.Db.OrganizationMembers.SingleAsync(); member.Role = OrganizationMemberRoles.Viewer; await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ConnectAsync(d.DraftId,
            new(Guid.NewGuid(), changed.Revision, "openai", "synthetic-only-key", 1), "admin", default));
        Assert.Equal(0, f.Tester.Calls);
    }

    [Fact]
    public async Task CredentialFailureKeepsOldKeyAndSuccessfulReplayDoesNotRotateAgainOrLeakSecret()
    {
        await using var f = await Fixture.CreateAsync(); var d = await f.PreparedAsync();
        f.Tester.Fail = true;
        var request = new AdminSetupConnectionRequest(Guid.NewGuid(), d.Revision, "openai", "synthetic-new-key", 1);
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ConnectAsync(d.DraftId, request, "admin", default));
        Assert.Equal(2, await f.Db.OrganizationProviderCredentials.CountAsync());
        Assert.Equal(2, await f.Db.OrganizationProviderCredentials.CountAsync(x => x.Status == "Active"));
        f.Tester.Fail = false;
        var saved = await f.Service.ConnectAsync(d.DraftId, request, "admin", default);
        var replay = await f.Service.ConnectAsync(d.DraftId, request, "admin", default);
        Assert.True(replay.Operation.Replayed); Assert.Equal(2, f.Tester.Calls);
        Assert.Equal(3, await f.Db.OrganizationProviderCredentials.CountAsync());
        Assert.DoesNotContain(request.ApiKey, JsonSerializer.Serialize(saved));
        Assert.DoesNotContain(request.ApiKey, (await f.Db.AdminSetupDrafts.SingleAsync()).FormJson);
        Assert.All(await f.Db.AccountAuditLogs.ToListAsync(), x => Assert.DoesNotContain(request.ApiKey, x.DetailsJson ?? ""));
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ConnectAsync(d.DraftId, request with { ApiKey = "different-secret-key" }, "admin", default));
    }

    [Fact]
    public async Task PricingNeedsConfirmationAndKeepsOriginalUnitWhenDisplayedValueIsUnchanged()
    {
        await using var f = await Fixture.CreateAsync(); var d = await f.PreparedAsync();
        var original = await f.Db.CostRates.FirstAsync(x => x.UsageType == "InputToken"); original.Unit = "1KTokens"; original.UnitPrice = .002m; await f.Db.SaveChangesAsync();
        var check = await f.Service.ReadinessAsync(d.DraftId, "admin", default);
        var same = await f.Service.SavePricingAsync(d.DraftId, new(Guid.NewGuid(), d.Revision, false, check.Rates), "admin", default);
        Assert.Equal(5, await f.Db.CostRates.CountAsync());
        Assert.Equal("1KTokens", (await f.Db.CostRates.SingleAsync(x => x.CostRateId == original.CostRateId)).Unit);
        var changed = check.Rates.Select((r, n) => n == 0 ? r with { Price = 3 } : r).ToArray();
        await Assert.ThrowsAsync<AccountApiException>(() => f.Service.SavePricingAsync(d.DraftId, new(Guid.NewGuid(), same.Draft.Revision, false, changed), "admin", default));
        Assert.Equal(5, await f.Db.CostRates.CountAsync());
        var saved = await f.Service.SavePricingAsync(d.DraftId, new(Guid.NewGuid(), same.Draft.Revision, true, changed), "admin", default);
        Assert.Equal(6, await f.Db.CostRates.CountAsync());
        Assert.Equal(5, await f.Db.CostRates.CountAsync(x => x.IsActive));
    }

    [Fact]
    public async Task DraftPlanWithoutSepayIsPrivateAndInactiveAndCannotActivateGroup()
    {
        await using var f = await Fixture.CreateAsync(); var d = await f.PreparedAsync(); f.Payments.Enabled = false;
        var result = await f.Service.PublishAsync(d.DraftId, new(Guid.NewGuid(), d.Revision, false, ""), "admin", default);
        Assert.False(result.Plan!.IsActive); Assert.False(result.Plan.IsPublic);
        Assert.False((await f.Db.OrganizationPoolOrganizations.SingleAsync()).IsAutoAssignmentEnabled);
    }

    [Fact]
    public async Task ChangedCapacityOrFalVariantInvalidatesPublishAndBudgetCannotDropBelowUsage()
    {
        await using var f = await Fixture.CreateAsync(); var d = await f.PreparedAsync();
        var check = await f.Service.ReadinessAsync(d.DraftId, "admin", default);
        var link = await f.Db.OrganizationPoolOrganizations.SingleAsync(); link.SeatCapacity++; await f.Db.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.PublishAsync(d.DraftId, new(Guid.NewGuid(), d.Revision, true, check.Fingerprint), "admin", default));
        Assert.Equal("setup_changed", conflict.Code);
        link = await f.Db.OrganizationPoolOrganizations.SingleAsync(); link.SeatCapacity = d.Form.Capacity; await f.Db.SaveChangesAsync();
        f.Db.OrganizationBudgetPeriods.Add(new() { OrganizationBudgetPeriodId = Guid.NewGuid(), OrganizationId = d.OrganizationId!.Value,
            StartsAtUtc = new DateTime(2026, 10, 1), EndsAtUtc = new DateTime(2026, 11, 1), HardLimit = 100, ActualCost = 70, ReservedCost = 20 });
        await f.Db.SaveChangesAsync();
        d = await f.Service.SaveDraftAsync(d.DraftId, new(d.Revision, 2, d.Form with { OrganizationBudget = 80 }), "admin", default);
        var budget = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ConfigureAsync(d.DraftId, new(Guid.NewGuid(), d.Revision), "admin", default));
        Assert.Equal("budget_below_usage", budget.Code);
    }

    [Fact]
    public async Task ConcurrentOrganizationEditCannotBeOverwrittenByUnchangedDraftRevision()
    {
        await using var f = await Fixture.CreateAsync(); var d = await f.PreparedAsync();
        var org = await f.Db.Organizations.SingleAsync(); org.MonthlyBudgetLimit = 150; await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.ConfigureAsync(d.DraftId, new(Guid.NewGuid(), d.Revision), "admin", default));
        Assert.Equal("organization_configuration_changed", error.Code);
        Assert.Equal(150, (await f.Db.Organizations.AsNoTracking().SingleAsync()).MonthlyBudgetLimit);
        Assert.False((await f.Service.ReadinessAsync(d.DraftId, "admin", default)).Ready);
    }

    [Fact]
    public async Task NewlySelectedGlobalDefaultAndWrongFalEndpointCannotBorrowOtherModelRates()
    {
        await using var f = await Fixture.CreateAsync(); var d = await f.PreparedAsync();
        var catalog = await f.Service.ReadinessAsync(d.DraftId, "admin", default);
        var old = await f.Db.ProviderModels.SingleAsync(x => x.Modality == "Text"); old.IsDefault = false;
        var replacement = new AiProviderModel { ProviderModelId = Guid.NewGuid(), ProviderId = old.ProviderId, ModelCode = "new-default", DisplayName = "New default", Modality = "Text", IsEnabled = true, IsDefault = true, UpdatedAtUtc = f.Clock.Utc.UtcDateTime };
        f.Db.ProviderModels.Add(replacement);
        var videoRate = await f.Db.CostRates.SingleAsync(x => x.UsageType == "VideoSecond");
        videoRate.MetadataJson = """{"resolution":"720p","nativeAudio":true,"endpointId":"fal-ai/veo3.1/image-to-video"}""";
        await f.Db.SaveChangesAsync();
        var check = await f.Service.ReadinessAsync(d.DraftId, "admin", default);
        Assert.False(check.Ready);
        Assert.Equal(replacement.ProviderModelId, check.Models.Single(x => x.Modality == "Text").ModelId);
        Assert.Contains(check.Checks, x => x.Code == "pricing_Text_InputToken" && !x.Ready);
        Assert.Contains(check.Checks, x => x.Code == "pricing_Video_VideoSecond" && !x.Ready);
        var conflict = await Assert.ThrowsAsync<AccountApiException>(() => f.Service.SavePricingAsync(d.DraftId, new(Guid.NewGuid(), d.Revision, true, catalog.Rates), "admin", default));
        Assert.Equal("pricing_model_changed", conflict.Code);
        Assert.Equal(5, await f.Db.CostRates.CountAsync());
    }

    [Fact]
    public async Task WizardPlanRechecksImagePricingBeforeCheckoutWithoutChangingLegacyPlanRules()
    {
        await using var f = await Fixture.CreateAsync(); var d = await f.PreparedAsync();
        var check = await f.Service.ReadinessAsync(d.DraftId, "admin", default);
        var published = await f.Service.PublishAsync(d.DraftId, new(Guid.NewGuid(), d.Revision, true, check.Fingerprint), "admin", default);
        var legacy = await new AdminLicenseService(f.Db, f.Clock).CreatePlanAsync(new("legacy", "Gói cũ", null, 1, 1, 0, 30, null, true, 70000, true), "admin", default);
        f.Db.LicensePlanOrganizationPools.Add(new() { LicensePlanId = legacy.LicensePlanId, OrganizationPoolId = d.PoolId!.Value, IsActive = true });
        await f.Db.SaveChangesAsync();
        var seats = new OrganizationSeatProvisioningService(f.Db, NullLogger<OrganizationSeatProvisioningService>.Instance, f.Legacy, f.Clock);
        var ids = new[] { published.Plan!.LicensePlanId, legacy.LicensePlanId };
        Assert.All((await seats.GetAvailabilityAsync(ids, default)).Values, x => Assert.True(x.IsAvailable));
        var image = await f.Db.ProviderModels.SingleAsync(x => x.Modality == "Image");
        f.Db.CostRates.RemoveRange(await f.Db.CostRates.Where(x => x.ProviderModelId == image.ProviderModelId).ToListAsync()); await f.Db.SaveChangesAsync();
        var offers = await seats.GetAvailabilityAsync(ids, default);
        Assert.False(offers[published.Plan.LicensePlanId].IsAvailable);
        Assert.True(offers[legacy.LicensePlanId].IsAvailable);
        await Assert.ThrowsAsync<AccountApiException>(() => seats.ReserveAsync(new LicensePayment { LicensePaymentId = Guid.NewGuid(), LicensePlanId = published.Plan.LicensePlanId,
            UserId = "customer", CreatedAtUtc = f.Clock.Utc.UtcDateTime, ExpiresAtUtc = f.Clock.Utc.UtcDateTime.AddMinutes(10) }, f.Clock.Utc.UtcDateTime, default));
        Assert.Empty(await f.Db.OrganizationSeatAssignments.ToListAsync());
        Assert.Equal(0, (await f.Db.OrganizationPoolOrganizations.SingleAsync()).ReservedSeatCount);
    }

    private sealed class Clock : TimeProvider { public DateTimeOffset Utc = new(2026, 10, 14, 0, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Utc; }
    private sealed class Tester : IOrganizationProviderCredentialTester
    { public int Calls; public bool Fail; public Task TestAsync(string p, string? u, string k, CancellationToken ct) { Calls++; if (Fail) throw new AccountApiException(422, "test_failed", "Kiểm tra thất bại."); return Task.CompletedTask; } }
    private sealed class Protector : IProviderCredentialProtector
    { public string Protect(string k) => "test-protected:" + k; public string Unprotect(string k) => k[15..]; }
    private sealed class Payments : ISepayPaymentConfiguration
    { public bool Enabled = true; public Task<SepayPaymentOptions> GetOptionsAsync(CancellationToken ct) => Task.FromResult(new SepayPaymentOptions { Enabled = Enabled, ReceiverBankCode = "TEST", ReceiverAccountNumber = "11111111", ReceiverAccountName = "TEST" }); }
    private sealed class Legacy : IOrganizationProvisioningReadinessEvaluator
    { public bool Ready = true; public Task<OrganizationProvisioningReadiness> EvaluateAsync(Guid id, CancellationToken ct) => Task.FromResult(new OrganizationProvisioningReadiness(Ready, "Test readiness")); }
    private sealed class Fixture : IAsyncDisposable
    {
        private SqliteConnection connection = null!;
        public AccountDbContext Db = null!; public AdminSetupService Service = null!;
        public Clock Clock = new(); public Tester Tester = new(); public Payments Payments = new(); public Legacy Legacy = new();
        public Guid Video;
        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture { connection = new SqliteConnection("Data Source=:memory:") }; await f.connection.OpenAsync();
            f.Db = new(new DbContextOptionsBuilder<AccountDbContext>().UseSqlite(f.connection).Options);
            var script = f.Db.Database.GenerateCreateScript().Replace("nvarchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase)
                .Replace("BLOB NOT NULL", "BLOB NOT NULL DEFAULT X'01'", StringComparison.Ordinal);
            script = Regex.Replace(script, @"DEFAULT \((NEWSEQUENTIALID|SYSUTCDATETIME)\(\)\)", "", RegexOptions.IgnoreCase);
            await f.Db.Database.ExecuteSqlRawAsync(script);
            f.Db.Users.Add(new ApplicationUser { Id = "admin", UserName = "admin@example.test", DisplayName = "Fixture", CreatedAtUtc = f.Clock.Utc.UtcDateTime, UpdatedAtUtc = f.Clock.Utc.UtcDateTime });
            var open = new AiProvider { ProviderId = Guid.NewGuid(), ProviderCode = "openai", DisplayName = "OpenAI", IsEnabled = true, BaseUrl = "https://api.openai.com/v1/" };
            var fal = new AiProvider { ProviderId = Guid.NewGuid(), ProviderCode = "fal", DisplayName = "Fal", IsEnabled = true, BaseUrl = "https://queue.fal.run/" };
            f.Db.Providers.AddRange(open, fal);
            foreach (var (modality, code, provider) in new[] { ("Text", "test-text", open), ("Image", "test-image", open), ("Video", "fal-ai/veo3.1/fast/image-to-video", fal) })
            {
                var m = new AiProviderModel { ProviderModelId = Guid.NewGuid(), ProviderId = provider.ProviderId, ModelCode = code, DisplayName = code, Modality = modality, IsEnabled = true, IsDefault = true };
                f.Db.ProviderModels.Add(m); if (modality == "Video") f.Video = m.ProviderModelId;
                foreach (var usage in modality == "Video" ? new[] { "VideoSecond" } : new[] { "InputToken", "OutputToken" })
                    f.Db.CostRates.Add(new() { CostRateId = Guid.NewGuid(), ProviderModelId = m.ProviderModelId, UsageType = usage, Unit = modality == "Video" ? "Second" : "MillionTokens", UnitPrice = modality == "Video" ? .1m : 2,
                        CurrencyCode = "USD", IsActive = true, EffectiveFromUtc = f.Clock.Utc.UtcDateTime.AddDays(-1), MetadataJson = modality == "Video" ? JsonSerializer.Serialize(new { resolution = "720p", nativeAudio = true, endpointId = code }) : null });
            }
            await f.Db.SaveChangesAsync();
            var composer = new AdminLicensePlanComposerService(f.Db, new AdminLicenseService(f.Db, f.Clock), f.Legacy, f.Payments, f.Clock);
            f.Service = new(f.Db, composer, f.Tester, new Protector(), f.Payments, f.Clock); return f;
        }
        public async Task<AdminSetupDraft> PreparedAsync()
        {
            var d = await Service.CreateDraftAsync(new(Guid.NewGuid()), "admin", default);
            d = await Service.SaveDraftAsync(d.DraftId, new(d.Revision, 1, new("Studio Việt", Video, 100, 10, 20, "Gói tháng", 70000)), "admin", default);
            d = (await Service.PrepareOrganizationAsync(d.DraftId, new(Guid.NewGuid(), d.Revision), "admin", default)).Draft;
            foreach (var p in await Db.Providers.ToListAsync()) Db.OrganizationProviderCredentials.Add(new() { OrganizationProviderCredentialId = Guid.NewGuid(), OrganizationId = d.OrganizationId!.Value, ProviderId = p.ProviderId,
                Version = 1, Name = "Fixture", EncryptedPayload = "test-protected:synthetic-existing-key", SecretHint = "…-key", CreatedByUserId = "admin", Status = "Active" });
            await Db.SaveChangesAsync();
            return (await Service.ConfigureAsync(d.DraftId, new(Guid.NewGuid(), d.Revision), "admin", default)).Draft;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
