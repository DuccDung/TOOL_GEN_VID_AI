using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;

namespace TOOL_SERVER.TikTok;

public interface ITikTokAccessService
{
    Task RequireActiveLicenseAsync(string userId, Guid deviceId, CancellationToken cancellationToken);
}

internal sealed class TikTokAccessService(
    AccountDbContext accountDb,
    TimeProvider timeProvider) : ITikTokAccessService
{
    public async Task RequireActiveLicenseAsync(
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var valid = await accountDb.LicenseActivations
            .AsNoTracking()
            .AnyAsync(x => x.DeviceId == deviceId &&
                           x.Status == "Active" &&
                           x.RevokedAtUtc == null &&
                           x.LastVerifiedAtUtc >= now.Subtract(LicensePolicy.LeaseDuration) &&
                           x.UserLicense.UserId == userId &&
                           (x.UserLicense.Status == "Trial" || x.UserLicense.Status == "Active") &&
                           x.UserLicense.StartsAtUtc <= now &&
                           (x.UserLicense.ExpiresAtUtc == null || x.UserLicense.ExpiresAtUtc > now) &&
                           x.UserLicense.RevokedAtUtc == null,
                cancellationToken);
        if (!valid)
        {
            throw new AccountApiException(
                StatusCodes.Status403Forbidden,
                "license_unavailable",
                "License hoặc lease của thiết bị không còn hiệu lực; hãy heartbeat lại trước khi dùng TikTok.");
        }
    }
}
