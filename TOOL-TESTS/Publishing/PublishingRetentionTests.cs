using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Publishing;

namespace TOOL_TESTS.Publishing;

public sealed class PublishingRetentionTests
{
    [Fact]
    public async Task Cleanup_KeepsHistoryAndLiveMedia_RemovesExpiredPayloadsEvenWhenWorkerStopped()
    {
        var root = Path.Combine(Path.GetTempPath(), "publishing-retention-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            await using var db = new PublishingDbContext(new DbContextOptionsBuilder<PublishingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var expired = new PublishingRun { RunId = Guid.NewGuid(), Status = "Completed", DeadlineAtUtc = DateTime.UtcNow.AddDays(-8), MediaSha256 = new('a', 64), MediaSizeBytes = 32 };
            var live = new PublishingRun { RunId = Guid.NewGuid(), Status = "Publishing", DeadlineAtUtc = DateTime.UtcNow.AddDays(-8), LeaseUntilUtc = DateTime.UtcNow.AddMinutes(5), MediaSha256 = new('b', 64) };
            var recent = new PublishingRun { RunId = Guid.NewGuid(), Status = "Completed", DeadlineAtUtc = DateTime.UtcNow, MediaSha256 = new('c', 64) };
            db.Runs.AddRange(expired, live, recent);
            db.Deliveries.Add(new() { DeliveryId = Guid.NewGuid(), RunId = expired.RunId, Status = "Completed", ProtectedUploadUrl = "encrypted-upload", ExternalId = "video_id" });
            db.Images.Add(new() { ImageId = Guid.NewGuid(), ExpiresAtUtc = DateTime.UtcNow.AddDays(-1), ProtectedPayload = [1, 2] });
            db.OAuthSessions.Add(new() { OAuthSessionId = Guid.NewGuid(), ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1) }); await db.SaveChangesAsync();
            foreach (var run in new[] { expired, live, recent }) await File.WriteAllBytesAsync(Path.Combine(root, run.RunId.ToString("N") + ".mp4"), [1, 2]);
            await File.WriteAllTextAsync(Path.Combine(root, "keep.txt"), "unrelated");
            var retention = new PublishingRetention(db, Options.Create(new PublishingOptions { Enabled = true, WorkerEnabled = false, EmergencyDisabled = true, MediaRoot = root }), TimeProvider.System);
            await retention.SweepAsync(default); await retention.SweepAsync(default);
            Assert.False(File.Exists(Path.Combine(root, expired.RunId.ToString("N") + ".mp4")));
            Assert.True(File.Exists(Path.Combine(root, live.RunId.ToString("N") + ".mp4")));
            Assert.True(File.Exists(Path.Combine(root, recent.RunId.ToString("N") + ".mp4"))); Assert.True(File.Exists(Path.Combine(root, "keep.txt")));
            Assert.Equal(3, await db.Runs.CountAsync()); Assert.Equal("Completed", expired.Status); Assert.Null(expired.MediaSha256);
            var delivery = await db.Deliveries.SingleAsync(); Assert.Equal("video_id", delivery.ExternalId); Assert.Null(delivery.ProtectedUploadUrl);
            Assert.Empty(db.Images); Assert.Empty(db.OAuthSessions);
        }
        finally
        {
            // Own freshly-created fixture files only; no recursive directory traversal.
            foreach (var file in Directory.EnumerateFiles(root)) File.Delete(file);
            Directory.Delete(root);
        }
    }
}
