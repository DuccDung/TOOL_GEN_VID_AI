using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Data;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Publishing;
using TOOL_SHARED.Contracts.Publishing;

namespace TOOL_TESTS.Publishing;

public sealed class PublishingPublisherTests
{
    [Fact]
    public async Task YouTube_ResumesAfterLostUploadSuccessWithoutCreatingAnotherPost()
    {
        await using var f = new Fixture(); await f.Seed();
        f.Http.Responses.Enqueue(_ =>
        {
            var result = new HttpResponseMessage(HttpStatusCode.OK); result.Headers.Location = new("https://www.googleapis.com/upload/youtube/v3/videos?upload_id=fake"); return result;
        });
        await f.Publisher.StepAsync(f.Run, default);
        Assert.Equal("Uploading", f.Delivery.Status);
        f.Http.Responses.Enqueue(request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method); Assert.Equal("bytes */32", request.Content!.Headers.GetValues("Content-Range").Single());
            return Json("""{"id":"video_id"}""");
        });
        // Simulate restart by clearing tracking and resolving the same durable upload checkpoint.
        f.Db.ChangeTracker.Clear(); var reloaded = await f.Db.Runs.SingleAsync();
        await f.Publisher.StepAsync(reloaded, default);
        Assert.Equal("Processing", (await f.Db.Deliveries.SingleAsync()).Status);
        f.Http.Responses.Enqueue(_ => Json("""{"items":[{"status":{"uploadStatus":"processed"}}]}"""));
        await f.Publisher.StepAsync(reloaded, default);
        Assert.Equal("Completed", reloaded.Status); Assert.Equal(1, f.Http.Methods.Count(x => x == HttpMethod.Post));
        Assert.Equal("https://www.youtube.com/watch?v=video_id", (await f.Db.Deliveries.SingleAsync()).PostUrl);
    }

    [Fact]
    public async Task YouTube_UsesServerConfirmedByteOffsetForResume()
    {
        await using var f = new Fixture(); await f.Seed();
        f.Delivery.Status = "Uploading"; f.Delivery.ProtectedUploadUrl = f.UploadProtector.Protect("https://www.googleapis.com/upload/youtube/v3/videos?upload_id=fake"); await f.Db.SaveChangesAsync();
        f.Http.Responses.Enqueue(_ => { var r = new HttpResponseMessage((HttpStatusCode)308); r.Headers.TryAddWithoutValidation("Range", "bytes=0-7"); return r; });
        f.Http.Responses.Enqueue(request =>
        {
            Assert.Equal(8, request.Content!.Headers.ContentRange!.From); Assert.Equal(24, request.Content.Headers.ContentLength);
            var bytes = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult(); Assert.Equal(f.Bytes[8..], bytes);
            return Json("""{"id":"resumed_video"}""");
        });
        await f.Publisher.StepAsync(f.Run, default);
        Assert.Equal("Processing", f.Delivery.Status); Assert.DoesNotContain(HttpMethod.Post, f.Http.Methods);
    }

    [Fact]
    public async Task AmbiguousInitialization_IsNeverAutomaticallyPostedAgain()
    {
        await using var f = new Fixture(); await f.Seed();
        f.Http.Responses.Enqueue(_ => throw new HttpRequestException("Simulated lost response"));
        await f.Publisher.StepAsync(f.Run, default); Assert.Equal("Unknown", f.Delivery.Status);
        await f.Publisher.StepAsync(f.Run, default); Assert.Single(f.Http.Methods); Assert.Equal("NeedsAttention", f.Run.Status);
    }

    [Fact]
    public async Task ChangedMedia_IsRejectedBeforeAllocatingUploadSession()
    {
        await using var f = new Fixture(); await f.Seed();
        await File.WriteAllBytesAsync(Path.Combine(f.Root, f.Run.RunId.ToString("N") + ".mp4"), new byte[32]);
        await f.Publisher.StepAsync(f.Run, default); Assert.Equal("Failed", f.Delivery.Status); Assert.Empty(f.Http.Methods);
    }

    [Fact]
    public async Task TikTok_WaitsForConcreteVideoReview()
    {
        await using var f = new Fixture(); await f.Seed();
        f.Delivery.Platform = "TikTok"; f.Delivery.SettingsJson = PublishingService.Write(new PublishingTarget("TikTok", f.Delivery.ConnectionId, "review"));
        await f.Db.SaveChangesAsync();
        await f.Publisher.StepAsync(f.Run, default); Assert.Equal("AwaitingReview", f.Run.Status); Assert.Equal("Pending", f.Delivery.Status); Assert.Empty(f.Http.Methods);
    }

    [Fact]
    public async Task ExpiredDelivery_DoesNotStartAnUpload()
    {
        await using var f = new Fixture(); await f.Seed(); f.Run.DeadlineAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await f.Publisher.StepAsync(f.Run, default); Assert.Equal("Cancelled", f.Delivery.Status); Assert.Empty(f.Http.Methods);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
    private sealed class Factory : HttpMessageHandler, IHttpClientFactory
    {
        public readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> Responses = new(); public readonly List<HttpMethod> Methods = [];
        public HttpClient CreateClient(string name) => new(this, false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Methods.Add(request.Method); if (Responses.Count == 0) throw new InvalidOperationException("Unexpected request"); return Task.FromResult(Responses.Dequeue()(request)); }
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "publishing-tests-" + Guid.NewGuid().ToString("N"));
        public readonly byte[] Bytes = Enumerable.Range(0, 32).Select(x => (byte)x).ToArray();
        public readonly PublishingDbContext Db = new(new DbContextOptionsBuilder<PublishingDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly VideoFactoryDbContext Video = new(new DbContextOptionsBuilder<VideoFactoryDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public readonly Factory Http = new(); public readonly PublishingRun Run; public readonly PublishingDelivery Delivery;
        public readonly IDataProtector UploadProtector; public readonly PublishingPublisher Publisher;
        private readonly PublishingConnection _connection;
        public Fixture()
        {
            Directory.CreateDirectory(Root); var tool = Path.Combine(Root, "ffprobe.exe"); File.WriteAllBytes(tool, [1]);
            var options = new PublishingOptions { Enabled = true, WorkerEnabled = true, MediaRoot = Root, FfprobePath = tool, FfprobeSha256 = new('a', 64),
                YouTube = new() { Enabled = true, ClientId = "test-client", ClientSecret = "test-not-a-real-secret", RedirectUri = "https://example.test/api/publishing/oauth/callback" } };
            var protection = new EphemeralDataProtectionProvider(); var input = PublishingTests.Input();
            Run = new() { RunId = Guid.NewGuid(), ScheduleId = Guid.NewGuid(), UserId = "owner", InputJson = PublishingService.Write(input), Status = "ReadyToPublish",
                PublishAtUtc = DateTime.UtcNow, DeadlineAtUtc = DateTime.UtcNow.AddHours(1), MediaSha256 = Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant(), MediaSizeBytes = Bytes.Length };
            Delivery = new() { DeliveryId = Guid.NewGuid(), RunId = Run.RunId, Platform = "YouTube", ConnectionId = input.Targets[0].ConnectionId,
                AccountName = "Test channel", SettingsJson = PublishingService.Write(input.Targets[0]) };
            _connection = new() { ConnectionId = Delivery.ConnectionId, UserId = "owner", Platform = "YouTube", ExternalId = "test_channel", DisplayName = "Test channel" };
            _connection.ProtectedTokens = protection.CreateProtector("VideoMaker.Publishing.Tokens.v1", "owner", _connection.ConnectionId.ToString("N"), "YouTube")
                .Protect(PublishingService.Write(new PublishingTokens("test-access", "test-refresh", DateTime.UtcNow.AddDays(1), PublishingService.Hash(options.YouTube.ClientId + "\n" + options.YouTube.RedirectUri))));
            UploadProtector = protection.CreateProtector("VideoMaker.Publishing.Upload.v1", Run.RunId.ToString("N"), Delivery.DeliveryId.ToString("N"));
            var social = new PublishingSocialService(Db, null!, null!, protection, Http, Options.Create(options), TimeProvider.System);
            var media = new PublishingMedia(Video, Options.Create(options), Options.Create(new VideoOutputOptions()), TimeProvider.System);
            Publisher = new(Db, social, null!, media, protection, TimeProvider.System);
        }
        public async Task Seed()
        {
            Db.Connections.Add(_connection); Db.Runs.Add(Run); Db.Deliveries.Add(Delivery); await Db.SaveChangesAsync();
            await File.WriteAllBytesAsync(Path.Combine(Root, Run.RunId.ToString("N") + ".mp4"), Bytes);
        }
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync(); await Video.DisposeAsync(); Http.Dispose();
            // Exact fixture-owned directory; never a shared/root workspace.
            foreach (var path in Directory.EnumerateFiles(Root)) File.Delete(path);
            Directory.Delete(Root);
        }
    }
}
