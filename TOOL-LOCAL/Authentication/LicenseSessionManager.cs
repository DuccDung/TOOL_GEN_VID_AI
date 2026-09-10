using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_LOCAL.Authentication;

public sealed class LicenseSessionManager(LicenseApiClient apiClient) : IAsyncDisposable
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _heartbeatTask;
    private string? _invalidReason;

    public CurrentLicenseResponse? Current { get; private set; }

    public bool IsLocked => !HasValidLease;

    public bool HasValidLease =>
        Current is { HasActiveLicense: true, CurrentDeviceActivated: true } license &&
        EffectiveAccessExpiry(license) > DateTime.UtcNow &&
        _invalidReason is null;

    public event Action<string>? LicenseInvalidated;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshNowAsync(cancellationToken);
    }

    public async Task<CurrentLicenseResponse> EnsureAccessAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            if (Current is not { HasActiveLicense: true })
            {
                throw Unavailable(
                    Current?.AccessMessage ??
                    _invalidReason ??
                    "Tài khoản chưa có gói sử dụng còn hiệu lực.");
            }

            if (HasValidLease && Current is { } current && EffectiveAccessExpiry(current) > DateTime.UtcNow.AddMinutes(1))
            {
                return Current;
            }

            var refreshed = await RefreshCoreAsync(cancellationToken);
            if (!HasValidLease)
            {
                throw Unavailable(refreshed.AccessMessage ?? "License chưa sẵn sàng.");
            }
            return refreshed;
        }
        finally
        {
            _sync.Release();
        }
    }

    public async Task<CurrentLicenseResponse> RefreshNowAsync(CancellationToken cancellationToken = default)
    {
        await _sync.WaitAsync(cancellationToken);
        try
        {
            return await RefreshCoreAsync(cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task<CurrentLicenseResponse> RefreshCoreAsync(CancellationToken cancellationToken)
    {
        var license = Current;
        try
        {
            license = await apiClient.GetCurrentAsync(cancellationToken);
            if (!license.HasActiveLicense)
            {
                return SetLockedLicense(license);
            }
            if (!license.CurrentDeviceActivated)
            {
                license = await apiClient.ActivateCurrentDeviceAsync(cancellationToken);
            }
            license = await apiClient.HeartbeatAsync(cancellationToken);
            EnsureResponseIsUsable(license);
            Current = license;
            _invalidReason = null;
            StartHeartbeatIfNeeded();
            return license;
        }
        catch (AccountClientException exception) when (exception.StatusCode is 403 or 409 or 423)
        {
            license ??= new CurrentLicenseResponse(false, null, null, null, null, null, null,
                0, 0, 0, null, false, DateTime.UtcNow);
            return SetLockedLicense(license with
            {
                LeaseExpiresAtUtc = null,
                AccessState = exception.Code switch
                {
                    "concurrent_session_limit" => LicenseAccessStates.SessionLimit,
                    "device_limit_reached" => LicenseAccessStates.DeviceLimit,
                    "license_suspended" => LicenseAccessStates.Suspended,
                    "license_revoked" => LicenseAccessStates.Revoked,
                    _ => LicenseAccessStates.Unavailable
                },
                AccessReasonCode = exception.Code,
                AccessMessage = exception.Message
            });
        }
    }

    private CurrentLicenseResponse SetLockedLicense(CurrentLicenseResponse license)
    {
        var reason = license.AccessMessage ?? "License không còn hiệu lực.";
        var changed = _invalidReason != reason || Current?.AccessState != license.AccessState
            || Current?.AccessReasonCode != license.AccessReasonCode;
        Current = license with { LeaseExpiresAtUtc = null };
        _invalidReason = reason;
        if (changed) LicenseInvalidated?.Invoke(reason);
        return Current;
    }

    private void StartHeartbeatIfNeeded()
    {
        if (_heartbeatTask is null || _heartbeatTask.IsCompleted)
        {
            _heartbeatTask = RunHeartbeatLoopAsync(_shutdown.Token);
        }
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var seconds = Math.Clamp(Current?.HeartbeatIntervalSeconds ?? 300, 60, 600);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
                if (IsLocked)
                {
                    return;
                }
                await RefreshNowAsync(cancellationToken);
                if (IsLocked)
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (AccountClientException exception) when (exception.StatusCode is 401 or 403 or 409 or 423)
            {
                try
                {
                    Current = await apiClient.GetCurrentAsync(cancellationToken);
                }
                catch (Exception refreshException) when (refreshException is AccountClientException or HttpRequestException)
                {
                    // Keep the last state and surface the original heartbeat failure.
                }
                Invalidate(Current?.AccessMessage ?? exception.Message);
                return;
            }
            catch (HttpRequestException)
            {
                if (Current is not { } current || EffectiveAccessExpiry(current) <= DateTime.UtcNow)
                {
                    Invalidate("Không thể xác minh license với server. Vui lòng kiểm tra kết nối mạng.");
                    return;
                }
            }
        }
    }

    private static void EnsureResponseIsUsable(CurrentLicenseResponse license)
    {
        if (!license.HasActiveLicense || !license.CurrentDeviceActivated)
        {
            throw Unavailable("License hoặc thiết bị không còn hiệu lực.");
        }

        if (license.ExpiresAtUtc is { } expiresAt && expiresAt <= license.ServerTimeUtc)
        {
            throw Unavailable("Gói sử dụng đã hết hạn.");
        }
    }

    private void Invalidate(string reason)
    {
        if (Current is { } current)
        {
            SetLockedLicense(current with
            {
                AccessState = LicenseAccessStates.Unavailable,
                AccessMessage = reason
            });
        }
        else
        {
            _invalidReason = reason;
            LicenseInvalidated?.Invoke(reason);
        }
    }

    private static AccountClientException Unavailable(string message) =>
        new("license_unavailable", message, 403);

    private static DateTime EffectiveAccessExpiry(CurrentLicenseResponse license)
    {
        if (license.LeaseExpiresAtUtc is not { } leaseExpiry)
        {
            return DateTime.MinValue;
        }

        var offlineExpiry = leaseExpiry.AddHours(Math.Max(0, license.OfflineGraceHours));
        return license.ExpiresAtUtc is { } licenseExpiry && licenseExpiry < offlineExpiry
            ? licenseExpiry
            : offlineExpiry;
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        if (_heartbeatTask is not null)
        {
            try
            {
                await _heartbeatTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _shutdown.Dispose();
        _sync.Dispose();
    }
}
