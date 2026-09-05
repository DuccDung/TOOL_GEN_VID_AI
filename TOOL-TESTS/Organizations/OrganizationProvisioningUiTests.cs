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
        Assert.Contains("Sẵn sàng là kết quả kiểm tra của hệ thống", page, StringComparison.Ordinal);
        Assert.Contains("id=\"poolPlanId\"", page, StringComparison.Ordinal);
        Assert.Contains("/api/admin/organization-pools", script, StringComparison.Ordinal);
        Assert.Contains("data-check-pool-organization", script, StringComparison.Ordinal);
        Assert.Contains("data-retry-assignment", script, StringComparison.Ordinal);
        Assert.Contains("isAutoAssignmentEnabled", script, StringComparison.Ordinal);
        Assert.Contains("Thành viên tự động", script, StringComparison.Ordinal);
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

    [Fact]
    public void PoolCreation_ExplainsTheSetupFlowAndStartsAsDraft()
    {
        var page = ReadRepositoryFile("TOOL-SERVER", "Pages", "Admin", "Index.cshtml");
        var script = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin-organizations.js");

        Assert.Contains("pool-setup-steps", page, StringComparison.Ordinal);
        Assert.Contains("organizationPoolSubmit", page, StringComparison.Ordinal);
        Assert.Contains("value=\"Inactive\"", page, StringComparison.Ordinal);
        Assert.Contains("poolCodeFromName", script, StringComparison.Ordinal);
        Assert.Contains("pool?.status || 'Inactive'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyPool_ShowsSetupChecklistOnlyWhenTheAdminOpensSetup()
    {
        var script = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin-organizations.js");

        Assert.Contains("pool-setup-panel", script, StringComparison.Ordinal);
        Assert.Contains("pool-setup-checklist", script, StringComparison.Ordinal);
        Assert.Contains("hasReadyOrganization", script, StringComparison.Ordinal);
        Assert.Contains("organizationState.poolSetupVisible", script, StringComparison.Ordinal);
        Assert.Contains("data-show-pool-setup", script, StringComparison.Ordinal);
        Assert.Contains("data-open-pool-setup", script, StringComparison.Ordinal);
        Assert.Contains("pool-detail-panel standalone", script, StringComparison.Ordinal);
        Assert.Contains("data-close-pool", script, StringComparison.Ordinal);
        Assert.Contains("Tiếp tục thiết lập", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminAllocationUi_UsesLiveAllocatableCapacityAndPaidProvisioningState()
    {
        var script = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin-organizations.js");
        var contracts = ReadRepositoryFile("TOOL-SHARED.Contracts", "Organizations", "OrganizationProvisioningContracts.cs");

        Assert.Contains("allocatableAvailableSeats", script, StringComparison.Ordinal);
        Assert.Contains("activeLicensePlanCount", script, StringComparison.Ordinal);
        Assert.Contains("Đã nhận tiền — chờ cấp tổ chức", script, StringComparison.Ordinal);
        Assert.Contains("result.paymentStatus !== 'Fulfilled'", script, StringComparison.Ordinal);
        Assert.Contains("organizationState.scope === 'setup'", script, StringComparison.Ordinal);
        Assert.Contains("data-setup-next", script, StringComparison.Ordinal);
        Assert.Contains("poolOrganizationCapacityMinimum", script, StringComparison.Ordinal);
        Assert.Contains("string? PaymentStatus = null", contracts, StringComparison.Ordinal);
        Assert.Contains("int AllocatableAvailableSeats = 0", contracts, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminSetupCenter_ExposesPublicBuildMarkerAndAccessibleProgress()
    {
        var page = ReadRepositoryFile("TOOL-SERVER", "Pages", "Admin", "Index.cshtml");
        var adminScript = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin.js");
        var script = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin-organizations.js");

        Assert.Contains("const string AdminUiBuildMarker = \"admin-setup-center-20260904.1\";", page, StringComparison.Ordinal);
        Assert.Contains("name=\"videomaker-admin-ui-build\" content=\"@AdminUiBuildMarker\"", page, StringComparison.Ordinal);
        Assert.Contains("data-admin-ui-build=\"@AdminUiBuildMarker\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"adminUiBuildMarker\"", page, StringComparison.Ordinal);
        Assert.Contains("data-nav-parent=\"organizations\"", page, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"organizationSubmenu\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"organizationSubmenu\"", page, StringComparison.Ordinal);
        Assert.Contains("class=\"nav-subitem active\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("organization-scope-bar", page, StringComparison.Ordinal);
        Assert.Contains("function setOrganizationMenuExpanded", adminScript, StringComparison.Ordinal);
        Assert.Contains("data-nav-parent", adminScript, StringComparison.Ordinal);
        Assert.Contains("organizationMenuExpanded", adminScript, StringComparison.Ordinal);
        Assert.Contains("<progress class=\"setup-progress\" role=\"progressbar\"", script, StringComparison.Ordinal);
        Assert.Contains("aria-valuemin=\"0\"", script, StringComparison.Ordinal);
        Assert.Contains("aria-valuemax=\"${stages.length}\"", script, StringComparison.Ordinal);
        Assert.Contains("aria-valuenow=\"${completed}\"", script, StringComparison.Ordinal);
        Assert.Contains("organizationMenuExpanded", script, StringComparison.Ordinal);
        Assert.Contains("event.key === 'ArrowDown'", script, StringComparison.Ordinal);
        Assert.Contains("event.key === 'Home'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminSetupCenterAndAllocationComponents_HaveResponsiveAccessibleStyles()
    {
        var styles = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin.css");
        var requiredSelectors = new[]
        {
            ".admin-build-marker",
            ".nav-group",
            ".nav-parent",
            ".nav-submenu",
            ".nav-subitem",
            ".setup-center-panel",
            ".setup-center-intro",
            ".setup-center-hero",
            ".setup-progress",
            ".setup-stage-list",
            ".setup-stage-number",
            ".setup-center-note",
            ".organization-setup-hero",
            ".organization-setup-steps",
            ".policy-focus-layout",
            ".video-policy-card",
            ".admin-disclosure",
            ".pricing-provider-summary",
            ".rate-preview",
            ".pool-list-summary",
            ".pool-detail-panel.standalone",
            ".pool-back-button",
            ".pool-setup-checklist",
            ".readiness-explanation"
        };

        foreach (var selector in requiredSelectors)
        {
            Assert.Contains(selector, styles, StringComparison.Ordinal);
        }

        Assert.Contains("@media (prefers-reduced-motion: reduce)", styles, StringComparison.Ordinal);
        Assert.Contains("@media (forced-colors: active)", styles, StringComparison.Ordinal);
        Assert.Contains(".nav-subitem:focus-visible", styles, StringComparison.Ordinal);
        Assert.DoesNotContain(".organization-scope-bar", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminPageAndOrganizationScript_StaticClassNamesHaveCssSelectors()
    {
        var page = ReadRepositoryFile("TOOL-SERVER", "Pages", "Admin", "Index.cshtml");
        var script = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin-organizations.js");
        var styles = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin.css");
        var classNames = System.Text.RegularExpressions.Regex
            .Matches(page + script, "class=\"(?<classes>[^\"`]+)\"")
            .SelectMany(match => match.Groups["classes"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(value => System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-z][a-z0-9-]+$"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var missingSelectors = classNames
            .Where(className => !styles.Contains($".{className}", StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            missingSelectors.Length == 0,
            $"Admin CSS is missing selectors for: {string.Join(", ", missingSelectors)}");
    }

    [Fact]
    public void AdminRuntimeDiagnostic_IsLocalReadOnlyAndChecksServedHtmlCssAndJavaScript()
    {
        var diagnostic = ReadRepositoryFile("scripts", "Test-AdminRuntimeAssets.ps1");

        Assert.Contains("[string]$ExpectedMarker = 'admin-setup-center-20260904.1'", diagnostic, StringComparison.Ordinal);
        Assert.Contains("if (-not $AdminUrl.IsLoopback)", diagnostic, StringComparison.Ordinal);
        Assert.Contains("Method = 'Get'", diagnostic, StringComparison.Ordinal);
        Assert.Contains("'/admin/admin.css'", diagnostic, StringComparison.Ordinal);
        Assert.Contains("'/admin/admin.js'", diagnostic, StringComparison.Ordinal);
        Assert.Contains("'/admin/admin-organizations.js'", diagnostic, StringComparison.Ordinal);
        Assert.Contains("Wrong build/checkout or stale cache", diagnostic, StringComparison.Ordinal);
        Assert.Contains("Cache-Control no-store", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-RestMethod", diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Method = 'Post'", diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Method = 'Put'", diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Method = 'Delete'", diagnostic, StringComparison.OrdinalIgnoreCase);
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
