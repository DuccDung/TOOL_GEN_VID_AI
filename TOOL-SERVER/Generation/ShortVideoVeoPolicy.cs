using TOOL_SERVER.Authentication;
using TOOL_SERVER.Models;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_SERVER.Generation;

internal static class ShortVideoVeoPolicy
{
    public static void Validate(ProjectVideoSnapshot snapshot, Project project)
    {
        if (snapshot.ProviderCode != ProviderCodes.Fal || !FalVeoPolicy.IsApprovedEndpoint(snapshot.ModelCode))
            throw new AccountApiException(409, "short_video_veo_required",
                "Video ngắn dùng Veo. Dự án cũ cần chuyển sang Veo; tổ chức cần cấu hình Fal/Veo trong policy Video dài.");
        if (!snapshot.Capabilities.ReferenceImage || !snapshot.NativeAudio || !snapshot.Capabilities.NativeAudio ||
            snapshot.Resolution != FalVeoPolicy.Resolution || !snapshot.Capabilities.Resolutions.Contains(snapshot.Resolution) ||
            !ShortVideoVeo.SupportsDuration(project.TargetDurationSeconds) || !ShortVideoVeo.SupportsAspectRatio(project.AspectRatio) ||
            !snapshot.Capabilities.AllowedDurationsSeconds.Contains(project.TargetDurationSeconds) || !snapshot.Capabilities.AspectRatios.Contains(project.AspectRatio))
            throw new AccountApiException(422, "short_video_veo_variant_invalid",
                "Video ngắn Veo cần 720p, Native Audio, tỷ lệ 9:16 hoặc 16:9 và thời lượng 4, 6 hoặc 8 giây.");
    }
}
