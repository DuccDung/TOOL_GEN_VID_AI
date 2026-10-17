using System.Globalization;
using System.Text.RegularExpressions;

namespace TOOL_SHARED.Contracts.Updates;

/// <summary>Retains the release service's build-first ordering across maintenance peers.</summary>
public static partial class DesktopReleaseVersionPolicy
{
    public static bool IsSupportedStableVersion(string? value) => value is { Length: > 0 and <= 32 } && StableVersion().IsMatch(value)
        && value.Split('.').All(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _));

    public static bool IsReleaseNewerVersionFirst(string releaseVersion, int releaseBuild, string currentVersion, int currentBuild)
    {
        if (!IsSupportedStableVersion(releaseVersion) || !IsSupportedStableVersion(currentVersion)) return false;
        var comparison = CompareVersions(releaseVersion, currentVersion);
        return comparison > 0 || comparison == 0 && releaseBuild > currentBuild;
    }

    [GeneratedRegex(@"\A(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex StableVersion();

    public static bool IsReleaseNewer(string releaseVersion, int releaseBuild, string currentVersion, int currentBuild) =>
        releaseBuild != currentBuild ? releaseBuild > currentBuild : CompareVersions(releaseVersion, currentVersion) > 0;

    public static int CompareVersions(string? left, string? right)
    {
        var a = Parse(left); var b = Parse(right);
        for (var index = 0; index < Math.Max(a.Length, b.Length); index++)
        {
            var comparison = (index < a.Length ? a[index] : 0).CompareTo(index < b.Length ? b[index] : 0);
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    private static int[] Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [0];
        var match = VersionPrefix().Match(value.Trim());
        return !match.Success ? [0] : match.Value.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0).ToArray();
    }

    [GeneratedRegex(@"\d+(?:\.\d+)*", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPrefix();
}
