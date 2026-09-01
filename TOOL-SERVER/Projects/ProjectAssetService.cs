using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Models;
using TOOL_SERVER.Organizations;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Projects;

namespace TOOL_SERVER.Projects;

public interface IProjectAssetService
{
    Task<ProjectAssetLibraryResponse> GetLibraryAsync(
        Guid projectId,
        Guid? organizationId,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken);

    Task<ProjectAssetSummary> CreateAsync(
        Guid projectId,
        CreateProjectAssetRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken);

    Task<ProjectAssetSummary> UpdateAsync(
        Guid projectId,
        Guid projectAssetId,
        UpdateProjectAssetRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken);

    Task<ProjectAssetSummary> LockAsync(
        Guid projectId,
        Guid projectAssetId,
        ChangeProjectAssetLockRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken);

    Task<ProjectAssetSummary> UnlockAsync(
        Guid projectId,
        Guid projectAssetId,
        ChangeProjectAssetLockRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken);

    Task<ApproveAiProjectAssetsResponse> ApproveAiAssetsAsync(
        Guid projectId,
        ApproveAiProjectAssetsRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid projectId,
        Guid projectAssetId,
        DeleteProjectAssetRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken);

    Task<SceneAssetAssignmentSummary> UpdateSceneAssignmentsAsync(
        Guid projectId,
        Guid sceneId,
        UpdateSceneAssetAssignmentsRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken);

    Task<ConfirmSceneProjectAssetsResponse> ConfirmSceneAssetsAsync(
        Guid projectId,
        Guid sceneId,
        ConfirmSceneProjectAssetsRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken);

    Task<MaterializeProjectAssetPlanResponse> MaterializeAsync(
        Guid projectId,
        MaterializeProjectAssetPlanRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken);
}

