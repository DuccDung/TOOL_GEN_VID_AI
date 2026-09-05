using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_SHARED.Contracts.Organizations;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubOcrAuthorizationTests
{
    public static TheoryData<string> AllowedRoles => new()
    {
        OrganizationRoles.Owner,
        OrganizationRoles.OrganizationAdmin,
        OrganizationRoles.BillingManager,
        OrganizationRoles.Member
    };

    [Theory]
    [MemberData(nameof(AllowedRoles))]
    public async Task Authorizer_AllowsActiveCostGeneratingRoles(string role)
    {
        var organizationId = Guid.NewGuid();
        var context = new FakeAccessContext("user-1", organizationId, role, "Active");
        var authorizer = new VietsubLocalJobAuthorizer(context);

        await authorizer.AuthorizeAsync(
            "user-1",
            organizationId,
            CreateProject("user-1", organizationId),
            CancellationToken.None);

        Assert.Equal(1, context.AccessChecks);
        Assert.Equal(1, context.MembershipChecks);
    }

    [Theory]
    [InlineData(OrganizationRoles.Viewer, "Active")]
    [InlineData(OrganizationRoles.Member, "Disabled")]
    public async Task Authorizer_BlocksViewerAndInactiveMembership(string role, string status)
    {
        var organizationId = Guid.NewGuid();
        var authorizer = new VietsubLocalJobAuthorizer(
            new FakeAccessContext("user-1", organizationId, role, status));

        var exception = await Assert.ThrowsAsync<VietsubOcrException>(() =>
            authorizer.AuthorizeAsync(
                "user-1",
                organizationId,
                CreateProject("user-1", organizationId),
                CancellationToken.None));

        Assert.Equal(VietsubOcrErrorCodes.AccessDenied, exception.Code);
    }

    [Fact]
    public async Task Authorizer_BlocksWrongSelectedOrganizationBeforeRemoteChecks()
    {
        var organizationId = Guid.NewGuid();
        var context = new FakeAccessContext(
            "user-1",
            Guid.NewGuid(),
            OrganizationRoles.Member,
            "Active");
        var authorizer = new VietsubLocalJobAuthorizer(context);

        var exception = await Assert.ThrowsAsync<VietsubOcrException>(() =>
            authorizer.AuthorizeAsync(
                "user-1",
                organizationId,
                CreateProject("user-1", organizationId),
                CancellationToken.None));

        Assert.Equal(VietsubOcrErrorCodes.AccessDenied, exception.Code);
        Assert.Equal(0, context.AccessChecks);
        Assert.Equal(0, context.MembershipChecks);
    }

    [Fact]
    public async Task Authorizer_BlocksProjectOwnedByAnotherUser()
    {
        var organizationId = Guid.NewGuid();
        var context = new FakeAccessContext(
            "user-1",
            organizationId,
            OrganizationRoles.Member,
            "Active");
        var authorizer = new VietsubLocalJobAuthorizer(context);

        var exception = await Assert.ThrowsAsync<VietsubOcrException>(() =>
            authorizer.AuthorizeAsync(
                "user-1",
                organizationId,
                CreateProject("user-2", organizationId),
                CancellationToken.None));

        Assert.Equal(VietsubOcrErrorCodes.AccessDenied, exception.Code);
        Assert.Equal(0, context.AccessChecks);
    }

    private static VietsubProjectManifest CreateProject(string ownerUserId, Guid organizationId) => new()
    {
        ProjectId = Guid.NewGuid(),
        OwnerUserId = ownerUserId,
        OrganizationId = organizationId,
        Name = "OCR authorization"
    };

    private sealed class FakeAccessContext(
        string userId,
        Guid organizationId,
        string role,
        string status) : IVietsubLocalAccessContext
    {
        public string? CurrentUserId => userId;

        public Guid? SelectedOrganizationId => organizationId;

        public int AccessChecks { get; private set; }

        public int MembershipChecks { get; private set; }

        public Task EnsureSessionAndLicenseAsync(CancellationToken cancellationToken)
        {
            AccessChecks++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OrganizationSummaryResponse>> GetOrganizationsAsync(
            CancellationToken cancellationToken)
        {
            MembershipChecks++;
            return Task.FromResult<IReadOnlyList<OrganizationSummaryResponse>>(
                [new OrganizationSummaryResponse(
                    organizationId,
                    "ORG",
                    "Organization",
                    role,
                    status,
                    0,
                    0,
                    0,
                    0,
                    "USD",
                    DateTime.UtcNow,
                    DateTime.UtcNow.AddMonths(1))]);
        }
    }
}
