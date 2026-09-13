namespace TOOL_SHARED.Contracts.Common;

/// <summary>Capabilities of this installation; does not replace authentication or project authorization.</summary>
public sealed class ApplicationFeaturePolicy
{
    public const string SectionName = "Application";
    public const string LocalOnlyErrorCode = "vietsub_local_only";
    public const string LocalOnlyMessage = "Ứng dụng đang ở chế độ Vietsub local. Tạo video AI và Dịch Cloud đã bị khóa.";

    public bool VietsubLocalOnly { get; set; } = true;

    public static bool IsLocalDesktopCommand(string command) => command is
        "app.ready" or "dashboard.refresh" or "organization.select" or
        "license.refresh" or "license.offers.get" or "license.payment.create" or
        "license.payment.current.get" or "license.payment.status" or
        "desktop.settings.get" or "media.tools.check" or "auth.logout";

    public static bool IsVideoApiPath(string path) =>
        path.StartsWith("/api/generation/", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/api/generation", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/api/projects/", StringComparison.OrdinalIgnoreCase);

    public static bool IsCloudSubmission(string path, string method) =>
        method.Equals("POST", StringComparison.OrdinalIgnoreCase)
        && path.StartsWith("/api/vietsub/projects/", StringComparison.OrdinalIgnoreCase)
        && path.Contains("/cloud-translation/", StringComparison.OrdinalIgnoreCase)
        && (path.TrimEnd('/').EndsWith("/jobs", StringComparison.OrdinalIgnoreCase)
            || path.TrimEnd('/').EndsWith("/resume", StringComparison.OrdinalIgnoreCase)
            || path.TrimEnd('/').EndsWith("/retry", StringComparison.OrdinalIgnoreCase));
}
