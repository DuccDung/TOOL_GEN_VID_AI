using TOOL_SERVER.Domain.Organizations;

namespace TOOL_TESTS.Organizations;

public sealed class OrganizationRoleTests
{
    [Theory]
    [InlineData(OrganizationMemberRoles.Owner, true, true, true, true)]
    [InlineData(OrganizationMemberRoles.OrganizationAdmin, true, true, true, true)]
    [InlineData(OrganizationMemberRoles.BillingManager, false, true, false, true)]
    [InlineData(OrganizationMemberRoles.Member, false, false, false, true)]
    [InlineData(OrganizationMemberRoles.Viewer, false, false, false, false)]
    public void RoleMatrix_EnforcesOrganizationCapabilities(
        string role,
        bool managesMembers,
        bool managesBilling,
        bool managesCredentials,
        bool generates)
    {
        Assert.Equal(managesMembers, OrganizationMemberRoles.CanManageMembers(role));
        Assert.Equal(managesBilling, OrganizationMemberRoles.CanManageBilling(role));
        Assert.Equal(managesCredentials, OrganizationMemberRoles.CanManageCredentials(role));
        Assert.Equal(generates, OrganizationMemberRoles.CanGenerate(role));
    }
}
