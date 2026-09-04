using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Models;
using TOOL_SHARED.Contracts.Organizations;

namespace TOOL_SERVER.Generation;

internal sealed record VideoModelCapabilities(
    int MinimumDurationSeconds,
    int MaximumDurationSeconds,
    IReadOnlySet<int> AllowedDurationsSeconds,
    int FramesPerSecond,
    IReadOnlySet<string> Resolutions,
    IReadOnlySet<string> AspectRatios,
    bool NativeAudio,
    bool ReferenceImage)
{
    public static VideoModelCapabilities KlingDefault { get; } = new(
        3,
        15,
        Enumerable.Range(3, 13).ToHashSet(),
        24,
        new HashSet<string>(["720p"], StringComparer.OrdinalIgnoreCase),
        new HashSet<string>(["16:9", "9:16", "1:1"], StringComparer.OrdinalIgnoreCase),
        true,
        true);

    public static VideoModelCapabilities Parse(string? json, string providerCode)
    {
        var fallback = providerCode == ProviderCodes.BytePlus
            ? new VideoModelCapabilities(
                4,
                15,
                Enumerable.Range(4, 12).ToHashSet(),
                24,
                new HashSet<string>(["720p"], StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(["16:9", "9:16", "1:1"], StringComparer.OrdinalIgnoreCase),
                true,
                true)
            : KlingDefault;
        if (string.IsNullOrWhiteSpace(json))
        {
            return fallback;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var minimum = ReadInt(root, "minDurationSeconds") ??
                          ReadIntArray(root, "durations").DefaultIfEmpty(fallback.MinimumDurationSeconds).Min();
            var maximum = ReadInt(root, "maxDurationSeconds") ??
                          ReadIntArray(root, "durations").DefaultIfEmpty(fallback.MaximumDurationSeconds).Max();
            var configuredDurations = ReadIntArray(root, "durations")
                .Where(x => x > 0)
                .Distinct()
                .ToHashSet();
            var allowedDurations = configuredDurations.Count > 0
                ? configuredDurations
                : Enumerable.Range(minimum, maximum - minimum + 1).ToHashSet();
            var fps = ReadInt(root, "framesPerSecond") ?? fallback.FramesPerSecond;
            return new VideoModelCapabilities(
                minimum,
                maximum,
                allowedDurations,
                fps,
                ReadStringSet(root, "resolutions", fallback.Resolutions),
                ReadStringSet(root, "aspectRatios", fallback.AspectRatios),
                ReadBool(root, "nativeAudio") ?? fallback.NativeAudio,
                ReadBool(root, "referenceImage") ?? fallback.ReferenceImage);
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static int? ReadInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static bool? ReadBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static IEnumerable<int> ReadIntArray(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(x => x.TryGetInt32(out _)).Select(x => x.GetInt32())
            : [];

    private static IReadOnlySet<string> ReadStringSet(
        JsonElement root,
        string name,
        IReadOnlySet<string> fallback)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return fallback;
        }
        var result = value.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return result.Count == 0 ? fallback : result;
    }
}

internal sealed record ProjectVideoSnapshot(
    string ProviderCode,
    string ProviderName,
    string ModelCode,
    string ModelName,
    int PolicyVersion,
    string Resolution,
    bool NativeAudio,
    VideoModelCapabilities Capabilities);

internal interface IProjectVideoPolicyResolver
{
    Task<ProjectVideoSnapshot> ResolveAsync(
        Project project,
        Guid organizationId,
        string policyScope,
        CancellationToken cancellationToken);
}

internal sealed class ProjectVideoPolicyResolver(
    AiGovernanceDbContext governanceDb,
    ProviderAdminDbContext providerDb,
    TimeProvider timeProvider) : IProjectVideoPolicyResolver
{
    public async Task<ProjectVideoSnapshot> ResolveAsync(
        Project project,
        Guid organizationId,
        string policyScope,
        CancellationToken cancellationToken)
    {
        if (project.OrganizationId != organizationId)
        {
            throw new AccountApiException(
                StatusCodes.Status404NotFound,
                "project_not_found",
                "Không tìm thấy dự án trong tổ chức.");
        }

        if (!string.IsNullOrWhiteSpace(project.VideoProviderCode) &&
            !string.IsNullOrWhiteSpace(project.VideoModelCode) &&
            project.VideoPolicyVersion is > 0 &&
            !string.IsNullOrWhiteSpace(project.VideoResolution) &&
            project.VideoNativeAudio is { } nativeAudio)
        {
            return await LoadSnapshotAsync(
                project.VideoProviderCode,
                project.VideoModelCode,
                project.VideoPolicyVersion.Value,
                project.VideoResolution,
                nativeAudio,
                false,
                cancellationToken);
        }

        policyScope = ValidatePolicyScope(policyScope);
        var policy = await governanceDb.OrganizationVideoPolicies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrganizationId == organizationId &&
                     x.PolicyScope == policyScope &&
                     x.IsActive,
                cancellationToken);
        if (policy is null && policyScope == OrganizationVideoPolicyScopes.LongForm)
        {
            policy = await governanceDb.OrganizationVideoPolicies
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.OrganizationId == organizationId &&
                         x.PolicyScope == OrganizationVideoPolicyScopes.Default &&
                         x.IsActive,
                    cancellationToken);
        }
        if (policy is null)
        {
            throw new AccountApiException(
                StatusCodes.Status503ServiceUnavailable,
                "video_policy_not_configured",
                "Tổ chức chưa cấu hình model tạo video. Hãy liên hệ quản trị viên tổ chức.");
        }
        var provider = await providerDb.Providers
            .AsNoTracking()
            .Include(x => x.Models)
            .SingleOrDefaultAsync(
                x => x.ProviderId == policy.ProviderId && x.IsEnabled,
                cancellationToken);
        var model = provider?.Models.SingleOrDefault(
            x => x.ProviderModelId == policy.ProviderModelId &&
                 x.IsEnabled &&
                 x.Modality == "Video");
        if (provider is null || model is null)
        {
            throw new AccountApiException(
                StatusCodes.Status503ServiceUnavailable,
                "video_model_not_enabled",
                "Model video của tổ chức hiện không được Global Admin cho phép.");
        }

        var capabilities = VideoModelCapabilities.Parse(model.CapabilitiesJson, provider.ProviderCode);
        ValidateVariant(policy.Resolution, policy.NativeAudio, capabilities);
        project.VideoProviderCode = provider.ProviderCode;
        project.VideoModelCode = model.ModelCode;
        project.VideoPolicyVersion = policy.PolicyVersion;
        project.VideoResolution = policy.Resolution;
        project.VideoNativeAudio = policy.NativeAudio;
        project.VideoSnapshotAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        return new ProjectVideoSnapshot(
            provider.ProviderCode,
            provider.DisplayName,
            model.ModelCode,
            model.DisplayName,
            policy.PolicyVersion,
            policy.Resolution,
            policy.NativeAudio,
            capabilities);
    }

    private async Task<ProjectVideoSnapshot> LoadSnapshotAsync(
        string providerCode,
        string modelCode,
        int policyVersion,
        string resolution,
        bool nativeAudio,
        bool requireEnabled,
        CancellationToken cancellationToken)
    {
        var provider = await providerDb.Providers
            .AsNoTracking()
            .Include(x => x.Models)
            .SingleOrDefaultAsync(
                x => x.ProviderCode == providerCode && (!requireEnabled || x.IsEnabled),
                cancellationToken)
            ?? throw SnapshotUnavailable();
        var model = provider.Models.SingleOrDefault(
            x => x.ModelCode == modelCode && x.Modality == "Video" && (!requireEnabled || x.IsEnabled))
            ?? throw SnapshotUnavailable();
        var capabilities = VideoModelCapabilities.Parse(model.CapabilitiesJson, provider.ProviderCode);
        ValidateVariant(resolution, nativeAudio, capabilities);
        return new ProjectVideoSnapshot(
            provider.ProviderCode,
            provider.DisplayName,
            model.ModelCode,
            model.DisplayName,
            policyVersion,
            resolution,
            nativeAudio,
            capabilities);
    }

    internal static void ValidateVariant(
        string resolution,
        bool nativeAudio,
        VideoModelCapabilities capabilities)
    {
        if (!capabilities.Resolutions.Contains(resolution) ||
            (nativeAudio && !capabilities.NativeAudio))
        {
            throw new AccountApiException(
                StatusCodes.Status422UnprocessableEntity,
                "video_variant_not_supported",
                "Model video không hỗ trợ độ phân giải hoặc Native Audio đã cấu hình.");
        }
    }

    private static AccountApiException SnapshotUnavailable() =>
        new(
            StatusCodes.Status503ServiceUnavailable,
            "video_snapshot_unavailable",
            "Provider/model đã gắn với dự án không còn trong catalog. Không tự động chuyển sang model khác.");

    private static string ValidatePolicyScope(string? value) => value switch
    {
        OrganizationVideoPolicyScopes.Default => value,
        OrganizationVideoPolicyScopes.LongForm => value,
        _ => throw new AccountApiException(
            StatusCodes.Status422UnprocessableEntity,
            "video_policy_scope_invalid",
            "Phạm vi policy video không hợp lệ.")
    };
}
