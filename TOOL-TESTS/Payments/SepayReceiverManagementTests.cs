using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Controllers;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Payments;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_TESTS.Payments;

public sealed class SepayReceiverManagementTests
{
    [Fact]
    public async Task OffersFollowSqlReceiverAndSalesSwitchWithoutCreatingPaymentOrLicense()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Empty(await fixture.Payments.GetOffersAsync(default));

        var state = await fixture.AddAccountAsync("VCB", "11111111", true);
        Assert.Empty(await fixture.Payments.GetOffersAsync(default));
        var blocked = await Assert.ThrowsAsync<AccountApiException>(() =>
            fixture.PurchaseAsync("sales-disabled-request"));
        Assert.Equal(503, blocked.StatusCode);
        Assert.Equal("payments_unavailable", blocked.Code);

        state = await fixture.Admin.SetEnabledAsync(new(true, state.Revision), "admin-user", default);
        Assert.Equal(fixture.PlanId,
            Assert.Single(await fixture.Payments.GetOffersAsync(default)).LicensePlanId);

        await fixture.Admin.SetEnabledAsync(new(false, state.Revision), "admin-user", default);
        Assert.Empty(await fixture.Payments.GetOffersAsync(default));
        Assert.Empty(await fixture.Db.LicensePayments.ToListAsync());
        Assert.Empty(await fixture.Db.UserLicenses.ToListAsync());
    }

    [Fact]
    public async Task SqlSelectionOverridesLegacyConfigurationAndSurvivesNewServiceScope()
    {
        await using var fixture = await Fixture.CreateAsync();
        var state = await fixture.AddAccountAsync("VCB", "11111111", true);
        Assert.False(state.Enabled);
        state = await fixture.Admin.SetEnabledAsync(new(true, state.Revision), "admin-user", default);
        var restored = new SepayPaymentAdminService(fixture.Db, Options.Create(fixture.Defaults), TimeProvider.System);
        var options = await restored.GetOptionsAsync(default);
        Assert.True(options.IsReady);
        Assert.Equal("11111111", options.ReceiverAccountNumber);
        Assert.Equal(state.SelectedAccountId, (await restored.GetAsync(default)).SelectedAccountId);
        Assert.Equal(2, await fixture.Db.AccountAuditLogs.CountAsync());
        Assert.DoesNotContain("11111111", string.Join(' ', await fixture.Db.AccountAuditLogs.Select(x => x.DetailsJson).ToListAsync()));
    }

    [Fact]
    public async Task EnablingWithoutReceiverFailsWithoutChangingRevisionOrAudit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.Admin.GetAsync(default);
        var error = await Assert.ThrowsAsync<AccountApiException>(() => fixture.Admin.SetEnabledAsync(
            new(true, before.Revision), "admin-user", default));
        Assert.Equal("invalid_sepay_settings", error.Code);
        var after = await fixture.Admin.GetAsync(default);
        Assert.False(after.Enabled);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Empty(after.Accounts);
        Assert.Empty(await fixture.Db.AccountAuditLogs.ToListAsync());
        Assert.False((await fixture.Admin.GetOptionsAsync(default)).IsReady);
    }

    [Theory]
    [InlineData("bad bank!", "11111111", "HOLDER")]
    [InlineData("VCB", "123", "HOLDER")]
    [InlineData("VCB", "11111111", "   ")]
    [InlineData("VCB", "11111111", "HOLDER\n")]
    public async Task InvalidReceiverCannotBePersisted(string bank, string number, string holder)
    {
        await using var fixture = await Fixture.CreateAsync();
        var state = await fixture.Admin.GetAsync(default);
        var error = await Assert.ThrowsAsync<AccountApiException>(() => fixture.Admin.SaveAccountAsync(null,
            Request(state, bank, number) with { AccountName = holder }, "admin-user", default));
        Assert.Equal("invalid_sepay_settings", error.Code);
        Assert.Empty(await fixture.Db.SepayReceivingAccounts.ToListAsync());
    }

    [Fact]
    public async Task DuplicateBankAccountAndStaleAdminCannotOverwriteSelection()
    {
        await using var fixture = await Fixture.CreateAsync();
        var initial = await fixture.Admin.GetAsync(default);
        var first = await fixture.AddAccountAsync("VCB", "11111111", true);
        var stale = await Assert.ThrowsAsync<AccountApiException>(() => fixture.Admin.SetEnabledAsync(
            new(true, initial.Revision), "another-admin", default));
        Assert.Equal("sepay_settings_changed", stale.Code);
        var duplicate = await Assert.ThrowsAsync<AccountApiException>(() => fixture.Admin.SaveAccountAsync(null,
            Request(first, "vcb", "11111111"), "admin-user", default));
        Assert.Equal("sepay_account_exists", duplicate.Code);
        Assert.Single(await fixture.Db.SepayReceivingAccounts.ToListAsync());
        Assert.Equal(first.SelectedAccountId, (await fixture.Admin.GetAsync(default)).SelectedAccountId);
    }

    [Fact]
    public async Task StaleAccountRevisionAndUnknownSelectionAreRejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var state = await fixture.AddAccountAsync("VCB", "11111111", true);
        var account = Assert.Single(state.Accounts);
        var stale = await Assert.ThrowsAsync<AccountApiException>(() => fixture.Admin.SaveAccountAsync(account.AccountId,
            Request(state, "VCB", "22222222") with { Revision = Guid.NewGuid() }, "admin-user", default));
        Assert.Equal("sepay_settings_changed", stale.Code);
        var missing = await Assert.ThrowsAsync<AccountApiException>(() => fixture.Admin.SelectAccountAsync(
            new(Guid.NewGuid(), state.Revision), "admin-user", default));
        Assert.Equal("sepay_account_not_found", missing.Code);
        Assert.Equal("11111111", (await fixture.Admin.GetOptionsAsync(default)).ReceiverAccountNumber);
    }

    [Fact]
    public async Task ReceiverChangeKeepsOldQrAndAcceptsOldWebhookWhileNewOrdersUseNewReceiver()
    {
        await using var fixture = await Fixture.CreateAsync();
        var state = await fixture.AddAccountAsync("VCB", "11111111", true);
        state = await fixture.Admin.SetEnabledAsync(new(true, state.Revision), "admin-user", default);
        var oldOrder = await fixture.PurchaseAsync("old-order-request");
        state = await fixture.AddAccountAsync("TCB", "22222222", true);
        var newOrder = await fixture.Payments.CreateOrReuseAsync("buyer-2",
            new(fixture.PlanId, "new-order-request"), default);
        Assert.Equal("11111111", oldOrder.ReceiverAccountNumber);
        Assert.Equal("22222222", newOrder.ReceiverAccountNumber);
        var replay = await fixture.PurchaseAsync("old-order-request");
        Assert.Equal(oldOrder.QrImageUrl, replay.QrImageUrl);
        Assert.Equal("11111111", replay.ReceiverAccountNumber);
        await fixture.Payments.HandleWebhookAsync(Webhook(oldOrder, "22222222"), default);
        Assert.Empty(await fixture.Db.UserLicenses.ToListAsync());
        await fixture.Payments.HandleWebhookAsync(Webhook(oldOrder, "11111111"), default);
        await fixture.Payments.HandleWebhookAsync(Webhook(oldOrder, "11111111"), default);
        Assert.Single(await fixture.Db.UserLicenses.ToListAsync());
        Assert.Equal(LicensePaymentStatuses.Fulfilled, (await fixture.Payments.GetStatusAsync("buyer-1", oldOrder.OrderCode, default)).Status);
    }

    [Fact]
    public async Task EditingReceiverAndDisablingSalesStillSettlesOutstandingOrder()
    {
        await using var fixture = await Fixture.CreateAsync();
        var state = await fixture.AddAccountAsync("VCB", "11111111", true);
        state = await fixture.Admin.SetEnabledAsync(new(true, state.Revision), "admin-user", default);
        var order = await fixture.PurchaseAsync("before-edit-request");
        var account = Assert.Single(state.Accounts);
        state = await fixture.Admin.SaveAccountAsync(account.AccountId,
            Request(state, "VCB", "33333333") with { Revision = account.Revision }, "admin-user", default);
        state = await fixture.Admin.SetEnabledAsync(new(false, state.Revision), "admin-user", default);
        var blocked = await Assert.ThrowsAsync<AccountApiException>(() => fixture.Payments.CreateOrReuseAsync(
            "buyer-2", new(fixture.PlanId, "disabled-order-request"), default));
        Assert.Equal("payments_unavailable", blocked.Code);
        Assert.Equal("11111111", (await fixture.Payments.GetCurrentAsync("buyer-1", default)).Payment!.ReceiverAccountNumber);
        await fixture.Payments.HandleWebhookAsync(Webhook(order, "11111111"), default);
        Assert.Single(await fixture.Db.UserLicenses.ToListAsync());
    }

    [Fact]
    public async Task AuditFailureRollsBackAccountSelectionAndRevisionAtomically()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AccountDbContext>().UseSqlite(connection).Options;
        await using var db = new AccountDbContext(options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE SepayReceivingAccounts (AccountId TEXT PRIMARY KEY, DisplayName TEXT NOT NULL,
                BankCode TEXT NOT NULL, BankName TEXT NOT NULL, AccountNumber TEXT NOT NULL, AccountName TEXT NOT NULL,
                Revision TEXT NOT NULL, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL);
            CREATE TABLE SepayPaymentSettings (SettingsId INTEGER PRIMARY KEY, Enabled INTEGER NOT NULL,
                SelectedAccountId TEXT NULL REFERENCES SepayReceivingAccounts(AccountId), Revision TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL);
            CREATE TABLE AccountAuditLogs (AccountAuditLogId INTEGER PRIMARY KEY AUTOINCREMENT, UserId TEXT NULL,
                EventType TEXT NOT NULL, Succeeded INTEGER NOT NULL, IpAddress TEXT NULL, UserAgent TEXT NULL,
                CorrelationId TEXT NULL, DetailsJson TEXT NULL, OccurredAtUtc TEXT NOT NULL);
            CREATE TRIGGER RejectAudit BEFORE INSERT ON AccountAuditLogs BEGIN SELECT RAISE(ABORT, 'audit failure'); END;
            """);
        var first = Account("VCB", "11111111");
        var second = Account("TCB", "22222222");
        var settings = new SepayPaymentSettings { SelectedAccountId = first.AccountId, UpdatedAtUtc = DateTime.UtcNow };
        db.AddRange(first, second, settings);
        await db.SaveChangesAsync();
        var service = new SepayPaymentAdminService(db, Options.Create(new SepayPaymentOptions()), TimeProvider.System);
        var originalRevision = settings.Revision;
        await Assert.ThrowsAsync<DbUpdateException>(() => service.SelectAccountAsync(
            new(second.AccountId, originalRevision), "admin-user", default));
        await using var verification = new AccountDbContext(options);
        var persisted = await verification.SepayPaymentSettings.SingleAsync();
        Assert.Equal(first.AccountId, persisted.SelectedAccountId);
        Assert.Equal(originalRevision, persisted.Revision);
        Assert.Empty(await verification.AccountAuditLogs.ToListAsync());
    }

    [Fact]
    public void AdminRoutesRequireGlobalAdminAndWebhookKeepsAnonymousIntegration()
    {
        var authorization = Assert.Single(typeof(AdminSepayPaymentsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("Admin", authorization.Roles);
        Assert.Empty(typeof(AdminSepayPaymentsController).GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        Assert.Single(typeof(SepayWebhookController).GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Owner", false)]
    [InlineData("OrganizationAdmin", false)]
    [InlineData("BillingManager", false)]
    [InlineData("Member", false)]
    [InlineData("Viewer", false)]
    [InlineData(null, false)]
    public async Task ControllerPolicyAllowsOnlyGlobalAdmin(string? role, bool allowed)
    {
        var attribute = Assert.Single(typeof(AdminSepayPaymentsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationCore(options => options.AddPolicy("SepayAdmin", policy =>
            policy.RequireAuthenticatedUser().RequireRole(attribute.Roles!.Split(','))));
        await using var provider = services.BuildServiceProvider();
        var claims = role is null ? Array.Empty<Claim>() : new[] { new Claim(ClaimTypes.Role, role) };
        var identity = new ClaimsIdentity(claims, role is null ? null : "Test");
        var result = await provider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(new ClaimsPrincipal(identity), null, "SepayAdmin");
        Assert.Equal(allowed, result.Succeeded);
    }

    private static SaveSepayAccountRequest Request(AdminSepaySettingsResponse state, string bank, string number) =>
        new("Account " + bank, bank, bank, number, "TEST HOLDER", true, state.Revision);
    private static SepayReceivingAccount Account(string bank, string number) => new()
    {
        DisplayName = bank, BankCode = bank, BankName = bank, AccountNumber = number,
        AccountName = "TEST HOLDER", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
    };
    private static SepayWebhookPayload Webhook(LicensePaymentCheckoutResponse order, string receiver) =>
        new(1234567, "TESTBANK", null, receiver, null, order.TransferCode,
            order.TransferCode, "in", null, order.AmountVnd, order.AmountVnd, "TEST-REFERENCE");

    private sealed class Fixture(AccountDbContext db, SepayPaymentOptions defaults) : IAsyncDisposable
    {
        public AccountDbContext Db { get; } = db;
        public SepayPaymentOptions Defaults { get; } = defaults;
        public Guid PlanId { get; } = Guid.NewGuid();
        public SepayPaymentAdminService Admin { get; } = new(db, Options.Create(defaults), TimeProvider.System);
        public LicensePaymentService Payments => new(Db, Options.Create(Defaults), TimeProvider.System,
            new LicensePaymentTelemetry(), NullLogger<LicensePaymentService>.Instance, paymentConfiguration: Admin);
        public static async Task<Fixture> CreateAsync()
        {
            var db = new AccountDbContext(new DbContextOptionsBuilder<AccountDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var fixture = new Fixture(db, new SepayPaymentOptions
            {
                Enabled = true, ReceiverBankCode = "LEGACY", ReceiverAccountNumber = "99999999", ReceiverAccountName = "LEGACY HOLDER"
            });
            db.SepayPaymentSettings.Add(new() { UpdatedAtUtc = DateTime.UtcNow });
            db.LicensePlans.Add(new()
            {
                LicensePlanId = fixture.PlanId, PlanCode = "test-month", Name = "Test plan", SalePriceVnd = 132000,
                DefaultDurationDays = 30, IsActive = true, IsPublic = true, MaxActivatedDevices = 1,
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return fixture;
        }
        public async Task<AdminSepaySettingsResponse> AddAccountAsync(string bank, string number, bool selected) =>
            await Admin.SaveAccountAsync(null, Request(await Admin.GetAsync(default), bank, number) with { UseForNewOrders = selected }, "admin-user", default);
        public Task<LicensePaymentCheckoutResponse> PurchaseAsync(string request) =>
            Payments.CreateOrReuseAsync("buyer-1", new(PlanId, request), default);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
