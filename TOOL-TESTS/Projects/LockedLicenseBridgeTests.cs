using TOOL_LOCAL.WebView;

namespace TOOL_TESTS.Projects;

public sealed class LockedLicenseBridgeTests
{
    [Theory]
    [InlineData("app.ready")]
    [InlineData("dashboard.refresh")]
    [InlineData("license.refresh")]
    [InlineData("license.offers.get")]
    [InlineData("license.payment.create")]
    [InlineData("license.payment.current.get")]
    [InlineData("license.payment.status")]
    [InlineData("auth.logout")]
    public void LockedState_AllowsOnlyRecoveryOperations(string operation)
    {
        Assert.True(DashboardBridge.IsAllowedWhileLocked(operation));
    }

    [Theory]
    [InlineData("project.create")]
    [InlineData("organization.select")]
    [InlineData("generation.content")]
    [InlineData("generation.video")]
    [InlineData("render.final")]
    [InlineData("media.tools.check")]
    [InlineData("providers.settings.get")]
    public void LockedState_BlocksApplicationOperations(string operation)
    {
        Assert.False(DashboardBridge.IsAllowedWhileLocked(operation));
    }
}
