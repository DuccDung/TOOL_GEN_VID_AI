using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.TikTok;
using TOOL_SHARED.Contracts.Publishing;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_SERVER.Publishing;

internal interface IPublishingPublisher { Task StepAsync(PublishingRun run, CancellationToken ct); }

internal sealed class PublishingPublisher(PublishingDbContext db, PublishingSocialService social,
    ITikTokService tikTok, PublishingMedia media, IDataProtectionProvider protection, TimeProvider time) : IPublishingPublisher
{
    private DateTime Now => time.GetUtcNow().UtcDateTime;
    private IDataProtector Protector(PublishingDelivery d) => protection.CreateProtector("VideoMaker.Publishing.Upload.v1", d.RunId.ToString("N"), d.DeliveryId.ToString("N"));

    public async Task StepAsync(PublishingRun run, CancellationToken ct)
    {
        var deliveries = await db.Deliveries.Where(x => x.RunId == run.RunId).OrderBy(x => x.Platform).ToListAsync(ct);
        foreach (var delivery in deliveries)
        {
            if (delivery.Status is "Completed" or "Failed" or "Unknown" or "Cancelled") continue;
            if (Now >= run.DeadlineAtUtc && delivery.Status is "Pending" or "Initializing" or "Uploading" or "Uploaded")
            { delivery.Status = "Cancelled"; delivery.Message = "Đã quá hạn đăng trong lịch."; await SaveAsync(delivery, ct); continue; }
            if (delivery.Platform == "TikTok" && run.ReviewedAtUtc is null) continue;
            try { await DeliverAsync(run, delivery, ct); }
            catch (Exception ex) when (ex is TOOL_SERVER.Authentication.AccountApiException or HttpRequestException or System.Text.Json.JsonException or IOException)
            {
                // Never create another external post after an ambiguous mutation.
                delivery.Status = delivery.Status is "Pending" ? "Failed" : "Unknown";
                delivery.Message = ex is TOOL_SERVER.Authentication.AccountApiException known ? known.Message : "Chưa xác định kết quả gửi bài. Cần đối soát bài cũ trước khi thử đăng lại.";
                await SaveAsync(delivery, ct);
            }
        }
        if (deliveries.All(x => x.Status == "Completed")) run.Status = "Completed";
        else if (deliveries.All(x => x.Status is "Completed" or "Failed" or "Unknown" or "Cancelled")) run.Status = deliveries.Any(x => x.Status == "Completed") ? "PartialFailure" : "NeedsAttention";
        else if (run.ReviewedAtUtc is null && deliveries.Any(x => x.Platform == "TikTok" && x.Status == "Pending")) run.Status = "AwaitingReview";
        else run.Status = "Publishing";
        run.UpdatedAtUtc = Now; await db.SaveChangesAsync(ct);
    }

    private async Task DeliverAsync(PublishingRun run, PublishingDelivery delivery, CancellationToken ct)
    {
        var input = PublishingService.Read<PublishingScheduleInput>(run.InputJson);
        var target = PublishingService.Read<PublishingTarget>(delivery.SettingsJson);
        if (delivery.Platform == "TikTok") { await TikTokAsync(run, delivery, input, target, ct); return; }
        var settings = social.RequireConfigured(target.Platform, target.Privacy == "public");
        var (connection, token) = await social.TokenAsync(delivery.ConnectionId, run.UserId, delivery.Platform, ct);
        delivery.AccountName = connection.DisplayName;
        if (delivery.Status == "Pending")
        {
            // Validate the exact bytes before allocating a publishing session.
            await using (var verified = await media.OpenVerifiedAsync(run, ct)) { }
            delivery.Status = "Initializing"; await SaveAsync(delivery, ct);
            if (delivery.Platform == "YouTube")
            {
                using var request = PublishingSocialService.Authorized(HttpMethod.Post, "https://www.googleapis.com/upload/youtube/v3/videos?uploadType=resumable&part=snippet,status", token);
                request.Content = JsonContent.Create(new { snippet = new { title = input.Title, description = input.Description, categoryId = "22" },
                    status = new { privacyStatus = target.Privacy, selfDeclaredMadeForKids = target.MadeForKids, containsSyntheticMedia = true } });
                request.Headers.Add("X-Upload-Content-Length", run.MediaSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
                request.Headers.Add("X-Upload-Content-Type", "video/mp4");
                using var response = await social.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode || response.Headers.Location is null) throw PublishingCalendar.Error("publishing_upload_init_failed", "YouTube chưa cấp phiên tải video.", 409);
                PublishingSocialService.ValidateUri(response.Headers.Location);
                delivery.ProtectedUploadUrl = Protector(delivery).Protect(response.Headers.Location.AbsoluteUri);
            }
            else
            {
                using var request = PublishingSocialService.Authorized(HttpMethod.Post, $"https://graph.facebook.com/{settings.ApiVersion}/{connection.ExternalId}/video_reels", token);
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["upload_phase"] = "start" });
                using var response = await social.JsonAsync(request, ct);
                delivery.ExternalId = SafeId(response.RootElement.GetProperty("video_id").GetString()!);
                var uploadUrl = new Uri(response.RootElement.GetProperty("upload_url").GetString()!);
                PublishingSocialService.ValidateUri(uploadUrl);
                delivery.ProtectedUploadUrl = Protector(delivery).Protect(uploadUrl.AbsoluteUri);
            }
            delivery.Status = "Uploading"; await SaveAsync(delivery, ct); return;
        }
        if (delivery.Status == "Initializing")
        { delivery.Status = "Unknown"; delivery.Message = "Khởi tạo đăng bài bị gián đoạn. Kiểm tra phiên cũ trước khi gửi lại."; await SaveAsync(delivery, ct); return; }
        if (delivery.Status == "Uploading")
        {
            var url = Protector(delivery).Unprotect(delivery.ProtectedUploadUrl!);
            await using var file = await media.OpenVerifiedAsync(run, ct);
            if (delivery.Platform == "YouTube")
            {
                // Resumable status lookup is safe after a restart, including a lost success response.
                using var probe = PublishingSocialService.Authorized(HttpMethod.Put, url, token);
                probe.Content = new ByteArrayContent([]); probe.Content.Headers.TryAddWithoutValidation("Content-Range", $"bytes */{file.Length}");
                using var status = await social.SendAsync(probe, ct);
                if (status.IsSuccessStatusCode)
                {
                    using var result = await PublishingSocialService.ReadJsonAsync(status, ct);
                    delivery.ExternalId = SafeId(result.RootElement.GetProperty("id").GetString()!); delivery.Status = "Processing";
                    await SaveAsync(delivery, ct); return;
                }
                if ((int)status.StatusCode != 308) throw PublishingCalendar.Error("publishing_upload_expired", "Phiên YouTube không còn xác định được. Kiểm tra kênh trước khi đăng lại.", 409);
                var offset = 0L;
                if (status.Headers.TryGetValues("Range", out var ranges))
                {
                    var match = Regex.Match(ranges.Single(), @"^bytes=0-(\d+)$");
                    if (!match.Success || !long.TryParse(match.Groups[1].Value, out var end)) throw PublishingCalendar.Error("publishing_upload_range", "YouTube trả về vị trí upload không hợp lệ.", 502);
                    offset = checked(end + 1);
                }
                if (offset < 0 || offset >= file.Length) throw PublishingCalendar.Error("publishing_upload_range", "Vị trí upload không khớp video.", 502);
                file.Position = offset;
                using var upload = PublishingSocialService.Authorized(HttpMethod.Put, url, token);
                upload.Content = new StreamContent(file); upload.Content.Headers.ContentType = new("video/mp4");
                upload.Content.Headers.ContentLength = file.Length - offset;
                upload.Content.Headers.ContentRange = new(offset, file.Length - 1, file.Length);
                using var response = await social.SendAsync(upload, ct);
                if ((int)response.StatusCode == 308) return;
                using var posted = await PublishingSocialService.ReadJsonAsync(response, ct);
                delivery.ExternalId = SafeId(posted.RootElement.GetProperty("id").GetString()!); delivery.Status = "Processing";
            }
            else
            {
                // The upload is bound to the persisted Facebook video ID. An ambiguous response is not blindly retried.
                delivery.Status = "Transferring"; await SaveAsync(delivery, ct);
                using var upload = new HttpRequestMessage(HttpMethod.Post, url);
                upload.Headers.Authorization = new AuthenticationHeaderValue("OAuth", token);
                upload.Headers.Add("offset", "0"); upload.Headers.Add("file_size", file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
                upload.Content = new StreamContent(file); upload.Content.Headers.ContentLength = file.Length; upload.Content.Headers.ContentType = new("application/octet-stream");
                using var response = await social.JsonAsync(upload, ct);
                if (!response.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean()) throw PublishingCalendar.Error("publishing_upload_failed", "Facebook chưa xác nhận nhận đủ video.", 409);
                delivery.Status = "Uploaded";
            }
            await SaveAsync(delivery, ct); return;
        }
        if (delivery.Status == "Transferring")
        { delivery.Status = "Unknown"; delivery.Message = "Upload bị gián đoạn. Cần kiểm tra video trên nền tảng trước khi tiếp tục."; await SaveAsync(delivery, ct); return; }
        if (delivery.Status == "Uploaded" && delivery.Platform == "Facebook")
        {
            delivery.Status = "Processing"; await SaveAsync(delivery, ct);
            using var finish = PublishingSocialService.Authorized(HttpMethod.Post, $"https://graph.facebook.com/{settings.ApiVersion}/{connection.ExternalId}/video_reels", token);
            finish.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["upload_phase"] = "finish", ["video_id"] = delivery.ExternalId!,
                ["video_state"] = "PUBLISHED", ["title"] = input.Title, ["description"] = input.Description });
            using var result = await social.JsonAsync(finish, ct);
            if (!result.RootElement.TryGetProperty("success", out var success) || !success.GetBoolean()) throw PublishingCalendar.Error("publishing_publish_failed", "Facebook chưa xác nhận xuất bản.", 409);
            return;
        }
        if (delivery.Status == "Processing")
        {
            if (delivery.Platform == "YouTube")
            {
                using var query = PublishingSocialService.Authorized(HttpMethod.Get, $"https://www.googleapis.com/youtube/v3/videos?part=status,processingDetails&id={delivery.ExternalId}", token);
                using var response = await social.JsonAsync(query, ct);
                var item = response.RootElement.GetProperty("items").EnumerateArray().SingleOrDefault();
                if (item.ValueKind == System.Text.Json.JsonValueKind.Undefined) throw PublishingCalendar.Error("publishing_video_missing", "Không tìm thấy video trên YouTube.", 409);
                var status = item.GetProperty("status").GetProperty("uploadStatus").GetString();
                if (status == "processed") { delivery.Status = "Completed"; delivery.PostUrl = "https://www.youtube.com/watch?v=" + delivery.ExternalId; }
                else if (status is "failed" or "rejected" or "deleted") { delivery.Status = "Failed"; delivery.Message = "YouTube không chấp nhận video đã tải lên."; }
            }
            else
            {
                using var query = PublishingSocialService.Authorized(HttpMethod.Get, $"https://graph.facebook.com/{settings.ApiVersion}/{delivery.ExternalId}?fields=status", token);
                using var response = await social.JsonAsync(query, ct);
                var status = response.RootElement.GetProperty("status");
                if (status.TryGetProperty("publishing_phase", out var phase) && phase.TryGetProperty("status", out var state) && state.GetString() == "complete")
                { delivery.Status = "Completed"; delivery.PostUrl = "https://www.facebook.com/reel/" + delivery.ExternalId; }
                else if (status.TryGetProperty("video_status", out var vs) && vs.GetString() == "error")
                { delivery.Status = "Failed"; delivery.Message = "Facebook không xử lý được video đã tải lên."; }
            }
            await SaveAsync(delivery, ct);
        }
    }

    private async Task TikTokAsync(PublishingRun run, PublishingDelivery delivery, PublishingScheduleInput input, PublishingTarget target, CancellationToken ct)
    {
        if (delivery.Status == "Pending")
        {
            await using (var verified = await media.OpenVerifiedAsync(run, ct)) { }
            delivery.Status = "Initializing"; await SaveAsync(delivery, ct);
            var initialized = await tikTok.InitializePublishAsync(run.UserId, new(delivery.DeliveryId, target.Title ?? input.Title, target.Privacy,
                true, true, true, target.BrandContent, target.BrandOrganic, true, run.MediaSizeBytes, input.DurationSeconds, "video/mp4", target.ConnectionId), ct);
            if (initialized.BlockedCreator is not null) throw PublishingCalendar.Error("publishing_tiktok_blocked", initialized.BlockedCreator.PublishingIssue?.Message ?? "Tài khoản TikTok chưa đủ điều kiện đăng.", 409);
            delivery.ExternalId = initialized.PublishJobId.ToString("D");
            delivery.ProtectedUploadUrl = Protector(delivery).Protect(PublishingService.Write(initialized));
            delivery.Status = "Uploading"; await SaveAsync(delivery, ct); return;
        }
        if (delivery.Status == "Uploading")
        {
            var plan = PublishingService.Read<InitializeTikTokPublishResponse>(Protector(delivery).Unprotect(delivery.ProtectedUploadUrl!));
            if (plan.UploadUrlExpiresAtUtc <= Now || plan.TotalChunkCount is < 1 or > 100 || plan.ChunkSizeBytes is <= 0 or > 64L * 1024 * 1024 ||
                run.MediaSizeBytes - plan.ChunkSizeBytes * (plan.TotalChunkCount - 1L) is <= 0 or > 128L * 1024 * 1024)
                throw PublishingCalendar.Error("publishing_upload_plan_invalid", "Phiên upload TikTok đã hết hạn hoặc không hợp lệ.", 409);
            delivery.Status = "Transferring"; await SaveAsync(delivery, ct);
            await using var file = await media.OpenVerifiedAsync(run, ct);
            long offset = 0;
            for (var chunk = 0; chunk < plan.TotalChunkCount; chunk++)
            {
                var length = chunk == plan.TotalChunkCount - 1 ? file.Length - offset : plan.ChunkSizeBytes;
                var bytes = new byte[checked((int)length)]; await file.ReadExactlyAsync(bytes, ct);
                using var request = new HttpRequestMessage(HttpMethod.Put, plan.UploadUrl) { Content = new ByteArrayContent(bytes) };
                request.Content.Headers.ContentType = new("video/mp4"); request.Content.Headers.ContentRange = new(offset, offset + length - 1, file.Length);
                using var response = await social.SendAsync(request, ct);
                var expected = chunk == plan.TotalChunkCount - 1 ? HttpStatusCode.Created : HttpStatusCode.PartialContent;
                if (response.StatusCode != expected) throw PublishingCalendar.Error("publishing_tiktok_upload_failed", "TikTok chưa xác nhận phần video đang tải. Kiểm tra lượt đăng cũ trước khi thử lại.", 409);
                offset += length;
            }
            delivery.Status = "Processing"; await SaveAsync(delivery, ct); return;
        }
        if (delivery.Status is "Processing" or "Transferring")
        {
            var status = await tikTok.ReadPublishStatusAsync(run.UserId, Guid.Parse(delivery.ExternalId!), ct);
            if (status.IsTerminal) { delivery.Status = status.Status == "PUBLISH_COMPLETE" ? "Completed" : "Failed"; delivery.Message = status.FailureReason; }
            await SaveAsync(delivery, ct); return;
        }
        if (delivery.Status == "Initializing")
        { delivery.Status = "Unknown"; delivery.Message = "Khởi tạo TikTok bị gián đoạn. Đối soát trong lịch sử TikTok trước khi đăng lại."; await SaveAsync(delivery, ct); }
    }

    private async Task SaveAsync(PublishingDelivery d, CancellationToken ct) { d.UpdatedAtUtc = Now; await db.SaveChangesAsync(ct); }
    private static string SafeId(string value)
    {
        if (!Regex.IsMatch(value, @"^[a-zA-Z0-9_-]{1,150}$")) throw PublishingCalendar.Error("publishing_identity_invalid", "Mã video của nền tảng không hợp lệ.", 502);
        return value;
    }
}
