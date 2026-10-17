using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Generation;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_SHARED.Contracts.Organizations;

namespace TOOL_LOCAL.Vietsub.Ocr;

internal interface IVietsubLocalJobAuthorizer
{
    Task AuthorizeAsync(
        string userId,
        Guid organizationId,
        VietsubProjectManifest project,
        CancellationToken cancellationToken);
}

internal static class VietsubLocalJobAuthorizationErrorCodes
{
    public const string AccessDenied = "vietsub_local_access_denied";
    public const string LicenseRequired = "vietsub_local_license_required";
}

internal sealed class VietsubLocalJobAuthorizationException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

internal interface IVietsubLocalAccessContext
{
    string? CurrentUserId { get; }

    Guid? SelectedOrganizationId { get; }

    Task EnsureSessionAndLicenseAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizationSummaryResponse>> GetOrganizationsAsync(
        CancellationToken cancellationToken);
}

internal sealed class DesktopVietsubLocalAccessContext(
    AccountSessionManager sessionManager,
    LicenseSessionManager licenseManager,
    IGenerationClient generationClient) : IVietsubLocalAccessContext
{
    public string? CurrentUserId => sessionManager.Current?.User.UserId;

    public Guid? SelectedOrganizationId => TOOL_SHARED.Contracts.Accounts.LicenseProductPolicy.IsLocalOnly(licenseManager.Current?.FeatureFlagsJson)
        ? Guid.Empty : generationClient.SelectedOrganizationId;

    public async Task EnsureSessionAndLicenseAsync(CancellationToken cancellationToken)
    {
        _ = await sessionManager.GetValidAccessTokenAsync(cancellationToken);
        var license = await licenseManager.EnsureAccessAsync(cancellationToken);
        if (!TOOL_SHARED.Contracts.Accounts.LicenseProductPolicy.VietsubAllowed(license.FeatureFlagsJson))
            throw new AccountClientException("license_feature_unavailable", "Gói hiện tại không cấp quyền tool dịch.", 403);
    }

    public Task<IReadOnlyList<OrganizationSummaryResponse>> GetOrganizationsAsync(
        CancellationToken cancellationToken) =>
        generationClient.GetOrganizationsAsync(cancellationToken);
}

internal sealed class VietsubLocalJobAuthorizer(
    IVietsubLocalAccessContext accessContext) : IVietsubLocalJobAuthorizer
{
    private static readonly HashSet<string> AllowedRoles = new(StringComparer.Ordinal)
    {
        OrganizationRoles.Owner,
        OrganizationRoles.OrganizationAdmin,
        OrganizationRoles.BillingManager,
        OrganizationRoles.Member
    };

    public async Task AuthorizeAsync(
        string userId,
        Guid organizationId,
        VietsubProjectManifest project,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(accessContext.CurrentUserId, userId, StringComparison.Ordinal)
            || accessContext.SelectedOrganizationId != organizationId
            || project.OrganizationId != organizationId
            || !string.Equals(project.OwnerUserId, userId, StringComparison.Ordinal))
        {
            throw new VietsubLocalJobAuthorizationException(
                VietsubLocalJobAuthorizationErrorCodes.AccessDenied,
                "Phiên đăng nhập, tổ chức hoặc dự án local job không còn khớp.");
        }

        try
        {
            await accessContext.EnsureSessionAndLicenseAsync(cancellationToken);
        }
        catch (AccountClientException exception)
        {
            throw new VietsubLocalJobAuthorizationException(
                VietsubLocalJobAuthorizationErrorCodes.LicenseRequired,
                "License hoặc phiên thiết bị không còn hiệu lực.",
                exception);
        }

        // Personal Local projects are owned by the authenticated user, not an organization.
        // Recheck after the asynchronous license refresh before permitting any job.
        if (accessContext.CurrentUserId != userId || accessContext.SelectedOrganizationId != organizationId)
            throw new VietsubLocalJobAuthorizationException(VietsubLocalJobAuthorizationErrorCodes.AccessDenied,
                "Ngữ cảnh tài khoản đã thay đổi.");
        if (organizationId == Guid.Empty) return;

        IReadOnlyList<OrganizationSummaryResponse> organizations;
        try
        {
            organizations = await accessContext.GetOrganizationsAsync(cancellationToken);
        }
        catch (AccountClientException exception)
        {
            throw new VietsubLocalJobAuthorizationException(
                VietsubLocalJobAuthorizationErrorCodes.AccessDenied,
                "Không thể xác minh quyền thành viên tổ chức để chạy local job.",
                exception);
        }
        var membership = organizations.SingleOrDefault(item => item.OrganizationId == organizationId);
        if (membership is null
            || !string.Equals(membership.Status, "Active", StringComparison.OrdinalIgnoreCase)
            || !AllowedRoles.Contains(membership.Role))
        {
            throw new VietsubLocalJobAuthorizationException(
                VietsubLocalJobAuthorizationErrorCodes.AccessDenied,
                "Vai trò hiện tại không được phép phát sinh local job.");
        }
    }
}
