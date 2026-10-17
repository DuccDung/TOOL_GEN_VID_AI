using System.Reflection;

namespace TOOL_LOCAL.Updates;

internal static class DesktopBuildInfo
{
    private static readonly Assembly Assembly = typeof(DesktopBuildInfo).Assembly;
    public static int DistributionProtocol => Metadata("DesktopDistributionProtocol") == "3" ? 3 : 2;
    public static string Profile => Metadata("DesktopReleaseProfile") ?? "Production";
    private static string? Metadata(string name) => Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == name)?.Value;

    public static string Version =>
        (Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0")
        .Split('+', 2)[0];

    public static int BuildNumber
    {
        get
        {
            var value = Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "DesktopBuildNumber")?.Value;
            return int.TryParse(value, out var buildNumber) && buildNumber > 0 ? buildNumber : 1;
        }
    }
}
