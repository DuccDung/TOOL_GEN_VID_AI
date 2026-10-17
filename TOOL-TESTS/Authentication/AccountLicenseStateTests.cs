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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StopPublicSaleKeepsExistingLicenseAndHeartbeatActive(bool catalogActive)
    {
        await using var db = CreateDb(); var now = DateTime.UtcNow;
        var admin = new AdminLicenseService(db, TimeProvider.System);
        var request = new SaveLicensePlanRequest("paid-plan", "Gói đã mua", null, 1, 1, 0, 30, null, true, 100000, true);
        var created = await admin.CreatePlanAsync(request, "admin", default);
        var user = new ApplicationUser { Id = "customer", UserName = "customer@example.test", DisplayName = "Khách thử" };
        var device = new RegisteredDevice { DeviceId = Guid.NewGuid(), UserId = user.Id, DeviceName = "Fixture", DeviceFingerprintHash = new byte[32], FirstSeenAtUtc = now, LastSeenAtUtc = now };
        var license = new UserLicense { UserLicenseId = Guid.NewGuid(), UserId = user.Id, LicensePlanId = created.LicensePlanId, Status = "Active",
            StartsAtUtc = now.AddDays(-1), ExpiresAtUtc = now.AddDays(29), CreatedAtUtc = now, UpdatedAtUtc = now };
        var session = new UserSession { SessionId = Guid.NewGuid(), UserId = user.Id, DeviceId = device.DeviceId, StartedAtUtc = now, LastSeenAtUtc = now, AbsoluteExpiresAtUtc = now.AddHours(1) };
        db.Users.Add(user); db.RegisteredDevices.Add(device); db.UserLicenses.Add(license); db.UserSessions.Add(session);
        db.LicenseActivations.Add(new() { LicenseActivationId = Guid.NewGuid(), UserLicenseId = license.UserLicenseId, DeviceId = device.DeviceId, Status = "Active", ActivatedAtUtc = now, LastVerifiedAtUtc = now });
        await db.SaveChangesAsync();
        var stopped = await admin.UpdatePlanAsync(created.LicensePlanId, request with { IsPublic = false, IsActive = catalogActive }, "admin", default);
        Assert.Equal(catalogActive, stopped.IsActive); Assert.False(stopped.IsPublic);
        var accounts = new AccountManagementService(db, TimeProvider.System);
        Assert.True((await accounts.GetCurrentLicenseAsync(user.Id, device.DeviceId, default)).HasActiveLicense);
        Assert.True((await accounts.VerifyHeartbeatAsync(user.Id, device.DeviceId, session.SessionId, default)).HasActiveLicense);
        Assert.Equal(now.AddDays(29), (await db.UserLicenses.SingleAsync()).ExpiresAtUtc);
    }

    private static AccountDbContext CreateDb() => new(
        new DbContextOptionsBuilder<AccountDbContext>()
            .UseInMemoryDatabase($"account-license-state-{Guid.NewGuid():N}")
            .ConfigureWarnings(options => options.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
}
