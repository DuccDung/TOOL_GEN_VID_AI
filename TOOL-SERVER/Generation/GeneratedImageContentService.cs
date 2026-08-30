using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Organizations;

namespace TOOL_SERVER.Generation;

public sealed record GeneratedImageContent(byte[] Payload, string MimeType, string Sha256, long SizeBytes);

public interface IGeneratedImageContentService
{
    Task<GeneratedImageContent> GetAsync(
        Guid providerRequestId,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken);
}

internal sealed class GeneratedImageContentService(
    VideoFactoryDbContext dbContext,
    IGenerationAccessService accessService,
    TimeProvider timeProvider) : IGeneratedImageContentService
{
    public async Task<GeneratedImageContent> GetAsync(
        Guid providerRequestId,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var request = await dbContext.ProviderRequests
            .Include(x => x.GeneratedImageOutput)
            .SingleOrDefaultAsync(
                x => x.ProviderRequestId == providerRequestId &&
                     x.RequestKind == "Image" &&
                     x.ProviderCode == ProviderCodes.OpenAi,
                cancellationToken)
            ?? throw NotFound();

        var access = await accessService.RequireAsync(
            userId,
            deviceId,
            request.OrganizationId,
            request.ProjectId,
            cancellationToken);
        if (request.OrganizationId != access.OrganizationId ||
            request.RequestedByUserId != userId ||
            request.CharacterId is null ||
            access.Project?.RemoteUserId != userId ||
            !await dbContext.Characters.AsNoTracking().AnyAsync(
                x => x.CharacterId == request.CharacterId && x.ProjectId == request.ProjectId,
                cancellationToken))
        {
            throw NotFound();
        }

        var output = request.GeneratedImageOutput;
        if (output is null || output.ExpiresAtUtc <= UtcNow())
        {
            throw new AccountApiException(
                StatusCodes.Status410Gone,
                "generated_image_expired",
                "Ảnh tạm trên server đã hết hạn. Hãy tạo lại ảnh nhân vật.");
        }
        if (output.Payload.LongLength != output.SizeBytes || output.Payload.LongLength > 10 * 1024 * 1024)
        {
            throw new AccountApiException(
                StatusCodes.Status502BadGateway,
                "generated_image_storage_invalid",
                "Ảnh tạm trên server không còn hợp lệ.");
        }

        output.DownloadedAtUtc ??= UtcNow();
        await dbContext.SaveChangesAsync(cancellationToken);
        return new GeneratedImageContent(output.Payload, output.MimeType, output.Sha256, output.SizeBytes);
    }

    private static AccountApiException NotFound() =>
        new(StatusCodes.Status404NotFound, "generated_image_not_found", "Không tìm thấy ảnh nhân vật.");

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
