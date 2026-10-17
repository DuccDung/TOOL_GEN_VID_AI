using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Accounts;

internal static class LicenseProductAuthorization
{
    public static async Task RequireAsync(AccountDbContext db, string userId, Guid deviceId,
        string capability, DateTime now, CancellationToken ct)
    {
        var license = await EffectiveLicense.Current(db, userId, now).AsNoTracking()
            .Select(x => new { x.UserLicenseId, Flags = x.EntitlementSnapshotJson ?? x.LicensePlan.FeatureFlagsJson })
            .FirstOrDefaultAsync(ct);
        if (license is null) throw new AccountApiException(403, "license_unavailable", "License không còn hiệu lực.");
        var active = await db.LicenseActivations.AsNoTracking().AnyAsync(x => x.UserLicenseId == license.UserLicenseId &&
            x.DeviceId == deviceId && x.Status == "Active" &&
            x.RevokedAtUtc == null && x.LastVerifiedAtUtc >= now.Subtract(LicensePolicy.LeaseDuration) &&
            x.UserLicense.UserId == userId && (x.UserLicense.Status == "Trial" || x.UserLicense.Status == "Active") &&
            x.UserLicense.StartsAtUtc <= now && (x.UserLicense.ExpiresAtUtc == null || x.UserLicense.ExpiresAtUtc > now) &&
            x.UserLicense.RevokedAtUtc == null, ct);
        if (!active) throw new AccountApiException(403, "license_unavailable", "License hoặc lease thiết bị không còn hiệu lực.");
        var allowed = capability switch
        {
            "vietsub-cloud" => LicenseProductPolicy.CloudAllowed(license.Flags),
            "vietsub" => LicenseProductPolicy.VietsubAllowed(license.Flags),
            "video" => LicenseProductPolicy.VideoAllowed(license.Flags),
            _ => false
        };
        if (!allowed) throw new AccountApiException(403, "license_feature_unavailable", "Chức năng này không nằm trong gói sử dụng hiện tại.");
    }
}
