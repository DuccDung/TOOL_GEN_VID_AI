using TOOL_LOCAL.Authentication;
using TOOL_SHARED.Contracts.Authentication;

namespace TOOL_TESTS.Authentication;

public sealed class AccountSessionManagerTests
{
    [Fact]
    public async Task GetValidAccessTokenAsync_InvalidRefresh_ClearsSessionAndRaisesInvalidation()
    {
        var tokenStore = new StubTokenStore(new StoredRefreshToken(
            "initial-refresh-token",
            DateTime.UtcNow.AddDays(1)));
        var apiClient = new RefreshSequenceApiClient(CreateTokenResponse());
        using var manager = new AccountSessionManager(
            apiClient,
            tokenStore,
            new DeviceIdentityService());
        var invalidationMessages = new List<string>();
        manager.SessionInvalidated += invalidationMessages.Add;

        Assert.True(await manager.TryRestoreAsync());

        var exception = await Assert.ThrowsAsync<AccountClientException>(
            () => manager.GetValidAccessTokenAsync());

        Assert.Equal("invalid_refresh_token", exception.Code);
        Assert.False(manager.IsAuthenticated);
        Assert.Null(manager.Current);
        Assert.Null(tokenStore.Stored);
        Assert.Equal(1, tokenStore.ClearCount);
        Assert.Equal(AccountSessionManager.SessionExpiredMessage, Assert.Single(invalidationMessages));
    }

    private static AuthTokenResponse CreateTokenResponse() =>
        new(
            "short-lived-access-token",
            DateTime.UtcNow.AddSeconds(10),
            "rotated-refresh-token",
            DateTime.UtcNow.AddDays(1),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new UserProfileResponse(
                "user-001",
                "user@example.com",
                "Test User",
                "Active",
                ["User"]));

    private sealed class RefreshSequenceApiClient(AuthTokenResponse firstResponse) : IAccountApiClient
    {
        private int _refreshCalls;

        public Task<AuthTokenResponse> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AuthTokenResponse> LoginAsync(
            LoginRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RequestPasswordResetAsync(
            ForgotPasswordRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ResetPasswordAsync(
            ResetPasswordRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AuthTokenResponse> RefreshAsync(
            RefreshTokenRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _refreshCalls) == 1)
            {
                return Task.FromResult(firstResponse);
            }

            return Task.FromException<AuthTokenResponse>(new AccountClientException(
                "invalid_refresh_token",
                "Refresh token không hợp lệ hoặc đã hết hạn.",
                401));
        }

        public Task LogoutAsync(
            string accessToken,
            LogoutRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubTokenStore(StoredRefreshToken? stored) : ITokenStore
    {
        public StoredRefreshToken? Stored { get; private set; } = stored;

        public int ClearCount { get; private set; }

        public Task<StoredRefreshToken?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored);

        public Task SaveAsync(StoredRefreshToken token, CancellationToken cancellationToken = default)
        {
            Stored = token;
            return Task.CompletedTask;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Stored = null;
            ClearCount++;
            return Task.CompletedTask;
        }
    }
}
