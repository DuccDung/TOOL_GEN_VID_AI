using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;

namespace TOOL_SERVER.Accounts;

// Sale availability belongs to the catalog. Issued rights end by expiry or explicit revocation.
internal static class EffectiveLicense
{
    internal static IOrderedQueryable<UserLicense> Current(AccountDbContext db, string userId, DateTime now) =>
        db.UserLicenses.Where(x => x.UserId == userId && x.RevokedAtUtc == null &&
            (x.Status == "Active" || x.Status == "Trial") && x.StartsAtUtc <= now &&
            (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now))
        .OrderByDescending(x => x.Status == "Active")
        .ThenByDescending(x => x.ExpiresAtUtc)
        .ThenByDescending(x => x.CreatedAtUtc)
        .ThenBy(x => x.UserLicenseId);
}
