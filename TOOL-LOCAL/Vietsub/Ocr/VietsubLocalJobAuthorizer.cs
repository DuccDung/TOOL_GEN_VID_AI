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

    public Guid? SelectedOrganizationId => generationClient.SelectedOrganizationId;

    public async Task EnsureSessionAndLicenseAsync(CancellationToken cancellationToken)
    {
        _ = await sessionManager.GetValidAccessTokenAsync(cancellationToken);
        _ = await licenseManager.EnsureAccessAsync(cancellationToken);
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
            || organizationId == Guid.Empty
            || accessContext.SelectedOrganizationId != organizationId
            || project.OrganizationId != organizationId
            || !string.Equals(project.OwnerUserId, userId, StringComparison.Ordinal))
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.AccessDenied,
                "Phiên đăng nhập, tổ chức hoặc dự án OCR không còn khớp.");
        }

        try
        {
            await accessContext.EnsureSessionAndLicenseAsync(cancellationToken);
        }
        catch (AccountClientException exception)
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.LicenseRequired,
                "License hoặc phiên thiết bị không còn hiệu lực.",
                exception);
        }

        IReadOnlyList<OrganizationSummaryResponse> organizations;
        try
        {
            organizations = await accessContext.GetOrganizationsAsync(cancellationToken);
        }
        catch (AccountClientException exception)
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.AccessDenied,
                "Không thể xác minh quyền thành viên tổ chức để chạy OCR.",
                exception);
        }
        var membership = organizations.SingleOrDefault(item => item.OrganizationId == organizationId);
        if (membership is null
            || !string.Equals(membership.Status, "Active", StringComparison.OrdinalIgnoreCase)
            || !AllowedRoles.Contains(membership.Role))
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.AccessDenied,
                "Vai trò hiện tại không được phép phát sinh OCR local.");
        }
    }
}
