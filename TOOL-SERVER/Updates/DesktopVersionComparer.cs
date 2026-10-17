using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SERVER.Updates;

public static partial class DesktopVersionComparer
{
    public static bool IsReleaseNewer(string releaseVersion, int releaseBuild, string currentVersion, int currentBuild) =>
        DesktopReleaseVersionPolicy.IsReleaseNewer(releaseVersion, releaseBuild, currentVersion, currentBuild);

    public static int CompareVersions(string? left, string? right) =>
        DesktopReleaseVersionPolicy.CompareVersions(left, right);
}
