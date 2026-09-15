using System.Data;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Models;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Organizations;

namespace TOOL_SERVER.Generation;

internal sealed partial class ShortVideoOutfitService
{
    public async Task<ShortVideoVeoMigrationResponse> MigrateToVeoAsync(ShortVideoVeoMigrationRequest request, string user, Guid device, CancellationToken ct)
    {
        var allowed = await access.RequireAsync(user, device, request.OrganizationId, request.ProjectId, ct);
        var project = allowed.Project!;
        if (!ShortVideoVeo.SupportsDuration(request.DurationSeconds) || !ShortVideoVeo.SupportsAspectRatio(request.AspectRatio))
            throw Error("short_video_veo_variant_invalid", "Chọn tỷ lệ 9:16/16:9 và thời lượng 4/6/8 giây.", 422);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await db.Entry(project).ReloadAsync(ct);
        var scene = await db.Scenes.Include(x => x.ScenePrompts).SingleOrDefaultAsync(x => x.ProjectId == project.ProjectId && x.ScenePlanVersion == project.CurrentScenePlanVersion, ct);
        if (scene is null || !await db.Scripts.AnyAsync(x => x.ScriptId == scene.ScriptId && x.StructureType == GenerationWorkflowTypes.DirectShortVideo, ct))
            throw Error("short_video_mode_invalid", "Chỉ chuyển dự án video ngắn sang Veo.");
        if (IsOutfit(scene.RequiredCapabilitiesJson)) Enabled();
        if (project.VideoProviderCode == ProviderCodes.Fal && project.TargetDurationSeconds == request.DurationSeconds && project.AspectRatio == request.AspectRatio)
        {
            await tx.CommitAsync(ct);
            return new(project.ProjectId, project.VideoProviderCode, project.VideoModelCode!, project.TargetDurationSeconds, project.AspectRatio,
                await db.ShortVideoOutfits.AnyAsync(x => x.SceneId == scene.SceneId && x.CompositionId != null, ct));
        }
        if (!string.Equals(project.VideoProviderCode, request.ExpectedProviderCode, StringComparison.OrdinalIgnoreCase))
            throw Error("short_video_project_changed", "Provider dự án đã đổi. Hãy tải lại trạng thái.");
        if (project.VideoProviderCode == ProviderCodes.Fal)
            throw Error("short_video_project_changed", "Dự án đã dùng Veo. Tạo bản sao để thay thiết lập.");
        await NoPendingVideoAsync(scene.SceneId, ct);
        if (await db.ProviderRequests.AnyAsync(x => x.SceneId == scene.SceneId && x.RequestKind == "Image" &&
            x.Status != "Completed" && x.Status != "Failed" && x.Status != "Cancelled" && x.Status != "Expired", ct))
            throw Error("short_video_operation_pending", "Ảnh đầu vào trước đang xử lý hoặc chưa rõ kết quả. Chưa thể chuyển dự án.");
        if (await db.ShortVideoOperations.AnyAsync(x => x.SceneId == scene.SceneId && (x.Status == "Submitting" || x.Status == "Unknown"), ct))
            throw Error("short_video_operation_pending", "Cần chờ hoặc đối soát thao tác đang chạy trước khi chuyển Veo.");
        // Resolve into a separate object: existing snapshots are changed only by this explicit action.
        var target = new Project { ProjectId = project.ProjectId, OrganizationId = project.OrganizationId,
            TargetDurationSeconds = request.DurationSeconds, AspectRatio = request.AspectRatio };
        var snapshot = await policies.ResolveAsync(target, request.OrganizationId, OrganizationVideoPolicyScopes.LongForm, ct);
        ShortVideoVeoPolicy.Validate(snapshot, target);
        var runtime = await providers.ResolveModelAsync(request.OrganizationId, snapshot.ProviderCode, "Video", snapshot.ModelCode, null, true, ct);
        var price = await costs.QuoteVideoAsync(snapshot.ProviderCode, runtime.ProviderModelId, request.DurationSeconds, snapshot.Resolution, snapshot.NativeAudio, snapshot.Capabilities.FramesPerSecond, ct);
        if (price.EstimatedCost <= 0) throw Error("pricing_not_configured", "Chưa cấu hình giá Veo hợp lệ.");
        var ratioChanged = project.AspectRatio != request.AspectRatio;
        project.VideoProviderCode = snapshot.ProviderCode; project.VideoModelCode = snapshot.ModelCode;
        project.VideoPolicyVersion = snapshot.PolicyVersion; project.VideoResolution = snapshot.Resolution;
        project.VideoNativeAudio = snapshot.NativeAudio; project.VideoSnapshotAtUtc = Now;
        project.TargetDurationSeconds = request.DurationSeconds; project.AspectRatio = request.AspectRatio;
        project.OutputWidth = request.AspectRatio == "9:16" ? 1080 : 1920;
        project.OutputHeight = request.AspectRatio == "9:16" ? 1920 : 1080;
        project.Status = "ScenePlanning"; project.UpdatedAtUtc = Now;
        scene.ContentDurationMs = scene.GenerationDurationMs = scene.TimelineEndMs = request.DurationSeconds * 1000L;
        scene.TimelineStartMs = 0; scene.TailTrimMs = 0;
        scene.ApprovedGenerationId = null; scene.ApprovedRenderMediaAssetId = null;
        scene.Status = "PromptReady"; scene.LastErrorCode = null; scene.LastErrorMessage = null; scene.UpdatedAtUtc = Now;
        var capability = JsonNode.Parse(scene.RequiredCapabilitiesJson ?? "{}")!.AsObject();
        capability["textToVideo"] = false; capability["requiresFirstFrame"] = true;
        capability["maxDurationSeconds"] = 8; capability["requestedDurationSeconds"] = request.DurationSeconds;
        capability["providerDurationSeconds"] = request.DurationSeconds; capability["aspectRatio"] = request.AspectRatio;
        scene.RequiredCapabilitiesJson = capability.ToJsonString();
        foreach (var quote in await db.ShortVideoOperations.Where(x => x.SceneId == scene.SceneId && x.Status == "Quoted").ToListAsync(ct))
            quote.ExpiresAtUtc = Now;
        var state = await db.ShortVideoOutfits.SingleOrDefaultAsync(x => x.SceneId == scene.SceneId, ct);
        var preserved = false;
        if (state is not null)
        {
            if (ratioChanged) { state.Revision++; state.CompositionId = null; }
            else if (state.CompositionId is { } compositionId)
            {
                var composition = await db.ShortVideoOperations.SingleAsync(x => x.OperationId == compositionId, ct);
                if (composition.Status == "Approved")
                {
                    await ShortVideoVeoFirstFrame.BindAsync(db, project, scene, composition, Now, ct);
                    preserved = true;
                }
            }
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(project.ProjectId, snapshot.ProviderCode, snapshot.ModelCode, request.DurationSeconds, request.AspectRatio, preserved);
    }
}
