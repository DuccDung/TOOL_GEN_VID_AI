using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Data;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.TikTok;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Publishing;

namespace TOOL_SERVER.Publishing;

internal sealed class PublishingService(PublishingDbContext db, AccountDbContext accounts,
    IGenerationAccessService access, ITikTokService tikTok, PublishingSocialService social,
    IDataProtectionProvider protection, IOptions<PublishingOptions> options, TimeProvider time)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private DateTime Now => time.GetUtcNow().UtcDateTime;
    private static Exception Missing() => PublishingCalendar.Error("publishing_not_found", "Không tìm thấy lịch trong tổ chức và tài khoản này.", 404);
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    internal static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, Json)!;
    internal static string Write<T>(T value) => JsonSerializer.Serialize(value, Json);

    internal void RequireEnabled()
    {
        if (!options.Value.Enabled || options.Value.EmergencyDisabled)
            throw PublishingCalendar.Error("publishing_disabled", "Lên lịch chưa được bật trên server. Quản trị viên cần triển khai schema và cấu hình chạy lịch.", 409);
    }

    internal async Task RequireSessionAsync(string user, Guid device, Guid session, CancellationToken ct)
    {
        var now = Now;
        if (!await accounts.UserSessions.AsNoTracking().AnyAsync(x => x.SessionId == session && x.UserId == user &&
            x.DeviceId == device && x.Status == "Active" && x.RevokedAtUtc == null && x.AbsoluteExpiresAtUtc > now &&
            !x.Device!.IsRevoked && x.User.AccountStatus == "Active" && x.User.DeletedAtUtc == null, ct))
            throw PublishingCalendar.Error("publishing_session_expired", "Phiên đăng nhập hoặc thiết bị không còn hiệu lực. Đăng nhập và kích hoạt lại lịch.", 403);
    }

    public async Task<PublishingState> StateAsync(Guid org, string user, Guid device, CancellationToken ct)
    {
        // Disabled deployments do not query tables whose migration might not be installed yet.
        if (!options.Value.Enabled || options.Value.EmergencyDisabled)
            return new(false, "Chức năng cần được quản trị viên bật trên server sau khi triển khai dữ liệu và kết nối nền tảng.", [], [], [], social.Readiness());
        await access.RequireProjectAccessAsync(user, device, org, null, ct);
        var schedules = await db.Schedules.AsNoTracking().Where(x => x.OrganizationId == org && x.UserId == user)
            .OrderByDescending(x => x.UpdatedAtUtc).Take(100).ToListAsync(ct);
        var runs = await db.Runs.AsNoTracking().Where(x => x.OrganizationId == org && x.UserId == user)
            .OrderByDescending(x => x.PublishAtUtc).Take(200).ToListAsync(ct);
        var ids = runs.Select(x => x.RunId).ToArray();
        var deliveries = await db.Deliveries.AsNoTracking().Where(x => ids.Contains(x.RunId)).ToListAsync(ct);
        var connections = await ConnectionsAsync(user, ct);
        return new(true, options.Value.WorkerEnabled ? null : "Lịch được lưu nhưng bộ chạy tự động đang tắt trên server.",
            schedules.Select(Summary).ToArray(), runs.Select(x => new PublishingRunSummary(x.RunId, x.ScheduleId,
                Read<PublishingScheduleInput>(x.InputJson).Title, x.GenerateAtUtc, x.PublishAtUtc, x.DeadlineAtUtc, x.Status,
                x.ErrorCode, x.Message, x.ProjectId, x.EstimatedCost, deliveries.Where(d => d.RunId == x.RunId)
                    .Select(d => new PublishingDeliverySummary(d.DeliveryId, d.Platform, d.AccountName, d.Status, d.PostUrl, d.Message)).ToArray(), x.UpdatedAtUtc, x.MediaSha256, Read<PublishingScheduleInput>(x.InputJson), x.Status == "NeedsAttention" && x.ResumeStatus != null && x.DeadlineAtUtc > Now)).ToArray(),
            connections, social.Readiness());
    }

    internal async Task<IReadOnlyList<PublishingConnectionSummary>> ConnectionsAsync(string user, CancellationToken ct)
    {
        var connections = await db.Connections.AsNoTracking().Where(x => x.UserId == user && x.Status != "Disconnected")
            .Select(x => new PublishingConnectionSummary(x.ConnectionId, x.Platform, x.DisplayName, x.Status)).ToListAsync(ct);
        var state = await tikTok.GetConnectionsStateAsync(user, ct);
        connections.AddRange((state.Connections ?? []).Select(x => new PublishingConnectionSummary(x.ConnectionId, "TikTok", x.CreatorNickname,
            x.Scopes.Contains("video.publish") ? x.Status : "MissingPermission")));
        return connections;
    }

    public async Task<PublishingImage> UploadImageAsync(UploadPublishingImageRequest request, string user, Guid device, CancellationToken ct)
    {
        RequireEnabled();
        await access.RequireAsync(user, device, request.OrganizationId, null, ct);
        if (request.Role is not ("Character" or "Product")) throw PublishingCalendar.Error("publishing_image_role", "Vai trò ảnh không hợp lệ.");
        var bytes = ShortVideoOutfitService.ValidateInput(request.Image, request.Image.Info);
        if (await db.Images.CountAsync(x => x.UserId == user, ct) >= 200)
            throw PublishingCalendar.Error("publishing_image_quota", "Đã đạt giới hạn ảnh lịch. Liên hệ quản trị viên để dọn ảnh hết hạn.", 409);
        var id = Guid.NewGuid();
        var encrypted = protection.CreateProtector("VideoMaker.Publishing.Image.v1", user, request.OrganizationId.ToString("N"), id.ToString("N")).Protect(bytes);
        db.Images.Add(new() { ImageId = id, OrganizationId = request.OrganizationId, UserId = user, Role = request.Role,
            InfoJson = Write(request.Image.Info), ProtectedPayload = encrypted, CreatedAtUtc = Now, ExpiresAtUtc = Now.AddDays(460) });
        await db.SaveChangesAsync(ct);
        return new(id, request.Role, request.Image.Info);
    }

    internal async Task<ShortVideoImageInput> ImageAsync(Guid id, Guid org, string user, string role, CancellationToken ct)
    {
        var row = await db.Images.SingleOrDefaultAsync(x => x.ImageId == id && x.OrganizationId == org && x.UserId == user && x.Role == role && x.ExpiresAtUtc > Now, ct)
            ?? throw PublishingCalendar.Error("publishing_image_missing", "Ảnh tham chiếu đã hết hạn hoặc không thuộc lịch.", 409);
        var bytes = protection.CreateProtector("VideoMaker.Publishing.Image.v1", user, org.ToString("N"), id.ToString("N")).Unprotect(row.ProtectedPayload);
        var image = new ShortVideoImageInput(Read<ShortVideoImageInfo>(row.InfoJson), Convert.ToBase64String(bytes));
        _ = ShortVideoOutfitService.ValidateInput(image, image.Info);
        return image;
    }

    public async Task<PublishingScheduleSummary> SaveAsync(SavePublishingScheduleRequest request, string user, Guid device, Guid session, CancellationToken ct)
    {
        RequireEnabled(); await RequireSessionAsync(user, device, session, ct);
        await access.RequireAsync(user, device, request.OrganizationId, null, ct);
        if (request.ScheduleId == Guid.Empty || request.ExpectedRevision < 0) throw PublishingCalendar.Error("publishing_invalid_id", "Mã lịch không hợp lệ.");
        PublishingCalendar.Validate(request.Input, Now);
        _ = await ImageAsync(request.Input.CharacterImageId, request.OrganizationId, user, "Character", ct);
        _ = await ImageAsync(request.Input.ProductImageId, request.OrganizationId, user, "Product", ct);
        await ValidateTargetsAsync(request.Input.Targets, user, false, ct);
        var normalized = request.Input with { Title = request.Input.Title.Trim(), Description = request.Input.Description.Trim() };
        var json = Write(normalized);
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var row = await db.Schedules.SingleOrDefaultAsync(x => x.ScheduleId == request.ScheduleId, ct);
        if (row is not null)
        {
            if (row.UserId != user || row.OrganizationId != request.OrganizationId) throw Missing();
            if (row.Revision != request.ExpectedRevision)
            {
                if (row.InputHash == Hash(json) && row.Revision == request.ExpectedRevision + 1 && row.Status == "Draft") return Summary(row);
                throw PublishingCalendar.Error("publishing_revision_changed", "Lịch đã thay đổi trong phiên khác. Hãy tải lại trước khi lưu.", 409);
            }
            if (row.Status == "Active") throw PublishingCalendar.Error("publishing_pause_before_edit", "Tạm dừng lịch trước khi sửa. Lượt đã tạo giữ nguyên nội dung cũ.", 409);
            row.Revision++;
        }
        else
        {
            if (request.ExpectedRevision != 0) throw Missing();
            if (await db.Schedules.CountAsync(x => x.UserId == user && x.OrganizationId == request.OrganizationId, ct) >= 100)
                throw PublishingCalendar.Error("publishing_schedule_quota", "Mỗi tổ chức/tài khoản hỗ trợ tối đa 100 lịch.", 409);
            row = new() { ScheduleId = request.ScheduleId, OrganizationId = request.OrganizationId, UserId = user, CreatedAtUtc = Now };
            db.Schedules.Add(row);
        }
        row.DeviceId = device; row.SessionId = session; row.InputJson = json; row.InputHash = Hash(json);
        row.Status = "Draft"; row.ConsentAtUtc = null; row.NextPublishAtUtc = null; row.UpdatedAtUtc = Now;
        await db.SaveChangesAsync(ct); if (tx is not null) await tx.CommitAsync(ct);
        return Summary(row);
    }

    public async Task<PublishingScheduleSummary> ChangeAsync(ChangePublishingScheduleRequest request, string user, Guid device, Guid session, CancellationToken ct)
    {
        RequireEnabled(); await RequireSessionAsync(user, device, session, ct);
        await access.RequireAsync(user, device, request.OrganizationId, null, ct);
        var row = await db.Schedules.SingleOrDefaultAsync(x => x.ScheduleId == request.ScheduleId && x.UserId == user && x.OrganizationId == request.OrganizationId, ct) ?? throw Missing();
        if (row.Revision != request.ExpectedRevision) throw PublishingCalendar.Error("publishing_revision_changed", "Lịch đã thay đổi. Hãy tải lại.", 409);
        if (request.Action == "Activate")
        {
            if (!request.ConfirmAutomaticGeneration) throw PublishingCalendar.Error("publishing_consent_required", "Xác nhận tự động tạo video trong giới hạn chi phí cho từng lượt.");
            if (!options.Value.WorkerEnabled) throw PublishingCalendar.Error("publishing_worker_disabled", "Bộ chạy lịch chưa được bật trên server.", 409);
            if (!PublishingMedia.IsConfigured(options.Value)) throw PublishingCalendar.Error("publishing_media_not_ready", "Server cần kho media và FFprobe đã xác minh để chạy lịch.", 409);
            var input = Read<PublishingScheduleInput>(row.InputJson);
            PublishingCalendar.Validate(input, Now);
            await ValidateTargetsAsync(input.Targets, user, true, ct);
            // Never retrospectively charge for missed occurrences on activation/resume.
            row.NextPublishAtUtc = PublishingCalendar.Next(input, Now.AddMinutes(input.LeadMinutes));
            if (row.NextPublishAtUtc is null) throw PublishingCalendar.Error("publishing_no_future_occurrence", "Không còn giờ đăng đủ thời gian chuẩn bị trong khoảng lịch.", 409);
            row.Status = "Active"; row.ConsentAtUtc = Now; row.DeviceId = device; row.SessionId = session;
        }
        else if (request.Action == "Pause") { row.Status = "Paused"; row.NextPublishAtUtc = null; }
        else if (request.Action == "Cancel") { row.Status = "Cancelled"; row.NextPublishAtUtc = null; row.ConsentAtUtc = null; }
        else throw PublishingCalendar.Error("publishing_action_invalid", "Thao tác lịch không hợp lệ.");
        row.Revision++; row.UpdatedAtUtc = Now;
        await db.SaveChangesAsync(ct); return Summary(row);
    }

    internal async Task ValidateTargetsAsync(IReadOnlyList<PublishingTarget> targets, string user, bool requireReady, CancellationToken ct)
    {
        var connections = await ConnectionsAsync(user, ct);
        foreach (var target in targets)
        {
            if (!connections.Any(x => x.ConnectionId == target.ConnectionId && x.Platform == target.Platform && x.Status == "Connected"))
                throw PublishingCalendar.Error("publishing_connection_unavailable", "Một tài khoản đăng chưa được kết nối hoặc không thuộc người dùng hiện hành.", 409);
            if (requireReady && target.Platform != "TikTok")
            {
                social.RequireConfigured(target.Platform, target.Privacy == "public");
                _ = await social.TokenAsync(target.ConnectionId, user, target.Platform, ct);
            }
        }
    }

    internal static PublishingScheduleSummary Summary(PublishingSchedule row) => new(row.ScheduleId, row.Revision, row.Status,
        Read<PublishingScheduleInput>(row.InputJson), row.NextPublishAtUtc, row.UpdatedAtUtc);
}
