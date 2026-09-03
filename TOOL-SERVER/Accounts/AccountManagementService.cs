using System.Data;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Accounts;

public sealed class AccountManagementService(AccountDbContext dbContext, TimeProvider timeProvider)
    : IAccountManagementService
{
    public async Task<IReadOnlyList<RegisteredDeviceResponse>> GetDevicesAsync(
        string userId,
        Guid currentDeviceId,
        CancellationToken cancellationToken)
    {
        return await dbContext.RegisteredDevices
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.LastSeenAtUtc)
            .Select(x => new RegisteredDeviceResponse(
                x.DeviceId,
                x.DeviceName,
                x.OperatingSystem,
                x.ApplicationVersion,
                x.IsTrusted,
                x.IsRevoked,
                x.DeviceId == currentDeviceId,
                x.FirstSeenAtUtc,
                x.LastSeenAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task RevokeDeviceAsync(string userId, Guid deviceId, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var updated = await dbContext.RegisteredDevices
            .Where(x => x.DeviceId == deviceId && x.UserId == userId && !x.IsRevoked)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.IsRevoked, true)
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevokedReason, "Revoked by account owner"), cancellationToken);

        if (updated == 0)
        {
            var exists = await dbContext.RegisteredDevices.AnyAsync(
                x => x.DeviceId == deviceId && x.UserId == userId,
                cancellationToken);
            if (!exists)
            {
                throw new AccountApiException(StatusCodes.Status404NotFound, "device_not_found", "Không tìm thấy thiết bị.");
            }
        }

        var sessionIds = dbContext.UserSessions
            .Where(x => x.UserId == userId && x.DeviceId == deviceId)
            .Select(x => x.SessionId);
        await dbContext.RefreshTokens
            .Where(x => sessionIds.Contains(x.SessionId) && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevokedReason, "Device revoked"), cancellationToken);
        await dbContext.UserSessions
            .Where(x => x.UserId == userId && x.DeviceId == deviceId && x.Status == SessionStatuses.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, SessionStatuses.Revoked)
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevokedReason, "Device revoked"), cancellationToken);
        await dbContext.LicenseActivations
            .Where(x => x.DeviceId == deviceId && x.Status == "Active")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "Revoked")
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevokedReason, "Device revoked"), cancellationToken);

        dbContext.AccountAuditLogs.Add(new AccountAuditLog
        {
            UserId = userId,
            EventType = "DeviceRevoked",
            Succeeded = true,
            DetailsJson = $$"""{"deviceId":"{{deviceId:D}}"}""",
            OccurredAtUtc = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CurrentLicenseResponse> GetCurrentLicenseAsync(
        string userId,
        Guid currentDeviceId,
        CancellationToken cancellationToken)
    {
        var license = await FindCurrentLicenseAsync(userId, cancellationToken);
        if (license is not null)
        {
            return await BuildLicenseResponseAsync(license, currentDeviceId, cancellationToken);
        }

        var latestLicense = await dbContext.UserLicenses
            .AsNoTracking()
            .Include(x => x.LicensePlan)
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.ExpiresAtUtc)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return latestLicense is null
            ? EmptyLicense(UtcNow(), LicenseAccessStates.Missing, "license_missing", "Tài khoản chưa có gói sử dụng.")
            : BuildInactiveLicenseResponse(latestLicense, UtcNow());
    }

    public async Task<CurrentLicenseResponse> ActivateCurrentDeviceAsync(
        string userId,
        Guid currentDeviceId,
        Guid currentSessionId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var license = await FindCurrentLicenseAsync(userId, cancellationToken)
            ?? throw new AccountApiException(
                StatusCodes.Status403Forbidden,
                "license_required",
                "Tài khoản chưa có license còn hiệu lực.");
        var device = await dbContext.RegisteredDevices.SingleOrDefaultAsync(
            x => x.DeviceId == currentDeviceId && x.UserId == userId && !x.IsRevoked,
            cancellationToken)
            ?? throw new AccountApiException(
                StatusCodes.Status403Forbidden,
                "device_unavailable",
                "Thiết bị không hợp lệ.");
        var session = await dbContext.UserSessions.SingleOrDefaultAsync(
            x => x.SessionId == currentSessionId &&
                 x.UserId == userId &&
                 x.DeviceId == currentDeviceId &&
                 x.Status == SessionStatuses.Active,
            cancellationToken)
            ?? throw new AccountApiException(
                StatusCodes.Status403Forbidden,
                "session_unavailable",
                "Phiên hoạt động không hợp lệ.");

        var activation = await dbContext.LicenseActivations.SingleOrDefaultAsync(
            x => x.UserLicenseId == license.UserLicenseId && x.DeviceId == currentDeviceId,
            cancellationToken);
        var now = UtcNow();
        if (activation is null || activation.Status != "Active")
        {
            var activeCount = await dbContext.LicenseActivations.CountAsync(
                x => x.UserLicenseId == license.UserLicenseId &&
                     x.Status == "Active" &&
                     x.DeviceId != currentDeviceId,
                cancellationToken);
            if (activeCount >= license.LicensePlan.MaxActivatedDevices)
            {
                throw new AccountApiException(
                    StatusCodes.Status409Conflict,
                    "device_limit_reached",
                    "License đã đạt số thiết bị tối đa.");
            }

            if (activation is null)
            {
                dbContext.LicenseActivations.Add(new LicenseActivation
                {
                    LicenseActivationId = Guid.NewGuid(),
                    UserLicenseId = license.UserLicenseId,
                    DeviceId = device.DeviceId,
                    Status = "Active",
                    ActivatedAtUtc = now,
                    LastVerifiedAtUtc = now
                });
            }
            else
            {
                activation.Status = "Active";
                activation.LastVerifiedAtUtc = now;
                activation.RevokedAtUtc = null;
                activation.RevokedReason = null;
            }
        }
        else
        {
            activation.Status = "Active";
            activation.LastVerifiedAtUtc = now;
            activation.RevokedAtUtc = null;
            activation.RevokedReason = null;
        }

        device.LastSeenAtUtc = now;
        session.LastSeenAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await BuildLicenseResponseAsync(license, currentDeviceId, cancellationToken);
    }

    public async Task<CurrentLicenseResponse> VerifyHeartbeatAsync(
        string userId,
        Guid currentDeviceId,
        Guid currentSessionId,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var license = await FindCurrentLicenseAsync(userId, cancellationToken)
            ?? throw new AccountApiException(
                StatusCodes.Status403Forbidden,
                "license_required",
                "Tài khoản chưa có license còn hiệu lực.");
        var activation = await dbContext.LicenseActivations
            .Include(x => x.Device)
            .SingleOrDefaultAsync(
                x => x.UserLicenseId == license.UserLicenseId &&
                     x.DeviceId == currentDeviceId &&
                     x.Status == "Active" &&
                     !x.Device.IsRevoked,
                cancellationToken)
            ?? throw new AccountApiException(
                StatusCodes.Status403Forbidden,
                "device_not_activated",
                "Thiết bị chưa được kích hoạt cho license này.");
        var session = await dbContext.UserSessions.SingleOrDefaultAsync(
            x => x.SessionId == currentSessionId &&
                 x.UserId == userId &&
                 x.DeviceId == currentDeviceId &&
                 x.Status == SessionStatuses.Active &&
                 x.AbsoluteExpiresAtUtc > now,
            cancellationToken)
            ?? throw new AccountApiException(
                StatusCodes.Status403Forbidden,
                "session_unavailable",
                "Phiên hoạt động không còn hiệu lực.");

        var featureFlags = license.EntitlementSnapshotJson ?? license.LicensePlan.FeatureFlagsJson;
        var maxSessions = LicensePolicy.GetMaxConcurrentSessions(featureFlags);
        var allowedSessionIds = await dbContext.UserSessions
            .AsNoTracking()
            .Where(x => x.UserId == userId &&
                        x.Status == SessionStatuses.Active &&
                        x.AbsoluteExpiresAtUtc > now &&
                        !x.Device!.IsRevoked)
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(maxSessions)
            .Select(x => x.SessionId)
            .ToListAsync(cancellationToken);
        if (!allowedSessionIds.Contains(currentSessionId))
        {
            throw new AccountApiException(
                StatusCodes.Status409Conflict,
                "concurrent_session_limit",
                "Gói đã đạt số phiên chạy đồng thời tối đa.");
        }

        activation.LastVerifiedAtUtc = now;
        activation.Device.LastSeenAtUtc = now;
        session.LastSeenAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await BuildLicenseResponseAsync(license, currentDeviceId, cancellationToken);
    }

    private Task<UserLicense?> FindCurrentLicenseAsync(string userId, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        return dbContext.UserLicenses
            .Include(x => x.LicensePlan)
            .Where(x => x.UserId == userId &&
                        (x.Status == "Trial" || x.Status == "Active") &&
                        x.StartsAtUtc <= now &&
                        (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now) &&
                        x.LicensePlan.IsActive)
            .OrderByDescending(x => x.Status == "Active")
            .ThenByDescending(x => x.ExpiresAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<CurrentLicenseResponse> BuildLicenseResponseAsync(
        UserLicense license,
        Guid currentDeviceId,
        CancellationToken cancellationToken)
    {
        var activeDeviceCount = await dbContext.LicenseActivations.CountAsync(
            x => x.UserLicenseId == license.UserLicenseId && x.Status == "Active",
            cancellationToken);
        var currentDeviceActivated = await dbContext.LicenseActivations.AnyAsync(
            x => x.UserLicenseId == license.UserLicenseId &&
                 x.DeviceId == currentDeviceId &&
                 x.Status == "Active",
            cancellationToken);
        var now = UtcNow();
        return new CurrentLicenseResponse(
            true,
            license.UserLicenseId,
            license.LicensePlan.PlanCode,
            license.LicensePlan.Name,
            license.Status,
            license.StartsAtUtc,
            license.ExpiresAtUtc,
            license.LicensePlan.MaxActivatedDevices,
            activeDeviceCount,
            license.LicensePlan.OfflineGraceHours,
            license.EntitlementSnapshotJson ?? license.LicensePlan.FeatureFlagsJson,
            currentDeviceActivated,
            now,
            currentDeviceActivated ? LicensePolicy.LeaseExpiry(now, license.ExpiresAtUtc) : null,
            LicensePolicy.DefaultHeartbeatIntervalSeconds,
            LicenseAccessStates.Active,
            null,
            null);
    }

    private static CurrentLicenseResponse BuildInactiveLicenseResponse(UserLicense license, DateTime now)
    {
        var accessState = license.Status switch
        {
            "Suspended" => LicenseAccessStates.Suspended,
            "Revoked" => LicenseAccessStates.Revoked,
            _ when license.ExpiresAtUtc is { } expiresAt && expiresAt <= now => LicenseAccessStates.Expired,
            "Expired" => LicenseAccessStates.Expired,
            _ => LicenseAccessStates.Missing
        };
        var reasonCode = accessState switch
        {
            LicenseAccessStates.Suspended => "license_suspended",
            LicenseAccessStates.Revoked => "license_revoked",
            LicenseAccessStates.Expired => "license_expired",
            _ => "license_missing"
        };
        var message = accessState switch
        {
            LicenseAccessStates.Suspended => "Gói sử dụng đang bị tạm khóa. Vui lòng liên hệ quản trị viên.",
            LicenseAccessStates.Revoked => "Gói sử dụng đã bị thu hồi. Vui lòng liên hệ quản trị viên.",
            LicenseAccessStates.Expired => "Gói sử dụng đã hết hạn. Vui lòng chọn gói để tiếp tục.",
            _ => "Tài khoản chưa có gói sử dụng."
        };
        return new CurrentLicenseResponse(
            false,
            license.UserLicenseId,
            license.LicensePlan.PlanCode,
            license.LicensePlan.Name,
            license.Status,
            license.StartsAtUtc,
            license.ExpiresAtUtc,
            license.LicensePlan.MaxActivatedDevices,
            0,
            license.LicensePlan.OfflineGraceHours,
            license.EntitlementSnapshotJson ?? license.LicensePlan.FeatureFlagsJson,
            false,
            now,
            null,
            LicensePolicy.DefaultHeartbeatIntervalSeconds,
            accessState,
            reasonCode,
            message);
    }

    private static CurrentLicenseResponse EmptyLicense(
        DateTime now,
        string accessState,
        string reasonCode,
        string message) =>
        new(
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            0,
            0,
            0,
            null,
            false,
            now,
            null,
            LicensePolicy.DefaultHeartbeatIntervalSeconds,
            accessState,
            reasonCode,
            message);

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
