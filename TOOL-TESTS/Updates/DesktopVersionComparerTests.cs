using TOOL_SERVER.Updates;

namespace TOOL_TESTS.Updates;

public sealed class DesktopVersionComparerTests
{
    [Theory]
    [InlineData("1.2.0", 11, "1.9.9", 10, true)]
    [InlineData("1.2.0", 10, "1.1.9", 10, true)]
    [InlineData("v2.0.0-beta", 5, "1.9.9", 5, true)]
    [InlineData("1.0.0", 9, "2.0.0", 10, false)]
    [InlineData("1.0.0", 10, "1.0.0", 10, false)]
    public void IsReleaseNewer_UsesBuildThenNumericVersion(
        string releaseVersion,
        int releaseBuild,
        string currentVersion,
        int currentBuild,
        bool expected)
    {
        Assert.Equal(expected, DesktopVersionComparer.IsReleaseNewer(
            releaseVersion,
            releaseBuild,
            currentVersion,
            currentBuild));
    }

    [Theory]
    [InlineData("1.10.0", "1.9.9", 1)]
    [InlineData("1.0", "1.0.0", 0)]
    [InlineData("v2.1.3-beta", "2.1.2", 1)]
    public void CompareVersions_ComparesNumericComponents(string left, string right, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(DesktopVersionComparer.CompareVersions(left, right)));
    }
}
