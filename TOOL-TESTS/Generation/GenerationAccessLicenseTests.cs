using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.Organizations;

namespace TOOL_TESTS.Generation;

public sealed class GenerationAccessLicenseTests
{
    [Fact]
    public async Task ExpiredLicense_IsRejectedBeforeAnyOrganizationOrAiAccess()
    {
        var now = new DateTime(2026, 9, 3, 5, 0, 0, DateTimeKind.Utc);
        var time = new FixedTimeProvider(now);
        await using var accountDb = new AccountDbContext(
            new DbContextOptionsBuilder<AccountDbContext>()
                .UseInMemoryDatabase($"generation-license-account-{Guid.NewGuid():N}")
                .Options);
        await using var governanceDb = new AiGovernanceDbContext(
            new DbContextOptionsBuilder<AiGovernanceDbContext>()
                .UseInMemoryDatabase($"generation-license-governance-{Guid.NewGuid():N}")
                .Options);
        await using var videoDb = new VideoFactoryDbContext(
            new DbContextOptionsBuilder<VideoFactoryDbContext>()
                .UseInMemoryDatabase($"generation-license-video-{Guid.NewGuid():N}")
                .Options);
        var userId = "expired-user";
        var deviceId = Guid.NewGuid();
        var plan = new LicensePlan
        {
            LicensePlanId = Guid.NewGuid(),
            PlanCode = "expired-plan",
            Name = "Expired plan",
            MaxActivatedDevices = 1,
            OfflineGraceHours = 0,
            IsActive = true,
            CreatedAtUtc = now.AddMonths(-2),
            UpdatedAtUtc = now.AddMonths(-2)
        };
        var license = new UserLicense
        {
            UserLicenseId = Guid.NewGuid(),
            UserId = userId,
            LicensePlanId = plan.LicensePlanId,
            LicensePlan = plan,
            Status = "Active",
            StartsAtUtc = now.AddDays(-31),
            ExpiresAtUtc = now.AddSeconds(-1),
            CreatedAtUtc = now.AddDays(-31),
            UpdatedAtUtc = now.AddDays(-31)
        };
        var device = new RegisteredDevice
        {
            DeviceId = deviceId,
            UserId = userId,
            DeviceName = "Expired license device",
            DeviceFingerprintHash = new byte[32],
            FirstSeenAtUtc = now.AddDays(-31),
            LastSeenAtUtc = now,
            IsRevoked = false
        };
        accountDb.LicensePlans.Add(plan);
        accountDb.UserLicenses.Add(license);
        accountDb.RegisteredDevices.Add(device);
        accountDb.LicenseActivations.Add(new LicenseActivation
        {
            LicenseActivationId = Guid.NewGuid(),
            UserLicenseId = license.UserLicenseId,
            UserLicense = license,
            DeviceId = deviceId,
            Device = device,
            Status = "Active",
            ActivatedAtUtc = now.AddDays(-20),
            LastVerifiedAtUtc = now
        });
        await accountDb.SaveChangesAsync();
        var service = new GenerationAccessService(accountDb, governanceDb, videoDb, time);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => service.RequireAsync(
            userId,
            deviceId,
            null,
            null,
            CancellationToken.None));

        Assert.Equal(403, exception.StatusCode);
        Assert.Equal("license_unavailable", exception.Code);
    }

    [Fact]
    public async Task LocalLicenseCanAccessOnlyItsVietsubOrganizationAndCannotUseVideoReadEndpoints()
    {
        var now = DateTime.UtcNow; var device = Guid.NewGuid(); var org = Guid.NewGuid();
        await using var accounts = new AccountDbContext(new DbContextOptionsBuilder<AccountDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await using var governance = new AiGovernanceDbContext(new DbContextOptionsBuilder<AiGovernanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await using var video = new VideoFactoryDbContext(new DbContextOptionsBuilder<VideoFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        accounts.LicenseActivations.Add(new() { LicenseActivationId = Guid.NewGuid(), DeviceId = device, Status = "Active", LastVerifiedAtUtc = now,
            UserLicense = new() { UserLicenseId = Guid.NewGuid(), UserId = "owner", Status = "Active", StartsAtUtc = now.AddDays(-1),
                ExpiresAtUtc = now.AddDays(1), EntitlementSnapshotJson = """{"productScope":"vietsub"}""",
                LicensePlan = new() { LicensePlanId = Guid.NewGuid(), Name = "Local", PlanCode = "local" } } });
        governance.OrganizationMembers.Add(new() { UserId = "owner", Role = "Member", Status = "Active", Organization = new() {
            OrganizationId = org, Name = "Local", Code = "local", CreatedByUserId = "owner", Status = "Active", MonthlyBudgetLimit = 0 } });
        await accounts.SaveChangesAsync(); await governance.SaveChangesAsync();
        var service = new GenerationAccessService(accounts, governance, video, new FixedTimeProvider(now));
        Assert.Equal(org, (await service.RequireVietsubAccessAsync("owner", device, org, default)).OrganizationId);
        Assert.Equal("organization_access_denied", (await Assert.ThrowsAsync<AccountApiException>(() =>
            service.RequireVietsubAccessAsync("owner", device, Guid.NewGuid(), default))).Code);
        Assert.Equal("license_feature_unavailable", (await Assert.ThrowsAsync<AccountApiException>(() =>
            service.RequireProjectAccessAsync("owner", device, org, null, default))).Code);
        Assert.Empty(await video.Projects.ToArrayAsync());
    }

    [Fact]
    public async Task PersonalLocalAccessNeedsCurrentDeviceLicenseWithoutAnyMembership()
    {
        var now = DateTime.UtcNow; var device = Guid.NewGuid();
        await using var accounts = new AccountDbContext(new DbContextOptionsBuilder<AccountDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await using var governance = new AiGovernanceDbContext(new DbContextOptionsBuilder<AiGovernanceDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await using var video = new VideoFactoryDbContext(new DbContextOptionsBuilder<VideoFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        accounts.LicenseActivations.Add(new() { LicenseActivationId = Guid.NewGuid(), DeviceId = device, Status = "Active", LastVerifiedAtUtc = now,
            UserLicense = new() { UserLicenseId = Guid.NewGuid(), UserId = "owner", Status = "Active", StartsAtUtc = now.AddDays(-1),
                ExpiresAtUtc = now.AddDays(1), EntitlementSnapshotJson = """{"productScope":"vietsub"}""",
                LicensePlan = new() { LicensePlanId = Guid.NewGuid(), Name = "Local", PlanCode = "local" } } });
        await accounts.SaveChangesAsync();
        var service = new GenerationAccessService(accounts, governance, video, new FixedTimeProvider(now));
        await service.RequirePersonalVietsubAccessAsync("owner", device, default);
        foreach (var who in new[] { ("other", device), ("owner", Guid.NewGuid()) })
            Assert.Equal("license_unavailable", (await Assert.ThrowsAsync<AccountApiException>(() =>
                service.RequirePersonalVietsubAccessAsync(who.Item1, who.Item2, default))).Code);
        Assert.Equal("license_feature_unavailable", (await Assert.ThrowsAsync<AccountApiException>(() =>
            service.RequireAsync("owner", device, Guid.Empty, null, default))).Code);
        var activation = await accounts.LicenseActivations.SingleAsync(); activation.RevokedAtUtc = now;
        await accounts.SaveChangesAsync();
        Assert.Equal("license_unavailable", (await Assert.ThrowsAsync<AccountApiException>(() =>
            service.RequirePersonalVietsubAccessAsync("owner", device, default))).Code);
        Assert.Empty(await governance.OrganizationMembers.ToArrayAsync());
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
