namespace TOOL_LOCAL.SystemSetup;

internal sealed class StartupSystemSetupGate
{
    private readonly SystemSetupCoordinator _coordinator;
    private bool _satisfied;

    public StartupSystemSetupGate(SystemSetupCoordinator coordinator, bool required)
    {
        _coordinator = coordinator;
        _satisfied = !required;
    }

    public bool IsBlocking
    {
        get
        {
            if (_satisfied) return false;
            if (!StartupSystemSetupWorkflow.IsReady(_coordinator.GetSnapshot())) return true;
            _satisfied = true;
            return false;
        }
    }

    internal static bool IsAllowedCommand(string type) =>
        SystemSetupBridge.IsCommand(type) || type is
            "app.ready" or
            "dashboard.refresh" or
            "license.refresh" or
            "license.offers.get" or
            "license.payment.create" or
            "license.payment.current.get" or
            "license.payment.status" or
            "auth.logout" or
            "media.tools.install.prepare" or
            "media.tools.install";
}
