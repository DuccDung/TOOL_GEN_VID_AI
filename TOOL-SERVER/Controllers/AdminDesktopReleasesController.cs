using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Updates;

namespace TOOL_SERVER.Controllers;

[ApiController]
[Authorize(Roles = "Admin")]
[Route("api/admin/desktop-releases")]
public sealed class AdminDesktopReleasesController(IDesktopReleaseService releaseService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<AdminDesktopReleaseResponse>> List(CancellationToken cancellationToken) =>
        releaseService.GetAdminReleasesAsync(cancellationToken);

    [HttpPost]
    public async Task<ActionResult<AdminDesktopReleaseResponse>> Create(
        AdminDesktopReleaseRequest request,
        CancellationToken cancellationToken)
    {
        var release = await releaseService.CreateAsync(request, cancellationToken);
        return Created($"/api/admin/desktop-releases/{release.ReleaseId:D}", release);
    }

    [HttpPut("{releaseId:guid}")]
    public Task<AdminDesktopReleaseResponse> Update(
        Guid releaseId,
        AdminDesktopReleaseRequest request,
        CancellationToken cancellationToken) =>
        releaseService.UpdateAsync(releaseId, request, cancellationToken);

    [HttpPost("{releaseId:guid}/artifacts/{kind}")]
    [DisableRequestSizeLimit]
    public async Task<AdminDesktopArtifactResponse> Upload(
        Guid releaseId,
        string kind,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length <= 0)
        {
            throw new ArgumentException("Vui lòng chọn artifact để upload.");
        }

        await using var stream = file.OpenReadStream();
        return await releaseService.SaveArtifactAsync(
            releaseId,
            kind,
            file.FileName,
            stream,
            file.Length,
            cancellationToken);
    }

    [HttpDelete("{releaseId:guid}")]
    public async Task<IActionResult> Delete(Guid releaseId, CancellationToken cancellationToken)
    {
        await releaseService.DeleteAsync(releaseId, cancellationToken);
        return NoContent();
    }
}
