using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/desktop-bootstrappers")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminDesktopBootstrappersController(DesktopBootstrapperService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<DesktopBootstrapperAdminResponse>> List(CancellationToken token) => service.ListAsync(token);
    [HttpGet("status")]
    public DesktopSharedBootstrapperStatusResponse Status() => service.Status();
    [HttpPost]
    [RequestSizeLimit(DesktopArtifactDownloadPolicy.MaximumSetupArtifactBytes + 65536)]
    public async Task<DesktopBootstrapperAdminResponse> Upload([FromForm] IFormFile file, CancellationToken token)
    {
        if (file is null) throw new ArgumentException("Thiếu file Setup.");
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException();
        await using var source = file.OpenReadStream();
        return await service.UploadAsync(actor, file.FileName, source, file.Length, token);
    }
    [HttpPost("{id:guid}/activate")]
    public Task<DesktopBootstrapperAdminResponse> Activate(Guid id, DesktopBootstrapperActivationRequest request, CancellationToken token) => service.ActivateAsync(id, request.Revision, token);
    [HttpPost("{id:guid}/revoke")]
    public Task<DesktopBootstrapperAdminResponse> Revoke(Guid id, DesktopBootstrapperActivationRequest request, CancellationToken token) => service.RevokeAsync(id, request.Revision, token);
}
