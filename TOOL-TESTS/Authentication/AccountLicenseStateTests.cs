using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_TESTS.Authentication;

public sealed class AccountLicenseStateTests
{
    [Fact]
    public async Task CurrentLicense_ExpiredPlan_ReturnsLockedStateWithPlanDetails()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var plan = new LicensePlan
        {
            LicensePlanId = Guid.NewGuid(),
            PlanCode = "monthly",
            Name = "Gói tháng",
            MaxActivatedDevices = 1,
            OfflineGraceHours = 0,
            DefaultDurationDays = 30,
            IsActive = true,
            CreatedAtUtc = now.AddMonths(-2),
            UpdatedAtUtc = now.AddMonths(-2)
        };
        db.LicensePlans.Add(plan);
        db.UserLicenses.Add(new UserLicense
        {
            UserLicenseId = Guid.NewGuid(),
            UserId = "expired-user",
            LicensePlanId = plan.LicensePlanId,
            Status = "Active",
            StartsAtUtc = now.AddDays(-31),
            ExpiresAtUtc = now.AddDays(-1),
            CreatedAtUtc = now.AddDays(-31),
            UpdatedAtUtc = now.AddDays(-31)
        });
        await db.SaveChangesAsync();
        var service = new AccountManagementService(db, TimeProvider.System);

        var result = await service.GetCurrentLicenseAsync(
            "expired-user",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.HasActiveLicense);
        Assert.Equal(LicenseAccessStates.Expired, result.AccessState);
        Assert.Equal("monthly", result.PlanCode);
        Assert.NotNull(result.ExpiresAtUtc);
    }

    [Fact]
    public async Task CurrentLicense_MissingPlan_ReturnsMissingInsteadOfThrowing()
    {
        await using var db = CreateDb();
        var service = new AccountManagementService(db, TimeProvider.System);

        var result = await service.GetCurrentLicenseAsync(
            "missing-user",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.False(result.HasActiveLicense);
        Assert.Equal(LicenseAccessStates.Missing, result.AccessState);
        Assert.Equal("license_missing", result.AccessReasonCode);
    }

    [Theory]
    [InlineData("activate")]
    [InlineData("heartbeat")]
    public async Task InactiveLicense_ActivationAndHeartbeatRemainDenied(string operation)
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var plan = new LicensePlan
        {
            LicensePlanId = Guid.NewGuid(),
            PlanCode = "expired-monthly",
            Name = "Gói tháng đã hết hạn",
            MaxActivatedDevices = 1,
            OfflineGraceHours = 0,
            DefaultDurationDays = 30,
            IsActive = true,
            CreatedAtUtc = now.AddMonths(-2),
            UpdatedAtUtc = now.AddMonths(-2)
        };
        db.LicensePlans.Add(plan);
        db.UserLicenses.Add(new UserLicense
        {
            UserLicenseId = Guid.NewGuid(),
            UserId = "expired-user",
            LicensePlanId = plan.LicensePlanId,
            Status = "Active",
            StartsAtUtc = now.AddDays(-31),
            ExpiresAtUtc = now.AddDays(-1),
            CreatedAtUtc = now.AddDays(-31),
            UpdatedAtUtc = now.AddDays(-31)
        });
        await db.SaveChangesAsync();
        var service = new AccountManagementService(db, TimeProvider.System);

        var exception = operation == "activate"
            ? await Assert.ThrowsAsync<AccountApiException>(() => service.ActivateCurrentDeviceAsync(
                "expired-user",
                Guid.NewGuid(),
                Guid.NewGuid(),
                CancellationToken.None))
            : await Assert.ThrowsAsync<AccountApiException>(() => service.VerifyHeartbeatAsync(
                "expired-user",
                Guid.NewGuid(),
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal(403, exception.StatusCode);
        Assert.Equal("license_required", exception.Code);
    }

    private static AccountDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AccountDbContext>()
            .UseInMemoryDatabase($"account-license-state-{Guid.NewGuid():N}")
            .ConfigureWarnings(options => options.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
}
