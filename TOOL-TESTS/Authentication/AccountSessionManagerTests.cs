using System.Security.Cryptography;
using TOOL_LOCAL.Authentication;
using TOOL_SHARED.Contracts.Authentication;

namespace TOOL_TESTS.Authentication;

public sealed class AccountSessionManagerTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"vm-registration-{Guid.NewGuid():N}");

    [Fact]
    public async Task RegisterAsync_PersistsSessionBeforePublishingAuthentication()
    {
        var saveCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokenStore = new StubTokenStore(null) { SaveTask = saveCompleted.Task };
        var response = CreateTokenResponse();
        var apiClient = new RefreshSequenceApiClient(response);
        using var manager = new AccountSessionManager(apiClient, tokenStore, CreateDeviceIdentity());

        var registration = manager.RegisterAsync(" user@example.test ", "StrongPass1!", " Test User ");
        Assert.Equal(1, tokenStore.SaveCount);
        Assert.False(manager.IsAuthenticated);
        saveCompleted.SetResult();
        await registration;

        Assert.Same(response, manager.Current);
        Assert.Equal(response.RefreshToken, tokenStore.Stored?.RefreshToken);
        Assert.Equal("user@example.test", apiClient.RegistrationRequest?.Email);
        Assert.Equal("Test User", apiClient.RegistrationRequest?.DisplayName);
        Assert.Equal(1, apiClient.RegistrationCount);
    }

    [Theory]
    [InlineData("io")]
    [InlineData("access")]
    [InlineData("crypto")]
    public async Task RegisterAsync_StorageFailure_DoesNotPublishSessionOrRepeatRegistration(string failure)
    {
        var tokenStore = new StubTokenStore(null)
        {
            SaveException = failure switch
            {
                "io" => new IOException("sensitive-test-detail"),
                "access" => new UnauthorizedAccessException("sensitive-test-detail"),
                _ => new CryptographicException("sensitive-test-detail")
            }
        };
        var apiClient = new RefreshSequenceApiClient(CreateTokenResponse());
        using var manager = new AccountSessionManager(apiClient, tokenStore, CreateDeviceIdentity());

        var exception = await Assert.ThrowsAsync<AccountClientException>(() =>
            manager.RegisterAsync("user@example.test", "StrongPass1!", null));

        Assert.Equal("session_storage_failed", exception.Code);
        Assert.Contains("Tài khoản đã được tạo", exception.Message);
        Assert.DoesNotContain("sensitive-test-detail", exception.Message);
        Assert.False(manager.IsAuthenticated);
        Assert.Null(tokenStore.Stored);
        Assert.Equal(1, apiClient.RegistrationCount);
    }

    [Fact]
    public async Task RegisterAsync_CancelledWithLateApiResponse_DoesNotSaveOrAuthenticate()
    {
        using var lifetime = new CancellationTokenSource();
        var lateResponse = new TaskCompletionSource<AuthTokenResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var apiClient = new RefreshSequenceApiClient(CreateTokenResponse()) { RegistrationTask = lateResponse.Task };
        var tokenStore = new StubTokenStore(null);
        using var manager = new AccountSessionManager(apiClient, tokenStore, CreateDeviceIdentity());

        var registration = manager.RegisterAsync("user@example.test", "StrongPass1!", null, lifetime.Token);
        lifetime.Cancel();
        lateResponse.SetResult(CreateTokenResponse());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registration);
        Assert.True(apiClient.RegistrationCancellation.IsCancellationRequested);
        Assert.False(manager.IsAuthenticated);
        Assert.Equal(0, tokenStore.SaveCount);
    }

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
            CreateDeviceIdentity());
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
        public RegisterRequest? RegistrationRequest { get; private set; }
        public int RegistrationCount { get; private set; }
        public CancellationToken RegistrationCancellation { get; private set; }
        public Task<AuthTokenResponse>? RegistrationTask { get; init; }

        public Task<AuthTokenResponse> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken = default)
        {
            RegistrationRequest = request;
            RegistrationCancellation = cancellationToken;
            RegistrationCount++;
            return RegistrationTask ?? Task.FromResult(firstResponse);
        }

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
        public int SaveCount { get; private set; }
        public Exception? SaveException { get; init; }
        public Task? SaveTask { get; init; }

        public Task<StoredRefreshToken?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored);

        public async Task SaveAsync(StoredRefreshToken token, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            if (SaveException is not null) throw SaveException;
            if (SaveTask is not null) await SaveTask;
            Stored = token;
        }

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            Stored = null;
            ClearCount++;
            return Task.CompletedTask;
        }
    }

    private DeviceIdentityService CreateDeviceIdentity() => new(Path.Combine(_workspace, "device-id.bin"));

    public void Dispose()
    {
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, recursive: true);
    }
}
