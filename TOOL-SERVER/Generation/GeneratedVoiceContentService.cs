using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Organizations;

namespace TOOL_SERVER.Generation;

public sealed record GeneratedVoiceContent(byte[] Payload, string MimeType, string Sha256, long SizeBytes);

public interface IGeneratedVoiceContentService
{
    Task<GeneratedVoiceContent> GetAsync(
        Guid providerRequestId,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken);
}

internal sealed class GeneratedVoiceContentService(
    VideoFactoryDbContext dbContext,
    IGenerationAccessService accessService,
    TimeProvider timeProvider) : IGeneratedVoiceContentService
{
    private const int MaximumVoiceBytes = 50 * 1024 * 1024;

    public async Task<GeneratedVoiceContent> GetAsync(
        Guid providerRequestId,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var request = await dbContext.ProviderRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ProviderRequestId == providerRequestId &&
                     x.RequestKind == "Voice" &&
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
            request.SceneId is null ||
            access.Project?.RemoteUserId != userId ||
            !await dbContext.Scenes.AsNoTracking().AnyAsync(
                x => x.SceneId == request.SceneId && x.ProjectId == request.ProjectId,
                cancellationToken))
        {
            throw NotFound();
        }

        var now = UtcNow();
        var output = await dbContext.GeneratedVoiceOutputs
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProviderRequestId == providerRequestId, cancellationToken);
        if (output is null || output.ExpiresAtUtc <= now)
        {
            throw new AccountApiException(
                StatusCodes.Status410Gone,
                "generated_voice_expired",
                "Giọng đọc tạm trên server đã hết hạn. Hãy tạo lại giọng đọc cho cảnh.");
        }
        var actualHash = Convert.ToHexString(SHA256.HashData(output.Payload)).ToLowerInvariant();
        if (output.MimeType != "audio/wav" ||
            output.Payload.LongLength != output.SizeBytes ||
            output.Payload.LongLength is <= 0 or > MaximumVoiceBytes ||
            !string.Equals(actualHash, output.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new AccountApiException(
                StatusCodes.Status502BadGateway,
                "generated_voice_storage_invalid",
                "Giọng đọc tạm trên server không còn hợp lệ.");
        }

        if (output.DownloadedAtUtc is null)
        {
            if (dbContext.Database.IsRelational())
            {
                await dbContext.GeneratedVoiceOutputs
                    .Where(x => x.ProviderRequestId == providerRequestId && x.DownloadedAtUtc == null)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(x => x.DownloadedAtUtc, now),
                        cancellationToken);
            }
            else
            {
                var tracked = await dbContext.GeneratedVoiceOutputs
                    .SingleAsync(x => x.ProviderRequestId == providerRequestId, cancellationToken);
                tracked.DownloadedAtUtc ??= now;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        return new GeneratedVoiceContent(output.Payload, output.MimeType, output.Sha256, output.SizeBytes);
    }

    private static AccountApiException NotFound() =>
        new(StatusCodes.Status404NotFound, "generated_voice_not_found", "Không tìm thấy giọng đọc của cảnh.");

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
