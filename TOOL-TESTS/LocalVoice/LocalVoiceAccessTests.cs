using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Controllers;
using TOOL_SERVER.Organizations;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.LocalVoice;

public sealed class LocalVoiceAccessTests
{
    [Fact]
    public async Task Controller_UsesExistingLicenseMembershipRoleAndOwnershipGate()
    {
        var access = new Access(); var org = Guid.NewGuid(); var project = Guid.NewGuid(); var device = Guid.NewGuid();
        var controller = Controller(access, new(ClaimTypes.NameIdentifier, "user"), new(AuthClaimTypes.DeviceId, device.ToString()));
        var result = await controller.AuthorizeProject(new(org, project), default);
        Assert.Equal(new LocalVoiceAccessResponse(org, project), result);
        Assert.Equal(("user", device, (Guid?)org, (Guid?)project), access.Received);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingIdentityClaims_ReturnUnauthorizedBeforeAccessService(bool missingUser)
    {
        var access = new Access();
        var controller = Controller(access, missingUser ? new Claim(AuthClaimTypes.DeviceId, Guid.NewGuid().ToString()) : new Claim(ClaimTypes.NameIdentifier, "user"));
        await Assert.ThrowsAsync<AccountApiException>(() => controller.AuthorizeProject(new(Guid.NewGuid(), Guid.NewGuid()), default));
        Assert.Null(access.Received);
    }

    [Fact]
    public async Task RevokedOrViewerAccess_IsNotBypassedByLocalProcessing()
    {
        var access = new Access { Denied = true };
        var controller = Controller(access, new(ClaimTypes.NameIdentifier, "user"), new(AuthClaimTypes.DeviceId, Guid.NewGuid().ToString()));
        await Assert.ThrowsAsync<AccountApiException>(() => controller.AuthorizeProject(new(Guid.NewGuid(), Guid.NewGuid()), default));
    }

    private static LocalVoiceController Controller(Access access, params Claim[] claims) => new(access)
    { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) } } };
    private sealed class Access : IGenerationAccessService
    {
        public bool Denied { get; init; }
        public (string, Guid, Guid?, Guid?)? Received { get; private set; }
        public Task<GenerationAccessContext> RequireAsync(string userId, Guid deviceId, Guid? org, Guid? project, CancellationToken token)
        {
            Received = (userId, deviceId, org, project);
            if (Denied) throw new AccountApiException(403, "organization_access_denied", "Access denied");
            return Task.FromResult(new GenerationAccessContext(org!.Value, "test", "Editor", new() { ProjectId = project!.Value }));
        }
    }
}
