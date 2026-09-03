using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Payments;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_TESTS.Payments;

public sealed class LicensePaymentServiceTests
{
    [Fact]
    public async Task Offers_OnlyReturnsPublicSellablePlans()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        fixture.AddPlan("public", 120_000, true);
        fixture.AddPlan("internal", 80_000, false);
        fixture.AddPlan("no-price", null, true);
        fixture.AddPlan("invalid-duration", 90_000, true).DefaultDurationDays = 3651;
        await fixture.Db.SaveChangesAsync();

        var offers = await fixture.Service.GetOffersAsync(CancellationToken.None);

        var offer = Assert.Single(offers);
        Assert.Equal("public", offer.PlanCode);
        Assert.Equal(120_000, offer.PriceVnd);
        Assert.Equal(["Tạo video không giới hạn", "Hỗ trợ dựng video"], offer.MarketingFeatures);
    }

    [Fact]
    public async Task CreatePayment_RejectsPlanOutsideSupportedDuration()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var plan = fixture.AddPlan("invalid-duration", 90_000, true);
        plan.DefaultDurationDays = 3651;
        await fixture.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            fixture.Service.CreateOrReuseAsync(
                "user-1",
                new CreateLicensePaymentRequest(plan.LicensePlanId, "request-invalid-duration"),
                CancellationToken.None));

        Assert.Equal(404, exception.StatusCode);
        Assert.Equal("license_offer_not_found", exception.Code);
    }

    [Fact]
    public async Task CreatePayment_UsesServerPriceAndReusesPendingPayment()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var plan = fixture.AddPlan("monthly", 132_000, true);
        await fixture.Db.SaveChangesAsync();

        var first = await fixture.Service.CreateOrReuseAsync(
            "user-1",
            new CreateLicensePaymentRequest(plan.LicensePlanId, "request-12345678"),
            CancellationToken.None);
        var second = await fixture.Service.CreateOrReuseAsync(
            "user-1",
            new CreateLicensePaymentRequest(plan.LicensePlanId, "request-abcdefgh"),
            CancellationToken.None);

        Assert.Equal(132_000, first.AmountVnd);
        Assert.Equal(first.OrderCode, second.OrderCode);
        Assert.True(second.ReusedExistingPayment);
        Assert.Contains("amount=132000", first.QrImageUrl, StringComparison.Ordinal);
        Assert.Contains(first.TransferCode, first.QrImageUrl, StringComparison.Ordinal);
        Assert.Equal(1, await fixture.Db.LicensePayments.CountAsync());
        Assert.Equal(1, fixture.Telemetry.Created);
    }

    [Fact]
    public async Task CreatePayment_RepeatedIdempotencyKeyReturnsTheOriginalPayment()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var plan = fixture.AddPlan("monthly", 132_000, true);
        await fixture.Db.SaveChangesAsync();
        var request = new CreateLicensePaymentRequest(plan.LicensePlanId, "same-request-12345678");

        var first = await fixture.Service.CreateOrReuseAsync("user-1", request, CancellationToken.None);
        var second = await fixture.Service.CreateOrReuseAsync("user-1", request, CancellationToken.None);

        Assert.Equal(first.OrderCode, second.OrderCode);
        Assert.True(second.ReusedExistingPayment);
        Assert.Equal(1, await fixture.Db.LicensePayments.CountAsync());
    }

    [Fact]
    public async Task CurrentPayment_ReturnsUnexpiredPendingCheckoutForAppRestart()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var checkout = await fixture.CreatePaymentAsync();

        var current = await fixture.Service.GetCurrentAsync("user-1", CancellationToken.None);

        Assert.NotNull(current.Payment);
        Assert.Equal(checkout.OrderCode, current.Payment.OrderCode);
        Assert.True(current.Payment.ReusedExistingPayment);
    }

    [Fact]
    public async Task PaymentResponses_RestoreUtcKindForSqlServerDateTimes()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var checkout = await fixture.CreatePaymentAsync();
        var payment = await fixture.Db.LicensePayments.SingleAsync();
        payment.CreatedAtUtc = DateTime.SpecifyKind(payment.CreatedAtUtc, DateTimeKind.Unspecified);
        payment.ExpiresAtUtc = DateTime.SpecifyKind(payment.ExpiresAtUtc, DateTimeKind.Unspecified);
        await fixture.Db.SaveChangesAsync();

        var current = await fixture.Service.GetCurrentAsync("user-1", CancellationToken.None);
        var status = await fixture.Service.GetStatusAsync("user-1", checkout.OrderCode, CancellationToken.None);

        Assert.NotNull(current.Payment);
        Assert.Equal(DateTimeKind.Utc, current.Payment.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, current.Payment.ExpiresAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, current.Payment.ServerTimeUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, status.ExpiresAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, status.ServerTimeUtc.Kind);
    }

    [Fact]
    public async Task Status_DoesNotExposeAnotherUsersPayment()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var checkout = await fixture.CreatePaymentAsync();

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            fixture.Service.GetStatusAsync("user-2", checkout.OrderCode, CancellationToken.None));

        Assert.Equal(404, exception.StatusCode);
        Assert.Equal("license_payment_not_found", exception.Code);
    }

    [Fact]
    public async Task Webhook_MatchingTransactionDoesNotRequireAuthorization()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var checkout = await fixture.CreatePaymentAsync();

        await fixture.Service.HandleWebhookAsync(
            fixture.Webhook(checkout),
            CancellationToken.None);

        Assert.Single(fixture.Db.UserLicenses);
        Assert.Equal("Fulfilled", (await fixture.Db.LicensePayments.SingleAsync()).Status);
    }

    [Fact]
    public async Task Webhook_WrongAmountDoesNotGrantLicense()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var checkout = await fixture.CreatePaymentAsync();
        var payload = fixture.Webhook(checkout) with { TransferAmount = checkout.AmountVnd - 1 };

        await fixture.Service.HandleWebhookAsync(
            payload,
            CancellationToken.None);

        Assert.Empty(fixture.Db.UserLicenses);
        Assert.Equal("Pending", (await fixture.Db.LicensePayments.SingleAsync()).Status);
        Assert.Contains(LicensePaymentWebhookMismatchReason.PaymentDetails, fixture.Telemetry.UnmatchedReasons);
    }

    [Fact]
    public async Task Webhook_FractionalAmountDoesNotGrantLicense()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var checkout = await fixture.CreatePaymentAsync();
        var payload = fixture.Webhook(checkout) with { TransferAmount = checkout.AmountVnd + 0.5m };

        await fixture.Service.HandleWebhookAsync(
            payload,
            CancellationToken.None);

        Assert.Empty(fixture.Db.UserLicenses);
        Assert.Equal("Pending", (await fixture.Db.LicensePayments.SingleAsync()).Status);
    }

    [Theory]
    [InlineData("out", "123456789", "matching")]
    [InlineData("in", "999999999", "matching")]
    [InlineData("in", "123456789", "unmatched")]
    public async Task Webhook_MismatchedDirectionAccountOrCode_DoesNotGrantLicense(
        string transferType,
        string accountNumber,
        string matchMode)
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var checkout = await fixture.CreatePaymentAsync();
        var payload = fixture.Webhook(checkout) with
        {
            TransferType = transferType,
            AccountNumber = accountNumber,
            Code = matchMode == "matching" ? checkout.TransferCode : "UNKNOWNCODE",
            Content = matchMode == "matching" ? checkout.TransferCode : "unrelated transfer",
            Description = matchMode == "matching" ? checkout.TransferCode : "unrelated transfer"
        };

        await fixture.Service.HandleWebhookAsync(
            payload,
            CancellationToken.None);

        Assert.Empty(fixture.Db.UserLicenses);
        Assert.Equal("Pending", (await fixture.Db.LicensePayments.SingleAsync()).Status);
    }

    [Fact]
    public async Task Webhook_NormalizedTransferCodeFallback_GrantsLicense()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var checkout = await fixture.CreatePaymentAsync();
        var separatedCode = string.Join('-', checkout.TransferCode.Chunk(3).Select(x => new string(x)));
        var payload = fixture.Webhook(checkout) with
        {
            Code = null,
            Content = $"Thanh toan {separatedCode}",
            Description = null
        };

        await fixture.Service.HandleWebhookAsync(
            payload,
            CancellationToken.None);

        Assert.Single(fixture.Db.UserLicenses);
        Assert.Equal("Fulfilled", (await fixture.Db.LicensePayments.SingleAsync()).Status);
    }

    [Fact]
    public async Task Webhook_ValidAndDuplicate_GrantsExactlyOnceWithoutRevokingSession()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var checkout = await fixture.CreatePaymentAsync();
        fixture.Db.UserSessions.Add(new UserSession
        {
            SessionId = Guid.NewGuid(),
            UserId = "user-1",
            Status = SessionStatuses.Active,
            StartedAtUtc = fixture.Now,
            LastSeenAtUtc = fixture.Now,
            AbsoluteExpiresAtUtc = fixture.Now.AddDays(1)
        });
        await fixture.Db.SaveChangesAsync();
        var payload = fixture.Webhook(checkout);

        await fixture.Service.HandleWebhookAsync(payload, CancellationToken.None);
        var firstExpiry = (await fixture.Db.UserLicenses.SingleAsync()).ExpiresAtUtc;
        await fixture.Service.HandleWebhookAsync(payload, CancellationToken.None);

        var license = await fixture.Db.UserLicenses.SingleAsync();
        Assert.Equal(firstExpiry, license.ExpiresAtUtc);
        Assert.Equal("Fulfilled", (await fixture.Db.LicensePayments.SingleAsync()).Status);
        Assert.Equal(SessionStatuses.Active, (await fixture.Db.UserSessions.SingleAsync()).Status);
        Assert.Equal(1, fixture.Telemetry.Fulfilled);
        Assert.Equal(1, fixture.Telemetry.DuplicateWebhook);
    }

    [Fact]
    public async Task Webhook_LateButMatchingPayment_IsStillFulfilled()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var checkout = await fixture.CreatePaymentAsync();
        fixture.Time.Advance(TimeSpan.FromMinutes(20));
        var expired = await fixture.Service.GetStatusAsync("user-1", checkout.OrderCode, CancellationToken.None);
        Assert.True(expired.IsExpired);

        await fixture.Service.HandleWebhookAsync(
            fixture.Webhook(checkout),
            CancellationToken.None);

        Assert.Single(fixture.Db.UserLicenses);
        Assert.Equal("Fulfilled", (await fixture.Db.LicensePayments.SingleAsync()).Status);
        Assert.Equal(1, fixture.Telemetry.Expired);
        Assert.Equal(1, fixture.Telemetry.Fulfilled);
    }

    [Fact]
    public async Task Webhook_ExistingActiveLicense_ExtendsFromCurrentExpiry()
    {
        await using var fixture = await PaymentFixture.CreateAsync();
        var checkout = await fixture.CreatePaymentAsync();
        var payment = await fixture.Db.LicensePayments.SingleAsync();
        var currentExpiry = fixture.Now.AddDays(12);
        fixture.Db.UserLicenses.Add(new UserLicense
        {
            UserLicenseId = Guid.NewGuid(),
            UserId = "user-1",
            LicensePlanId = payment.LicensePlanId,
            Status = "Active",
            StartsAtUtc = fixture.Now.AddDays(-10),
            ExpiresAtUtc = currentExpiry,
            CreatedAtUtc = fixture.Now.AddDays(-10),
            UpdatedAtUtc = fixture.Now.AddDays(-10)
        });
        await fixture.Db.SaveChangesAsync();

        await fixture.Service.HandleWebhookAsync(
            fixture.Webhook(checkout),
            CancellationToken.None);

        var license = await fixture.Db.UserLicenses.SingleAsync();
        Assert.Equal(currentExpiry.AddDays(payment.DurationSnapshotDays), license.ExpiresAtUtc);
    }

    private sealed class PaymentFixture : IAsyncDisposable
    {
        private PaymentFixture(
            AccountDbContext db,
            LicensePaymentService service,
            MutableTimeProvider time,
            SepayPaymentOptions options,
            RecordingLicensePaymentTelemetry telemetry)
        {
            Db = db;
            Service = service;
            Time = time;
            Options = options;
            Telemetry = telemetry;
        }

        public AccountDbContext Db { get; }
        public LicensePaymentService Service { get; }
        public MutableTimeProvider Time { get; }
        public SepayPaymentOptions Options { get; }
        public RecordingLicensePaymentTelemetry Telemetry { get; }
        public DateTime Now => Time.GetUtcNow().UtcDateTime;

        public static async Task<PaymentFixture> CreateAsync()
        {
            var db = new AccountDbContext(
                new DbContextOptionsBuilder<AccountDbContext>()
                    .UseInMemoryDatabase($"license-payment-{Guid.NewGuid():N}")
                    .Options);
            var time = new MutableTimeProvider(new DateTime(2026, 9, 3, 4, 0, 0, DateTimeKind.Utc));
            var options = new SepayPaymentOptions
            {
                Enabled = true,
                QrBaseUrl = "https://vietqr.app/img",
                ReceiverBankCode = "TESTBANK",
                ReceiverAccountNumber = "123456789",
                ReceiverAccountName = "VIDEO MAKER TEST",
                TransferCodePrefix = "VM",
                PaymentExpireMinutes = 15
            };
            db.Users.Add(new ApplicationUser
            {
                Id = "user-1",
                UserName = "user-1@example.test",
                Email = "user-1@example.test",
                AccountStatus = AccountStatuses.Active,
                CreatedAtUtc = time.GetUtcNow().UtcDateTime,
                UpdatedAtUtc = time.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync();
            var telemetry = new RecordingLicensePaymentTelemetry();
            var service = new LicensePaymentService(
                db,
                Microsoft.Extensions.Options.Options.Create(options),
                time,
                telemetry,
                NullLogger<LicensePaymentService>.Instance);
            return new PaymentFixture(db, service, time, options, telemetry);
        }

        public LicensePlan AddPlan(string code, decimal? price, bool isPublic)
        {
            var plan = new LicensePlan
            {
                LicensePlanId = Guid.NewGuid(),
                PlanCode = code,
                Name = $"Plan {code}",
                Description = "Plan for payment tests",
                MaxActivatedDevices = 1,
                OfflineGraceHours = 0,
                DefaultDurationDays = 30,
                FeatureFlagsJson = "{\"maxConcurrentSessions\":1}",
                SalePriceVnd = price,
                IsPublic = isPublic,
                DisplayOrder = 1,
                MarketingFeaturesJson = "[\"Tạo video không giới hạn\",\"Hỗ trợ dựng video\"]",
                IsActive = true,
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now
            };
            Db.LicensePlans.Add(plan);
            return plan;
        }

        public async Task<LicensePaymentCheckoutResponse> CreatePaymentAsync()
        {
            var plan = AddPlan("monthly", 132_000, true);
            await Db.SaveChangesAsync();
            return await Service.CreateOrReuseAsync(
                "user-1",
                new CreateLicensePaymentRequest(plan.LicensePlanId, $"request-{Guid.NewGuid():N}"),
                CancellationToken.None);
        }

        public SepayWebhookPayload Webhook(LicensePaymentCheckoutResponse checkout) => new(
            987654321,
            "TESTBANK",
            "2026-09-03 11:01:00",
            Options.ReceiverAccountNumber,
            string.Empty,
            checkout.TransferCode,
            $"{checkout.TransferCode} thanh toan",
            "in",
            "test transfer",
            checkout.AmountVnd,
            checkout.AmountVnd,
            "TEST-REFERENCE");

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class RecordingLicensePaymentTelemetry : ILicensePaymentTelemetry
    {
        public int Created { get; private set; }
        public int Fulfilled { get; private set; }
        public int Expired { get; private set; }
        public int DuplicateWebhook { get; private set; }
        public List<LicensePaymentWebhookMismatchReason> UnmatchedReasons { get; } = [];

        public void RecordCreated() => Created++;
        public void RecordFulfilled() => Fulfilled++;
        public void RecordExpired() => Expired++;
        public void RecordDuplicateWebhook() => DuplicateWebhook++;
        public void RecordUnmatchedWebhook(LicensePaymentWebhookMismatchReason reason) => UnmatchedReasons.Add(reason);
    }

    private sealed class MutableTimeProvider(DateTime nowUtc) : TimeProvider
    {
        private DateTimeOffset _now = new(nowUtc);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
