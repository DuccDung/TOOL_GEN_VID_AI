using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Accounts;

public interface IAccountManagementService
{
    Task<IReadOnlyList<RegisteredDeviceResponse>> GetDevicesAsync(
        string userId,
        Guid currentDeviceId,
        CancellationToken cancellationToken);

    Task RevokeDeviceAsync(string userId, Guid deviceId, CancellationToken cancellationToken);

    Task<CurrentLicenseResponse> GetCurrentLicenseAsync(
        string userId,
        Guid currentDeviceId,
        CancellationToken cancellationToken);

    Task<CurrentLicenseResponse> ActivateCurrentDeviceAsync(
        string userId,
        Guid currentDeviceId,
        Guid currentSessionId,
        CancellationToken cancellationToken);

    Task<CurrentLicenseResponse> VerifyHeartbeatAsync(
        string userId,
        Guid currentDeviceId,
        Guid currentSessionId,
        CancellationToken cancellationToken);
}
