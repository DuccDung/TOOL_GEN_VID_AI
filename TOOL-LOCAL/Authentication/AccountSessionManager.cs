using System.Runtime.InteropServices;
using TOOL_SHARED.Contracts.Authentication;

namespace TOOL_LOCAL.Authentication;

public sealed class AccountSessionManager(
    IAccountApiClient apiClient,
    ITokenStore tokenStore,
    DeviceIdentityService deviceIdentity) : IDisposable
{
    public const string SessionExpiredMessage = "Phiên đăng nhập đã hết hạn hoặc bị thu hồi. Vui lòng đăng nhập lại.";

    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private string? _refreshToken;
    private bool _persistSession = true;
    private int _sessionInvalidated;

    public AuthTokenResponse? Current { get; private set; }

    public bool IsAuthenticated => Current is not null;

    public event Action<string>? SessionInvalidated;

    public async Task<bool> TryRestoreAsync(CancellationToken cancellationToken = default)
    {
        var stored = await tokenStore.LoadAsync(cancellationToken);
        if (stored is null || stored.ExpiresAtUtc <= DateTime.UtcNow)
        {
            await ClearSessionWithoutLockAsync(cancellationToken);
            return false;
        }

        try
        {
            _persistSession = true;
            _refreshToken = stored.RefreshToken;
            await RefreshCoreAsync(stored.RefreshToken, cancellationToken);
            return true;
        }
        catch (AccountClientException exception) when (exception.StatusCode is 401 or 403)
        {
            await ClearSessionWithoutLockAsync(cancellationToken);
            return false;
        }
    }

    public Task LoginAsync(string email, string password, CancellationToken cancellationToken = default) =>
        LoginAsync(email, password, true, cancellationToken);

    public async Task LoginAsync(
        string email,
        string password,
        bool rememberMe,
        CancellationToken cancellationToken = default)
    {
        var response = await apiClient.LoginAsync(
            new LoginRequest(email.Trim(), password, CreateDeviceRequest()),
            cancellationToken);
        await SetCurrentAsync(response, rememberMe, cancellationToken);
    }

    public async Task RegisterAsync(
        string email,
        string password,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        var response = await apiClient.RegisterAsync(
            new RegisterRequest(email.Trim(), password, displayName?.Trim(), CreateDeviceRequest()),
            cancellationToken);
        await SetCurrentAsync(response, true, cancellationToken);
    }

    public Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken = default) =>
        apiClient.RequestPasswordResetAsync(new ForgotPasswordRequest(email.Trim()), cancellationToken);

    public Task ResetPasswordAsync(
        string email,
        string otp,
        string newPassword,
        CancellationToken cancellationToken = default) =>
        apiClient.ResetPasswordAsync(
            new ResetPasswordRequest(email.Trim(), otp.Trim(), newPassword),
            cancellationToken);

    public async Task<string> GetValidAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            if (Current is null)
            {
                await InvalidateSessionWithoutLockAsync();
                throw SessionExpired();
            }

            if (Current.AccessTokenExpiresAtUtc > DateTime.UtcNow.AddMinutes(1))
            {
                return Current.AccessToken;
            }

            var refreshToken = _refreshToken;
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                var stored = await tokenStore.LoadAsync(cancellationToken);
                if (stored is null)
                {
                    await InvalidateSessionWithoutLockAsync();
                    throw SessionExpired();
                }

                refreshToken = stored.RefreshToken;
            }

            try
            {
                await RefreshCoreWithoutLockAsync(refreshToken, cancellationToken);
            }
            catch (AccountClientException exception) when (exception.StatusCode is 401 or 403)
            {
                await InvalidateSessionWithoutLockAsync();
                throw;
            }

            return Current!.AccessToken;
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task LogoutAsync(bool allDevices = false, CancellationToken cancellationToken = default)
    {
        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            var current = Current;
            var stored = await tokenStore.LoadAsync(cancellationToken);
            var refreshToken = _refreshToken ?? stored?.RefreshToken;
            if (current is not null)
            {
                try
                {
                    await apiClient.LogoutAsync(
                        current.AccessToken,
                        new LogoutRequest(refreshToken, allDevices),
                        cancellationToken);
                }
                catch (AccountClientException exception) when (exception.StatusCode == 401)
                {
                    // Local credentials must still be cleared when the access token expired.
                }
            }

            await ClearSessionWithoutLockAsync(cancellationToken);
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task InvalidateAsync(CancellationToken cancellationToken = default)
    {
        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            await InvalidateSessionWithoutLockAsync();
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private async Task RefreshCoreAsync(string refreshToken, CancellationToken cancellationToken)
    {
        await _sessionLock.WaitAsync(cancellationToken);
        try
        {
            await RefreshCoreWithoutLockAsync(refreshToken, cancellationToken);
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private async Task RefreshCoreWithoutLockAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var response = await apiClient.RefreshAsync(new RefreshTokenRequest(refreshToken), cancellationToken);
        await SetCurrentAsync(response, _persistSession, cancellationToken);
    }

    private async Task SetCurrentAsync(
        AuthTokenResponse response,
        bool persistSession,
        CancellationToken cancellationToken)
    {
        Current = response;
        _refreshToken = response.RefreshToken;
        _persistSession = persistSession;
        Interlocked.Exchange(ref _sessionInvalidated, 0);
        if (persistSession)
        {
            await tokenStore.SaveAsync(
                new StoredRefreshToken(response.RefreshToken, response.RefreshTokenExpiresAtUtc),
                cancellationToken);
        }
        else
        {
            await tokenStore.ClearAsync(cancellationToken);
        }
    }

    private async Task InvalidateSessionWithoutLockAsync()
    {
        await ClearSessionWithoutLockAsync(CancellationToken.None);
        if (Interlocked.Exchange(ref _sessionInvalidated, 1) == 0)
        {
            SessionInvalidated?.Invoke(SessionExpiredMessage);
        }
    }

    private async Task ClearSessionWithoutLockAsync(CancellationToken cancellationToken)
    {
        Current = null;
        _refreshToken = null;
        await tokenStore.ClearAsync(cancellationToken);
    }

    private static AccountClientException SessionExpired() =>
        new("session_expired", SessionExpiredMessage, 401);

    private DeviceRegistrationRequest CreateDeviceRequest() =>
        new(
            deviceIdentity.GetOrCreateFingerprint(),
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            Application.ProductVersion);

    public void Dispose() => _sessionLock.Dispose();
}