internal sealed class ProjectAssetService(
    VideoFactoryDbContext dbContext,
    IGenerationAccessService accessService,
    TimeProvider timeProvider) : IProjectAssetService
{
    private const int MaximumAssetsPerScene = 20;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<ProjectAssetLibraryResponse> GetLibraryAsync(
        Guid projectId,
        Guid? organizationId,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var access = await RequireAccessAsync(
            projectId,
            organizationId,
            userId,
            deviceId,
            cancellationToken);
        var assets = await dbContext.ProjectAssets
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.AssetType)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var currentPlanVersion = access.Project!.CurrentScenePlanVersion;
        var assignments = currentPlanVersion is null
            ? []
            : await dbContext.SceneAssetAssignments
                .AsNoTracking()
                .Where(x => x.Scene.ProjectId == projectId &&
                            x.Scene.ScenePlanVersion == currentPlanVersion.Value)
                .Select(x => new AssignmentRow(x.SceneId, x.ProjectAssetId, x.ProjectAsset.Status))
                .ToListAsync(cancellationToken);
        var currentSceneIds = currentPlanVersion is null
            ? []
            : await dbContext.Scenes
                .AsNoTracking()
                .Where(x => x.ProjectId == projectId && x.ScenePlanVersion == currentPlanVersion.Value)
                .OrderBy(x => x.SequenceNumber)
                .Select(x => x.SceneId)
                .ToListAsync(cancellationToken);
        var sceneIdsByAsset = assignments
            .GroupBy(x => x.ProjectAssetId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<Guid>)x.Select(row => row.SceneId).Distinct().ToArray());
        var assetSummaries = assets
            .Select(asset => ToSummary(
                asset,
                sceneIdsByAsset.GetValueOrDefault(asset.ProjectAssetId) ?? []))
            .ToArray();
        var assignmentsByScene = assignments
            .GroupBy(x => x.SceneId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        var assetsById = assets.ToDictionary(x => x.ProjectAssetId);
        var sceneAssignments = new List<SceneAssetAssignmentSummary>(currentSceneIds.Count);
        foreach (var sceneId in currentSceneIds)
        {
            var rows = assignmentsByScene.GetValueOrDefault(sceneId) ?? [];
            var assignedAssets = rows
                .Select(x => assetsById.GetValueOrDefault(x.ProjectAssetId))
                .Where(x => x is not null)
                .Cast<ProjectAsset>()
                .ToArray();
            var preflight = await EvaluateSceneAssignmentAsync(
                access.Project,
                sceneId,
                assignedAssets,
                cancellationToken);
            sceneAssignments.Add(ToAssignmentSummary(sceneId, assignedAssets, preflight));
        }

        return new ProjectAssetLibraryResponse(
            projectId,
            OrganizationMemberRoles.CanGenerate(access.OrganizationRole),
            assetSummaries,
            sceneAssignments);
    }

    public async Task<ProjectAssetSummary> CreateAsync(
        Guid projectId,
        CreateProjectAssetRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var access = await RequireAccessAsync(
            projectId,
            request.OrganizationId,
            userId,
            deviceId,
            cancellationToken);
        RequireEdit(access);
        var input = ValidateInput(request.AssetType, request.Name, request.CanonicalDescription);
        await ValidateKlingLongFormVietnameseAsync(
            access.Project!,
            input.Name,
            input.CanonicalDescription,
            cancellationToken);
        var now = UtcNow();
        var projectAssetId = Guid.NewGuid();
        var asset = new ProjectAsset
        {
            ProjectAssetId = projectAssetId,
            ProjectId = projectId,
            AssetKey = $"manual-{projectAssetId:N}",
            AssetType = input.AssetType,
            Name = input.Name,
            CanonicalDescription = input.CanonicalDescription,
            Status = ProjectAssetStatuses.Draft,
            SourceKind = ProjectAssetSourceKinds.Manual,
            CurrentVersion = 0,
            CreatedAtUtc = now,
            CreatedByUserId = userId,
            UpdatedAtUtc = now,
            UpdatedByUserId = userId,
            RowVersion = new byte[8]
        };
        dbContext.ProjectAssets.Add(asset);
        await SaveWithNameConflictAsync(cancellationToken);
        return ToSummary(asset, []);
    }

    public async Task<MaterializeProjectAssetPlanResponse> MaterializeAsync(
        Guid projectId,
        MaterializeProjectAssetPlanRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        if (request.ProviderRequestId == Guid.Empty || request.ScenePlanVersion < 1)
        {
            throw new ArgumentException("Provider request và phiên bản scene plan không hợp lệ.");
        }
        var access = await RequireAccessAsync(
            projectId,
            request.OrganizationId,
            userId,
            deviceId,
            cancellationToken);
        RequireEdit(access);
        if (access.Project!.CurrentScenePlanVersion != request.ScenePlanVersion)
        {
            throw Conflict(
                "asset_plan_version_changed",
                "Scene plan hiện hành đã thay đổi. Hãy làm mới dự án trước khi đồng bộ tài sản AI.");
        }

        var providerRequest = await dbContext.ProviderRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ProviderRequestId == request.ProviderRequestId &&
                     x.ProjectId == projectId &&
                     x.OrganizationId == access.OrganizationId &&
                     x.RequestKind == "Text" &&
                     x.Status == "Completed",
                cancellationToken)
            ?? throw NotFound(
                "asset_plan_request_not_found",
                "Không tìm thấy content plan AI đã hoàn tất cho dự án.");
        if (string.IsNullOrWhiteSpace(providerRequest.ResponseJson))
        {
            throw Conflict("asset_plan_response_missing", "Content plan AI không có dữ liệu để đồng bộ tài sản.");
        }

        GeneratedContentResponse response;
        try
        {
            response = JsonSerializer.Deserialize<GeneratedContentResponse>(providerRequest.ResponseJson, JsonOptions)
                ?? throw new JsonException("Empty content response.");
        }
        catch (JsonException exception)
        {
            throw new AccountApiException(
                StatusCodes.Status409Conflict,
                "asset_plan_response_invalid",
                $"Content plan AI đã lưu không đúng cấu trúc: {exception.Message}");
        }
        if (response.ProviderRequestId != request.ProviderRequestId)
        {
            throw Conflict("asset_plan_request_mismatch", "Content plan AI không khớp provider request đã chọn.");
        }

        var generatedAssets = response.Plan.Assets ?? [];
        if (generatedAssets.Count == 0)
        {
            return new MaterializeProjectAssetPlanResponse(
                projectId,
                request.ProviderRequestId,
                request.ScenePlanVersion,
                0,
                0,
                0,
                0,
                ["Content plan cũ chưa có thư viện tài sản AI."]);
        }
        var validatedAssets = ValidateGeneratedAssets(generatedAssets);
        var scenes = await dbContext.Scenes
            .Where(x => x.ProjectId == projectId && x.ScenePlanVersion == request.ScenePlanVersion)
            .OrderBy(x => x.SequenceNumber)
            .ToListAsync(cancellationToken);
        if (scenes.Count != response.Plan.Scenes.Count)
        {
            throw Conflict(
                "asset_plan_scenes_not_ready",
                "Các cảnh của content plan chưa được lưu đầy đủ. Hãy thử đồng bộ lại sau.");
        }
        var scenesBySequence = scenes.ToDictionary(x => x.SequenceNumber);
        if (response.Plan.Scenes.Any(scene => !scenesBySequence.ContainsKey(scene.SequenceNumber)))
        {
            throw Conflict("asset_plan_scene_mapping_invalid", "Không thể ánh xạ asset plan vào danh sách cảnh hiện hành.");
        }

        var existingAssets = await dbContext.ProjectAssets
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        var existingByKey = existingAssets.ToDictionary(
            x => string.IsNullOrWhiteSpace(x.AssetKey) ? $"manual-{x.ProjectAssetId:N}" : x.AssetKey,
            StringComparer.OrdinalIgnoreCase);
        var existingByTypeAndName = existingAssets.ToDictionary(
            x => AssetNameKey(x.AssetType, x.Name),
            StringComparer.OrdinalIgnoreCase);
        var materializedByKey = new Dictionary<string, ProjectAsset>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var now = UtcNow();
        var created = 0;
        var updated = 0;
        var preserved = 0;

        foreach (var generated in validatedAssets)
        {
            if (!existingByKey.TryGetValue(generated.AssetKey, out var asset) &&
                existingByTypeAndName.TryGetValue(AssetNameKey(generated.AssetType, generated.Name), out var sameName))
            {
                asset = sameName;
            }

            if (asset is null)
            {
                asset = new ProjectAsset
                {
                    ProjectAssetId = Guid.NewGuid(),
                    ProjectId = projectId,
                    AssetKey = generated.AssetKey,
                    AssetType = generated.AssetType,
                    Name = generated.Name,
                    CanonicalDescription = generated.CanonicalDescription,
                    Status = ProjectAssetStatuses.Draft,
                    SourceKind = ProjectAssetSourceKinds.AiGenerated,
                    SourcePlanVersion = request.ScenePlanVersion,
                    GeneratedByProviderRequestId = request.ProviderRequestId,
                    CurrentVersion = 0,
                    CreatedAtUtc = now,
                    CreatedByUserId = userId,
                    UpdatedAtUtc = now,
                    UpdatedByUserId = userId,
                    RowVersion = new byte[8]
                };
                dbContext.ProjectAssets.Add(asset);
                existingByKey[asset.AssetKey] = asset;
                existingByTypeAndName[AssetNameKey(asset.AssetType, asset.Name)] = asset;
                created++;
            }
            else if (asset.Status == ProjectAssetStatuses.Draft &&
                     asset.SourceKind == ProjectAssetSourceKinds.AiGenerated)
            {
                var unchanged = asset.AssetKey.Equals(generated.AssetKey, StringComparison.OrdinalIgnoreCase) &&
                                asset.AssetType == generated.AssetType &&
                                asset.Name == generated.Name &&
                                asset.CanonicalDescription == generated.CanonicalDescription &&
                                asset.SourcePlanVersion == request.ScenePlanVersion &&
                                asset.GeneratedByProviderRequestId == request.ProviderRequestId;
                if (unchanged)
                {
                    preserved++;
                }
                else
                {
                    var previousKey = asset.AssetKey;
                    var previousNameKey = AssetNameKey(asset.AssetType, asset.Name);
                    asset.AssetKey = generated.AssetKey;
                    asset.AssetType = generated.AssetType;
                    asset.Name = generated.Name;
                    asset.CanonicalDescription = generated.CanonicalDescription;
                    asset.SourcePlanVersion = request.ScenePlanVersion;
                    asset.GeneratedByProviderRequestId = request.ProviderRequestId;
                    asset.UpdatedAtUtc = now;
                    asset.UpdatedByUserId = userId;
                    existingByKey.Remove(previousKey);
                    existingByTypeAndName.Remove(previousNameKey);
                    existingByKey[asset.AssetKey] = asset;
                    existingByTypeAndName[AssetNameKey(asset.AssetType, asset.Name)] = asset;
                    updated++;
                }
            }
            else
            {
                preserved++;
                if (!asset.AssetKey.Equals(generated.AssetKey, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add($"Đã dùng tài sản hiện có “{asset.Name}” thay cho đề xuất AI trùng tên.");
                }
            }
            materializedByKey[generated.AssetKey] = asset;
        }

        var currentSceneIds = scenes.Select(x => x.SceneId).ToArray();
        var currentAssignments = await dbContext.SceneAssetAssignments
            .Where(x => currentSceneIds.Contains(x.SceneId))
            .ToListAsync(cancellationToken);
        dbContext.SceneAssetAssignments.RemoveRange(currentAssignments);

        var newAssignments = new List<SceneAssetAssignment>();
        foreach (var generatedScene in response.Plan.Scenes)
        {
            var assetKeys = (generatedScene.AssetKeys ?? [])
                .Select(NormalizeAssetKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (assetKeys.Length is < 1 or > MaximumAssetsPerScene ||
                assetKeys.Any(key => !materializedByKey.ContainsKey(key)) ||
                assetKeys.Count(key => materializedByKey[key].AssetType == ProjectAssetTypes.Background) != 1)
            {
                throw Conflict("asset_plan_scene_mapping_invalid", "Một cảnh tham chiếu tài sản AI không hợp lệ.");
            }
            var scene = scenesBySequence[generatedScene.SequenceNumber];
            newAssignments.AddRange(assetKeys.Select(key => new SceneAssetAssignment
            {
                SceneId = scene.SceneId,
                ProjectAssetId = materializedByKey[key].ProjectAssetId,
                AssignedByUserId = userId,
                AssignedAtUtc = now
            }));
        }
        dbContext.SceneAssetAssignments.AddRange(newAssignments);
        await SaveWithNameConflictAsync(cancellationToken);

        return new MaterializeProjectAssetPlanResponse(
            projectId,
            request.ProviderRequestId,
            request.ScenePlanVersion,
            created,
            updated,
            preserved,
            newAssignments.Count,
            warnings.Distinct().ToArray());
    }

    public async Task<ProjectAssetSummary> UpdateAsync(
        Guid projectId,
        Guid projectAssetId,
        UpdateProjectAssetRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var access = await RequireAccessAsync(
            projectId,
            request.OrganizationId,
            userId,
            deviceId,
            cancellationToken);
        RequireEdit(access);
        var input = ValidateInput(request.AssetType, request.Name, request.CanonicalDescription);
        await ValidateKlingLongFormVietnameseAsync(
            access.Project!,
            input.Name,
            input.CanonicalDescription,
            cancellationToken);
        var asset = await RequireAssetAsync(projectId, projectAssetId, cancellationToken);
        RequireConcurrency(asset, request.ConcurrencyToken);
        if (asset.Status != ProjectAssetStatuses.Draft)
        {
            throw Conflict("project_asset_locked", "Tài sản đã khóa. Hãy mở khóa trước khi chỉnh sửa.");
        }

        asset.AssetType = input.AssetType;
        asset.Name = input.Name;
        asset.CanonicalDescription = input.CanonicalDescription;
        asset.UpdatedAtUtc = UtcNow();
        asset.UpdatedByUserId = userId;
        await SaveWithNameConflictAsync(cancellationToken);
        return ToSummary(asset, await GetAssignedSceneIdsAsync(asset.ProjectAssetId, cancellationToken));
    }

    public async Task<ProjectAssetSummary> LockAsync(
        Guid projectId,
        Guid projectAssetId,
        ChangeProjectAssetLockRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var access = await RequireAccessAsync(
            projectId,
            request.OrganizationId,
            userId,
            deviceId,
            cancellationToken);
        RequireEdit(access);
        var asset = await RequireAssetAsync(projectId, projectAssetId, cancellationToken);
        RequireConcurrency(asset, request.ConcurrencyToken);
        if (asset.Status == ProjectAssetStatuses.Locked)
        {
            return ToSummary(asset, await GetAssignedSceneIdsAsync(asset.ProjectAssetId, cancellationToken));
        }
        _ = ValidateInput(asset.AssetType, asset.Name, asset.CanonicalDescription);
        await ValidateKlingLongFormVietnameseAsync(
            access.Project!,
            asset.Name,
            asset.CanonicalDescription,
            cancellationToken);

        var now = UtcNow();
        asset.CurrentVersion = checked(asset.CurrentVersion + 1);
        asset.Status = ProjectAssetStatuses.Locked;
        asset.LockedAtUtc = now;
        asset.UpdatedAtUtc = now;
        asset.UpdatedByUserId = userId;
        dbContext.ProjectAssetVersions.Add(new ProjectAssetVersion
        {
            ProjectAssetVersionId = Guid.NewGuid(),
            ProjectAssetId = asset.ProjectAssetId,
            Version = asset.CurrentVersion,
            AssetType = asset.AssetType,
            Name = asset.Name,
            CanonicalDescription = asset.CanonicalDescription,
            LockedAtUtc = now,
            LockedByUserId = userId
        });
        await SaveAssetMutationAsync(cancellationToken);
        return ToSummary(asset, await GetAssignedSceneIdsAsync(asset.ProjectAssetId, cancellationToken));
    }

    public async Task<ProjectAssetSummary> UnlockAsync(
        Guid projectId,
        Guid projectAssetId,
        ChangeProjectAssetLockRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var access = await RequireAccessAsync(
            projectId,
            request.OrganizationId,
            userId,
            deviceId,
            cancellationToken);
        RequireEdit(access);
        var asset = await RequireAssetAsync(projectId, projectAssetId, cancellationToken);
        RequireConcurrency(asset, request.ConcurrencyToken);
        if (asset.Status == ProjectAssetStatuses.Draft)
        {
            return ToSummary(asset, await GetAssignedSceneIdsAsync(asset.ProjectAssetId, cancellationToken));
        }

        asset.Status = ProjectAssetStatuses.Draft;
        asset.LockedAtUtc = null;
        asset.UpdatedAtUtc = UtcNow();
        asset.UpdatedByUserId = userId;
        await SaveAssetMutationAsync(cancellationToken);
        return ToSummary(asset, await GetAssignedSceneIdsAsync(asset.ProjectAssetId, cancellationToken));
    }

    public async Task<ApproveAiProjectAssetsResponse> ApproveAiAssetsAsync(
        Guid projectId,
        ApproveAiProjectAssetsRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var access = await RequireAccessAsync(
            projectId,
            request.OrganizationId,
            userId,
            deviceId,
            cancellationToken);
        RequireEdit(access);

        var requested = (request.Assets ?? [])
            .Where(x => x.ProjectAssetId != Guid.Empty)
            .GroupBy(x => x.ProjectAssetId)
            .Select(x => x.First())
            .ToArray();
        if (requested.Length != (request.Assets?.Count ?? 0))
        {
            throw new ArgumentException("Danh sách duyệt tài sản AI có phần tử trùng hoặc không hợp lệ.");
        }

        var currentPlanVersion = access.Project!.CurrentScenePlanVersion;
        var sceneIds = currentPlanVersion is null
            ? []
            : await dbContext.Scenes
                .Where(x => x.ProjectId == projectId && x.ScenePlanVersion == currentPlanVersion.Value)
                .OrderBy(x => x.SequenceNumber)
                .Select(x => x.SceneId)
                .ToListAsync(cancellationToken);
        var assignments = sceneIds.Count == 0
            ? []
            : await dbContext.SceneAssetAssignments
                .Where(x => sceneIds.Contains(x.SceneId))
                .ToListAsync(cancellationToken);
        var assignedAssetIds = assignments.Select(x => x.ProjectAssetId).Distinct().ToArray();
        var assignedAssets = assignedAssetIds.Length == 0
            ? []
            : await dbContext.ProjectAssets
                .Where(x => x.ProjectId == projectId && assignedAssetIds.Contains(x.ProjectAssetId))
                .ToListAsync(cancellationToken);
        var aiDraftAssets = assignedAssets
            .Where(x => x.SourceKind == ProjectAssetSourceKinds.AiGenerated &&
                        x.Status == ProjectAssetStatuses.Draft)
            .ToArray();
        var requestedIds = requested.Select(x => x.ProjectAssetId).ToHashSet();
        if (!requestedIds.SetEquals(aiDraftAssets.Select(x => x.ProjectAssetId)))
        {
            throw Conflict(
                "project_asset_approval_stale",
                "Danh sách tài sản AI đã thay đổi. Hãy tải lại dự án rồi duyệt lại.");
        }

        foreach (var asset in aiDraftAssets)
        {
            var input = requested.Single(x => x.ProjectAssetId == asset.ProjectAssetId);
            RequireConcurrency(asset, input.ConcurrencyToken);
            _ = ValidateInput(asset.AssetType, asset.Name, asset.CanonicalDescription);
            await ValidateKlingLongFormVietnameseAsync(
                access.Project,
                asset.Name,
                asset.CanonicalDescription,
                cancellationToken);
        }

        var preflights = new List<SceneAssignmentPreflight>(sceneIds.Count);
        foreach (var sceneId in sceneIds)
        {
            var ids = assignments
                .Where(x => x.SceneId == sceneId)
                .Select(x => x.ProjectAssetId)
                .ToHashSet();
            var sceneAssets = assignedAssets.Where(x => ids.Contains(x.ProjectAssetId)).ToArray();
            var preflight = await EvaluateSceneAssignmentAsync(
                access.Project,
                sceneId,
                sceneAssets,
                cancellationToken);
            if (!preflight.IsValid)
            {
                throw SceneAssignmentInvalid(preflight);
            }
            preflights.Add(preflight);
        }

        var now = UtcNow();
        foreach (var asset in aiDraftAssets)
        {
            asset.CurrentVersion = checked(asset.CurrentVersion + 1);
            asset.Status = ProjectAssetStatuses.Locked;
            asset.LockedAtUtc = now;
            asset.UpdatedAtUtc = now;
            asset.UpdatedByUserId = userId;
            dbContext.ProjectAssetVersions.Add(new ProjectAssetVersion
            {
                ProjectAssetVersionId = Guid.NewGuid(),
                ProjectAssetId = asset.ProjectAssetId,
                Version = asset.CurrentVersion,
                AssetType = asset.AssetType,
                Name = asset.Name,
                CanonicalDescription = asset.CanonicalDescription,
                LockedAtUtc = now,
                LockedByUserId = userId
            });
        }
        if (aiDraftAssets.Length > 0)
        {
            await SaveAssetMutationAsync(cancellationToken);
        }

        var lockedIds = aiDraftAssets.Select(x => x.ProjectAssetId).ToHashSet();
        var readyScenes = sceneIds.Count(sceneId =>
        {
            var sceneAssetIds = assignments
                .Where(x => x.SceneId == sceneId)
                .Select(x => x.ProjectAssetId)
                .ToArray();
            return preflights.Single(x => x.SceneId == sceneId).IsValid &&
                   sceneAssetIds.All(assetId =>
                       lockedIds.Contains(assetId) ||
                       assignedAssets.Single(x => x.ProjectAssetId == assetId).Status == ProjectAssetStatuses.Locked);
        });
        return new ApproveAiProjectAssetsResponse(aiDraftAssets.Length, readyScenes, sceneIds.Count);
    }

    public async Task DeleteAsync(
        Guid projectId,
        Guid projectAssetId,
        DeleteProjectAssetRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var access = await RequireAccessAsync(
            projectId,
            request.OrganizationId,
            userId,
            deviceId,
            cancellationToken);
        RequireEdit(access);
        var asset = await RequireAssetAsync(projectId, projectAssetId, cancellationToken);
        RequireConcurrency(asset, request.ConcurrencyToken);
        if (asset.Status != ProjectAssetStatuses.Draft || asset.CurrentVersion > 0)
        {
            throw Conflict(
                "project_asset_delete_denied",
                "Chỉ có thể xóa tài sản nháp chưa từng được khóa.");
        }
        if (await dbContext.SceneAssetAssignments.AnyAsync(
            x => x.ProjectAssetId == projectAssetId,
            cancellationToken))
        {
            throw Conflict(
                "project_asset_in_use",
                "Hãy bỏ tài sản khỏi các cảnh trước khi xóa.");
        }

        dbContext.ProjectAssets.Remove(asset);
        await SaveAssetMutationAsync(cancellationToken);
    }

    public async Task<SceneAssetAssignmentSummary> UpdateSceneAssignmentsAsync(
        Guid projectId,
        Guid sceneId,
        UpdateSceneAssetAssignmentsRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var access = await RequireAccessAsync(
            projectId,
            request.OrganizationId,
            userId,
            deviceId,
            cancellationToken);
        RequireEdit(access);
        var assetIds = (request.ProjectAssetIds ?? [])
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();
        if (assetIds.Length > MaximumAssetsPerScene)
        {
            throw new ArgumentException($"Mỗi cảnh chỉ được gắn tối đa {MaximumAssetsPerScene} tài sản text.");
        }
        var currentPlanVersion = access.Project!.CurrentScenePlanVersion;
        var sceneExists = currentPlanVersion is not null && await dbContext.Scenes.AnyAsync(
            x => x.SceneId == sceneId &&
                 x.ProjectId == projectId &&
                 x.ScenePlanVersion == currentPlanVersion.Value,
            cancellationToken);
        if (!sceneExists)
        {
            throw NotFound("scene_not_found", "Không tìm thấy cảnh trong kế hoạch hiện hành.");
        }
        var assets = assetIds.Length == 0
            ? []
            : await dbContext.ProjectAssets
                .Where(x => x.ProjectId == projectId && assetIds.Contains(x.ProjectAssetId))
                .ToListAsync(cancellationToken);
        if (assets.Count != assetIds.Length)
        {
            throw new ArgumentException("Danh sách có tài sản không thuộc dự án hiện tại.");
        }

        var preflight = await EvaluateSceneAssignmentAsync(
            access.Project,
            sceneId,
            assets,
            cancellationToken);
        if (!preflight.IsValid)
        {
            throw SceneAssignmentInvalid(preflight);
        }

        var currentAssignments = await dbContext.SceneAssetAssignments
            .Where(x => x.SceneId == sceneId)
            .ToListAsync(cancellationToken);
        dbContext.SceneAssetAssignments.RemoveRange(currentAssignments);
        var now = UtcNow();
        dbContext.SceneAssetAssignments.AddRange(assetIds.Select(assetId => new SceneAssetAssignment
        {
            SceneId = sceneId,
            ProjectAssetId = assetId,
            AssignedByUserId = userId,
            AssignedAtUtc = now
        }));
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToAssignmentSummary(sceneId, assets, preflight);
    }

    public async Task<ConfirmSceneProjectAssetsResponse> ConfirmSceneAssetsAsync(
        Guid projectId,
        Guid sceneId,
        ConfirmSceneProjectAssetsRequest request,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var access = await RequireAccessAsync(
            projectId,
            request.OrganizationId,
            userId,
            deviceId,
            cancellationToken);
        RequireEdit(access);

        var requested = (request.Assets ?? [])
            .Where(x => x.ProjectAssetId != Guid.Empty)
            .GroupBy(x => x.ProjectAssetId)
            .Select(x => x.First())
            .ToArray();
        if (requested.Length != (request.Assets?.Count ?? 0))
        {
            throw new ArgumentException("Danh sách tài sản cần xác nhận có phần tử trùng hoặc không hợp lệ.");
        }

        var currentPlanVersion = access.Project!.CurrentScenePlanVersion;
        var sceneExists = currentPlanVersion is not null && await dbContext.Scenes.AnyAsync(
            x => x.SceneId == sceneId &&
                 x.ProjectId == projectId &&
                 x.ScenePlanVersion == currentPlanVersion.Value,
            cancellationToken);
        if (!sceneExists)
        {
            throw NotFound("scene_not_found", "Không tìm thấy cảnh trong kế hoạch hiện hành.");
        }

        var assets = await dbContext.SceneAssetAssignments
            .Where(x => x.SceneId == sceneId && x.ProjectAsset.ProjectId == projectId)
            .Select(x => x.ProjectAsset)
            .Distinct()
            .ToListAsync(cancellationToken);
        var requestedIds = requested.Select(x => x.ProjectAssetId).ToHashSet();
        if (!requestedIds.SetEquals(assets.Select(x => x.ProjectAssetId)))
        {
            throw Conflict(
                "scene_asset_confirmation_stale",
                "Lựa chọn tài sản của cảnh vừa thay đổi. Hệ thống sẽ tải lại để bạn xác nhận lại.");
        }

        foreach (var asset in assets)
        {
            var input = requested.Single(x => x.ProjectAssetId == asset.ProjectAssetId);
            RequireConcurrency(asset, input.ConcurrencyToken);
        }

        var preflight = await EvaluateSceneAssignmentAsync(
            access.Project,
            sceneId,
            assets,
            cancellationToken);
        if (!preflight.IsValid)
        {
            throw SceneAssignmentInvalid(preflight);
        }

        var drafts = assets.Where(x => x.Status == ProjectAssetStatuses.Draft).ToArray();
        foreach (var asset in drafts)
        {
            _ = ValidateInput(asset.AssetType, asset.Name, asset.CanonicalDescription);
            await ValidateKlingLongFormVietnameseAsync(
                access.Project,
                asset.Name,
                asset.CanonicalDescription,
                cancellationToken);
        }

        var now = UtcNow();
        foreach (var asset in drafts)
        {
            asset.CurrentVersion = checked(asset.CurrentVersion + 1);
            asset.Status = ProjectAssetStatuses.Locked;
            asset.LockedAtUtc = now;
            asset.UpdatedAtUtc = now;
            asset.UpdatedByUserId = userId;
            dbContext.ProjectAssetVersions.Add(new ProjectAssetVersion
            {
                ProjectAssetVersionId = Guid.NewGuid(),
                ProjectAssetId = asset.ProjectAssetId,
                Version = asset.CurrentVersion,
                AssetType = asset.AssetType,
                Name = asset.Name,
                CanonicalDescription = asset.CanonicalDescription,
                LockedAtUtc = now,
                LockedByUserId = userId
            });
        }
        if (drafts.Length > 0)
        {
            await SaveAssetMutationAsync(cancellationToken);
        }

        return new ConfirmSceneProjectAssetsResponse(
            sceneId,
            drafts.Length,
            ToAssignmentSummary(sceneId, assets, preflight));
    }

    private async Task<SceneAssignmentPreflight> EvaluateSceneAssignmentAsync(
        Project project,
        Guid sceneId,
        IReadOnlyList<ProjectAsset> assets,
        CancellationToken cancellationToken)
    {
        var blockers = new List<SceneAssignmentBlocker>();
        var backgroundCount = assets.Count(x => x.AssetType == ProjectAssetTypes.Background);
        if (assets.Count > 0 && backgroundCount != 1)
        {
            blockers.Add(new SceneAssignmentBlocker(
                "scene_asset_background_invalid",
                backgroundCount == 0
                    ? "Cảnh có tài sản phải chọn đúng một bối cảnh."
                    : "Mỗi cảnh chỉ được chọn một bối cảnh."));
        }

        var promptCharacters = 0;
        var requiredPromptCharacters = 0;
        if (assets.Count > 0 &&
            string.Equals(project.VideoProviderCode, ProviderCodes.Kling, StringComparison.OrdinalIgnoreCase))
        {
            var scene = await dbContext.Scenes
                .AsNoTracking()
                .Where(x => x.SceneId == sceneId && x.ProjectId == project.ProjectId)
                .Select(x => new
                {
                    x.ScriptId,
                    x.CharacterIdsJson,
                    x.GenerationDurationMs,
                    x.Narration,
                    x.Dialogue,
                    x.RequiredCapabilitiesJson,
                    Prompt = x.ScenePrompts
                        .Where(prompt => prompt.Status == "Approved" || prompt.Status == "Ready")
                        .OrderByDescending(prompt => prompt.Version)
                        .Select(prompt => new { prompt.FinalPrompt, prompt.NegativePrompt })
                        .FirstOrDefault()
                })
                .SingleOrDefaultAsync(cancellationToken);
            if (scene?.Prompt is not null &&
                scene.GenerationDurationMs >= 3000 &&
                scene.GenerationDurationMs <= 15000 &&
                scene.GenerationDurationMs % 1000 == 0)
            {
                GenerationService.CharacterPromptSnapshot? character = null;
                var characterId = ParseFirstCharacterId(scene.CharacterIdsJson);
                if (characterId is not null)
                {
                    character = await dbContext.Characters
                        .AsNoTracking()
                        .Where(x => x.ProjectId == project.ProjectId && x.CharacterId == characterId.Value)
                        .Select(x => new GenerationService.CharacterPromptSnapshot(
                            x.CharacterId,
                            x.Version,
                            x.Name,
                            x.Role,
                            x.VisualIdentity,
                            x.ProfileJson,
                            x.WardrobeJson,
                            x.ForbiddenChangesJson,
                            x.Status,
                            null))
                        .SingleOrDefaultAsync(cancellationToken);
                }

                var structureType = await dbContext.Scripts
                    .AsNoTracking()
                    .Where(x => x.ScriptId == scene.ScriptId && x.ProjectId == project.ProjectId)
                    .Select(x => x.StructureType)
                    .SingleOrDefaultAsync(cancellationToken);
                var languageCode = KlingLongFormLanguagePolicy.Resolve(
                    project.VideoProviderCode,
                    project.LanguageCode,
                    structureType);
                var useVietnameseTemplate = KlingLongFormLanguagePolicy.RequiresVietnamese(
                    project.VideoProviderCode,
                    structureType);
                var snapshots = assets
                    .OrderBy(x => ProjectAssetTypeOrder(x.AssetType))
                    .ThenBy(x => x.Name)
                    .Select(x => new GenerationService.ProjectAssetPromptSnapshot(
                        x.ProjectAssetId,
                        Guid.Empty,
                        x.CurrentVersion,
                        x.AssetType,
                        x.Name,
                        x.CanonicalDescription))
                    .ToArray();
                try
                {
                    var speech = GenerationService.CreateKlingSpeechPrompt(
                        scene.Dialogue,
                        scene.Narration,
                        scene.RequiredCapabilitiesJson,
                        languageCode,
                        character?.Name);
                    var analysis = GenerationService.AnalyzeKlingPrompt(
                        scene.Prompt.FinalPrompt,
                        scene.Prompt.NegativePrompt,
                        character,
                        speech,
                        checked((int)(scene.GenerationDurationMs / 1000)),
                        project.AspectRatio,
                        snapshots,
                        useVietnameseTemplate: useVietnameseTemplate);
                    promptCharacters = analysis.FinalCharacters;
                    requiredPromptCharacters = analysis.RequiredCharacters;
                    if (!analysis.FitsRequiredContent)
                    {
                        blockers.Add(new SceneAssignmentBlocker(
                            "kling_prompt_too_long",
                            "Nội dung bắt buộc của cảnh vượt giới hạn Kling. Hãy bỏ bớt đạo cụ/item hoặc rút gọn mô tả tài sản."));
                    }
                }
                catch (KlingPromptValidationException exception)
                {
                    promptCharacters = 0;
                    requiredPromptCharacters = 0;
                    blockers.Add(new SceneAssignmentBlocker(exception.Code, exception.Message));
                }
            }
        }

        return new SceneAssignmentPreflight(
            sceneId,
            blockers.Count == 0,
            backgroundCount,
            promptCharacters,
            string.Equals(project.VideoProviderCode, ProviderCodes.Kling, StringComparison.OrdinalIgnoreCase)
                ? KlingNativeAudioPromptComposer.MaximumPromptLength
                : 0,
            requiredPromptCharacters,
            blockers);
    }

    private static SceneAssetAssignmentSummary ToAssignmentSummary(
        Guid sceneId,
        IReadOnlyList<ProjectAsset> assets,
        SceneAssignmentPreflight preflight) =>
        new(
            sceneId,
            assets.Select(x => x.ProjectAssetId).Distinct().ToArray(),
            assets.Any(x => x.Status != ProjectAssetStatuses.Locked),
            preflight.IsValid,
            preflight.BackgroundCount,
            preflight.PromptCharacters,
            preflight.PromptLimit,
            preflight.Blockers.Select(x => x.Message).ToArray(),
            preflight.RequiredPromptCharacters);

    private static AccountApiException SceneAssignmentInvalid(SceneAssignmentPreflight preflight)
    {
        var blocker = preflight.Blockers.First();
        return new AccountApiException(
            StatusCodes.Status422UnprocessableEntity,
            blocker.Code,
            blocker.Message,
            new Dictionary<string, string[]>
            {
                ["blockers"] = preflight.Blockers.Select(x => x.Message).ToArray()
            });
    }

    private static Guid? ParseFirstCharacterId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            var values = JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? [];
            var result = values
                .Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty)
                .FirstOrDefault(id => id != Guid.Empty);
            return result == Guid.Empty ? null : result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<GenerationAccessContext> RequireAccessAsync(
        Guid projectId,
        Guid? organizationId,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Project ID không hợp lệ.");
        }
        var access = await accessService.RequireProjectAccessAsync(
            userId,
            deviceId,
            organizationId,
            projectId,
            cancellationToken);
        if (access.Project?.ProjectId != projectId)
        {
            throw NotFound("project_not_found", "Không tìm thấy dự án.");
        }
        return access;
    }

    private static void RequireEdit(GenerationAccessContext access)
    {
        if (!OrganizationMemberRoles.CanGenerate(access.OrganizationRole))
        {
            throw new AccountApiException(
                StatusCodes.Status403Forbidden,
                "project_asset_edit_denied",
                "Vai trò Viewer chỉ được xem thư viện tài sản.");
        }
    }

    private async Task<ProjectAsset> RequireAssetAsync(
        Guid projectId,
        Guid projectAssetId,
        CancellationToken cancellationToken)
    {
        if (projectAssetId == Guid.Empty)
        {
            throw new ArgumentException("Tài sản được chọn không hợp lệ.");
        }
        return await dbContext.ProjectAssets.SingleOrDefaultAsync(
            x => x.ProjectAssetId == projectAssetId && x.ProjectId == projectId,
            cancellationToken)
            ?? throw NotFound("project_asset_not_found", "Không tìm thấy tài sản trong dự án.");
    }

    private async Task<IReadOnlyList<Guid>> GetAssignedSceneIdsAsync(
        Guid projectAssetId,
        CancellationToken cancellationToken) =>
        await dbContext.SceneAssetAssignments
            .AsNoTracking()
            .Where(x => x.ProjectAssetId == projectAssetId)
            .Select(x => x.SceneId)
            .Distinct()
            .ToListAsync(cancellationToken);

    private static ProjectAssetSummary ToSummary(ProjectAsset asset, IReadOnlyList<Guid> sceneIds) =>
        new(
            asset.ProjectAssetId,
            asset.AssetType,
            asset.Name,
            asset.CanonicalDescription,
            asset.Status,
            asset.CurrentVersion,
            asset.LockedAtUtc,
            asset.UpdatedAtUtc,
            Convert.ToBase64String(asset.RowVersion),
            sceneIds,
            string.IsNullOrWhiteSpace(asset.AssetKey) ? $"manual-{asset.ProjectAssetId:N}" : asset.AssetKey,
            string.IsNullOrWhiteSpace(asset.SourceKind) ? ProjectAssetSourceKinds.Manual : asset.SourceKind,
            asset.SourcePlanVersion,
            asset.GeneratedByProviderRequestId);

    private static IReadOnlyList<ValidatedGeneratedAsset> ValidateGeneratedAssets(
        IReadOnlyList<GeneratedProjectAsset> assets)
    {
        if (assets.Count is < 1 or > 60)
        {
            throw Conflict("asset_plan_invalid", "Asset plan phải có từ 1 đến 60 tài sản.");
        }
        var result = assets.Select(asset =>
        {
            var key = NormalizeAssetKey(asset.AssetKey);
            var input = ValidateInput(asset.AssetType, asset.Name, asset.CanonicalDescription);
            return new ValidatedGeneratedAsset(key, input.AssetType, input.Name, input.CanonicalDescription);
        }).ToArray();
        if (result.Select(x => x.AssetKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length ||
            result.Select(x => AssetNameKey(x.AssetType, x.Name)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length)
        {
            throw Conflict("asset_plan_invalid", "Asset plan có asset_key hoặc tên tài sản bị trùng.");
        }
        return result;
    }

    private static string NormalizeAssetKey(string? value)
    {
        var key = value?.Trim().ToLowerInvariant() ?? string.Empty;
        if (key.Length is < 1 or > 80 ||
            key.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-')))
        {
            throw Conflict("asset_plan_invalid", "Asset plan có asset_key không hợp lệ.");
        }
        return key;
    }

    private static string AssetNameKey(string assetType, string name) => $"{assetType}\n{name}";

    private async Task ValidateKlingLongFormVietnameseAsync(
        Project project,
        string name,
        string canonicalDescription,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(project.VideoProviderCode, ProviderCodes.Kling, StringComparison.OrdinalIgnoreCase) ||
            project.CurrentScriptVersion is null)
        {
            return;
        }

        var structureType = await dbContext.Scripts
            .AsNoTracking()
            .Where(x => x.ProjectId == project.ProjectId && x.Version == project.CurrentScriptVersion.Value)
            .Select(x => x.StructureType)
            .SingleOrDefaultAsync(cancellationToken);
        if (!KlingLongFormLanguagePolicy.RequiresVietnamese(project.VideoProviderCode, structureType))
        {
            return;
        }

        var violations = KlingVietnameseContentValidator.FindViolations([
            ("project_asset.name", name, true),
            ("project_asset.canonical_description", canonicalDescription, true)
        ]);
        if (violations.Count > 0)
        {
            throw new AccountApiException(
                StatusCodes.Status422UnprocessableEntity,
                "kling_prompt_language_invalid",
                "Tên và mô tả tài sản của video dài dùng Kling phải bằng tiếng Việt.",
                new Dictionary<string, string[]>
                {
                    ["fields"] = violations.ToArray()
                });
        }
    }

    private static ValidatedInput ValidateInput(string assetType, string name, string canonicalDescription)
    {
        var normalizedType = NormalizeAssetType(assetType);
        var normalizedName = name?.Trim() ?? string.Empty;
        var normalizedDescription = canonicalDescription?.Trim() ?? string.Empty;
        if (normalizedName.Length is < 1 or > 160)
        {
            throw new ArgumentException("Tên tài sản phải có từ 1 đến 160 ký tự.");
        }
        if (normalizedDescription.Length is < 1 or > 2000)
        {
            throw new ArgumentException("Mô tả chuẩn phải có từ 1 đến 2.000 ký tự.");
        }
        return new ValidatedInput(normalizedType, normalizedName, normalizedDescription);
    }

    private static string NormalizeAssetType(string? assetType)
    {
        var normalized = assetType?.Trim();
        if (string.Equals(normalized, ProjectAssetTypes.Background, StringComparison.OrdinalIgnoreCase))
        {
            return ProjectAssetTypes.Background;
        }
        if (string.Equals(normalized, ProjectAssetTypes.Prop, StringComparison.OrdinalIgnoreCase))
        {
            return ProjectAssetTypes.Prop;
        }
        if (string.Equals(normalized, ProjectAssetTypes.Item, StringComparison.OrdinalIgnoreCase))
        {
            return ProjectAssetTypes.Item;
        }
        throw new ArgumentException("Loại tài sản phải là Background, Prop hoặc Item.");
    }

    private static void RequireConcurrency(ProjectAsset asset, string concurrencyToken)
    {
        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(concurrencyToken ?? string.Empty);
        }
        catch (FormatException)
        {
            throw new ArgumentException("Phiên bản tài sản không hợp lệ.");
        }
        if (expected.Length != 8 ||
            asset.RowVersion.Length != 8 ||
            !CryptographicOperations.FixedTimeEquals(expected, asset.RowVersion))
        {
            throw Conflict(
                "project_asset_changed",
                "Tài sản vừa được cập nhật ở nơi khác. Hãy làm mới và thử lại.");
        }
    }

    private async Task SaveWithNameConflictAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict(
                "project_asset_changed",
                "Tài sản vừa được cập nhật ở nơi khác. Hãy làm mới và thử lại.");
        }
        catch (DbUpdateException)
        {
            throw new AccountApiException(
                StatusCodes.Status409Conflict,
                "project_asset_name_conflict",
                "Dự án đã có tài sản cùng loại và cùng tên.");
        }
    }

    private async Task SaveAssetMutationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw Conflict(
                "project_asset_changed",
                "Tài sản vừa được cập nhật ở nơi khác. Hãy làm mới và thử lại.");
        }
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static AccountApiException NotFound(string code, string message) =>
        new(StatusCodes.Status404NotFound, code, message);

    private static AccountApiException Conflict(string code, string message) =>
        new(StatusCodes.Status409Conflict, code, message);

    private static int ProjectAssetTypeOrder(string assetType) => assetType switch
    {
        ProjectAssetTypes.Background => 0,
        ProjectAssetTypes.Prop => 1,
        ProjectAssetTypes.Item => 2,
        _ => 3
    };

    private sealed record ValidatedInput(string AssetType, string Name, string CanonicalDescription);

    private sealed record ValidatedGeneratedAsset(
        string AssetKey,
        string AssetType,
        string Name,
        string CanonicalDescription);

    private sealed record AssignmentRow(Guid SceneId, Guid ProjectAssetId, string Status);

    private sealed record SceneAssignmentBlocker(string Code, string Message);

    private sealed record SceneAssignmentPreflight(
        Guid SceneId,
        bool IsValid,
        int BackgroundCount,
        int PromptCharacters,
        int PromptLimit,
        int RequiredPromptCharacters,
        IReadOnlyList<SceneAssignmentBlocker> Blockers);
}
