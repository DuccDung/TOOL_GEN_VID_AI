namespace TOOL_SERVER.Domain.Updates;

public sealed class AppRelease
{
    public Guid AppReleaseId { get; set; }

    public string Version { get; set; } = string.Empty;

    public int BuildNumber { get; set; }

    public string Channel { get; set; } = DesktopReleaseChannels.Stable;

    public string Platform { get; set; } = DesktopReleasePlatforms.WindowsX64;

    public string? MinimumSupportedDesktopVersion { get; set; }

    // Kept for backwards compatibility with the original schema. New internal
    // packages are represented by AppReleaseArtifact records.
    public string? DownloadUrl { get; set; }

    public string? Sha256 { get; set; }

    public string? ReleaseNotes { get; set; }

    public bool IsMandatory { get; set; }

    public bool IsActive { get; set; }

    public DateTime PublishedAtUtc { get; set; }

    public ICollection<AppReleaseArtifact> Artifacts { get; set; } = [];
}

public sealed class AppReleaseArtifact
{
    public Guid AppReleaseArtifactId { get; set; }

    public Guid AppReleaseId { get; set; }

    public string Kind { get; set; } = DesktopArtifactKinds.DesktopPackage;

    public string FileName { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public AppRelease Release { get; set; } = null!;
}

public static class DesktopReleaseChannels
{
    public const string Stable = "Stable";
    public const string Beta = "Beta";
    public const string Development = "Development";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [Stable, Beta, Development],
        StringComparer.OrdinalIgnoreCase);
}

public static class DesktopReleasePlatforms
{
    public const string WindowsX64 = "win-x64";
}

public static class DesktopArtifactKinds
{
    public const string DesktopPackage = "DesktopPackage";
    public const string Setup = "Setup";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [DesktopPackage, Setup],
        StringComparer.OrdinalIgnoreCase);
}
