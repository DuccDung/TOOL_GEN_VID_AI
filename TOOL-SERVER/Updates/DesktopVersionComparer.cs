using System.Globalization;
using System.Text.RegularExpressions;

namespace TOOL_SERVER.Updates;

public static partial class DesktopVersionComparer
{
    public static bool IsReleaseNewer(string releaseVersion, int releaseBuild, string currentVersion, int currentBuild)
    {
        if (releaseBuild != currentBuild)
        {
            return releaseBuild > currentBuild;
        }

        return CompareVersions(releaseVersion, currentVersion) > 0;
    }

    public static int CompareVersions(string? left, string? right)
    {
        var leftParts = Parse(left);
        var rightParts = Parse(right);
        var length = Math.Max(leftParts.Count, rightParts.Count);
        for (var index = 0; index < length; index++)
        {
            var leftValue = index < leftParts.Count ? leftParts[index] : 0;
            var rightValue = index < rightParts.Count ? rightParts[index] : 0;
            var comparison = leftValue.CompareTo(rightValue);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static IReadOnlyList<int> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [0];
        }

        var match = VersionPrefixRegex().Match(value.Trim());
        if (!match.Success)
        {
            return [0];
        }

        return match.Value
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0)
            .ToArray();
    }

    [GeneratedRegex(@"\d+(?:\.\d+)*", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPrefixRegex();
}
