using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Payments;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_TESTS.Payments;

public sealed class LicensePaymentRelationalTests
{
    [Fact]
    public async Task Webhook_WhenAuditWriteFails_RollsBackPaymentAndLicenseTogether()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<AccountDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AccountDbContext(dbOptions);
        await CreateSchemaAsync(db);
        var now = new DateTime(2026, 9, 3, 4, 0, 0, DateTimeKind.Utc);
        var time = new FixedTimeProvider(now);
        var paymentOptions = ReadyOptions();
        var plan = new LicensePlan
        {
            LicensePlanId = Guid.NewGuid(),
            PlanCode = "rollback-plan",
            Name = "Rollback plan",
            MaxActivatedDevices = 1,
            OfflineGraceHours = 0,
            DefaultDurationDays = 30,
            FeatureFlagsJson = "{}",
            SalePriceVnd = 132_000,
            IsPublic = true,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.LicensePlans.Add(plan);
        await db.SaveChangesAsync();
        var service = CreateService(db, paymentOptions, time);
        var checkout = await service.CreateOrReuseAsync(
            "rollback-user",
            new CreateLicensePaymentRequest(plan.LicensePlanId, "rollback-request-123"),
            CancellationToken.None);
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TRIGGER FailLicensePaymentAudit
            BEFORE INSERT ON AccountAuditLogs
            WHEN NEW.EventType = 'LicensePaymentFulfilled'
            BEGIN
                SELECT RAISE(ABORT, 'forced audit failure');
            END;
            """);
        var payload = Webhook(checkout, paymentOptions);

        await Assert.ThrowsAsync<DbUpdateException>(() => service.HandleWebhookAsync(
            payload,
            CancellationToken.None));

        await using var verificationDb = new AccountDbContext(dbOptions);
        var payment = await verificationDb.LicensePayments.AsNoTracking().SingleAsync();
        Assert.Equal(LicensePaymentStatuses.Pending, payment.Status);
        Assert.Null(payment.ProviderTransactionId);
        Assert.Empty(await verificationDb.UserLicenses.AsNoTracking().ToListAsync());
        Assert.Empty(await verificationDb.AccountAuditLogs.AsNoTracking().ToListAsync());
    }

    private static Task CreateSchemaAsync(AccountDbContext db) => db.Database.ExecuteSqlRawAsync(
        """
        CREATE TABLE LicensePlans (
            LicensePlanId TEXT NOT NULL PRIMARY KEY,
            PlanCode TEXT NOT NULL,
            Name TEXT NOT NULL,
            Description TEXT NULL,
            MaxActivatedDevices INTEGER NOT NULL,
            OfflineGraceHours INTEGER NOT NULL,
            DefaultDurationDays INTEGER NULL,
            FeatureFlagsJson TEXT NULL,
            SalePriceVnd TEXT NULL,
            IsPublic INTEGER NOT NULL,
            DisplayOrder INTEGER NOT NULL,
            MarketingFeaturesJson TEXT NULL,
            IsActive INTEGER NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            RowVersion BLOB NOT NULL DEFAULT X''
        );

        CREATE TABLE UserLicenses (
            UserLicenseId TEXT NOT NULL PRIMARY KEY,
            UserId TEXT NOT NULL,
            LicensePlanId TEXT NOT NULL,
            LicenseKeyHash BLOB NULL,
            Status TEXT NOT NULL,
            StartsAtUtc TEXT NOT NULL,
            ExpiresAtUtc TEXT NULL,
            EntitlementSnapshotJson TEXT NULL,
            GrantedByUserId TEXT NULL,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            RevokedAtUtc TEXT NULL,
            RevokedReason TEXT NULL,
            RowVersion BLOB NOT NULL DEFAULT X''
        );

        CREATE TABLE LicensePayments (
            LicensePaymentId TEXT NOT NULL PRIMARY KEY,
            UserId TEXT NOT NULL,
            LicensePlanId TEXT NOT NULL,
            OrderCode TEXT NOT NULL,
            TransferCode TEXT NOT NULL,
            IdempotencyKey TEXT NOT NULL,
            PriceSnapshotVnd TEXT NOT NULL,
            DurationSnapshotDays INTEGER NOT NULL,
            PlanCodeSnapshot TEXT NOT NULL,
            PlanNameSnapshot TEXT NOT NULL,
            EntitlementSnapshotJson TEXT NULL,
            Status TEXT NOT NULL,
            ReceiverBankCodeSnapshot TEXT NOT NULL,
            ReceiverAccountNumberSnapshot TEXT NOT NULL,
            ReceiverAccountNameSnapshot TEXT NOT NULL,
            ProviderTransactionId INTEGER NULL,
            ProviderReferenceCode TEXT NULL,
            FulfilledUserLicenseId TEXT NULL,
            CreatedAtUtc TEXT NOT NULL,
            ExpiresAtUtc TEXT NOT NULL,
            PaidAtUtc TEXT NULL,
            FulfilledAtUtc TEXT NULL,
            FailureCode TEXT NULL,
            RowVersion BLOB NOT NULL DEFAULT X''
        );

        CREATE UNIQUE INDEX UQ_LicensePayments_ProviderTransactionId
            ON LicensePayments(ProviderTransactionId)
            WHERE ProviderTransactionId IS NOT NULL;

        CREATE TABLE AccountAuditLogs (
            AccountAuditLogId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            UserId TEXT NULL,
            EventType TEXT NOT NULL,
            Succeeded INTEGER NOT NULL,
            IpAddress TEXT NULL,
            UserAgent TEXT NULL,
            CorrelationId TEXT NULL,
            DetailsJson TEXT NULL,
            OccurredAtUtc TEXT NOT NULL
        );
        """);

    private static LicensePaymentService CreateService(
        AccountDbContext db,
        SepayPaymentOptions options,
        TimeProvider timeProvider) => new(
        db,
        Options.Create(options),
        timeProvider,
        new LicensePaymentTelemetry(),
        NullLogger<LicensePaymentService>.Instance);

    private static SepayPaymentOptions ReadyOptions() => new()
    {
        Enabled = true,
        QrBaseUrl = "https://qr.sepay.vn/img",
        ReceiverBankCode = "TESTBANK",
        ReceiverAccountNumber = "123456789",
        ReceiverAccountName = "VIDEO MAKER TEST",
        TransferCodePrefix = "VM",
        PaymentExpireMinutes = 15
    };

    private static SepayWebhookPayload Webhook(
        LicensePaymentCheckoutResponse checkout,
        SepayPaymentOptions options) => new(
        987654321,
        "TESTBANK",
        "2026-09-03 11:01:00",
        options.ReceiverAccountNumber,
        string.Empty,
        checkout.TransferCode,
        checkout.TransferCode,
        "in",
        "test transfer",
        checkout.AmountVnd,
        checkout.AmountVnd,
        "TEST-REFERENCE");

    private sealed class FixedTimeProvider(DateTime nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(nowUtc);
    }
}
