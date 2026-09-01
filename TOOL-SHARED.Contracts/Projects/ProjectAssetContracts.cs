namespace TOOL_SHARED.Contracts.Projects;

public static class ProjectAssetTypes
{
    public const string Background = "Background";
    public const string Prop = "Prop";
    public const string Item = "Item";

    public static bool IsSupported(string? value) => value is Background or Prop or Item;
}

public static class ProjectAssetStatuses
{
    public const string Draft = "Draft";
    public const string Locked = "Locked";
}

public static class ProjectAssetSourceKinds
{
    public const string Manual = "Manual";
    public const string AiGenerated = "AiGenerated";
}

public sealed record ProjectAssetSummary(
    Guid ProjectAssetId,
    string AssetType,
    string Name,
    string CanonicalDescription,
    string Status,
    int CurrentVersion,
    DateTime? LockedAtUtc,
    DateTime UpdatedAtUtc,
    string ConcurrencyToken,
    IReadOnlyList<Guid> SceneIds,
    string AssetKey = "",
    string SourceKind = ProjectAssetSourceKinds.Manual,
    int? SourcePlanVersion = null,
    Guid? GeneratedByProviderRequestId = null);

public sealed record SceneAssetAssignmentSummary(
    Guid SceneId,
    IReadOnlyList<Guid> ProjectAssetIds,
    bool HasUnlockedAssets,
    bool IsValid = true,
    int BackgroundCount = 0,
    int PromptCharacters = 0,
    int PromptLimit = 0,
    IReadOnlyList<string>? Blockers = null,
    int RequiredPromptCharacters = 0);

public sealed record ProjectAssetLibraryResponse(
    Guid ProjectId,
    bool CanEdit,
    IReadOnlyList<ProjectAssetSummary> Assets,
    IReadOnlyList<SceneAssetAssignmentSummary> SceneAssignments);

public sealed record CreateProjectAssetRequest(
    string AssetType,
    string Name,
    string CanonicalDescription,
    Guid? OrganizationId = null);

public sealed record UpdateProjectAssetRequest(
    string AssetType,
    string Name,
    string CanonicalDescription,
    string ConcurrencyToken,
    Guid? OrganizationId = null);

public sealed record ChangeProjectAssetLockRequest(
    string ConcurrencyToken,
    Guid? OrganizationId = null);

public sealed record ApproveProjectAssetInput(
    Guid ProjectAssetId,
    string ConcurrencyToken);

public sealed record ApproveAiProjectAssetsRequest(
    IReadOnlyList<ApproveProjectAssetInput> Assets,
    Guid? OrganizationId = null);

public sealed record ApproveAiProjectAssetsResponse(
    int LockedAssets,
    int ReadyScenes,
    int TotalScenes);

public sealed record ConfirmSceneProjectAssetsRequest(
    IReadOnlyList<ApproveProjectAssetInput> Assets,
    Guid? OrganizationId = null);

public sealed record ConfirmSceneProjectAssetsResponse(
    Guid SceneId,
    int LockedAssets,
    SceneAssetAssignmentSummary Assignment);

public sealed record DeleteProjectAssetRequest(
    string ConcurrencyToken,
    Guid? OrganizationId = null);

public sealed record UpdateSceneAssetAssignmentsRequest(
    IReadOnlyList<Guid> ProjectAssetIds,
    Guid? OrganizationId = null);

public sealed record MaterializeProjectAssetPlanRequest(
    Guid ProviderRequestId,
    int ScenePlanVersion,
    Guid? OrganizationId = null);

public sealed record MaterializeProjectAssetPlanResponse(
    Guid ProjectId,
    Guid ProviderRequestId,
    int ScenePlanVersion,
    int CreatedAssets,
    int UpdatedDraftAssets,
    int PreservedAssets,
    int SceneAssignments,
    IReadOnlyList<string> Warnings);
