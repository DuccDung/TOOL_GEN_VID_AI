using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.TikTok.Domain;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_SERVER.TikTok;

public sealed partial class TikTokService
{
    public async Task<TikTokFeatureStateResponse> GetConnectionsStateAsync(string userId, CancellationToken cancellationToken)
    {
        var access = await credentialRuntime.GetUserAccessAsync(userId, cancellationToken);
        var connections = await db.Connections.AsNoTracking().Where(x => x.UserId == userId)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.TikTokConnectionId).ToListAsync(cancellationToken);
        var jobs = await db.PublishJobs.AsNoTracking().Where(x => x.UserId == userId &&
            x.Status != TikTokPublishStatuses.Complete && x.Status != TikTokPublishStatuses.Failed)
            .OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
        var recent = await db.PublishJobs.AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedAtUtc).ThenByDescending(x => x.TikTokPublishJobId).Take(100).ToListAsync(cancellationToken);
        var attempts = await db.PublishAttempts.AsNoTracking().Where(x => x.UserId == userId && x.TikTokPublishJobId == null)
            .OrderByDescending(x => x.CreatedAtUtc).Take(20)
            .Select(x => new TikTokPublishAttemptSummary(x.ClientRequestId, x.TikTokConnectionId, x.Status, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return new TikTokFeatureStateResponse(access.Enabled, access.Configured, null,
            IsCredentialVerification: access.IsCredentialVerification, UnavailableReason: access.UnavailableReason,
            Connections: connections.Select(ToSummary).ToArray(), ActivePublishes: jobs.Select(ToStatusResponse).ToArray(),
            MultiAccountEnabled: _options.MultiAccountEnabled, RecentPublishes: recent.Select(ToStatusResponse).ToArray(), UnresolvedAttempts: attempts);
    }

    public async Task<TikTokPublishHistoryResponse> GetPublishHistoryAsync(
        string userId, Guid? connectionId, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (page < 1 || page > 100_000 || pageSize < 1 || pageSize > 100)
            throw Error(400, "tiktok_invalid_page", "Phân trang lịch sử TikTok không hợp lệ.");
        if (connectionId is { } id) _ = await FindOwnedConnectionAsync(userId, id, cancellationToken);
        var query = db.PublishJobs.AsNoTracking().Where(x => x.UserId == userId);
        if (connectionId is { } selected) query = query.Where(x => x.TikTokConnectionId == selected);
        var total = await query.CountAsync(cancellationToken);
        var jobs = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.TikTokPublishJobId)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new TikTokPublishHistoryResponse(jobs.Select(ToStatusResponse).ToArray(), page, pageSize, total);
    }

    public async Task<TikTokPublishStatusResponse> ReadPublishStatusAsync(string userId, Guid publishJobId, CancellationToken cancellationToken)
    {
        var job = await db.PublishJobs.AsNoTracking().SingleOrDefaultAsync(
            x => x.UserId == userId && x.TikTokPublishJobId == publishJobId, cancellationToken)
            ?? throw Error(404, "tiktok_publish_not_found", "Không tìm thấy phiên đăng TikTok.");
        return ToStatusResponse(job);
    }

    private async Task RequireLegacyClientAsync(string userId, CancellationToken cancellationToken)
    {
        if (await db.Connections.CountAsync(x => x.UserId == userId, cancellationToken) > 1)
            throw Error(409, "tiktok_client_update_required", "Tài khoản có nhiều kết nối TikTok. Hãy cập nhật VideoMaker để chọn đúng tài khoản.");
    }

    private async Task<TikTokConnection> FindOwnedConnectionAsync(string userId, Guid connectionId, CancellationToken cancellationToken) =>
        await db.Connections.SingleOrDefaultAsync(x => x.UserId == userId && x.TikTokConnectionId == connectionId, cancellationToken)
            ?? throw Error(404, "tiktok_connection_not_found", "Không tìm thấy tài khoản TikTok.");

    private static string HashAppKey(string clientKey) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientKey)));

    private async Task RecordAttemptFailureAsync(TikTokPublishAttempt attempt, bool rejected)
    {
        attempt.Status = rejected ? "Rejected" : "Unknown";
        attempt.UpdatedAtUtc = UtcNow();
        // Initializing is also non-replayable if recording the final error fails.
        try { await db.SaveChangesAsync(CancellationToken.None); }
        catch (DbUpdateException) { db.Entry(attempt).State = EntityState.Detached; }
    }
}
