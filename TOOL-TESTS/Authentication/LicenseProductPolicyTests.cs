using Microsoft.EntityFrameworkCore;
using TOOL_LOCAL.Authentication;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_TESTS.Authentication;

public sealed class LicenseProductPolicyTests
{
    private const string Local = """{"productScope":"vietsub","vietsubCloudEnabled":false}""";
    [Theory]
    [InlineData(null, true, true)]
    [InlineData("", true, true)]
    [InlineData("  ", true, true)]
    [InlineData("[]", false, false)]
    [InlineData("{}", true, true)]
    [InlineData(Local, false, false)]
    [InlineData("""{"productScope":"vietsub","vietsubCloudEnabled":true}""", false, true)]
    [InlineData("""{"productScope":"unknown"}""", false, false)]
    [InlineData("broken", false, false)]
    public void LegacyDefaultsAndExplicitScopes(string? flags, bool video, bool cloud)
    {
        Assert.Equal(video, LicenseProductPolicy.VideoAllowed(flags));
        Assert.Equal(cloud, LicenseProductPolicy.CloudAllowed(flags));
    }

    [Theory]
    [InlineData("vietsub.job.translate", true)]
    [InlineData("vietsub.job.voice", true)]
    [InlineData("vietsub.export.video", true)]
    [InlineData("vietsub.cloud.availability", true)]
    [InlineData("vietsub.cloud.cancel", true)]
    [InlineData("vietsub.cloud.results", true)]
    [InlineData("vietsub.cloud.retry", false)]
    [InlineData("vietsub.job.translate.cloud", false)]
    [InlineData("generation.video", false)]
    [InlineData("generation.content", false)]
    [InlineData("project.select", false)]
    [InlineData("providers.settings.get", false)]
    [InlineData("render.final", false)]
    [InlineData("short-video.create", false)]
    [InlineData("character.reference.generate", false)]
    [InlineData("project.create", false)]
    [InlineData("short-library.add", false)]
    [InlineData("tiktok.publish", false)]
    [InlineData("bilibili.scan", false)]
    [InlineData("license.payment.create", true)]
    [InlineData("license.current.get", true)]
    [InlineData("auth.logout", true)]
    [InlineData("system.setup.start", true)]
    [InlineData("organization.select", true)]
    [InlineData("media.tools.install.prepare", true)]
    [InlineData("media.tools.install", true)]
    public void NativeGateRejectsPaidAndUnrelatedCommands(string command, bool expected) =>
        Assert.Equal(expected, LicenseProductCommands.Allows(Local, command));

    [Fact]
    public async Task AuthorizationUsesIssuedSnapshotAndRejectsWrongDeviceBeforeProviderAccess()
    {
        await using var db = new AccountDbContext(new DbContextOptionsBuilder<AccountDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTime.UtcNow; var device = Guid.NewGuid();
        var plan = new LicensePlan { LicensePlanId=Guid.NewGuid(), PlanCode="test", Name="Test", FeatureFlagsJson="{}" };
        var license = new UserLicense { UserLicenseId=Guid.NewGuid(), UserId="test-user", LicensePlan=plan,
            Status="Active", StartsAtUtc=now.AddDays(-1), ExpiresAtUtc=now.AddDays(1), EntitlementSnapshotJson=Local };
        db.LicenseActivations.Add(new LicenseActivation { LicenseActivationId=Guid.NewGuid(), UserLicense=license,
            DeviceId=device, Status="Active", LastVerifiedAtUtc=now });
        await db.SaveChangesAsync();
        await LicenseProductAuthorization.RequireAsync(db,"test-user",device,"vietsub",now,default);
        foreach (var capability in new[] { "video", "vietsub-cloud" })
            Assert.Equal("license_feature_unavailable", (await Assert.ThrowsAsync<AccountApiException>(() =>
                LicenseProductAuthorization.RequireAsync(db,"test-user",device,capability,now,default))).Code);
        Assert.Equal("license_unavailable", (await Assert.ThrowsAsync<AccountApiException>(() =>
            LicenseProductAuthorization.RequireAsync(db,"test-user",Guid.NewGuid(),"vietsub",now,default))).Code);
    }

    [Fact]
    public async Task CurrentLicenseDoesNotUnionRightsWithAnOlderActivatedLicenseAndSurvivesSaleStop()
    {
        await using var db = new AccountDbContext(new DbContextOptionsBuilder<AccountDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTime.UtcNow; var device = Guid.NewGuid();
        foreach (var (scope, days) in new[] { ("{}", 1), (Local, 10) })
        {
            var license = new UserLicense { UserLicenseId = Guid.NewGuid(), UserId = "owner", Status = "Active",
                StartsAtUtc = now.AddDays(-1), ExpiresAtUtc = now.AddDays(days), EntitlementSnapshotJson = scope,
                LicensePlan = new LicensePlan { LicensePlanId = Guid.NewGuid(), PlanCode = $"plan-{days}", Name = "Plan", IsActive = false } };
            db.LicenseActivations.Add(new() { LicenseActivationId = Guid.NewGuid(), DeviceId = device,
                UserLicense = license, Status = "Active", LastVerifiedAtUtc = now });
        }
        await db.SaveChangesAsync();
        await LicenseProductAuthorization.RequireAsync(db, "owner", device, "vietsub", now, default);
        foreach (var capability in new[] { "video", "vietsub-cloud", "unknown-capability" })
            Assert.Equal("license_feature_unavailable", (await Assert.ThrowsAsync<AccountApiException>(() =>
                LicenseProductAuthorization.RequireAsync(db, "owner", device, capability, now, default))).Code);
        var current = await EffectiveLicense.Current(db, "owner", now).FirstAsync();
        (await db.LicenseActivations.SingleAsync(x => x.UserLicenseId == current.UserLicenseId)).RevokedAtUtc = now;
        await db.SaveChangesAsync();
        Assert.Equal("license_unavailable", (await Assert.ThrowsAsync<AccountApiException>(() =>
            LicenseProductAuthorization.RequireAsync(db, "owner", device, "vietsub", now, default))).Code);
    }
}
