using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.TikTok;
using TOOL_SHARED.Contracts.Publishing;

namespace TOOL_SERVER.Publishing;

internal sealed class PublishingReviewService(PublishingDbContext db, PublishingService service,
    IGenerationAccessService access, ITikTokService tikTok, PublishingMedia media, TimeProvider time)
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    internal async Task<PublishingRun> OwnedAsync(Guid org, Guid runId, string user, Guid device, CancellationToken ct)
    {
        service.RequireEnabled();
        await access.RequireAsync(user, device, org, null, ct);
        return await db.Runs.SingleOrDefaultAsync(x => x.RunId == runId && x.OrganizationId == org && x.UserId == user, ct)
            ?? throw PublishingCalendar.Error("publishing_run_missing", "Không tìm thấy lượt chạy trong tài khoản/tổ chức này.", 404);
    }

    public async Task ApproveAsync(ApprovePublishingRunRequest request, string user, Guid device, Guid session, CancellationToken ct)
    {
        var run = await OwnedAsync(request.OrganizationId, request.RunId, user, device, ct);
        if (run.LeaseUntilUtc > Now) throw PublishingCalendar.Error("publishing_run_busy", "Lượt chạy đang cập nhật. Hãy thử lại sau.", 409);
        if (run.Status is not ("AwaitingReview" or "ReadyToPublish") || run.MediaSha256 != request.MediaSha256 || Now >= run.DeadlineAtUtc)
            throw PublishingCalendar.Error("publishing_review_changed", "Video đã thay đổi trạng thái hoặc đã quá hạn đăng.", 409);
        var input = PublishingService.Read<PublishingScheduleInput>(run.InputJson);
        if (request.Targets is null || request.Targets.Count != input.Targets.Count ||
            !input.Targets.Select(x => (x.Platform, x.ConnectionId)).OrderBy(x => x).SequenceEqual(request.Targets.Select(x => (x.Platform, x.ConnectionId)).OrderBy(x => x)))
            throw PublishingCalendar.Error("publishing_review_targets", "Danh sách tài khoản phải khớp lượt tạo video.");
        var deliveries = await db.Deliveries.Where(x => x.RunId == run.RunId).ToListAsync(ct);
        if (!request.Approve)
        {
            foreach (var d in deliveries.Where(x => x.Status == "Pending")) { d.Status = "Cancelled"; d.Message = "Người dùng không duyệt đăng video."; }
            run.Status = deliveries.Any(x => x.Status == "Completed") ? "PartialFailure" : "Cancelled";
            run.UpdatedAtUtc = Now; await db.SaveChangesAsync(ct); return;
        }
        await service.RequireSessionAsync(user, device, session, ct);
        await using (var verified = await media.OpenVerifiedAsync(run, ct)) { }
        foreach (var target in request.Targets)
        {
            var original = input.Targets.Single(x => x.Platform == target.Platform && x.ConnectionId == target.ConnectionId);
            if (target.Platform != "TikTok")
            {
                if (target != original) throw PublishingCalendar.Error("publishing_review_targets", "Cài đặt Facebook/YouTube đã được chốt trong lịch.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(target.Title) || target.Title.Length > 1000) throw PublishingCalendar.Error("publishing_tiktok_title", "Nhập caption TikTok tối đa 1.000 ký tự.");
            var creator = await tikTok.GetCreatorInfoAsync(user, ct, target.ConnectionId);
            if (creator.PublishingIssue is not null || creator.MaximumVideoDurationSeconds < input.DurationSeconds ||
                !creator.PrivacyLevelOptions.Contains(target.Privacy) || target.BrandContent && target.Privacy == "SELF_ONLY")
                throw PublishingCalendar.Error("publishing_tiktok_settings", creator.PublishingIssue?.Message ?? "Chọn quyền riêng tư và nhãn thương mại phù hợp với tài khoản TikTok.", 409);
            var delivery = deliveries.Single(x => x.Platform == target.Platform && x.ConnectionId == target.ConnectionId);
            if (delivery.Status != "Pending") throw PublishingCalendar.Error("publishing_review_changed", "Lượt TikTok đã bắt đầu gửi.", 409);
            delivery.SettingsJson = PublishingService.Write(target); delivery.AccountName = creator.CreatorNickname; delivery.UpdatedAtUtc = Now;
        }
        run.ReviewedAtUtc = Now; run.ReviewedByUserId = user; run.DeviceId = device; run.SessionId = session;
        run.Status = "ReadyToPublish"; run.Message = null; run.NextCheckAtUtc = Now; run.UpdatedAtUtc = Now;
        await db.SaveChangesAsync(ct);
    }

    public async Task ActionAsync(PublishingRunActionRequest request, string user, Guid device, Guid session, CancellationToken ct)
    {
        var run = await OwnedAsync(request.OrganizationId, request.RunId, user, device, ct);
        if (run.LeaseUntilUtc > Now) throw PublishingCalendar.Error("publishing_run_busy", "Lượt chạy đang xử lý. Hãy thử lại sau.", 409);
        if (request.Action == "Cancel")
        {
            if (run.Status == "Completed") throw PublishingCalendar.Error("publishing_already_posted", "Video đã đăng; không thể thu hồi bằng thao tác hủy lịch.", 409);
            var pending = await db.Deliveries.Where(x => x.RunId == run.RunId && x.Status == "Pending").ToListAsync(ct);
            foreach (var d in pending) { d.Status = "Cancelled"; d.Message = "Người dùng hủy lượt đăng."; }
            run.Status = "Cancelled"; run.Message = "Đã dừng bước tiếp theo. Request AI hoặc bài đã gửi vẫn được nền tảng xử lý.";
        }
        else if (request.Action == "Resume")
        {
            if (run.Status != "NeedsAttention" || run.ResumeStatus is null || Now >= run.DeadlineAtUtc)
                throw PublishingCalendar.Error("publishing_resume_invalid", "Lượt này không thể tiếp tục hoặc đã quá hạn đăng.", 409);
            await service.RequireSessionAsync(user, device, session, ct);
            var schedule = await db.Schedules.AsNoTracking().SingleAsync(x => x.ScheduleId == run.ScheduleId, ct);
            if (schedule.Status is not ("Active" or "Completed") || schedule.ConsentAtUtc is null)
                throw PublishingCalendar.Error("publishing_schedule_paused", "Kích hoạt lại lịch trước khi tiếp tục lượt đang dừng.", 409);
            run.Status = run.ResumeStatus; run.ResumeStatus = null; run.ErrorCode = null; run.Message = null;
            run.DeviceId = device; run.SessionId = session; run.NextCheckAtUtc = Now;
        }
        else throw PublishingCalendar.Error("publishing_action_invalid", "Thao tác không hợp lệ.");
        run.UpdatedAtUtc = Now; await db.SaveChangesAsync(ct);
    }
}
