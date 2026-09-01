namespace TOOL_SHARED.Contracts.Vietsub;

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
