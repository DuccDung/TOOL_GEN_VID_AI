using System.Net;
using System.Net.Http.Json;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.TikTok;
using TOOL_SHARED.Contracts.Authentication;
using TOOL_SHARED.Contracts.Common;
using TOOL_SHARED.Contracts.TikTok;

namespace TOOL_TESTS.TikTok;

public sealed class TikTokGatewayReadinessTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task OAuth_DelegatesVerificationLicenseDecisionToServer(bool complete, bool forbidden)
    {
        using var handler = new GatewayHandler(forbidden);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.invalid/") };
        using var session = new AccountSessionManager(new AccountApiClient(http), new MemoryStore(), new DeviceIdentityService());
        Assert.True(await session.TryRestoreAsync());
        await using var license = new LicenseSessionManager(new LicenseApiClient(http, session));
        Assert.True(license.IsLocked);
        var gateway = new TikTokGatewayClient(http, session, license);

        async Task OAuth()
        {
            if (complete)
                await gateway.CompleteOAuthAsync(new CompleteTikTokOAuthRequest(Guid.NewGuid(), "fake-code", "fake-state", "fake-verifier"), CancellationToken.None);
            else
                await gateway.StartOAuthAsync(new StartTikTokOAuthRequest("http://127.0.0.1:12345/callback/", "fake-challenge"), CancellationToken.None);
        }

        if (forbidden)
        {
            var error = await Assert.ThrowsAsync<AccountClientException>(OAuth);
            Assert.Equal("license_unavailable", error.Code);
            Assert.Equal(403, error.StatusCode);
        }
        else await OAuth();
        Assert.Equal(complete ? "/api/tiktok/oauth/complete" : "/api/tiktok/oauth/start", Assert.Single(handler.TikTokPaths));
        Assert.True(session.IsAuthenticated);
        await Assert.ThrowsAsync<AccountClientException>(() => gateway.GetCreatorInfoAsync(CancellationToken.None));
        Assert.Single(handler.TikTokPaths); // Publishing operations still require the local license gate.
    }

    private sealed class GatewayHandler(bool forbidden) : HttpMessageHandler
    {
        public List<string> TikTokPaths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/auth/refresh")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new AuthTokenResponse(
                    "synthetic-access", DateTime.UtcNow.AddMinutes(10), "synthetic-refresh", DateTime.UtcNow.AddDays(1),
                    Guid.NewGuid(), Guid.NewGuid(), new UserProfileResponse("admin-a", "admin@example.test", "Admin", "Active", ["Admin"]))) });
            Assert.StartsWith("/api/tiktok/oauth/", path);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            TikTokPaths.Add(path);
            if (forbidden)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new ApiErrorResponse("license_unavailable", "Synthetic server refusal")) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = path.EndsWith("/start", StringComparison.Ordinal)
                ? JsonContent.Create(new StartTikTokOAuthResponse(Guid.NewGuid(), "https://www.tiktok.com/v2/auth/authorize/", "fake-state", DateTime.UtcNow.AddMinutes(10)))
                : JsonContent.Create(new TikTokFeatureStateResponse(true, true, null)) });
        }
    }

    private sealed class MemoryStore : ITokenStore
    {
        public Task<StoredRefreshToken?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<StoredRefreshToken?>(new StoredRefreshToken("synthetic-refresh", DateTime.UtcNow.AddDays(1)));
        public Task SaveAsync(StoredRefreshToken token, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
