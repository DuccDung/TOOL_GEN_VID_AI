using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_SHARED.Contracts.Organizations;

namespace TOOL_LOCAL.SystemSetup;

internal sealed class SystemSetupAuthorizer(IVietsubLocalAccessContext context)
{
    public string? UserId => context.CurrentUserId;
    public Guid? OrganizationId => context.SelectedOrganizationId;
    internal static bool CanManage(string role) => role is OrganizationRoles.Owner
        or OrganizationRoles.OrganizationAdmin
        or OrganizationRoles.BillingManager
        or OrganizationRoles.Member;

    public async Task AuthorizeAsync(string userId, Guid organizationId, CancellationToken token)
    {
        EnsureContext(userId, organizationId);
        await context.EnsureSessionAndLicenseAsync(token);
        var memberships = await context.GetOrganizationsAsync(token);
        EnsureContext(userId, organizationId);
        var member = memberships.SingleOrDefault(x => x.OrganizationId == organizationId);
        if (member is null || !string.Equals(member.Status, "Active", StringComparison.OrdinalIgnoreCase)
            || !CanManage(member.Role))
            throw new SetupException("system_setup_access_denied", "Vai trò hiện tại chỉ được xem trạng thái Setup.");
    }
    private void EnsureContext(string userId, Guid organizationId)
    {
        if (string.IsNullOrWhiteSpace(userId) || organizationId == Guid.Empty || UserId != userId || OrganizationId != organizationId)
            throw new SetupException("system_setup_context_changed", "Tài khoản hoặc tổ chức đã thay đổi. Hãy mở lại Setup.");
    }
}
