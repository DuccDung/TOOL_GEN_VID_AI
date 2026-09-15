using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Models;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_SERVER.Generation;

// An approved composition becomes a real SceneFirstFrame, retaining its original image request.
internal static class ShortVideoVeoFirstFrame
{
    internal const string Template = "short-video-outfit-veo-v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task BindAsync(VideoFactoryDbContext db, Project project, Scene scene,
        ShortVideoOperation operation, DateTime now, CancellationToken ct)
    {
        var image = JsonSerializer.Deserialize<ShortVideoComposition>(operation.ResultJson, Json)
            ?? throw new AccountApiException(409, "short_video_image_invalid", "Không còn metadata ảnh mặc thử.");
        var expected = project.AspectRatio == "9:16" ? (720, 1280) : (1280, 720);
        if (operation.Status != "Approved" || operation.ApprovedAtUtc is null || operation.ApprovedByUserId is null ||
            image.Width != expected.Item1 || image.Height != expected.Item2 || image.SizeBytes > FalVeoPolicy.MaximumReferenceImageBytes)
            throw new AccountApiException(409, "short_video_image_invalid", "Cần ảnh mặc thử đã duyệt, đúng tỷ lệ Veo và tối đa 8 MiB.");
        var frames = await db.SceneFirstFrames.Include(x => x.MediaAsset).Where(x => x.SceneId == scene.SceneId).ToListAsync(ct);
        var existing = frames.SingleOrDefault(x => x.GeneratedByProviderRequestId == operation.OperationId);
        if (existing is not null)
        {
            if (existing.Status != SceneFirstFrameStatuses.Approved || existing.MediaAsset.Sha256 != image.Sha256)
                throw new AccountApiException(409, "short_video_first_frame_stale", "Ảnh đầu vào Veo đã bị thay đổi. Hãy tạo và duyệt ảnh mới.");
            return;
        }
        var prompt = scene.ScenePrompts.Where(x => x.Status is "Approved" or "Ready").OrderByDescending(x => x.Version).First();
        foreach (var old in frames.Where(x => x.Status is SceneFirstFrameStatuses.Approved or SceneFirstFrameStatuses.PendingReview))
        { old.Status = SceneFirstFrameStatuses.Invalidated; old.InvalidatedAtUtc = now; }
        var version = frames.Select(x => x.Version).DefaultIfEmpty().Max() + 1;
        var media = new MediaAsset
        {
            MediaAssetId = Guid.NewGuid(), ProjectId = project.ProjectId, SceneId = scene.SceneId,
            AssetType = "SceneFirstFrame", DisplayName = $"Ảnh mặc thử cho Veo, bản {version}",
            RelativePath = $"projects/{project.ProjectId:N}/short-outfit/{image.Sha256.ToLowerInvariant()}{(image.MimeType == "image/png" ? ".png" : ".jpg")}",
            MimeType = image.MimeType, SizeBytes = image.SizeBytes, Sha256 = image.Sha256, Width = image.Width, Height = image.Height,
            Status = "Ready", SourceType = "Generated", SourceProviderCode = ProviderCodes.OpenAi,
            SourceProviderRequestId = operation.OperationId,
            MetadataJson = JsonSerializer.Serialize(new { operation.Revision, image.CompositionId }, Json),
            CreatedAtUtc = now, VerifiedAtUtc = operation.ApprovedAtUtc, RowVersion = new byte[8]
        };
        db.MediaAssets.Add(media);
        db.SceneFirstFrames.Add(new SceneFirstFrame
        {
            SceneFirstFrameId = Guid.NewGuid(), SceneId = scene.SceneId, MediaAssetId = media.MediaAssetId, MediaAsset = media,
            Version = version, Status = SceneFirstFrameStatuses.Approved, GeneratedByProviderRequestId = operation.OperationId,
            ScenePlanVersion = scene.ScenePlanVersion, ScenePromptId = prompt.ScenePromptId, ScenePromptVersion = prompt.Version,
            AspectRatio = project.AspectRatio, PromptTemplateVersion = Template, CreatedByUserId = operation.UserId,
            ApprovedByUserId = operation.ApprovedByUserId, ApprovedAtUtc = operation.ApprovedAtUtc, CreatedAtUtc = now, RowVersion = new byte[8]
        });
    }

    internal static async Task<bool> IsCurrentAsync(VideoFactoryDbContext db, SceneFirstFrame frame, ProviderRequest request, CancellationToken ct)
    {
        var state = await db.ShortVideoOutfits.AsNoTracking().SingleOrDefaultAsync(x => x.SceneId == frame.SceneId, ct);
        var operation = await db.ShortVideoOperations.AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == frame.GeneratedByProviderRequestId, ct);
        if (state is null || operation is null || state.CompositionId != operation.OperationId || state.Revision != operation.Revision ||
            operation.Status != "Approved" || operation.ApprovedAtUtc is null || operation.ProjectId != state.ProjectId ||
            request.ProjectId != state.ProjectId || request.SceneId != frame.SceneId || request.RequestKind != "Image" ||
            request.OrganizationId != operation.OrganizationId || request.RequestedByUserId != operation.UserId) return false;
        try
        {
            var image = JsonSerializer.Deserialize<ShortVideoComposition>(operation.ResultJson, Json);
            var output = JsonSerializer.Deserialize<ShortVideoComposition>(request.ResponseJson!, Json);
            return image is not null && output is not null && image == output && image.CompositionId == operation.OperationId &&
                image.Revision == state.Revision && image.Sha256 == frame.MediaAsset.Sha256 && image.SizeBytes == frame.MediaAsset.SizeBytes &&
                image.Width == frame.MediaAsset.Width && image.Height == frame.MediaAsset.Height && image.MimeType == frame.MediaAsset.MimeType;
        }
        catch (JsonException) { return false; }
    }
}
