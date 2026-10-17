using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using TOOL_SERVER.ModelDistribution;
using TOOL_SHARED.Contracts.Common;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.ModelDistribution;

namespace TOOL_SERVER.Controllers;

[ApiController]
[AllowAnonymous]
[RequireHttps]
[EnableRateLimiting("model-distribution")]
[Route("api/model-distribution")]
public sealed class ModelDistributionController(IModelDistributionStore store) : ControllerBase
{
    [HttpGet("{channel}/catalog")]
    [HttpHead("{channel}/catalog")]
    public async Task<IActionResult> Catalog(string channel, CancellationToken cancellationToken)
    {
        SetSecurityHeaders();
        try
        {
            var catalog = await store.GetCatalogAsync(channel, cancellationToken);
            Response.Headers.CacheControl = "no-cache, no-transform";
            return new FileContentResult(catalog.EnvelopeBytes, "application/json")
            {
                EntityTag = new EntityTagHeaderValue($"\"{catalog.EnvelopeSha256}\""),
                EnableRangeProcessing = false
            };
        }
        catch (ModelDistributionException exception)
        {
            return Failure(exception);
        }
    }

    [HttpGet("{channel}/artifacts/{sha256}/{fileName}")]
    [HttpHead("{channel}/artifacts/{sha256}/{fileName}")]
    public async Task<IActionResult> Artifact(string channel, string sha256, string fileName, CancellationToken cancellationToken)
    {
        SetSecurityHeaders();
        try
        {
            var artifact = await store.OpenArtifactAsync(channel, sha256, fileName, cancellationToken);
            Response.Headers.CacheControl = "public, max-age=31536000, immutable, no-transform";
            return new FileStreamResult(artifact.Stream, "application/octet-stream")
            {
                EntityTag = new EntityTagHeaderValue($"\"{artifact.Sha256}\""),
                FileDownloadName = artifact.FileName,
                EnableRangeProcessing = true
            };
        }
        catch (ModelDistributionException exception)
        {
            return Failure(exception);
        }
    }

    private void SetSecurityHeaders()
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "no-store";
    }

    private ObjectResult Failure(ModelDistributionException exception)
    {
        var statusCode = exception.Code is ModelDistributionErrors.Disabled or ModelDistributionErrors.ArtifactNotFound
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status503ServiceUnavailable;
        if (exception.Code == ModelDistributionErrors.Busy)
        {
            Response.Headers.RetryAfter = "5";
        }
        return StatusCode(statusCode, new ApiErrorResponse(exception.Code,
            "Kho model chưa sẵn sàng hoặc thành phần yêu cầu chưa được phát hành."));
    }
}
