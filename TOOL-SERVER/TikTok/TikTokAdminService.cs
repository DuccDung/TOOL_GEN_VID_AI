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

public sealed record TikTokAdminRequestContext(
    string UserId,
    string? IpAddress,
    string? UserAgent,
    string CorrelationId);

public interface ITikTokAdminService
{
    Task<TikTokAdminStateResponse> GetStateAsync(string adminUserId, CancellationToken cancellationToken);

    Task<TikTokAdminStateResponse> SaveCredentialAsync(
        SaveTikTokAdminCredentialRequest request,
        TikTokAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<TikTokAdminStateResponse> RequestVerificationAsync(
        Guid credentialId,
        TikTokAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<TikTokAdminStateResponse> RevokePendingCredentialAsync(
        Guid credentialId,
        TikTokAdminRequestContext context,
        CancellationToken cancellationToken);

    Task<TikTokAdminStateResponse> UpdateSettingsAsync(
        UpdateTikTokAdminSettingsRequest request,
        TikTokAdminRequestContext context,
        CancellationToken cancellationToken);
}

public sealed class TikTokAdminService(
    TikTokDbContext db,
    ITikTokAppCredentialProtector protector,
    IOptions<TikTokOptions> options,
    TimeProvider timeProvider) : ITikTokAdminService
{
    public const string RequiredRedirectUri = "http://127.0.0.1:*/callback/";
    private readonly TikTokOptions _options = options.Value;

    public async Task<TikTokAdminStateResponse> GetStateAsync(
        string adminUserId,
        CancellationToken cancellationToken)
    {
        var settings = await db.IntegrationSettings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TikTokIntegrationSettingId == 1, cancellationToken);
        var credentials = await db.AppCredentials.AsNoTracking()
            .OrderByDescending(x => x.Version)
            .Take(20)
            .ToListAsync(cancellationToken);
        var activeCredential = await db.AppCredentials.AsNoTracking().SingleOrDefaultAsync(
            x => x.Status == TikTokAppCredentialStatuses.Active,
            cancellationToken);
        if (activeCredential is not null &&
            credentials.All(x => x.TikTokAppCredentialId != activeCredential.TikTokAppCredentialId))
        {
            credentials.Add(activeCredential);
        }
        var hasDatabaseActive = activeCredential is not null;
        var hasLegacy = !string.IsNullOrWhiteSpace(_options.ClientKey) &&
                        !string.IsNullOrWhiteSpace(_options.ClientSecret);
        var now = UtcNow();
        var verificationInProgress = credentials.Any(
            x => x.Status == TikTokAppCredentialStatuses.Pending &&
                 x.VerificationRequestedByUserId is not null &&
                 x.VerificationExpiresAtUtc > now);
        var integrationEnabled = !_options.EmergencyDisabled &&
                                 !verificationInProgress &&
                                 (hasDatabaseActive
                                     ? settings?.Enabled == true
                                     : _options.Enabled && hasLegacy);
        var audited = hasDatabaseActive
            ? settings?.AuditedForPublicPosting == true
            : _options.AuditedForPublicPosting;
        var connectedUsers = await db.Connections.CountAsync(x => x.RevokedAtUtc == null, cancellationToken);
        var pendingJobs = await db.PublishJobs.CountAsync(
            x => x.Status != TikTokPublishStatuses.Complete &&
                 x.Status != TikTokPublishStatuses.Failed,
            cancellationToken);

        return new TikTokAdminStateResponse(
            _options.AdminManagedCredentialsEnabled && !_options.EmergencyDisabled,
            integrationEnabled,
            audited,
            hasDatabaseActive ? settings?.AuditEvidence : null,
            RequiredRedirectUri,
            _options.Scopes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            connectedUsers,
            pendingJobs,
            credentials.OrderByDescending(x => x.Version)
                .Select(x => ToSummary(x, adminUserId, now))
                .ToArray());
    }

    public async Task<TikTokAdminStateResponse> SaveCredentialAsync(
        SaveTikTokAdminCredentialRequest request,
        TikTokAdminRequestContext context,
        CancellationToken cancellationToken)
    {
        EnsureAdminManagementAvailable();
        var clientKey = Required(request.ClientKey, 200, "Client Key");
        var clientSecret = Required(request.ClientSecret, 500, "Client Secret");
        RejectControlCharacters(clientKey, "Client Key");
        RejectControlCharacters(clientSecret, "Client Secret");
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var now = UtcNow();
        var pending = await db.AppCredentials
            .Where(x => x.Status == TikTokAppCredentialStatuses.Pending)
            .ToListAsync(cancellationToken);
        foreach (var previous in pending)
        {
            previous.Status = TikTokAppCredentialStatuses.Revoked;
            previous.RevokedAtUtc = now;
            previous.VerificationRequestedByUserId = null;
            previous.VerificationExpiresAtUtc = null;
            previous.UpdatedAtUtc = now;
        }
        if (pending.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var version = (await db.AppCredentials.MaxAsync(x => (int?)x.Version, cancellationToken) ?? 0) + 1;
        var credentialId = Guid.NewGuid();
        var credential = new TikTokAppCredential
        {
            TikTokAppCredentialId = credentialId,
            Version = version,
            ProtectedPayload = protector.Protect(credentialId, clientKey, clientSecret),
            ClientKeyHint = HintClientKey(clientKey),
            SecretHint = HintSecret(clientSecret),
            Status = TikTokAppCredentialStatuses.Pending,
            CreatedByUserId = context.UserId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.AppCredentials.Add(credential);
        AddAudit(context, "TikTokAppCredentialSaved", new
        {
            credential.TikTokAppCredentialId,
            credential.Version,
            credential.ClientKeyHint,
            credential.SecretHint,
            credential.Status
        }, now);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return await GetStateAsync(context.UserId, cancellationToken);
    }

    public async Task<TikTokAdminStateResponse> RequestVerificationAsync(
        Guid credentialId,
        TikTokAdminRequestContext context,
        CancellationToken cancellationToken)
    {
        EnsureAdminManagementAvailable();
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
        var credential = await db.AppCredentials.SingleOrDefaultAsync(
            x => x.TikTokAppCredentialId == credentialId &&
                 x.Status == TikTokAppCredentialStatuses.Pending,
            cancellationToken)
            ?? throw NotFound();
        var connectedUsers = await db.Connections.CountAsync(x => x.RevokedAtUtc == null, cancellationToken);
        var pendingJobs = await db.PublishJobs.CountAsync(
            x => x.Status != TikTokPublishStatuses.Complete &&
                 x.Status != TikTokPublishStatuses.Failed,
            cancellationToken);
        if (connectedUsers > 0 || pendingJobs > 0)
        {
            throw new AccountApiException(
                StatusCodes.Status409Conflict,
                "tiktok_rotation_in_use",
                "Hãy ngắt toàn bộ tài khoản TikTok và chờ job kết thúc trước khi kiểm tra credential mới.");
        }

        var now = UtcNow();
        credential.VerificationRequestedByUserId = context.UserId;
        credential.VerificationExpiresAtUtc = now.AddMinutes(15);
        credential.LastTestFailureCode = null;
        credential.UpdatedAtUtc = now;
        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        settings.Enabled = false;
        settings.UpdatedByUserId = context.UserId;
        settings.UpdatedAtUtc = now;
        AddAudit(context, "TikTokAppCredentialVerificationRequested", new
        {
            credential.TikTokAppCredentialId,
            credential.Version,
            credential.VerificationExpiresAtUtc
        }, now);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return await GetStateAsync(context.UserId, cancellationToken);
    }

    public async Task<TikTokAdminStateResponse> RevokePendingCredentialAsync(
        Guid credentialId,
        TikTokAdminRequestContext context,
        CancellationToken cancellationToken)
    {
        EnsureAdminManagementAvailable();
        var credential = await db.AppCredentials.SingleOrDefaultAsync(
            x => x.TikTokAppCredentialId == credentialId &&
                 x.Status == TikTokAppCredentialStatuses.Pending,
            cancellationToken)
            ?? throw NotFound();
        var now = UtcNow();
        credential.Status = TikTokAppCredentialStatuses.Revoked;
        credential.RevokedAtUtc = now;
        credential.VerificationRequestedByUserId = null;
        credential.VerificationExpiresAtUtc = null;
        credential.UpdatedAtUtc = now;
        AddAudit(context, "TikTokAppCredentialRevoked", new
        {
            credential.TikTokAppCredentialId,
            credential.Version
        }, now);
        await db.SaveChangesAsync(cancellationToken);
        return await GetStateAsync(context.UserId, cancellationToken);
    }

    public async Task<TikTokAdminStateResponse> UpdateSettingsAsync(
        UpdateTikTokAdminSettingsRequest request,
        TikTokAdminRequestContext context,
        CancellationToken cancellationToken)
    {
        EnsureAdminManagementAvailable();
        var active = await db.AppCredentials.AsNoTracking().AnyAsync(
            x => x.Status == TikTokAppCredentialStatuses.Active,
            cancellationToken);
        if ((request.Enabled || request.AuditedForPublicPosting) && !active)
        {
            throw new AccountApiException(
                StatusCodes.Status409Conflict,
                "tiktok_active_credential_required",
                "Phải xác minh một TikTok credential trước khi bật integration.");
        }
        if (request.AuditedForPublicPosting && !request.Enabled)
        {
            throw Validation("tiktok_audit_requires_enabled", "Chỉ có thể bật đăng công khai khi integration đang bật.");
        }

        string? evidence = null;
        if (request.AuditedForPublicPosting)
        {
            if (!request.ConfirmAuditApproval)
            {
                throw Validation(
                    "tiktok_audit_confirmation_required",
                    "Cần xác nhận TikTok đã phê duyệt audit Content Posting API.");
            }
            evidence = Required(request.AuditEvidence, 500, "Bằng chứng audit");
            if (evidence.Length < 8)
            {
                throw Validation("tiktok_audit_evidence_required", "Bằng chứng audit cần ít nhất 8 ký tự.");
            }
            RejectControlCharacters(evidence, "Bằng chứng audit");
        }

        var now = UtcNow();
        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        settings.Enabled = request.Enabled;
        settings.AuditedForPublicPosting = request.AuditedForPublicPosting;
        settings.AuditEvidence = evidence;
        settings.UpdatedByUserId = context.UserId;
        settings.UpdatedAtUtc = now;
        AddAudit(context, "TikTokIntegrationSettingsUpdated", new
        {
            request.Enabled,
            request.AuditedForPublicPosting,
            HasAuditEvidence = evidence is not null
        }, now);
        await db.SaveChangesAsync(cancellationToken);
        return await GetStateAsync(context.UserId, cancellationToken);
    }

    private async Task<TikTokIntegrationSetting> GetOrCreateSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await db.IntegrationSettings.SingleOrDefaultAsync(
            x => x.TikTokIntegrationSettingId == 1,
            cancellationToken);
        if (settings is not null) return settings;
        settings = new TikTokIntegrationSetting { TikTokIntegrationSettingId = 1 };
        db.IntegrationSettings.Add(settings);
        return settings;
    }

    private void EnsureAdminManagementAvailable()
    {
        if (!_options.AdminManagedCredentialsEnabled || _options.EmergencyDisabled)
        {
            throw new AccountApiException(
                StatusCodes.Status503ServiceUnavailable,
                "tiktok_admin_management_disabled",
                "Quản lý TikTok credential trong Admin đang bị tắt bởi cấu hình môi trường.");
        }
    }

    private void AddAudit(TikTokAdminRequestContext context, string eventType, object details, DateTime now) =>
        db.AccountAuditLogs.Add(new AccountAuditLog
        {
            UserId = context.UserId,
            EventType = eventType,
            Succeeded = true,
            IpAddress = Limit(context.IpAddress, 45),
            UserAgent = Limit(context.UserAgent, 1000),
            CorrelationId = Limit(context.CorrelationId, 100),
            DetailsJson = JsonSerializer.Serialize(details),
            OccurredAtUtc = now
        });

    private static TikTokAdminCredentialSummary ToSummary(
        TikTokAppCredential credential,
        string adminUserId,
        DateTime now) =>
        new(
            credential.TikTokAppCredentialId,
            credential.Version,
            credential.ClientKeyHint,
            credential.SecretHint,
            credential.Status,
            DateTime.SpecifyKind(credential.CreatedAtUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(credential.UpdatedAtUtc, DateTimeKind.Utc),
            AsUtc(credential.VerificationExpiresAtUtc),
            credential.Status == TikTokAppCredentialStatuses.Pending &&
            credential.VerificationRequestedByUserId == adminUserId &&
            credential.VerificationExpiresAtUtc > now,
            AsUtc(credential.LastTestedAtUtc),
            credential.LastTestFailureCode,
            AsUtc(credential.ActivatedAtUtc),
            AsUtc(credential.RevokedAtUtc));

    // SQL datetime2 drops DateTime.Kind. These columns contain UTC clock values;
    // restore the UTC marker so JSON dates are not interpreted as browser-local time.
    private static DateTime? AsUtc(DateTime? value) =>
        value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

    private static string Required(string? value, int maximumLength, string field)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length < 8 || normalized.Length > maximumLength)
        {
            throw Validation(
                "tiktok_credential_invalid",
                $"{field} phải có từ 8 đến {maximumLength} ký tự.");
        }
        return normalized;
    }

    private static void RejectControlCharacters(string value, string field)
    {
        if (value.Any(char.IsControl))
        {
            throw Validation("tiktok_credential_invalid", $"{field} chứa ký tự không hợp lệ.");
        }
    }

    private static string HintClientKey(string value) =>
        value.Length <= 8 ? "****" : $"{value[..4]}****{value[^4..]}";

    private static string HintSecret(string value) =>
        value.Length <= 4 ? "****" : $"****{value[^4..]}";

    private static string? Limit(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maximumLength)];

    private static AccountApiException Validation(string code, string message) =>
        new(StatusCodes.Status400BadRequest, code, message);

    private static AccountApiException NotFound() =>
        new(StatusCodes.Status404NotFound, "tiktok_credential_not_found", "Không tìm thấy TikTok credential đang chờ.");

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
