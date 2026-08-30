using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_SERVER.Accounts;

public interface IAdminLicenseService
{
    Task<AdminLicenseOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminLicensePlanResponse>> GetPlansAsync(CancellationToken cancellationToken);
    Task<AdminLicensePlanResponse> CreatePlanAsync(SaveLicensePlanRequest request, string adminUserId, CancellationToken cancellationToken);
    Task<AdminLicensePlanResponse> UpdatePlanAsync(Guid planId, SaveLicensePlanRequest request, string adminUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AdminUserSummaryResponse>> GetUsersAsync(string? search, CancellationToken cancellationToken);
    Task<AdminUserDetailResponse> GetUserAsync(string userId, CancellationToken cancellationToken);
    Task<AdminUserLicenseResponse> GrantLicenseAsync(string userId, GrantUserLicenseRequest request, string adminUserId, CancellationToken cancellationToken);
    Task<AdminUserLicenseResponse> ExtendLicenseAsync(Guid licenseId, ExtendUserLicenseRequest request, string adminUserId, CancellationToken cancellationToken);
    Task<AdminUserLicenseResponse> ChangeLicenseStatusAsync(Guid licenseId, ChangeUserLicenseStatusRequest request, string adminUserId, CancellationToken cancellationToken);
    Task RevokeDeviceAsync(Guid deviceId, string adminUserId, CancellationToken cancellationToken);
    Task RevokeSessionAsync(Guid sessionId, string adminUserId, CancellationToken cancellationToken);
}
