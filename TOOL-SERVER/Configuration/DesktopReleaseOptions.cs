using System.ComponentModel.DataAnnotations;

namespace TOOL_SERVER.Configuration;

public sealed class DesktopReleaseOptions
{
    public const string SectionName = "DesktopReleases";

    [Required]
    public string StorageRoot { get; init; } = "App_Releases";

    [Range(1, long.MaxValue)]
    public long MaximumArtifactBytes { get; init; } = 2L * 1024 * 1024 * 1024;
}
