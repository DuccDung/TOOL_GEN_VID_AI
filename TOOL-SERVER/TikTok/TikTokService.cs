using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.TikTok.Data;
using TOOL_SERVER.TikTok.Domain;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_SERVER.TikTok;

public interface ITikTokService
{
    Task<TikTokFeatureStateResponse> GetStateAsync(string userId, CancellationToken cancellationToken);
    Task<StartTikTokOAuthResponse> StartOAuthAsync(string userId, Guid deviceId, StartTikTokOAuthRequest request, CancellationToken cancellationToken);
    Task<TikTokFeatureStateResponse> CompleteOAuthAsync(string userId, Guid deviceId, CompleteTikTokOAuthRequest request, CancellationToken cancellationToken);
    Task DisconnectAsync(string userId, CancellationToken cancellationToken, Guid? connectionId = null);
    Task<TikTokCreatorInfoResponse> GetCreatorInfoAsync(string userId, CancellationToken cancellationToken, Guid? connectionId = null);
    Task<InitializeTikTokPublishResponse> InitializePublishAsync(string userId, InitializeTikTokPublishRequest request, CancellationToken cancellationToken);
    Task<TikTokPublishStatusResponse> GetPublishStatusAsync(string userId, Guid publishJobId, CancellationToken cancellationToken);
    Task<TikTokFeatureStateResponse> GetConnectionsStateAsync(string userId, CancellationToken cancellationToken);
    Task<TikTokPublishHistoryResponse> GetPublishHistoryAsync(string userId, Guid? connectionId, int page, int pageSize, CancellationToken cancellationToken);
    Task<TikTokPublishStatusResponse> ReadPublishStatusAsync(string userId, Guid publishJobId, CancellationToken cancellationToken);
}

