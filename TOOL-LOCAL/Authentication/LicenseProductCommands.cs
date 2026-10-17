using TOOL_SHARED.Contracts.Accounts;

namespace TOOL_LOCAL.Authentication;

internal static class LicenseProductCommands
{
    public static bool Allows(string? flags, string operation)
    {
        if (LicenseProductPolicy.VideoAllowed(flags)) return true;
        if (operation is "app.ready" or "dashboard.refresh" or "organization.select" or "auth.logout" ||
            operation.StartsWith("media.tools.", StringComparison.Ordinal) ||
            operation.StartsWith("license.", StringComparison.Ordinal) || operation.StartsWith("desktop.settings.", StringComparison.Ordinal) ||
            operation.StartsWith("system.setup.", StringComparison.Ordinal) || operation.StartsWith("update.", StringComparison.Ordinal)) return true;
        if (!LicenseProductPolicy.VietsubAllowed(flags)) return false;
        if (operation == "vietsub.job.translate.cloud" || operation.StartsWith("vietsub.cloud.", StringComparison.Ordinal))
            return operation is "vietsub.cloud.availability" or "vietsub.cloud.current" or "vietsub.cloud.status" or
                "vietsub.cloud.results" or "vietsub.cloud.cancel" or "vietsub.cloud.pause" or "vietsub.cloud.ack" ||
                LicenseProductPolicy.CloudAllowed(flags);
        return operation.StartsWith("vietsub.", StringComparison.Ordinal);
    }
}
