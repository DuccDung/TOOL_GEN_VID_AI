using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Domain.Accounts;
using TOOL_SERVER.TikTok.Data;
using TOOL_SERVER.TikTok.Domain;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_SERVER.TikTok;

public sealed record TikTokRuntimeAccess(
    bool Enabled,
    bool Configured,
    bool AuditedForPublicPosting,
    TikTokAppCredentialMaterial? Credential,
    bool IsCredentialVerification,
    string? UnavailableReason = null);

public interface ITikTokCredentialRuntime
{
    Task<TikTokRuntimeAccess> GetUserAccessAsync(string userId, CancellationToken cancellationToken);

    Task<TikTokAppCredentialMaterial> GetOAuthCredentialAsync(
        Guid? credentialId,
        string userId,
        CancellationToken cancellationToken);

    Task<TikTokAppCredentialMaterial> GetActiveCredentialAsync(CancellationToken cancellationToken);

    Task<bool> HasPendingVerificationAsync(string userId, CancellationToken cancellationToken);

    Task ActivateVerifiedCredentialAsync(
        Guid credentialId,
        string userId,
        CancellationToken cancellationToken);

    Task RecordVerificationFailureAsync(
        Guid credentialId,
        string userId,
        string failureCode,
        CancellationToken cancellationToken);
}

