using TOOL_LOCAL.Authentication;
using TOOL_SHARED.Contracts.Authentication;

namespace TOOL_TESTS.Authentication;

public sealed class AccountLogoutRefreshTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Logout_ExpiredAccessToken_RefreshesAndRevokesWithRotatedTokens(bool allDevices)
    {
        var api = new LogoutApiClient(expired: true);
        var store = new MemoryTokenStore();
        using var manager = new AccountSessionManager(api, store, new DeviceIdentityService());
        Assert.True(await manager.TryRestoreAsync());

        await manager.LogoutAsync(allDevices);

        Assert.Equal(new[] { "stored-refresh", "initial-refresh" }, api.RefreshRequests);
        var logout = Assert.Single(api.LogoutRequests);
        Assert.Equal("renewed-access", logout.AccessToken);
        Assert.Equal("renewed-refresh", logout.Request.RefreshToken);
        Assert.Equal(allDevices, logout.Request.RevokeAllSessions);
        Assert.False(manager.IsAuthenticated);
        Assert.Null(store.Stored);
    }

    [Fact]
    public async Task Logout_ValidAccessToken_DoesNotRefreshUnnecessarily()
    {
        var api = new LogoutApiClient(expired: false);
        var store = new MemoryTokenStore();
        using var manager = new AccountSessionManager(api, store, new DeviceIdentityService());
        Assert.True(await manager.TryRestoreAsync());

        await manager.LogoutAsync();

        Assert.Single(api.RefreshRequests);
        Assert.Equal("initial-access", Assert.Single(api.LogoutRequests).AccessToken);
        Assert.False(manager.IsAuthenticated);
    }

    [Fact]
    public async Task Logout_RefreshRejected_ClearsInvalidLocalSession()
    {
        var api = new LogoutApiClient(expired: true)
        {
            RefreshFailure = new AccountClientException("invalid_refresh_token", "Expired session", 401)
        };
        var store = new MemoryTokenStore();
        using var manager = new AccountSessionManager(api, store, new DeviceIdentityService());
        Assert.True(await manager.TryRestoreAsync());

        await manager.LogoutAsync();

        Assert.Equal(2, api.RefreshRequests.Count);
        Assert.Empty(api.LogoutRequests);
        Assert.False(manager.IsAuthenticated);
        Assert.Null(store.Stored);
    }

    [Fact]
    public async Task Logout_RefreshNetworkFailure_KeepsSessionForRetry()
    {
        var api = new LogoutApiClient(expired: true) { RefreshFailure = new HttpRequestException("Test offline") };
        var store = new MemoryTokenStore();
        using var manager = new AccountSessionManager(api, store, new DeviceIdentityService());
        Assert.True(await manager.TryRestoreAsync());

        await Assert.ThrowsAsync<HttpRequestException>(() => manager.LogoutAsync());

        Assert.Empty(api.LogoutRequests);
        Assert.True(manager.IsAuthenticated);
        Assert.Equal("initial-refresh", store.Stored?.RefreshToken);
    }

    [Fact]
    public async Task Logout_ServerFailureAfterRefresh_KeepsRotatedTokenForRetry()
    {
        var api = new LogoutApiClient(expired: true)
        {
            LogoutFailure = new AccountClientException("server_error", "Test unavailable", 503)
        };
        var store = new MemoryTokenStore();
        using var manager = new AccountSessionManager(api, store, new DeviceIdentityService());
        Assert.True(await manager.TryRestoreAsync());

        await Assert.ThrowsAsync<AccountClientException>(() => manager.LogoutAsync());

        Assert.True(manager.IsAuthenticated);
        Assert.Equal("renewed-refresh", store.Stored?.RefreshToken);
        api.LogoutFailure = null;
        await manager.LogoutAsync();
        Assert.Equal(2, api.RefreshRequests.Count);
        Assert.Equal(2, api.LogoutRequests.Count);
        Assert.All(api.LogoutRequests, logout => Assert.Equal("renewed-access", logout.AccessToken));
        Assert.False(manager.IsAuthenticated);
    }

    [Fact]
    public async Task Logout_Unauthorized_ClearsSessionWithoutRevivingRejectedSession()
    {
        var api = new LogoutApiClient(expired: false)
        {
            LogoutFailure = new AccountClientException("session_expired", "Revoked session", 401)
        };
        var store = new MemoryTokenStore();
        using var manager = new AccountSessionManager(api, store, new DeviceIdentityService());
        Assert.True(await manager.TryRestoreAsync());

        await manager.LogoutAsync();

        Assert.Single(api.RefreshRequests);
        Assert.Single(api.LogoutRequests);
        Assert.False(manager.IsAuthenticated);
        Assert.Null(store.Stored);
    }

    private sealed class LogoutApiClient(bool expired) : IAccountApiClient
    {
        private readonly Guid _sessionId = Guid.NewGuid();
        private readonly Guid _deviceId = Guid.NewGuid();
        public List<string> RefreshRequests { get; } = [];
        public List<(string AccessToken, LogoutRequest Request)> LogoutRequests { get; } = [];
        public Exception? RefreshFailure { get; init; }
        public Exception? LogoutFailure { get; set; }

        public Task<AuthTokenResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
        {
            RefreshRequests.Add(request.RefreshToken);
            var first = RefreshRequests.Count == 1;
            if (!first && RefreshFailure is not null)
                return Task.FromException<AuthTokenResponse>(RefreshFailure);
            return Task.FromResult(new AuthTokenResponse(
                first ? "initial-access" : "renewed-access",
                DateTime.UtcNow.AddMinutes(first && expired ? -1 : 15),
                first ? "initial-refresh" : "renewed-refresh", DateTime.UtcNow.AddDays(1),
                _sessionId, _deviceId, new UserProfileResponse("test-user", "user@example.invalid", "Test", "Active", ["User"])));
        }

        public Task LogoutAsync(string accessToken, LogoutRequest request, CancellationToken cancellationToken = default)
        {
            LogoutRequests.Add((accessToken, request));
            return LogoutFailure is null ? Task.CompletedTask : Task.FromException(LogoutFailure);
        }

        public Task<AuthTokenResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AuthTokenResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task RequestPasswordResetAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MemoryTokenStore : ITokenStore
    {
        public StoredRefreshToken? Stored { get; private set; } = new("stored-refresh", DateTime.UtcNow.AddDays(1));
        public Task<StoredRefreshToken?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Stored);
        public Task SaveAsync(StoredRefreshToken token, CancellationToken cancellationToken = default)
        {
            Stored = token;
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Stored = null;
            return Task.CompletedTask;
        }
    }
}
