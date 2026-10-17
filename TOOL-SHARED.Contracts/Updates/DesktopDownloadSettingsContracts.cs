namespace TOOL_SHARED.Contracts.Updates;

/// <summary>Public website download link only; this does not register or activate a bootstrapper.</summary>
public sealed record DesktopDownloadSettingsResponse(
    string? SetupDownloadUrl,
    string? Revision,
    DateTime? UpdatedAtUtc);

public sealed record DesktopDownloadSettingsRequest(
    string SetupDownloadUrl,
    string? Revision = null);