public sealed class TikTokCredentialRuntime(
    TikTokDbContext db,
    ITikTokAppCredentialProtector protector,
    IOptions<TikTokOptions> options,
    TimeProvider timeProvider) : ITikTokCredentialRuntime
{
    private readonly TikTokOptions _options = options.Value;

    public async Task<TikTokRuntimeAccess> GetUserAccessAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        if (_options.EmergencyDisabled)
        {
            return new TikTokRuntimeAccess(false, false, false, null, false, TikTokUnavailableReasons.EmergencyDisabled);
        }

        var now = UtcNow();
        var ownExpiredVerification = false;
        if (_options.AdminManagedCredentialsEnabled)
        {
            var pending = await db.AppCredentials.AsNoTracking().SingleOrDefaultAsync(
                x => x.Status == TikTokAppCredentialStatuses.Pending &&
                     x.VerificationRequestedByUserId == userId &&
                     x.VerificationExpiresAtUtc > now,
                cancellationToken);
            if (pending is not null)
            {
                return new TikTokRuntimeAccess(
                    true,
                    true,
                    false,
                    protector.Unprotect(pending.TikTokAppCredentialId, pending.ProtectedPayload, true),
                    true);
            }

            var verificationInProgress = await db.AppCredentials.AsNoTracking().AnyAsync(
                x => x.Status == TikTokAppCredentialStatuses.Pending &&
                     x.VerificationRequestedByUserId != null &&
                     x.VerificationExpiresAtUtc > now,
                cancellationToken);
            if (verificationInProgress)
            {
                return new TikTokRuntimeAccess(false, true, false, null, false, TikTokUnavailableReasons.VerificationOtherAccount);
            }

            var active = await db.AppCredentials.AsNoTracking().SingleOrDefaultAsync(
                x => x.Status == TikTokAppCredentialStatuses.Active,
                cancellationToken);
            if (active is not null)
            {
                var settings = await db.IntegrationSettings.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.TikTokIntegrationSettingId == 1, cancellationToken);
                return new TikTokRuntimeAccess(
                    settings?.Enabled == true,
                    true,
                    settings?.AuditedForPublicPosting == true,
                    protector.Unprotect(active.TikTokAppCredentialId, active.ProtectedPayload, false),
                    false,
                    settings?.Enabled == true ? null : TikTokUnavailableReasons.IntegrationDisabled);
            }

            ownExpiredVerification = await db.AppCredentials.AsNoTracking().AnyAsync(
                x => x.Status == TikTokAppCredentialStatuses.Pending &&
                     x.VerificationRequestedByUserId == userId && x.VerificationExpiresAtUtc <= now,
                cancellationToken);
        }

        var legacy = LegacyCredential();
        return new TikTokRuntimeAccess(
            _options.Enabled && legacy is not null,
            legacy is not null,
            _options.AuditedForPublicPosting,
            legacy,
            false,
            legacy is null ? (ownExpiredVerification ? TikTokUnavailableReasons.VerificationExpired : TikTokUnavailableReasons.SetupRequired) :
                !_options.Enabled ? TikTokUnavailableReasons.IntegrationDisabled : null);
    }

    public async Task<TikTokAppCredentialMaterial> GetOAuthCredentialAsync(
        Guid? credentialId,
        string userId,
        CancellationToken cancellationToken)
    {
        EnsureNotEmergencyDisabled();
        if (credentialId is null)
        {
            return LegacyCredential()
                ?? throw NotConfigured();
        }

        var credential = await db.AppCredentials.AsNoTracking().SingleOrDefaultAsync(
            x => x.TikTokAppCredentialId == credentialId,
            cancellationToken)
            ?? throw NotConfigured();
        var isPending = credential.Status == TikTokAppCredentialStatuses.Pending;
        if (isPending &&
            (credential.VerificationRequestedByUserId != userId ||
             credential.VerificationExpiresAtUtc <= UtcNow()))
        {
            throw new AccountApiException(
                StatusCodes.Status409Conflict,
                "tiktok_credential_verification_expired",
                "Yêu cầu xác minh TikTok credential đã hết hạn. Hãy tạo yêu cầu mới trong Admin.");
        }
        if (!isPending && credential.Status != TikTokAppCredentialStatuses.Active)
        {
            throw NotConfigured();
        }

        return protector.Unprotect(credential.TikTokAppCredentialId, credential.ProtectedPayload, isPending);
    }

    public async Task<TikTokAppCredentialMaterial> GetActiveCredentialAsync(
        CancellationToken cancellationToken)
    {
        EnsureNotEmergencyDisabled();
        if (_options.AdminManagedCredentialsEnabled)
        {
            var active = await db.AppCredentials.AsNoTracking().SingleOrDefaultAsync(
                x => x.Status == TikTokAppCredentialStatuses.Active,
                cancellationToken);
            if (active is not null)
            {
                return protector.Unprotect(active.TikTokAppCredentialId, active.ProtectedPayload, false);
            }
        }

        return LegacyCredential() ?? throw NotConfigured();
    }

    public async Task<bool> HasPendingVerificationAsync(
        string userId,
        CancellationToken cancellationToken) =>
        !_options.EmergencyDisabled &&
        _options.AdminManagedCredentialsEnabled &&
        await db.AppCredentials.AsNoTracking().AnyAsync(
            x => x.Status == TikTokAppCredentialStatuses.Pending &&
                 x.VerificationRequestedByUserId == userId &&
                 x.VerificationExpiresAtUtc > UtcNow(),
            cancellationToken);

    public async Task ActivateVerifiedCredentialAsync(
        Guid credentialId,
        string userId,
        CancellationToken cancellationToken)
    {
        EnsureNotEmergencyDisabled();
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var now = UtcNow();
        var candidate = await db.AppCredentials.SingleOrDefaultAsync(
            x => x.TikTokAppCredentialId == credentialId &&
                 x.Status == TikTokAppCredentialStatuses.Pending,
            cancellationToken)
            ?? throw NotConfigured();
        if (candidate.VerificationRequestedByUserId != userId || candidate.VerificationExpiresAtUtc <= now)
        {
            throw new AccountApiException(
                StatusCodes.Status409Conflict,
                "tiktok_credential_verification_expired",
                "Yêu cầu xác minh TikTok credential đã hết hạn. Hãy tạo yêu cầu mới trong Admin.");
        }
        if (await db.Connections.AnyAsync(x => x.RevokedAtUtc == null, cancellationToken) ||
            await db.PublishJobs.AnyAsync(
                x => x.Status != TikTokPublishStatuses.Complete &&
                     x.Status != TikTokPublishStatuses.Failed,
                cancellationToken))
        {
            throw new AccountApiException(
                StatusCodes.Status409Conflict,
                "tiktok_rotation_in_use",
                "Không thể kích hoạt credential mới khi còn tài khoản kết nối hoặc job TikTok đang chạy.");
        }

        var current = await db.AppCredentials
            .Where(x => x.Status == TikTokAppCredentialStatuses.Active)
            .ToListAsync(cancellationToken);
        foreach (var credential in current)
        {
            credential.Status = TikTokAppCredentialStatuses.Revoked;
            credential.RetiredAtUtc = now;
            credential.RevokedAtUtc = now;
            credential.UpdatedAtUtc = now;
        }
        if (current.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        candidate.Status = TikTokAppCredentialStatuses.Active;
        candidate.LastTestedAtUtc = now;
        candidate.LastTestFailureCode = null;
        candidate.ActivatedAtUtc = now;
        candidate.VerificationRequestedByUserId = null;
        candidate.VerificationExpiresAtUtc = null;
        candidate.UpdatedAtUtc = now;
        var settings = await db.IntegrationSettings.SingleOrDefaultAsync(
            x => x.TikTokIntegrationSettingId == 1,
            cancellationToken);
        if (settings is null)
        {
            settings = new TikTokIntegrationSetting { TikTokIntegrationSettingId = 1 };
            db.IntegrationSettings.Add(settings);
        }
        settings.Enabled = true;
        settings.AuditedForPublicPosting = false;
        settings.AuditEvidence = null;
        settings.UpdatedByUserId = userId;
        settings.UpdatedAtUtc = now;
        AddAudit(userId, "TikTokAppCredentialActivated", new
        {
            candidate.TikTokAppCredentialId,
            candidate.Version,
            candidate.ClientKeyHint,
            candidate.SecretHint
        }, now);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public async Task RecordVerificationFailureAsync(
        Guid credentialId,
        string userId,
        string failureCode,
        CancellationToken cancellationToken)
    {
        var credential = await db.AppCredentials.SingleOrDefaultAsync(
            x => x.TikTokAppCredentialId == credentialId &&
                 x.Status == TikTokAppCredentialStatuses.Pending &&
                 x.VerificationRequestedByUserId == userId,
            cancellationToken);
        if (credential is null) return;
        var now = UtcNow();
        credential.LastTestedAtUtc = now;
        credential.LastTestFailureCode = NormalizeFailureCode(failureCode);
        credential.UpdatedAtUtc = now;
        AddAudit(userId, "TikTokAppCredentialVerificationFailed", new
        {
            credential.TikTokAppCredentialId,
            credential.Version,
            FailureCode = credential.LastTestFailureCode
        }, now, succeeded: false);
        await db.SaveChangesAsync(cancellationToken);
    }

    private TikTokAppCredentialMaterial? LegacyCredential() =>
        string.IsNullOrWhiteSpace(_options.ClientKey) || string.IsNullOrWhiteSpace(_options.ClientSecret)
            ? null
            : new TikTokAppCredentialMaterial(null, _options.ClientKey, _options.ClientSecret, false);

    private void EnsureNotEmergencyDisabled()
    {
        if (_options.EmergencyDisabled)
        {
            throw new AccountApiException(
                StatusCodes.Status503ServiceUnavailable,
                "tiktok_feature_disabled",
                "Tính năng đăng TikTok đang bị dừng khẩn cấp trên server.");
        }
    }

    private static AccountApiException NotConfigured() =>
        new(
            StatusCodes.Status503ServiceUnavailable,
            "tiktok_not_configured",
            "TikTok Developer App chưa có credential Active trên server.");

    private void AddAudit(string userId, string eventType, object details, DateTime now, bool succeeded = true) =>
        db.AccountAuditLogs.Add(new AccountAuditLog
        {
            UserId = userId,
            EventType = eventType,
            Succeeded = succeeded,
            DetailsJson = JsonSerializer.Serialize(details),
            OccurredAtUtc = now
        });

    private static string NormalizeFailureCode(string value)
    {
        var normalized = new string((value ?? string.Empty).Take(100)
            .Select(x => char.IsAsciiLetterOrDigit(x) || x is '_' or '-' ? char.ToLowerInvariant(x) : '_')
            .ToArray()).Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? "verification_failed" : normalized;
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
