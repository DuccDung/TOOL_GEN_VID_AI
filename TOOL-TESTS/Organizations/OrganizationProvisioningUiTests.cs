namespace TOOL_TESTS.Organizations;

public sealed class OrganizationProvisioningUiTests
{
    [Fact]
    public void AdminConsole_ExposesPoolCapacityMappingReadinessAndRetryControls()
    {
        var page = ReadRepositoryFile("TOOL-SERVER", "Pages", "Admin", "Index.cshtml");
        var script = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin-organizations.js");

        Assert.Contains("data-organization-scope=\"pools\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"poolOrganizationCapacity\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"poolOrganizationReady\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"poolPlanId\"", page, StringComparison.Ordinal);
        Assert.Contains("/api/admin/organization-pools", script, StringComparison.Ordinal);
        Assert.Contains("data-retry-assignment", script, StringComparison.Ordinal);
        Assert.Contains("isAutoAssignmentEnabled", script, StringComparison.Ordinal);
        Assert.Contains("Membership tự động", script, StringComparison.Ordinal);
        Assert.Contains("Tự động từ gói", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Desktop_ShowsCapacityAndPaidProvisioningState()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var bridge = ReadRepositoryFile("TOOL-LOCAL", "WebView", "DashboardBridge.cs");

        Assert.Contains("offer.organizationSeatAvailable", app, StringComparison.Ordinal);
        Assert.Contains("Tạm hết tổ chức sẵn sàng", app, StringComparison.Ordinal);
        Assert.Contains("Đang cấp tổ chức", app, StringComparison.Ordinal);
        Assert.Contains("assignedOrganizationName", app, StringComparison.Ordinal);
        Assert.Contains("license.AssignedOrganizationId", bridge, StringComparison.Ordinal);
        Assert.Contains("SelectOrganizationAsync(assignedOrganizationId", bridge, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Cannot locate repository file: {Path.Combine(relativeParts)}");
    }
}
