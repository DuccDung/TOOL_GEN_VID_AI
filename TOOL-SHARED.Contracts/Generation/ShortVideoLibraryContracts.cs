namespace TOOL_SHARED.Contracts.Generation;

public sealed record ShortVideoAssetRef(Guid AssetId, int Version);
public sealed record ShortVideoLibraryAsset(Guid AssetId, string Kind, string Name, int Version,
    ShortVideoImageInfo Image, string ThumbnailUrl, string PreviewUrl, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
public sealed record ShortVideoLibraryUpload(Guid UploadId, string SuggestedName, ShortVideoImageInfo Image, string PreviewUrl);
public sealed record ShortVideoDraft(string Content, string AspectRatio, int DurationSeconds, bool AudioEnabled,
    ShortVideoAssetRef? Character, ShortVideoAssetRef? Outfit, string Background = "", string Motion = "", int ServerRevision = 0);
public sealed record ShortVideoDraftState(int Revision, ShortVideoDraft? Draft, Guid? CreatedProjectId = null);
public sealed record ShortVideoLibraryState(IReadOnlyList<ShortVideoLibraryAsset> Items, ShortVideoDraftState Draft,
    IReadOnlyList<ShortVideoLibraryAsset>? Selections = null);
public sealed record ShortVideoCreatedNotice(Guid ProjectId, Guid OrganizationId, ShortVideoQuote? Quote, string? Message);

// These contracts describe the desktop library; they contain neither file paths nor image bytes.
public sealed record ShortVideoLibraryAction(Guid OrganizationId, Guid? ProjectId = null,
    string? Kind = null, Guid? AssetId = null, int Version = 0, string? Name = null,
    Guid? UploadId = null, int Revision = 0, ShortVideoDraft? Draft = null);
