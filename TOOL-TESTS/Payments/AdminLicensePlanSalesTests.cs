using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_TESTS.Payments;

public sealed class AdminLicensePlanSalesTests
{
    [Fact]
    public void AdminPlanForm_RequiresPriceAndDurationBeforeSubmittingPublicPlan()
    {
        var script = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin.js");

        Assert.Contains("function syncPlanSalesRequirements()", script);
        Assert.Contains("duration.required = isPublic;", script);
        Assert.Contains("salePrice.required = isPublic;", script);
        Assert.Contains("Vui lòng nhập thời hạn mặc định cho gói được mở bán.", script);
        Assert.Contains("Vui lòng nhập giá bán VND cho gói được mở bán.", script);
        Assert.Contains("planForm').addEventListener('invalid'", script);
    }

    [Fact]
    public async Task CreatePublicPlan_PersistsServerOwnedSalesFields()
    {
        await using var db = CreateDb();
        var service = new AdminLicenseService(db, TimeProvider.System);
        var request = ValidRequest() with
        {
            SalePriceVnd = 132_000,
            IsPublic = true,
            DisplayOrder = 3,
            MarketingFeaturesJson = "[\"  Tạo video  \",\"Dựng video cục bộ\"]"
        };

        var response = await service.CreatePlanAsync(
            request,
            "admin-user",
            CancellationToken.None);

        Assert.Equal(132_000, response.SalePriceVnd);
        Assert.True(response.IsPublic);
        Assert.Equal(3, response.DisplayOrder);
        var marketingFeatures = System.Text.Json.JsonSerializer.Deserialize<string[]>(response.MarketingFeaturesJson!);
        Assert.NotNull(marketingFeatures);
        Assert.Equal(["Tạo video", "Dựng video cục bộ"], marketingFeatures);
        var stored = await db.LicensePlans.SingleAsync();
        Assert.Equal(response.SalePriceVnd, stored.SalePriceVnd);
        Assert.Equal(response.MarketingFeaturesJson, stored.MarketingFeaturesJson);
    }

    [Theory]
    [InlineData(null, 30)]
    [InlineData(132000, null)]
    public async Task CreatePublicPlan_RequiresPriceAndDuration(int? price, int? durationDays)
    {
        await using var db = CreateDb();
        var service = new AdminLicenseService(db, TimeProvider.System);
        var request = ValidRequest() with
        {
            SalePriceVnd = price,
            DefaultDurationDays = durationDays,
            IsPublic = true
        };

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.CreatePlanAsync(request, "admin-user", CancellationToken.None));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("invalid_public_plan", exception.Code);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[\"\"]")]
    [InlineData("[1]")]
    public async Task CreatePlan_RejectsInvalidMarketingFeatures(string featuresJson)
    {
        await using var db = CreateDb();
        var service = new AdminLicenseService(db, TimeProvider.System);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.CreatePlanAsync(
                ValidRequest() with { MarketingFeaturesJson = featuresJson },
                "admin-user",
                CancellationToken.None));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("invalid_marketing_features", exception.Code);
    }

    [Fact]
    public async Task PaymentQuery_FindsExactOrderOrProviderTransactionWithoutSensitiveSnapshots()
    {
        await using var db = CreateDb();
        var now = new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc);
        var user = new ApplicationUser
        {
            Id = "payment-user",
            UserName = "payment@example.test",
            Email = "payment@example.test",
            AccountStatus = AccountStatuses.Active,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var plan = new LicensePlan
        {
            LicensePlanId = Guid.NewGuid(),
            PlanCode = "monthly",
            Name = "Gói tháng",
            MaxActivatedDevices = 1,
            OfflineGraceHours = 0,
            DefaultDurationDays = 30,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var payment = new LicensePayment
        {
            LicensePaymentId = Guid.NewGuid(),
            UserId = user.Id,
            LicensePlanId = plan.LicensePlanId,
            OrderCode = "VMOABC123",
            TransferCode = "VMXYZ789",
            IdempotencyKey = "admin-query-test",
            PriceSnapshotVnd = 132_000,
            DurationSnapshotDays = 30,
            PlanCodeSnapshot = plan.PlanCode,
            PlanNameSnapshot = plan.Name,
            EntitlementSnapshotJson = "{\"private\":true}",
            Status = LicensePaymentStatuses.Fulfilled,
            ReceiverBankCodeSnapshot = "TESTBANK",
            ReceiverAccountNumberSnapshot = "123456789",
            ReceiverAccountNameSnapshot = "SECRET ACCOUNT",
            ProviderTransactionId = 987654321,
            ProviderReferenceCode = "PRIVATE-REFERENCE",
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(15),
            PaidAtUtc = now.AddMinutes(1),
            FulfilledAtUtc = now.AddMinutes(1)
        };
        db.AddRange(user, plan, payment);
        await db.SaveChangesAsync();
        var service = new AdminLicenseService(db, TimeProvider.System);

        var byOrder = await service.GetPaymentsAsync("vmoabc123", null, 100, CancellationToken.None);
        var byProvider = await service.GetPaymentsAsync("987654321", "fulfilled", 100, CancellationToken.None);

        var response = Assert.Single(byOrder);
        Assert.Equal(payment.LicensePaymentId, response.LicensePaymentId);
        Assert.Equal(user.Email, response.UserEmail);
        Assert.Equal(payment.LicensePaymentId, Assert.Single(byProvider).LicensePaymentId);
        var propertyNames = typeof(AdminLicensePaymentResponse).GetProperties().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(propertyNames, x => x.Contains("Receiver", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, x => x.Contains("Idempotency", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, x => x.Contains("Entitlement", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, x => x.Contains("Reference", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, x => x.Contains("RowVersion", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("unknown", 100, "invalid_payment_status")]
    [InlineData(null, 0, "invalid_payment_page_size")]
    [InlineData(null, 201, "invalid_payment_page_size")]
    public async Task PaymentQuery_RejectsInvalidFilters(string? status, int take, string expectedCode)
    {
        await using var db = CreateDb();
        var service = new AdminLicenseService(db, TimeProvider.System);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.GetPaymentsAsync(null, status, take, CancellationToken.None));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task PaymentQuery_RejectsOversizedSearchTerm()
    {
        await using var db = CreateDb();
        var service = new AdminLicenseService(db, TimeProvider.System);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.GetPaymentsAsync(new string('A', 101), null, 100, CancellationToken.None));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("invalid_payment_search", exception.Code);
    }

    private static SaveLicensePlanRequest ValidRequest() => new(
        "monthly-30",
        "Gói 30 ngày",
        "Gói bán thử nghiệm",
        1,
        1,
        0,
        30,
        null,
        true);

    private static AccountDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AccountDbContext>()
            .UseInMemoryDatabase($"admin-license-sales-{Guid.NewGuid():N}")
            .Options);

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Cannot locate repository file: {Path.Combine(relativeParts)}");
    }
}
