using System.Data;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SHARED.Contracts.Authentication;

namespace TOOL_SERVER.Authentication;

public sealed class AuthService(
    AccountDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    ITokenFactory tokenFactory,
    TimeProvider timeProvider) : IAuthService
{
    public async Task<AuthTokenResponse> RegisterAsync(
        RegisterRequest request,
        ClientRequestContext client,
        CancellationToken cancellationToken)
    {
        ValidateRegistration(request);
        await EnsureRegistrationRoleExistsAsync(cancellationToken);
        var now = UtcNow();
        var email = request.Email.Trim();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = NormalizeOptional(request.DisplayName, 200),
            AccountStatus = AccountStatuses.Active,
            PreferredLanguageCode = "vi-VN",
            TimeZoneId = "SE Asia Standard Time",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            throw RegistrationIdentityErrorMapper.Map(createResult);
        }

        var roleResult = await userManager.AddToRoleAsync(user, "User");
        if (!roleResult.Succeeded)
        {
            throw IdentityOperationException("AssignUserRole", roleResult);
        }

        var response = await CreateSessionAsync(
            user,
            request.Device,
            client,
            "AccountRegistered",
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return response;
    }

    public async Task<AuthLoginResult> LoginAsync(
        LoginRequest request,
        ClientRequestContext client,
        CancellationToken cancellationToken)
    {
        if (ValidateLogin(request) is { } validationFailure)
        {
            return AuthLoginResult.Rejected(validationFailure);
        }

        var user = await userManager.FindByEmailAsync(request.Email.Trim());

        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            if (user is not null && user.LockoutEnabled)
            {
                await userManager.AccessFailedAsync(user);
            }

            await WriteAuditAsync(user?.Id, "LoginFailed", false, client, cancellationToken);
            return AuthLoginResult.Rejected(new AuthFailure(
                StatusCodes.Status401Unauthorized,
                "invalid_credentials",
                "Email hoặc mật khẩu không đúng."));
        }

        if (GetAccountSignInFailure(user) is { } accountFailure)
        {
            await WriteAuditAsync(user.Id, "LoginRejected", false, client, cancellationToken);
            return AuthLoginResult.Rejected(accountFailure);
        }

        if (user.LockoutEnabled && await userManager.IsLockedOutAsync(user))
        {
            await WriteAuditAsync(user.Id, "LoginLockedOut", false, client, cancellationToken);
            return AuthLoginResult.Rejected(new AuthFailure(
                StatusCodes.Status423Locked,
                "account_locked",
                "Tài khoản đang tạm khóa."));
        }

        if (user.AccessFailedCount > 0)
        {
            await userManager.ResetAccessFailedCountAsync(user);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var now = UtcNow();
        user.LastLoginAtUtc = now;
        user.UpdatedAtUtc = now;
        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            throw IdentityOperationException("UpdateUserAfterLogin", updateResult);
        }

        var response = await CreateSessionAsync(
            user,
            request.Device,
            client,
            "LoginSucceeded",
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return AuthLoginResult.Success(response);
    }

    public async Task<AuthTokenResponse> RefreshAsync(
        RefreshTokenRequest request,
        ClientRequestContext client,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || request.RefreshToken.Length > 2048)
        {
            throw InvalidRefreshToken();
        }

        var tokenHash = tokenFactory.HashRefreshToken(request.RefreshToken);
        var now = UtcNow();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        var existingToken = await dbContext.RefreshTokens
            .Include(x => x.Session)
            .ThenInclude(x => x.Device)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (existingToken is null)
        {
            throw InvalidRefreshToken();
        }

        if (existingToken.UsedAtUtc is not null || existingToken.RevokedAtUtc is not null)
        {
            await RevokeFamilyAsync(existingToken, "Refresh token reuse detected", now, cancellationToken);
            await WriteAuditAsync(existingToken.UserId, "RefreshTokenReuseDetected", false, client, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw InvalidRefreshToken();
        }

        if (existingToken.ExpiresAtUtc <= now ||
            existingToken.Session.Status != SessionStatuses.Active ||
            existingToken.Session.AbsoluteExpiresAtUtc <= now ||
            existingToken.Session.Device is null ||
            existingToken.Session.Device.IsRevoked)
        {
            await RevokeSessionAsync(existingToken.SessionId, "Session or refresh token expired", now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw InvalidRefreshToken();
        }

        var user = await userManager.FindByIdAsync(existingToken.UserId) ?? throw InvalidRefreshToken();
        EnsureAccountCanSignIn(user);
        var roles = await userManager.GetRolesAsync(user);
        if (!roles.Contains("Admin", StringComparer.OrdinalIgnoreCase))
        {
            var hasValidActivation = await dbContext.UserLicenses.AnyAsync(
                license => license.UserId == user.Id &&
                           (license.Status == "Active" || license.Status == "Trial") &&
                           license.StartsAtUtc <= now &&
                           (license.ExpiresAtUtc == null || license.ExpiresAtUtc > now) &&
                           license.LicensePlan.IsActive &&
                           license.Activations.Any(activation =>
                               activation.DeviceId == existingToken.Session.Device.DeviceId &&
                               activation.Status == "Active" &&
                               !activation.Device.IsRevoked),
                cancellationToken);
            if (!hasValidActivation)
            {
                await RevokeSessionAsync(existingToken.SessionId, "License or device activation is no longer valid", now, cancellationToken);
                await WriteAuditAsync(user.Id, "LicenseRefreshDenied", false, client, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw new AccountApiException(
                    StatusCodes.Status403Forbidden,
                    "license_required",
                    "License hoặc thiết bị không còn hiệu lực.");
            }
        }

        var claimed = await dbContext.RefreshTokens
            .Where(x => x.RefreshTokenId == existingToken.RefreshTokenId &&
                        x.UsedAtUtc == null &&
                        x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.UsedAtUtc, now),
                cancellationToken);

        if (claimed != 1)
        {
            await RevokeFamilyAsync(existingToken, "Concurrent refresh token reuse detected", now, cancellationToken);
            await WriteAuditAsync(existingToken.UserId, "RefreshTokenReuseDetected", false, client, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw InvalidRefreshToken();
        }

        var accessToken = tokenFactory.CreateAccessToken(
            user,
            roles,
            existingToken.SessionId,
            existingToken.Session.Device.DeviceId);
        var newRefreshMaterial = tokenFactory.CreateRefreshToken();
        var replacementTokenId = Guid.NewGuid();

        dbContext.RefreshTokens.Add(new RefreshToken
        {
            RefreshTokenId = replacementTokenId,
            UserId = user.Id,
            SessionId = existingToken.SessionId,
            TokenFamilyId = existingToken.TokenFamilyId,
            TokenHash = newRefreshMaterial.TokenHash,
            TokenPrefix = newRefreshMaterial.TokenPrefix,
            JwtId = accessToken.JwtId,
            CreatedAtUtc = now,
            ExpiresAtUtc = Min(newRefreshMaterial.ExpiresAtUtc, existingToken.Session.AbsoluteExpiresAtUtc),
            CreatedByIpAddress = NormalizeOptional(client.IpAddress, 45)
        });

        existingToken.Session.LastSeenAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        await dbContext.RefreshTokens
            .Where(x => x.RefreshTokenId == existingToken.RefreshTokenId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.ReplacedByTokenId, replacementTokenId),
                cancellationToken);

        await WriteAuditAsync(user.Id, "TokenRefreshed", true, client, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return BuildResponse(
            user,
            roles,
            existingToken.SessionId,
            existingToken.Session.Device.DeviceId,
            accessToken,
            newRefreshMaterial.PlainTextToken,
            Min(newRefreshMaterial.ExpiresAtUtc, existingToken.Session.AbsoluteExpiresAtUtc));
    }

    public async Task LogoutAsync(
        string userId,
        Guid currentSessionId,
        LogoutRequest request,
        ClientRequestContext client,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (request.RevokeAllSessions)
        {
            await dbContext.UserSessions
                .Where(x => x.UserId == userId && x.Status == SessionStatuses.Active)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, SessionStatuses.Revoked)
                    .SetProperty(x => x.RevokedAtUtc, now)
                    .SetProperty(x => x.RevokedReason, "User requested logout from all devices"), cancellationToken);

            await dbContext.RefreshTokens
                .Where(x => x.UserId == userId && x.RevokedAtUtc == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.RevokedAtUtc, now)
                    .SetProperty(x => x.RevokedReason, "User requested logout from all devices"), cancellationToken);
        }
        else
        {
            var sessionId = currentSessionId;
            if (!string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                var tokenHash = tokenFactory.HashRefreshToken(request.RefreshToken);
                var token = await dbContext.RefreshTokens
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.UserId == userId && x.TokenHash == tokenHash, cancellationToken);

                if (token is not null)
                {
                    sessionId = token.SessionId;
                }
            }

            await RevokeSessionAsync(sessionId, "User requested logout", now, cancellationToken);
        }

        await WriteAuditAsync(userId, request.RevokeAllSessions ? "LogoutAll" : "Logout", true, client, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<UserProfileResponse> GetProfileAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId)
            ?? throw new AccountApiException(StatusCodes.Status404NotFound, "user_not_found", "Không tìm thấy tài khoản.");
        var roles = await userManager.GetRolesAsync(user);
        return ToProfile(user, roles);
    }

    private async Task<AuthTokenResponse> CreateSessionAsync(
        ApplicationUser user,
        DeviceRegistrationRequest request,
        ClientRequestContext client,
        string auditEvent,
        CancellationToken cancellationToken)
    {
        ValidateDevice(request);
        var now = UtcNow();
        var fingerprintHash = SHA256.HashData(Encoding.UTF8.GetBytes(request.Fingerprint.Trim()));
        var device = await dbContext.RegisteredDevices
            .SingleOrDefaultAsync(
                x => x.UserId == user.Id && x.DeviceFingerprintHash == fingerprintHash,
                cancellationToken);

        if (device?.IsRevoked == true)
        {
            throw new AccountApiException(StatusCodes.Status403Forbidden, "device_revoked", "Thiết bị này đã bị thu hồi quyền truy cập.");
        }

        if (device is null)
        {
            device = new RegisteredDevice
            {
                DeviceId = Guid.NewGuid(),
                UserId = user.Id,
                DeviceFingerprintHash = fingerprintHash,
                DeviceName = request.DeviceName.Trim(),
                OperatingSystem = NormalizeOptional(request.OperatingSystem, 200),
                ApplicationVersion = NormalizeOptional(request.ApplicationVersion, 50),
                FirstSeenAtUtc = now,
                LastSeenAtUtc = now
            };
            dbContext.RegisteredDevices.Add(device);
        }
        else
        {
            device.DeviceName = request.DeviceName.Trim();
            device.OperatingSystem = NormalizeOptional(request.OperatingSystem, 200);
            device.ApplicationVersion = NormalizeOptional(request.ApplicationVersion, 50);
            device.LastSeenAtUtc = now;
        }

        var previousSessionIds = dbContext.UserSessions
            .Where(x => x.UserId == user.Id &&
                        x.DeviceId == device.DeviceId &&
                        x.Status == SessionStatuses.Active)
            .Select(x => x.SessionId);
        await dbContext.RefreshTokens
            .Where(x => previousSessionIds.Contains(x.SessionId) && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevokedReason, "Replaced by a new login on the same device"), cancellationToken);
        await dbContext.UserSessions
            .Where(x => x.UserId == user.Id &&
                        x.DeviceId == device.DeviceId &&
                        x.Status == SessionStatuses.Active)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, SessionStatuses.Revoked)
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevokedReason, "Replaced by a new login on the same device"), cancellationToken);

        var refreshMaterial = tokenFactory.CreateRefreshToken();
        var sessionId = Guid.NewGuid();
        var session = new UserSession
        {
            SessionId = sessionId,
            UserId = user.Id,
            DeviceId = device.DeviceId,
            Status = SessionStatuses.Active,
            StartedAtUtc = now,
            LastSeenAtUtc = now,
            AbsoluteExpiresAtUtc = refreshMaterial.ExpiresAtUtc,
            IpAddress = NormalizeOptional(client.IpAddress, 45),
            UserAgent = NormalizeOptional(client.UserAgent, 1000),
            ApplicationVersion = NormalizeOptional(request.ApplicationVersion, 50)
        };
        dbContext.UserSessions.Add(session);

        var roles = await userManager.GetRolesAsync(user);
        var accessMaterial = tokenFactory.CreateAccessToken(user, roles, sessionId, device.DeviceId);
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            RefreshTokenId = Guid.NewGuid(),
            UserId = user.Id,
            SessionId = sessionId,
            TokenFamilyId = Guid.NewGuid(),
            TokenHash = refreshMaterial.TokenHash,
            TokenPrefix = refreshMaterial.TokenPrefix,
            JwtId = accessMaterial.JwtId,
            CreatedAtUtc = now,
            ExpiresAtUtc = refreshMaterial.ExpiresAtUtc,
            CreatedByIpAddress = NormalizeOptional(client.IpAddress, 45)
        });

        AddAudit(user.Id, auditEvent, true, client);
        await dbContext.SaveChangesAsync(cancellationToken);

        return BuildResponse(
            user,
            roles,
            sessionId,
            device.DeviceId,
            accessMaterial,
            refreshMaterial.PlainTextToken,
            refreshMaterial.ExpiresAtUtc);
    }

    private async Task RevokeFamilyAsync(
        RefreshToken token,
        string reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await dbContext.RefreshTokens
            .Where(x => x.UserId == token.UserId && x.TokenFamilyId == token.TokenFamilyId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevokedReason, reason), cancellationToken);

        await RevokeSessionAsync(token.SessionId, reason, now, cancellationToken);
    }

    private async Task RevokeSessionAsync(
        Guid sessionId,
        string reason,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await dbContext.UserSessions
            .Where(x => x.SessionId == sessionId && x.Status != SessionStatuses.Revoked)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, SessionStatuses.Revoked)
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevokedReason, reason), cancellationToken);

        await dbContext.RefreshTokens
            .Where(x => x.SessionId == sessionId && x.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RevokedAtUtc, now)
                .SetProperty(x => x.RevokedReason, reason), cancellationToken);
    }

    private async Task WriteAuditAsync(
        string? userId,
        string eventType,
        bool succeeded,
        ClientRequestContext client,
        CancellationToken cancellationToken)
    {
        AddAudit(userId, eventType, succeeded, client);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private void AddAudit(string? userId, string eventType, bool succeeded, ClientRequestContext client)
    {
        dbContext.AccountAuditLogs.Add(new AccountAuditLog
        {
            UserId = userId,
            EventType = eventType,
            Succeeded = succeeded,
            IpAddress = NormalizeOptional(client.IpAddress, 45),
            UserAgent = NormalizeOptional(client.UserAgent, 1000),
            CorrelationId = NormalizeOptional(client.CorrelationId, 100),
            OccurredAtUtc = UtcNow()
        });
    }

    private static AuthTokenResponse BuildResponse(
        ApplicationUser user,
        IEnumerable<string> roles,
        Guid sessionId,
        Guid deviceId,
        AccessTokenMaterial accessToken,
        string refreshToken,
        DateTime refreshExpiresAtUtc) =>
        new(
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            refreshToken,
            refreshExpiresAtUtc,
            sessionId,
            deviceId,
            ToProfile(user, roles));

    private static UserProfileResponse ToProfile(ApplicationUser user, IEnumerable<string> roles) =>
        new(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName,
            user.AccountStatus,
            roles.ToArray());

    private static void ValidateRegistration(RegisterRequest request)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        if (email.Length == 0 ||
            email.Length > 256 ||
            !MailAddress.TryCreate(email, out var parsedEmail) ||
            !string.Equals(parsedEmail.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            throw ValidationException("email", "Email không hợp lệ.");
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length > 256)
        {
            throw ValidationException("password", "Mật khẩu không hợp lệ.");
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName) && request.DisplayName.Trim().Length > 200)
        {
            throw ValidationException("displayName", "Tên hiển thị không được vượt quá 200 ký tự.");
        }

        ValidateDevice(request.Device);
    }

    private async Task EnsureRegistrationRoleExistsAsync(CancellationToken cancellationToken)
    {
        var roleExists = await dbContext.Roles
            .AsNoTracking()
            .AnyAsync(role => role.NormalizedName == "USER", cancellationToken);
        if (!roleExists)
        {
            throw new InvalidOperationException(
                "Required Identity role 'User' is missing. Apply the account database initialization script.");
        }
    }

    private static AuthFailure? ValidateLogin(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            request.Email.Length > 256 ||
            request.Password.Length > 256)
        {
            return new AuthFailure(
                StatusCodes.Status401Unauthorized,
                "invalid_credentials",
                "Email hoặc mật khẩu không đúng.");
        }

        return GetDeviceValidationFailure(request.Device);
    }

    private static void ValidateDevice(DeviceRegistrationRequest device)
    {
        if (GetDeviceValidationFailure(device) is { } failure)
        {
            throw new AccountApiException(
                failure.StatusCode,
                failure.Code,
                failure.Message,
                failure.Errors);
        }
    }

    private static AuthFailure? GetDeviceValidationFailure(DeviceRegistrationRequest? device)
    {
        if (device is null || string.IsNullOrWhiteSpace(device.Fingerprint) || device.Fingerprint.Length > 2048)
        {
            return ValidationFailure("device.fingerprint", "Thông tin nhận diện thiết bị không hợp lệ.");
        }

        return string.IsNullOrWhiteSpace(device.DeviceName) || device.DeviceName.Length > 200
            ? ValidationFailure("device.deviceName", "Tên thiết bị không hợp lệ.")
            : null;
    }

    private static void EnsureAccountCanSignIn(ApplicationUser user)
    {
        if (GetAccountSignInFailure(user) is { } failure)
        {
            throw new AccountApiException(
                failure.StatusCode,
                failure.Code,
                failure.Message,
                failure.Errors);
        }
    }

    private static AuthFailure? GetAccountSignInFailure(ApplicationUser user)
    {
        if (user.DeletedAtUtc is not null || user.AccountStatus == AccountStatuses.Deleted)
        {
            return new AuthFailure(
                StatusCodes.Status403Forbidden,
                "account_deleted",
                "Tài khoản không còn hoạt động.");
        }

        return user.AccountStatus != AccountStatuses.Active
            ? new AuthFailure(
                StatusCodes.Status403Forbidden,
                "account_unavailable",
                "Tài khoản hiện không được phép đăng nhập.")
            : null;
    }

    private static InvalidOperationException IdentityOperationException(string operation, IdentityResult result) =>
        new($"Identity operation '{operation}' failed with error codes: " +
            string.Join(", ", result.Errors.Select(error => error.Code).Distinct(StringComparer.Ordinal)));

    private static AccountApiException ValidationException(string field, string message) =>
        new(StatusCodes.Status400BadRequest, "validation_failed", "Dữ liệu đầu vào không hợp lệ.",
            new Dictionary<string, string[]> { [field] = [message] });

    private static AuthFailure ValidationFailure(string field, string message) =>
        new(
            StatusCodes.Status400BadRequest,
            "validation_failed",
            "Dữ liệu đầu vào không hợp lệ.",
            new Dictionary<string, string[]> { [field] = [message] });

    private static AccountApiException InvalidRefreshToken() =>
        new(StatusCodes.Status401Unauthorized, "invalid_refresh_token", "Refresh token không hợp lệ hoặc đã hết hạn.");

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static DateTime Min(DateTime left, DateTime right) => left <= right ? left : right;
}