public sealed partial class TikTokService(
    TikTokDbContext db,
    ITikTokApiClient apiClient,
    ITikTokTokenProtector tokenProtector,
    ITikTokCredentialRuntime credentialRuntime,
    IOptions<TikTokOptions> options,
    TimeProvider timeProvider,
    TikTokAvatarCache? avatarCache = null) : ITikTokService
{
    private const long MaximumVideoBytes = 4L * 1024 * 1024 * 1024;
    private const long MaximumSingleChunkBytes = 64L * 1024 * 1024;
    private const long DefaultChunkBytes = 32L * 1024 * 1024;
    private static readonly HashSet<string> PrivacyLevels =
        ["PUBLIC_TO_EVERYONE", "MUTUAL_FOLLOW_FRIENDS", "FOLLOWER_OF_CREATOR", "SELF_ONLY"];
    private static readonly HashSet<string> MimeTypes =
        ["video/mp4", "video/quicktime", "video/webm"];
    private static readonly HashSet<string> UploadHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "open-upload.tiktokapis.com",
        "open-upload-sg.tiktokapis.com",
        "upload.us.tiktokapis.com"
    };
    private readonly TikTokOptions _options = options.Value;

    public async Task<TikTokFeatureStateResponse> GetStateAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var access = await credentialRuntime.GetUserAccessAsync(userId, cancellationToken);
        if (!access.Enabled || !access.Configured)
        {
            return new TikTokFeatureStateResponse(
                access.Enabled,
                access.Configured,
                null,
                IsCredentialVerification: access.IsCredentialVerification,
                UnavailableReason: access.UnavailableReason);
        }

        await RequireLegacyClientAsync(userId, cancellationToken);
        var connection = await db.Connections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId && x.RevokedAtUtc == null, cancellationToken);
        var activePublish = await db.PublishJobs
            .AsNoTracking()
            .Where(x => x.UserId == userId
                        && x.Status != TikTokPublishStatuses.Complete
                        && x.Status != TikTokPublishStatuses.Failed)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return new TikTokFeatureStateResponse(
            true,
            true,
            connection is null ? null : ToSummary(connection),
            activePublish is null ? null : ToStatusResponse(activePublish),
            access.IsCredentialVerification);
    }

    public async Task<StartTikTokOAuthResponse> StartOAuthAsync(
        string userId,
        Guid deviceId,
        StartTikTokOAuthRequest request,
        CancellationToken cancellationToken)
    {
        var access = await RequireAvailableAsync(userId, cancellationToken);
        if (!request.MultiAccount) await RequireLegacyClientAsync(userId, cancellationToken);
        if (request.TargetConnectionId is { } targetId)
            _ = await FindOwnedConnectionAsync(userId, targetId, cancellationToken);
        var redirectUri = ValidateRedirectUri(request.RedirectUri);
        var challenge = ValidateCodeChallenge(request.CodeChallenge);
        var now = UtcNow();
        var expiredSessions = await db.OAuthSessions
            .Where(x => x.UserId == userId && x.DeviceId == deviceId && x.ExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);
        db.OAuthSessions.RemoveRange(expiredSessions);
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var session = new TikTokOAuthSession
        {
            TikTokOAuthSessionId = Guid.NewGuid(),
            TikTokAppCredentialId = access.Credential!.CredentialId,
            TargetConnectionId = request.TargetConnectionId,
            MultiAccount = request.MultiAccount,
            UserId = userId,
            DeviceId = deviceId,
            StateHash = SHA256.HashData(Encoding.UTF8.GetBytes(state)),
            CodeChallenge = challenge,
            RedirectUri = redirectUri.AbsoluteUri,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(_options.OAuthSessionMinutes)
        };
        db.OAuthSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        var scope = string.Join(',', _options.Scopes.Distinct(StringComparer.Ordinal));
        var authorizationUrl =
            "https://www.tiktok.com/v2/auth/authorize/" +
            $"?client_key={Uri.EscapeDataString(access.Credential.ClientKey)}" +
            "&response_type=code" +
            $"&scope={Uri.EscapeDataString(scope)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri.AbsoluteUri)}" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}" +
            "&code_challenge_method=S256";
        return new StartTikTokOAuthResponse(
            session.TikTokOAuthSessionId,
            authorizationUrl,
            state,
            session.ExpiresAtUtc);
    }

    public async Task<TikTokFeatureStateResponse> CompleteOAuthAsync(
        string userId,
        Guid deviceId,
        CompleteTikTokOAuthRequest request,
        CancellationToken cancellationToken)
    {
        ValidateAuthorizationCode(request.Code);
        var verifier = ValidateCodeVerifier(request.CodeVerifier);
        await using var oauthLease = await TikTokOperationLock.AcquireAsync(db, $"oauth:{userId}", cancellationToken);
        var now = UtcNow();
        var session = await db.OAuthSessions.SingleOrDefaultAsync(
            x => x.TikTokOAuthSessionId == request.OAuthSessionId &&
                 x.UserId == userId &&
                 x.DeviceId == deviceId,
            cancellationToken)
            ?? throw Error(404, "tiktok_oauth_session_not_found", "Phiên kết nối TikTok không tồn tại.");
        if (session.ConsumedAtUtc is not null || session.ExpiresAtUtc <= now)
        {
            throw Error(409, "tiktok_oauth_session_expired", "Phiên kết nối TikTok đã hết hạn. Vui lòng kết nối lại.");
        }
        if (!FixedHashEquals(session.StateHash, request.State) ||
            !string.Equals(session.CodeChallenge, CreateCodeChallenge(verifier), StringComparison.OrdinalIgnoreCase))
        {
            throw Error(400, "tiktok_oauth_validation_failed", "Phản hồi đăng nhập TikTok không hợp lệ.");
        }

        if (!session.MultiAccount) await RequireLegacyClientAsync(userId, cancellationToken);
        // Consume before the external code exchange. A timeout must not replay a one-use code.
        session.ConsumedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        var credential = await credentialRuntime.GetOAuthCredentialAsync(
            session.TikTokAppCredentialId,
            userId,
            cancellationToken);
        TikTokTokenResult token;
        try
        {
            token = await apiClient.ExchangeCodeAsync(
                credential,
                request.Code,
                session.RedirectUri,
                verifier,
                cancellationToken);
        }
        catch (TikTokProviderException exception)
        {
            if (credential is { CredentialId: not null, PendingVerification: true })
            {
                await credentialRuntime.RecordVerificationFailureAsync(
                    credential.CredentialId.Value,
                    userId,
                    exception.ProviderCode,
                    cancellationToken);
            }
            throw MapProviderError(exception);
        }
        catch (HttpRequestException)
        {
            await RecordPendingVerificationFailureAsync(
                credential,
                userId,
                "provider_unavailable",
                cancellationToken);
            throw ProviderUnavailable();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await RecordPendingVerificationFailureAsync(
                credential,
                userId,
                "provider_timeout",
                cancellationToken);
            throw ProviderUnavailable();
        }
        var scopes = ParseScopes(token.Scope);
        if (!scopes.Contains("video.publish", StringComparer.Ordinal))
        {
            if (credential is { CredentialId: not null, PendingVerification: true })
            {
                await credentialRuntime.RecordVerificationFailureAsync(
                    credential.CredentialId.Value,
                    userId,
                    "scope_not_authorized",
                    cancellationToken);
            }
            throw Error(403, "tiktok_publish_scope_missing", "Tài khoản chưa cấp quyền đăng video cho ứng dụng.");
        }
        if (credential is { CredentialId: not null, PendingVerification: true })
        {
            await credentialRuntime.ActivateVerifiedCredentialAsync(
                credential.CredentialId.Value,
                userId,
                cancellationToken);
        }

        var appKeyHash = HashAppKey(credential.ClientKey);
        var candidates = await db.Connections.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        var connection = candidates.SingleOrDefault(x => x.OpenId == token.OpenId && x.AppKeyHash == appKeyHash)
            ?? candidates.SingleOrDefault(x => x.OpenId == token.OpenId && x.AppKeyHash == null);
        if (session.TargetConnectionId is { } expectedId && connection?.TikTokConnectionId != expectedId)
            throw Error(409, "tiktok_oauth_account_mismatch", "Bạn đã đăng nhập tài khoản TikTok khác. Hãy kết nối lại đúng tài khoản đã chọn.");
        if (connection is null && candidates.Count > 0 && (!session.MultiAccount || !_options.MultiAccountEnabled))
            throw Error(409, session.MultiAccount ? "tiktok_multi_account_disabled" : "tiktok_client_update_required",
                session.MultiAccount ? "Tính năng thêm nhiều tài khoản chưa được mở trên server." : "Hãy cập nhật VideoMaker để thêm tài khoản TikTok.");
        var connectionId = connection?.TikTokConnectionId ?? Guid.NewGuid();
        await using var connectionLease = await TikTokOperationLock.AcquireAsync(db, $"connection:{connectionId}", cancellationToken);
        if (connection is null)
        {
            connection = new TikTokConnection
            {
                TikTokConnectionId = connectionId,
                UserId = userId,
                CreatedAtUtc = now
            };
            db.Connections.Add(connection);
        }
        else await db.Entry(connection).ReloadAsync(cancellationToken);
        connection.OpenId = token.OpenId;
        connection.AppKeyHash = appKeyHash;
        connection.TikTokAppCredentialId = credential.CredentialId;
        connection.DisconnectedAtUtc = null;
        connection.Scopes = string.Join(',', scopes);
        connection.ProtectedAccessToken = tokenProtector.ProtectToken(userId, token.AccessToken);
        connection.ProtectedRefreshToken = tokenProtector.ProtectToken(userId, token.RefreshToken);
        connection.AccessTokenExpiresAtUtc = now.AddSeconds(ClampLifetime(token.ExpiresInSeconds));
        connection.RefreshTokenExpiresAtUtc = now.AddSeconds(ClampLifetime(token.RefreshExpiresInSeconds));
        connection.CreatorUsername = string.Empty;
        connection.CreatorNickname = string.Empty;
        connection.RevokedAtUtc = null;
        connection.UpdatedAtUtc = now;
        session.ConsumedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var creator = await apiClient.GetCreatorInfoAsync(token.AccessToken, cancellationToken);
            connection.CreatorUsername = creator.Username;
            connection.CreatorNickname = creator.Nickname;
            if (TikTokAvatarCache.IsAllowedUrl(creator.AvatarUrl))
            {
                connection.ProtectedAvatarUrl = tokenProtector.ProtectAvatarUrl(userId, creator.AvatarUrl!);
                connection.AvatarExpiresAtUtc = UtcNow().AddMinutes(90);
            }
            connection.UpdatedAtUtc = UtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is TikTokProviderException or HttpRequestException)
        {
            // The OAuth grant is valid even when the optional profile refresh is temporarily unavailable.
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The OAuth grant is valid even when the optional profile refresh times out.
        }

        return session.MultiAccount
            ? (await GetConnectionsStateAsync(userId, cancellationToken)) with { ConnectedConnectionId = connection.TikTokConnectionId }
            : await GetStateAsync(userId, cancellationToken);
    }

    public async Task DisconnectAsync(string userId, CancellationToken cancellationToken, Guid? connectionId = null)
    {
        var owned = connectionId is { } id ? await FindOwnedConnectionAsync(userId, id, cancellationToken)
            : await RequireConnectionAsync(userId, cancellationToken);
        await using var lease = await TikTokOperationLock.AcquireAsync(db, $"connection:{owned.TikTokConnectionId}", cancellationToken);
        await db.Entry(owned).ReloadAsync(cancellationToken);
        var connection = owned;
        if (connection.DisconnectedAtUtc is not null) return;
        try
        {
            var credential = await credentialRuntime.GetActiveCredentialAsync(cancellationToken);
            var accessToken = await GetAccessTokenAsync(connection, credential, cancellationToken);
            await apiClient.RevokeAsync(credential, accessToken, cancellationToken);
        }
        catch (Exception exception) when (exception is TikTokProviderException or CryptographicException or HttpRequestException or AccountApiException)
        {
            // Local authorization is removed even if TikTok cannot be reached; the refresh token is not retained.
        }
        catch (OperationCanceledException)
        {
            // Once revocation was attempted, finish local cleanup even if the desktop leaves.
        }
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        cancellationToken = cleanup.Token;
        var now = UtcNow();
        var pendingJobs = await db.PublishJobs
            .Where(x => x.UserId == userId && x.TikTokConnectionId == connection.TikTokConnectionId &&
                        x.Status != TikTokPublishStatuses.Complete &&
                        x.Status != TikTokPublishStatuses.Failed)
            .ToListAsync(cancellationToken);
        foreach (var job in pendingJobs)
        {
            job.Status = TikTokPublishStatuses.Failed;
            job.FailureReason = "connection_revoked";
            job.ProtectedUploadUrl = null;
            job.UpdatedAtUtc = now;
        }
        connection.ProtectedAccessToken = string.Empty;
        connection.ProtectedRefreshToken = string.Empty;
        connection.RevokedAtUtc = now;
        connection.DisconnectedAtUtc = now;
        connection.ProtectedAvatarUrl = null;
        connection.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<TikTokCreatorInfoResponse> GetCreatorInfoAsync(
        string userId,
        CancellationToken cancellationToken,
        Guid? connectionId = null)
    {
        var runtimeAccess = await RequireAvailableAsync(userId, cancellationToken);
        var connection = await RequireConnectionAsync(userId, cancellationToken, connectionId);
        string accessToken;
        await using (var lease = await TikTokOperationLock.AcquireAsync(db, $"connection:{connection.TikTokConnectionId}", cancellationToken))
        {
            await db.Entry(connection).ReloadAsync(cancellationToken);
            accessToken = await GetAccessTokenAsync(connection, runtimeAccess.Credential!, cancellationToken);
        }
        TikTokCreatorResult creator;
        try
        {
            creator = await apiClient.GetCreatorInfoAsync(accessToken, cancellationToken);
        }
        catch (TikTokProviderException exception)
        {
            throw MapProviderError(exception);
        }
        catch (HttpRequestException)
        {
            throw ProviderUnavailable();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw ProviderUnavailable();
        }
        // Readiness refreshes return live profile data without rewriting the connection.
        // Concurrent reads must not compete with token refresh or disconnect via RowVersion.
        var avatarUrl = avatarCache?.Register(userId, connection.TikTokConnectionId, creator.AvatarUrl, tokenProtector);
        return ToCreatorResponse(creator, runtimeAccess.AuditedForPublicPosting) with { ConnectionId = connection.TikTokConnectionId, AvatarUrl = avatarUrl };
    }

    public async Task<InitializeTikTokPublishResponse> InitializePublishAsync(
        string userId,
        InitializeTikTokPublishRequest request,
        CancellationToken cancellationToken)
    {
        var runtimeAccess = await RequireAvailableAsync(userId, cancellationToken);
        ValidatePublishRequest(request);
        var connection = await RequireConnectionAsync(userId, cancellationToken, request.ConnectionId);
        await using var requestLease = await TikTokOperationLock.AcquireAsync(db, $"publish:{userId}:{request.ClientRequestId}", cancellationToken);
        await using var connectionLease = await TikTokOperationLock.AcquireAsync(db, $"connection:{connection.TikTokConnectionId}", cancellationToken);
        await db.Entry(connection).ReloadAsync(cancellationToken);
        if (connection.RevokedAtUtc is not null) throw Error(409, "tiktok_reconnect_required", "Hãy kết nối lại tài khoản TikTok đã chọn.");
        var requestHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            request with { ConnectionId = connection.TikTokConnectionId, Title = request.Title.Trim() })));
        var previousAttempt = await db.PublishAttempts.AsNoTracking().SingleOrDefaultAsync(
            x => x.UserId == userId && x.ClientRequestId == request.ClientRequestId, cancellationToken);
        if (previousAttempt is not null && (previousAttempt.TikTokConnectionId != connection.TikTokConnectionId || previousAttempt.RequestHash != requestHash))
            throw Error(409, "tiktok_idempotency_conflict", "Mã lần đăng đã được dùng cho tài khoản hoặc nội dung khác.");
        var now = UtcNow();
        var existing = await db.PublishJobs.AsNoTracking().SingleOrDefaultAsync(
            x => x.UserId == userId && x.ClientRequestId == request.ClientRequestId,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.TikTokConnectionId != connection.TikTokConnectionId)
                throw Error(409, "tiktok_idempotency_conflict", "Mã lần đăng thuộc tài khoản TikTok khác.");
            if (previousAttempt is null && request.ConnectionId is not null)
                throw Error(409, "tiktok_publish_already_initialized", "Phiên đăng cũ đã tồn tại. Hãy kiểm tra lịch sử trước khi tạo bài mới.");
            if (existing.ProtectedUploadUrl is null || existing.UploadUrlExpiresAtUtc <= now)
            {
                throw Error(409, "tiktok_publish_already_initialized", "Phiên đăng này đã được khởi tạo và không thể tạo trùng.");
            }
            return ToInitializeResponse(existing, tokenProtector.UnprotectUploadUrl(userId, existing.ProtectedUploadUrl));
        }
        if (previousAttempt is not null)
            throw Error(409, previousAttempt.Status == "Rejected" ? "tiktok_publish_rejected" : "tiktok_publish_initialization_unknown",
                previousAttempt.Status == "Rejected" ? "TikTok đã từ chối lần đăng này. Hãy kiểm tra lỗi trước khi bắt đầu một lần đăng mới."
                    : "Lần đăng trước chưa xác định được kết quả khởi tạo. Hệ thống không gửi lại tự động; hãy kiểm tra tài khoản TikTok trước khi tạo bài mới.");
        var accessToken = await GetAccessTokenAsync(connection, runtimeAccess.Credential!, cancellationToken);
        TikTokCreatorResult creator;
        try
        {
            creator = await apiClient.GetCreatorInfoAsync(accessToken, cancellationToken);
        }
        catch (TikTokProviderException exception)
        {
            throw MapProviderError(exception);
        }
        catch (HttpRequestException)
        {
            throw ProviderUnavailable();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw ProviderUnavailable();
        }
        var creatorResponse = ToCreatorResponse(creator, runtimeAccess.AuditedForPublicPosting) with { ConnectionId = connection.TikTokConnectionId };
        if (creatorResponse.PublishingIssue is not null)
        {
            return new InitializeTikTokPublishResponse(Guid.Empty, string.Empty, 0, 0, default, creatorResponse);
        }
        ValidateAgainstCreator(request, creator, runtimeAccess.AuditedForPublicPosting);
        var (chunkSize, chunkCount) = CreateChunkPlan(request.VideoSizeBytes);

        var attempt = new TikTokPublishAttempt
        {
            TikTokPublishAttemptId = Guid.NewGuid(), UserId = userId, ClientRequestId = request.ClientRequestId,
            TikTokConnectionId = connection.TikTokConnectionId, RequestHash = requestHash,
            CreatedAtUtc = now, UpdatedAtUtc = now
        };
        db.PublishAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);

        TikTokPublishInitResult initialized;
        try
        {
            initialized = await apiClient.InitializePublishAsync(
                accessToken,
                new TikTokPublishInitPayload(
                    request.Title.Trim(),
                    request.PrivacyLevel,
                    request.DisableComment,
                    request.DisableDuet,
                    request.DisableStitch,
                    request.BrandContent,
                    request.BrandOrganic,
                    request.IsAiGenerated,
                    request.VideoSizeBytes,
                    chunkSize,
                    chunkCount),
                cancellationToken);
        }
        catch (TikTokProviderException exception)
        {
            await RecordAttemptFailureAsync(attempt, exception.StatusCode is >= 400 and < 500);
            throw MapProviderError(exception);
        }
        catch (HttpRequestException)
        {
            await RecordAttemptFailureAsync(attempt, false);
            throw ProviderUnavailable();
        }
        catch (OperationCanceledException)
        {
            await RecordAttemptFailureAsync(attempt, false);
            throw ProviderUnavailable();
        }
        ValidateUploadUrl(initialized.UploadUrl);
        var job = new TikTokPublishJob
        {
            TikTokPublishJobId = Guid.NewGuid(),
            TikTokConnectionId = connection.TikTokConnectionId,
            CreatorUsernameSnapshot = creator.Username,
            CreatorNicknameSnapshot = creator.Nickname,
            UserId = userId,
            ClientRequestId = request.ClientRequestId,
            TikTokPublishId = initialized.PublishId,
            ProtectedUploadUrl = tokenProtector.ProtectUploadUrl(userId, initialized.UploadUrl),
            UploadUrlExpiresAtUtc = now.AddHours(1),
            VideoSizeBytes = request.VideoSizeBytes,
            ChunkSizeBytes = chunkSize,
            TotalChunkCount = chunkCount,
            Status = TikTokPublishStatuses.Uploading,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.PublishJobs.Add(job);
        attempt.Status = "Initialized";
        attempt.TikTokPublishJobId = job.TikTokPublishJobId;
        attempt.UpdatedAtUtc = UtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return ToInitializeResponse(job, initialized.UploadUrl);
    }

    public async Task<TikTokPublishStatusResponse> GetPublishStatusAsync(
        string userId,
        Guid publishJobId,
        CancellationToken cancellationToken)
    {
        var job = await db.PublishJobs.SingleOrDefaultAsync(
            x => x.TikTokPublishJobId == publishJobId && x.UserId == userId,
            cancellationToken)
            ?? throw Error(404, "tiktok_publish_not_found", "Không tìm thấy phiên đăng TikTok.");
        if (TikTokPublishStatuses.IsTerminal(job.Status))
        {
            return ToStatusResponse(job);
        }
        var connection = await RequireConnectionAsync(userId, cancellationToken, job.TikTokConnectionId);
        var credential = await credentialRuntime.GetActiveCredentialAsync(cancellationToken);
        string accessToken;
        await using (var lease = await TikTokOperationLock.AcquireAsync(db, $"connection:{connection.TikTokConnectionId}", cancellationToken))
        {
            await db.Entry(connection).ReloadAsync(cancellationToken);
            accessToken = await GetAccessTokenAsync(connection, credential, cancellationToken);
        }
        TikTokStatusResult result;
        try
        {
            result = await apiClient.GetPublishStatusAsync(accessToken, job.TikTokPublishId, cancellationToken);
        }
        catch (TikTokProviderException exception)
        {
            throw MapProviderError(exception);
        }
        catch (HttpRequestException)
        {
            throw ProviderUnavailable();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw ProviderUnavailable();
        }
        job.Status = NormalizeStatus(result.Status);
        job.FailureReason = NormalizeFailureReason(result.FailureReason);
        job.UploadedBytes = Math.Clamp(result.UploadedBytes, 0, job.VideoSizeBytes);
        job.PublicPostIds = string.Join(',', result.PublicPostIds.Take(10));
        var now = UtcNow();
        if (job.Status == TikTokPublishStatuses.Uploading &&
            job.UploadedBytes < job.VideoSizeBytes &&
            job.UploadUrlExpiresAtUtc <= now)
        {
            job.Status = TikTokPublishStatuses.Failed;
            job.FailureReason = "upload_expired";
        }
        else if (!TikTokPublishStatuses.IsTerminal(job.Status) &&
                 job.CreatedAtUtc <= now.AddHours(-_options.MaximumJobAgeHours))
        {
            job.Status = TikTokPublishStatuses.Failed;
            job.FailureReason = "status_timeout";
        }
        job.UpdatedAtUtc = now;
        if (TikTokPublishStatuses.IsTerminal(job.Status))
        {
            job.ProtectedUploadUrl = null;
        }
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return ToStatusResponse(job);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(job).State = EntityState.Detached;
            var current = await db.PublishJobs.AsNoTracking().SingleOrDefaultAsync(
                x => x.TikTokPublishJobId == publishJobId && x.UserId == userId,
                cancellationToken)
                ?? throw Error(404, "tiktok_publish_not_found", "Không tìm thấy phiên đăng TikTok.");
            return ToStatusResponse(current);
        }
    }

    private async Task<TikTokConnection> RequireConnectionAsync(
        string userId,
        CancellationToken cancellationToken,
        Guid? connectionId = null)
    {
        if (connectionId is { } id)
        {
            var selected = await FindOwnedConnectionAsync(userId, id, cancellationToken);
            if (selected.RevokedAtUtc is not null) throw Error(409, "tiktok_reconnect_required", "Hãy kết nối lại tài khoản TikTok đã chọn.");
            return selected;
        }
        await RequireLegacyClientAsync(userId, cancellationToken);
        return await db.Connections.SingleOrDefaultAsync(x => x.UserId == userId && x.RevokedAtUtc == null, cancellationToken)
            ?? throw Error(409, "tiktok_not_connected", "Hãy kết nối tài khoản TikTok trước.");
    }

    private async Task<string> GetAccessTokenAsync(
        TikTokConnection connection,
        TikTokAppCredentialMaterial credential,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        if (connection.RevokedAtUtc is not null || connection.DisconnectedAtUtc is not null)
            throw Error(409, "tiktok_reconnect_required", "Hãy kết nối lại tài khoản TikTok đã chọn.");
        if (connection.AppKeyHash is not null && connection.AppKeyHash != HashAppKey(credential.ClientKey))
            throw Error(409, "tiktok_reconnect_required", "Tài khoản TikTok thuộc cấu hình ứng dụng trước. Hãy kết nối lại.");
        if (connection.RefreshTokenExpiresAtUtc <= now || string.IsNullOrWhiteSpace(connection.ProtectedRefreshToken))
        {
            connection.RevokedAtUtc = now;
            connection.UpdatedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
            throw Error(409, "tiktok_reconnect_required", "Quyền truy cập TikTok đã hết hạn. Vui lòng kết nối lại.");
        }
        try
        {
            if (connection.AccessTokenExpiresAtUtc > now.AddMinutes(5))
            {
                return tokenProtector.UnprotectToken(connection.UserId, connection.ProtectedAccessToken);
            }
            var refreshToken = tokenProtector.UnprotectToken(connection.UserId, connection.ProtectedRefreshToken);
            var refreshed = await apiClient.RefreshTokenAsync(credential, refreshToken, cancellationToken);
            var scopes = ParseScopes(refreshed.Scope);
            if (!string.Equals(connection.OpenId, refreshed.OpenId, StringComparison.Ordinal))
                throw Error(409, "tiktok_oauth_account_mismatch", "TikTok trả về danh tính không khớp. Hãy kết nối lại đúng tài khoản.");
            if (!scopes.Contains("video.publish", StringComparer.Ordinal))
                throw Error(403, "tiktok_publish_scope_missing", "Tài khoản TikTok chưa cấp quyền đăng video.");
            connection.AppKeyHash = HashAppKey(credential.ClientKey);
            connection.TikTokAppCredentialId = credential.CredentialId;
            connection.Scopes = string.Join(',', scopes);
            connection.ProtectedAccessToken = tokenProtector.ProtectToken(connection.UserId, refreshed.AccessToken);
            connection.ProtectedRefreshToken = tokenProtector.ProtectToken(connection.UserId, refreshed.RefreshToken);
            connection.AccessTokenExpiresAtUtc = now.AddSeconds(ClampLifetime(refreshed.ExpiresInSeconds));
            connection.RefreshTokenExpiresAtUtc = now.AddSeconds(ClampLifetime(refreshed.RefreshExpiresInSeconds));
            connection.UpdatedAtUtc = now;
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return refreshed.AccessToken;
            }
            catch (DbUpdateConcurrencyException)
            {
                db.Entry(connection).State = EntityState.Detached;
                var current = await RequireConnectionAsync(connection.UserId, cancellationToken, connection.TikTokConnectionId);
                if (current.AccessTokenExpiresAtUtc <= now)
                {
                    throw Error(409, "tiktok_reconnect_required", "Quyền truy cập TikTok cần được làm mới. Vui lòng thử lại.");
                }
                return tokenProtector.UnprotectToken(current.UserId, current.ProtectedAccessToken);
            }
        }
        catch (CryptographicException)
        {
            throw Error(409, "tiktok_reconnect_required", "Không thể đọc quyền truy cập TikTok. Vui lòng kết nối lại.");
        }
        catch (TikTokProviderException exception)
        {
            throw MapProviderError(exception);
        }
        catch (HttpRequestException)
        {
            throw ProviderUnavailable();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw ProviderUnavailable();
        }
    }

    internal static (long ChunkSizeBytes, int TotalChunkCount) CreateChunkPlan(long videoSizeBytes)
    {
        if (videoSizeBytes <= 0 || videoSizeBytes > MaximumVideoBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(videoSizeBytes));
        }
        if (videoSizeBytes <= MaximumSingleChunkBytes)
        {
            return (videoSizeBytes, 1);
        }
        var count = checked((int)(videoSizeBytes / DefaultChunkBytes));
        return (DefaultChunkBytes, Math.Max(2, count));
    }

    internal static string CreateCodeChallenge(string verifier) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).ToLowerInvariant();

    private static void ValidatePublishRequest(InitializeTikTokPublishRequest request)
    {
        if (request.ClientRequestId == Guid.Empty)
            throw Error(400, "tiktok_request_id_required", "Yêu cầu đăng phải có mã chống trùng.");
        if (request.Title.Length > 2200)
            throw Error(400, "tiktok_caption_too_long", "Caption TikTok không được vượt quá 2.200 ký tự.");
        if (!PrivacyLevels.Contains(request.PrivacyLevel))
            throw Error(400, "tiktok_privacy_invalid", "Quyền riêng tư TikTok không hợp lệ.");
        if (request.VideoSizeBytes <= 0 || request.VideoSizeBytes > MaximumVideoBytes)
            throw Error(400, "tiktok_video_size_invalid", "Video phải có dung lượng lớn hơn 0 và không vượt quá 4 GB.");
        if (request.VideoDurationSeconds is <= 0 or > 600)
            throw Error(400, "tiktok_video_duration_invalid", "Video phải có thời lượng hợp lệ và không vượt quá 10 phút.");
        if (!MimeTypes.Contains(request.MimeType))
            throw Error(400, "tiktok_video_format_invalid", "Định dạng video không được TikTok hỗ trợ.");
    }

    private static void ValidateAgainstCreator(
        InitializeTikTokPublishRequest request,
        TikTokCreatorResult creator,
        bool auditedForPublicPosting)
    {
        if (!auditedForPublicPosting && request.PrivacyLevel != "SELF_ONLY")
            throw Error(403, "tiktok_unaudited_private_only", "TikTok app chưa được audit; bài đăng chỉ được để ở chế độ Chỉ mình tôi.");
        if (!creator.PrivacyLevels.Contains(request.PrivacyLevel, StringComparer.Ordinal))
            throw Error(400, "tiktok_privacy_unavailable", "Tài khoản TikTok không cho phép quyền riêng tư đã chọn.");
        if (request.VideoDurationSeconds > creator.MaximumVideoDurationSeconds)
            throw Error(400, "tiktok_video_too_long", $"Tài khoản TikTok này chỉ cho phép video tối đa {creator.MaximumVideoDurationSeconds} giây.");
        if (creator.CommentDisabled && !request.DisableComment)
            throw Error(400, "tiktok_comment_unavailable", "Tài khoản TikTok đang tắt bình luận.");
        if (creator.DuetDisabled && !request.DisableDuet)
            throw Error(400, "tiktok_duet_unavailable", "Tài khoản TikTok đang tắt Duet.");
        if (creator.StitchDisabled && !request.DisableStitch)
            throw Error(400, "tiktok_stitch_unavailable", "Tài khoản TikTok đang tắt Stitch.");
        if (request.BrandContent && request.PrivacyLevel == "SELF_ONLY")
            throw Error(400, "tiktok_branded_content_privacy_invalid", "Nội dung hợp tác trả phí không thể đăng ở chế độ Chỉ mình tôi.");
    }

    private static TikTokPublishingIssue? GetAuditIssue(
        TikTokCreatorResult creator,
        bool auditedForPublicPosting)
    {
        if (auditedForPublicPosting) return null;
        if (creator.PrivacyLevels.Contains("PUBLIC_TO_EVERYONE", StringComparer.Ordinal))
        {
            return new TikTokPublishingIssue(
                "tiktok_private_test_account_required",
                "Ứng dụng TikTok chưa được audit. Hãy bật Tài khoản riêng tư trong cài đặt Quyền riêng tư của TikTok, rồi quay lại VideoMaker và bấm Làm mới. Khi đăng, vẫn chọn Chỉ mình tôi; chỉ chọn quyền riêng tư cho bài đăng là chưa đủ.");
        }
        return null;
    }

    private static Uri ValidateRedirectUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            (!uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
             !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) ||
            uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsoluteUri.Length > 512)
        {
            throw Error(400, "tiktok_redirect_uri_invalid", "Redirect URI TikTok phải là loopback local có port riêng.");
        }
        return uri;
    }

    private static string ValidateCodeChallenge(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length != 64 || normalized.Any(x => !Uri.IsHexDigit(x)))
            throw Error(400, "tiktok_pkce_invalid", "PKCE challenge không hợp lệ.");
        return normalized;
    }

    private static string ValidateCodeVerifier(string value)
    {
        var verifier = value?.Trim() ?? string.Empty;
        if (verifier.Length is < 43 or > 128 ||
            verifier.Any(x => !(char.IsAsciiLetterOrDigit(x) || x is '-' or '.' or '_' or '~')))
            throw Error(400, "tiktok_pkce_invalid", "PKCE verifier không hợp lệ.");
        return verifier;
    }

    private static void ValidateAuthorizationCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048)
            throw Error(400, "tiktok_authorization_code_invalid", "Mã ủy quyền TikTok không hợp lệ.");
    }

    private static void ValidateUploadUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.Port != 443 ||
            !UploadHosts.Contains(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            value.Length > 2048)
        {
            throw Error(502, "tiktok_upload_url_invalid", "TikTok trả về địa chỉ upload không an toàn.");
        }
    }

    private static bool FixedHashEquals(byte[] expected, string state)
    {
        if (string.IsNullOrWhiteSpace(state) || state.Length > 256) return false;
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(state));
        return expected.Length == actual.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string[] ParseScopes(string scopes) =>
        scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static long ClampLifetime(long seconds) => Math.Clamp(seconds, 60, 366L * 24 * 60 * 60);

    private static string NormalizeStatus(string value) =>
        value is "PROCESSING_UPLOAD" or "PROCESSING_DOWNLOAD" or "SEND_TO_USER_INBOX" or "PUBLISH_COMPLETE" or "FAILED"
            ? value
            : "PROCESSING_UPLOAD";

    private static string? NormalizeFailureReason(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new string(value.Trim().Take(200)
            .Where(x => char.IsAsciiLetterOrDigit(x) || x is '_' or '-')
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private async Task<TikTokRuntimeAccess> RequireAvailableAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var access = await credentialRuntime.GetUserAccessAsync(userId, cancellationToken);
        if (!access.Enabled)
            throw Error(503, "tiktok_feature_disabled", "Tính năng đăng TikTok chưa được bật trên server.");
        if (!access.Configured || access.Credential is null)
            throw Error(503, "tiktok_not_configured", "TikTok Developer App chưa được cấu hình trên server.");
        return access;
    }

    private Task RecordPendingVerificationFailureAsync(
        TikTokAppCredentialMaterial credential,
        string userId,
        string failureCode,
        CancellationToken cancellationToken) =>
        credential is { CredentialId: not null, PendingVerification: true }
            ? credentialRuntime.RecordVerificationFailureAsync(
                credential.CredentialId.Value,
                userId,
                failureCode,
                cancellationToken)
            : Task.CompletedTask;

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private TikTokConnectionSummary ToSummary(TikTokConnection connection) =>
        new(
            connection.TikTokConnectionId,
            connection.CreatorUsername,
            connection.CreatorNickname,
            ParseScopes(connection.Scopes),
            connection.AccessTokenExpiresAtUtc,
            connection.RefreshTokenExpiresAtUtc,
            connection.UpdatedAtUtc,
            connection.DisconnectedAtUtc is not null ? "Disconnected" : connection.RevokedAtUtc is not null || connection.RefreshTokenExpiresAtUtc <= UtcNow()
                ? "ReconnectRequired" : "Connected",
            connection.ProtectedAvatarUrl is not null && connection.AvatarExpiresAtUtc > UtcNow()
                ? $"api/tiktok/connections/{connection.TikTokConnectionId:D}/avatar" : null);

    private static TikTokCreatorInfoResponse ToCreatorResponse(
        TikTokCreatorResult creator,
        bool auditedForPublicPosting) =>
        new(
            creator.Username,
            creator.Nickname,
            auditedForPublicPosting
                ? creator.PrivacyLevels
                : creator.PrivacyLevels.Where(x => x == "SELF_ONLY").ToArray(),
            creator.CommentDisabled,
            creator.DuetDisabled,
            creator.StitchDisabled,
            creator.MaximumVideoDurationSeconds,
            GetAuditIssue(creator, auditedForPublicPosting));

    private static InitializeTikTokPublishResponse ToInitializeResponse(TikTokPublishJob job, string uploadUrl) =>
        new(job.TikTokPublishJobId, uploadUrl, job.ChunkSizeBytes, job.TotalChunkCount, job.UploadUrlExpiresAtUtc, ConnectionId: job.TikTokConnectionId);

    private static TikTokPublishStatusResponse ToStatusResponse(TikTokPublishJob job) =>
        new(
            job.TikTokPublishJobId,
            job.Status,
            job.FailureReason,
            job.UploadedBytes,
            job.PublicPostIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            job.UpdatedAtUtc,
            TikTokPublishStatuses.IsTerminal(job.Status),
            job.TikTokConnectionId, job.CreatorUsernameSnapshot, job.CreatorNicknameSnapshot, job.CreatedAtUtc);

    private static AccountApiException MapProviderError(TikTokProviderException exception) =>
        exception.ProviderCode switch
        {
            "access_token_invalid" => Error(409, "tiktok_reconnect_required", "Quyền truy cập TikTok không còn hợp lệ. Vui lòng kết nối lại."),
            "scope_not_authorized" => Error(403, "tiktok_publish_scope_missing", "Tài khoản chưa cấp quyền đăng video cho ứng dụng."),
            "rate_limit_exceeded" => Error(429, "tiktok_rate_limit_exceeded", "TikTok đang giới hạn số yêu cầu. Vui lòng thử lại sau."),
            _ => Error(exception.StatusCode is >= 400 and < 500 ? exception.StatusCode : 502,
                $"tiktok_{NormalizeProviderCode(exception.ProviderCode)}",
                "TikTok không thể xử lý yêu cầu. Vui lòng kiểm tra cấu hình/quyền và thử lại.")
        };

    private static AccountApiException ProviderUnavailable() =>
        Error(502, "tiktok_provider_unavailable", "Không thể kết nối TikTok. Vui lòng thử lại sau.");

    private static string NormalizeProviderCode(string code)
    {
        var normalized = new string((code ?? string.Empty).Take(80)
            .Select(x => char.IsAsciiLetterOrDigit(x) || x == '_' ? char.ToLowerInvariant(x) : '_')
            .ToArray()).Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? "provider_error" : normalized;
    }

    private static AccountApiException Error(int status, string code, string message) => new(status, code, message);
}
