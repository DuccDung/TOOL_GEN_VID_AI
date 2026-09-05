using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Organizations;

namespace TOOL_SERVER.Generation;

public sealed record GeneratedImageContent(byte[] Payload, string MimeType, string Sha256, long SizeBytes);

public enum GeneratedImageContentKind
{
    Any,
    CharacterReference,
    SceneFirstFrame
}

public interface IGeneratedImageContentService
{
    Task<GeneratedImageContent> GetAsync(
        Guid providerRequestId,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken,
        GeneratedImageContentKind kind = GeneratedImageContentKind.Any);
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
        CancellationToken cancellationToken,
        GeneratedImageContentKind kind = GeneratedImageContentKind.Any)
    {
        var request = await dbContext.ProviderRequests
            .Include(x => x.GeneratedImageOutput)
            .SingleOrDefaultAsync(
                x => x.ProviderRequestId == providerRequestId &&
                     x.RequestKind == "Image" &&
                     x.ProviderCode == ProviderCodes.OpenAi &&
                     (kind == GeneratedImageContentKind.Any ||
                      kind == GeneratedImageContentKind.CharacterReference && x.CharacterId != null && x.SceneId == null ||
                      kind == GeneratedImageContentKind.SceneFirstFrame && x.SceneId != null),
                cancellationToken)
            ?? throw NotFound();

        var access = await accessService.RequireAsync(
            userId,
            deviceId,
            request.OrganizationId,
            request.ProjectId,
            cancellationToken);
        var ownsCharacter = request.CharacterId is { } characterId &&
            await dbContext.Characters.AsNoTracking().AnyAsync(
                x => x.CharacterId == characterId && x.ProjectId == request.ProjectId,
                cancellationToken);
        var ownsScene = request.SceneId is { } sceneId &&
            await dbContext.Scenes.AsNoTracking().AnyAsync(
                x => x.SceneId == sceneId && x.ProjectId == request.ProjectId,
                cancellationToken);
        if (request.OrganizationId != access.OrganizationId ||
            request.RequestedByUserId != userId ||
            access.Project?.RemoteUserId != userId ||
            (!ownsCharacter && !ownsScene))
        {
            throw NotFound();
        }

        var output = request.GeneratedImageOutput;
        if (output is null || output.ExpiresAtUtc <= UtcNow())
        {
            throw new AccountApiException(
                StatusCodes.Status410Gone,
                "generated_image_expired",
                "Ảnh tạm trên server đã hết hạn. Hãy tạo lại ảnh.");
        }
        var maximumBytes = request.SceneId is null ? 10L * 1024 * 1024 : 8L * 1024 * 1024;
        if (output.Payload.LongLength != output.SizeBytes || output.Payload.LongLength > maximumBytes)
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
        new(StatusCodes.Status404NotFound, "generated_image_not_found", "Không tìm thấy ảnh đã sinh.");

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
