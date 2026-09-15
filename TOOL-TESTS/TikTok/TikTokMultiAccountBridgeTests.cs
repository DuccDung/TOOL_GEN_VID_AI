using System.Net;
using System.Text.Json;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.TikTok;
using TOOL_SHARED.Contracts.Accounts;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_TESTS.TikTok;

public sealed class TikTokMultiAccountBridgeTests
{
    [Theory]
    [InlineData("stale-media", "tiktok_video_changed", 0)]
    [InlineData("wrong-account", "tiktok_account_mismatch", 1)]
    [InlineData("media-replaced", "tiktok_video_changed", 1)]
    public async Task Publish_RejectsMismatchedSnapshotBeforeUploading(string scenario, string expectedError, int expectedInitCalls)
    {
        var path = Path.Combine(Path.GetTempPath(), "tiktok-bridge-" + Guid.NewGuid().ToString("N") + ".mp4");
        await File.WriteAllBytesAsync(path, new byte[128]);
        try
        {
            var media = new TikTokMediaService(new FfprobeService("fake-ffprobe", new Probe()), new Preflight());
            var selected = await media.SelectAsync(path, default);
            var accountId = Guid.NewGuid();
            var clientRequestId = Guid.NewGuid();
            var gateway = new Gateway(async request => {
                Assert.Equal(accountId, request.ConnectionId); Assert.Equal(clientRequestId, request.ClientRequestId);
                if (scenario == "media-replaced") await media.SelectAsync(path, default);
                return new(Guid.NewGuid(), "https://open-upload.tiktokapis.com/video/?upload_token=synthetic",
                    128, 1, DateTime.UtcNow.AddHours(1), ConnectionId: scenario == "wrong-account" ? Guid.NewGuid() : accountId);
            });
            await using var license = new LicenseSessionManager(null!);
            // This fixture isolates native account/media binding; license gates have their own HTTP tests.
            typeof(LicenseSessionManager).GetProperty(nameof(LicenseSessionManager.Current))!.SetValue(license,
                new CurrentLicenseResponse(true, Guid.NewGuid(), "test", "Test", "Active", DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1),
                    1, 1, 0, "{}", true, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(10), 300, LicenseAccessStates.Active, null, null));
            using var uploadHandler = new UploadHandler(); using var http = new HttpClient(uploadHandler);
            var responses = new List<string>();
            using var bridge = new TikTokWebBridge(true, license, gateway, new TikTokOAuthCoordinator(gateway), media,
                new TikTokUploadService(http), () => path, responses.Add);
            var json = JsonSerializer.Serialize(new {
                type = "tiktok.publish.start", requestId = "publish-test", payload = new {
                    connectionId = accountId, clientRequestId, mediaId = scenario == "stale-media" ? Guid.NewGuid() : selected.MediaId,
                    title = "Caption", privacyLevel = "SELF_ONLY", consentConfirmed = true
                }
            });
            Assert.True(await bridge.TryHandleAsync(json));
            Assert.Equal(expectedInitCalls, gateway.InitializeCalls); Assert.Equal(0, uploadHandler.Calls);
            using var error = JsonDocument.Parse(responses.Last());
            Assert.Equal("publish-test", error.RootElement.GetProperty("requestId").GetString());
            Assert.Equal(expectedError, error.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.All(responses, response => {
                Assert.DoesNotContain(path, response, StringComparison.Ordinal);
                Assert.DoesNotContain("upload_token", response, StringComparison.Ordinal);
            });
        }
        finally { File.Delete(path); }
    }

    private sealed class Gateway(Func<InitializeTikTokPublishRequest, Task<InitializeTikTokPublishResponse>> initialize) : ITikTokGatewayClient
    {
        public int InitializeCalls { get; private set; }
        public Task<InitializeTikTokPublishResponse> InitializePublishAsync(InitializeTikTokPublishRequest request, CancellationToken token)
        { InitializeCalls++; return initialize(request); }
        public Task<TikTokFeatureStateResponse> GetStateAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<StartTikTokOAuthResponse> StartOAuthAsync(StartTikTokOAuthRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<TikTokFeatureStateResponse> CompleteOAuthAsync(CompleteTikTokOAuthRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task DisconnectAsync(CancellationToken token, Guid? connectionId = null) => throw new NotSupportedException();
        public Task<TikTokCreatorInfoResponse> GetCreatorInfoAsync(CancellationToken token, Guid? connectionId = null) => throw new NotSupportedException();
        public Task<TikTokPublishHistoryResponse> GetHistoryAsync(Guid? connectionId, int page, CancellationToken token) => throw new NotSupportedException();
        public Task<TikTokPublishStatusResponse> GetPublishStatusAsync(Guid jobId, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Probe : IExternalProcessRunner
    {
        public Task<ProcessExecutionResult> RunAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken token = default) =>
            Task.FromResult(new ProcessExecutionResult(0, """{"format":{"duration":"2"},"streams":[{"codec_type":"video","codec_name":"h264","width":640,"height":360,"avg_frame_rate":"30/1"}]}""", ""));
    }
    private sealed class Preflight : IMediaToolPreflightService
    {
        public Task<MediaToolStatusSummary> GetStatusAsync(bool force, CancellationToken token) => RequireReadyAsync(token);
        public Task<MediaToolStatusSummary> RequireReadyAsync(CancellationToken token) => Task.FromResult(new MediaToolStatusSummary(true, null, "Test", "test", "test", DateTime.UtcNow));
    }
    private sealed class UploadHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)); }
    }
}
