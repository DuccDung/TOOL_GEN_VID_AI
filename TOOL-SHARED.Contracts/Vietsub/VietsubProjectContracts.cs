namespace TOOL_SHARED.Contracts.Vietsub;

/// <summary>Guid.Empty explicitly selects the authenticated user's personal Local workspace.
/// The server stores a NULL organization and always enforces project ownership and device license.
/// Cloud operations require a real organization.</summary>
public static class VietsubWorkspace
{
    public static readonly Guid Personal = Guid.Empty;
}

public sealed record CreateVietsubProjectRequest(
    Guid ProjectId,
    Guid OrganizationId,
    string Name,
    string SourceLanguageCode = "auto",
    string TargetLanguageCode = "vi");

public sealed record RenameVietsubProjectRequest(
    Guid OrganizationId,
    string Name);

public sealed record VietsubProjectResponse(
    Guid ProjectId,
    Guid OrganizationId,
    string CreatedByUserId,
    string Name,
    string Status,
    string SourceLanguageCode,
    string TargetLanguageCode,
    bool IsArchived,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? ArchivedAtUtc);
