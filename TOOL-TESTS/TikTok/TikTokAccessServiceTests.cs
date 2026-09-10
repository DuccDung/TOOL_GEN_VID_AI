using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.TikTok;

namespace TOOL_TESTS.TikTok;

public sealed class TikTokAccessServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveDeviceLease_IsRequired(bool expired)
    {
        var now = new DateTime(2026, 9, 7, 5, 0, 0, DateTimeKind.Utc);
        await using var db = new AccountDbContext(
            new DbContextOptionsBuilder<AccountDbContext>()
                .UseInMemoryDatabase($"tiktok-access-{Guid.NewGuid():N}")
                .Options);
        var userId = "user-a";
        var deviceId = Guid.NewGuid();
        var plan = new LicensePlan
        {
            LicensePlanId = Guid.NewGuid(),
            PlanCode = "tiktok-plan",
            Name = "TikTok plan",
            MaxActivatedDevices = 1,
            IsActive = true,
            CreatedAtUtc = now.AddDays(-2),
            UpdatedAtUtc = now.AddDays(-2)
        };
        var license = new UserLicense
        {
            UserLicenseId = Guid.NewGuid(),
            UserId = userId,
            LicensePlanId = plan.LicensePlanId,
            LicensePlan = plan,
            Status = "Active",
            StartsAtUtc = now.AddDays(-1),
            ExpiresAtUtc = expired ? now.AddSeconds(-1) : now.AddDays(1),
            CreatedAtUtc = now.AddDays(-1),
            UpdatedAtUtc = now.AddDays(-1)
        };
        var device = new RegisteredDevice
        {
            DeviceId = deviceId,
            UserId = userId,
            DeviceName = "TikTok test device",
            DeviceFingerprintHash = new byte[32],
            FirstSeenAtUtc = now.AddDays(-1),
            LastSeenAtUtc = now
        };
        db.LicensePlans.Add(plan);
        db.UserLicenses.Add(license);
        db.RegisteredDevices.Add(device);
        db.LicenseActivations.Add(new LicenseActivation
        {
            LicenseActivationId = Guid.NewGuid(),
            UserLicenseId = license.UserLicenseId,
            UserLicense = license,
            DeviceId = deviceId,
            Device = device,
            Status = "Active",
            ActivatedAtUtc = now.AddDays(-1),
            LastVerifiedAtUtc = now
        });
        await db.SaveChangesAsync();
        var service = new TikTokAccessService(db, new FixedTimeProvider(now));

        if (expired)
        {
            var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
                service.RequireActiveLicenseAsync(userId, deviceId, CancellationToken.None));
            Assert.Equal("license_unavailable", exception.Code);
        }
        else
        {
            await service.RequireActiveLicenseAsync(userId, deviceId, CancellationToken.None);
        }
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
