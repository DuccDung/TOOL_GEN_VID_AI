using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace TOOL_SERVER.Publishing;

internal sealed class PublishingRetention(PublishingDbContext db, IOptions<PublishingOptions> options, TimeProvider time)
{
    // Keep occurrence/audit records. Only temporary media, expired image payloads and OAuth material are removed.
    internal async Task SweepAsync(CancellationToken ct)
    {
        if (!options.Value.Enabled) return; // The schema may not exist on disabled deployments.
        var now = time.GetUtcNow().UtcDateTime;
        var cutoff = now.AddDays(-7);
        var root = options.Value.MediaRoot;
        if (!string.IsNullOrWhiteSpace(root))
        {
            PublishingMedia.ValidatePath(root); root = Path.GetFullPath(root);
            var runs = await db.Runs.Where(x => x.DeadlineAtUtc < cutoff && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now) &&
                (x.MediaSha256 != null || x.Status != "Completed" && x.Status != "Failed" && x.Status != "PartialFailure" && x.Status != "Skipped" && x.Status != "Cancelled"))
                .OrderBy(x => x.DeadlineAtUtc).Take(100).ToListAsync(ct);
            foreach (var run in runs)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var suffix in new[] { ".mp4", ".mp4.part" })
                {
                    var file = Path.Combine(root, run.RunId.ToString("N") + suffix);
                    PublishingMedia.ValidatePath(file);
                    if (File.Exists(file)) File.Delete(file); // Deterministic, verified file only; never recursive.
                }
                run.MediaSha256 = null; run.MediaSizeBytes = 0; run.ResumeStatus = null;
                var deliveries = await db.Deliveries.Where(x => x.RunId == run.RunId).ToListAsync(ct);
                foreach (var delivery in deliveries)
                {
                    delivery.ProtectedUploadUrl = null;
                    if (delivery.Status is "Pending" or "Initializing" or "Uploading" or "Uploaded")
                    { delivery.Status = "Cancelled"; delivery.Message = "Đã hết thời gian lưu video của lượt này."; }
                    else if (delivery.Status is "Processing" or "Transferring")
                    { delivery.Status = "Unknown"; delivery.Message = "Đã hết thời gian đối soát tự động. Kiểm tra kết quả trên nền tảng."; }
                }
                if (!PublishingDispatcher.Terminal(run.Status) || run.Status == "NeedsAttention")
                { run.Status = deliveries.Any(x => x.Status == "Completed") ? "PartialFailure" : "Failed"; run.Message = "Lượt đã hết hạn lưu video. Lịch sử được giữ để đối soát."; }
                await db.SaveChangesAsync(ct);
            }
            // A crash may leave a file without a persisted media hash. Only known expired run IDs are eligible.
            if (Directory.Exists(root))
                foreach (var file in Directory.EnumerateFiles(root, "*.mp4*", SearchOption.TopDirectoryOnly).Take(200))
                {
                    var name = Path.GetFileName(file);
                    var suffix = name.EndsWith(".mp4.part", StringComparison.Ordinal) ? ".mp4.part" : name.EndsWith(".mp4", StringComparison.Ordinal) ? ".mp4" : null;
                    if (suffix is null) continue;
                    var stem = name[..^suffix.Length];
                    if (!Guid.TryParseExact(stem, "N", out var id) || File.GetLastWriteTimeUtc(file) >= cutoff) continue;
                    if (!await db.Runs.AnyAsync(x => x.RunId == id && x.DeadlineAtUtc < cutoff && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= now), ct)) continue;
                    PublishingMedia.ValidatePath(file); File.Delete(file);
                }
        }
        var expiredImages = await db.Images.Where(x => x.ExpiresAtUtc <= now).Take(200).ToListAsync(ct);
        var expiredOAuth = await db.OAuthSessions.Where(x => x.ExpiresAtUtc <= now).Take(500).ToListAsync(ct);
        db.Images.RemoveRange(expiredImages); db.OAuthSessions.RemoveRange(expiredOAuth);
        await db.SaveChangesAsync(ct);
    }
}
