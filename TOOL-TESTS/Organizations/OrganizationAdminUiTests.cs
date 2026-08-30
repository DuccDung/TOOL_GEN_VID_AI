namespace TOOL_TESTS.Organizations;

public sealed class OrganizationAdminUiTests
{
    [Fact]
    public void AdminPage_ContainsOrganizationNavigationAndFiveDetailTabs()
    {
        var page = ReadRepositoryFile("TOOL-SERVER", "Pages", "Admin", "Index.cshtml");

        Assert.Contains("data-view=\"organizations\"", page);
        Assert.Contains("Tổ chức &amp; AI", page);
        foreach (var tab in new[] { "overview", "members", "usage", "providers", "audit" })
        {
            Assert.Contains($"data-organization-tab=\"{tab}\"", page);
        }
    }

    [Fact]
    public void CredentialDialog_IsOneTimePasswordInputAndOrganizationScriptResetsIt()
    {
        var page = ReadRepositoryFile("TOOL-SERVER", "Pages", "Admin", "Index.cshtml");
        var script = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin-organizations.js");

        Assert.Contains("id=\"organizationCredentialKey\" type=\"password\"", page);
        Assert.Contains("autocomplete=\"new-password\"", page);
        Assert.Contains("keyInput.value = '';", script);
        Assert.Contains("addEventListener('close', resetCredentialDialog)", script);
        Assert.DoesNotContain("localStorage", script, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledProvider_ShowsActivationDialogInsteadOfCredentialAction()
    {
        var page = ReadRepositoryFile("TOOL-SERVER", "Pages", "Admin", "Index.cshtml");
        var script = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin-organizations.js");

        Assert.Contains("id=\"organizationProviderUnavailableDialog\"", page);
        Assert.Contains("id=\"organizationProviderUnavailableSetup\"", page);
        Assert.Contains("provider_disabled: 'Provider hiện chưa được kích hoạt.'", script);
        Assert.Contains("if (!catalogProvider.isEnabled) {", script);
        Assert.Contains("provider.isEnabled", script);
        Assert.Contains("data-provider-unavailable=", script);
        Assert.Contains("openProviderUnavailableDialog", script);
        Assert.Contains("navigateToReadinessSetup('pricing', providerCode)", script);
    }

    [Fact]
    public void OrganizationFeature_IsLazyLoadedOutsideBaseAdminLoad()
    {
        var shellScript = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin.js");
        var organizationScript = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin-organizations.js");

        Assert.DoesNotContain("api('/api/organizations')", shellScript, StringComparison.Ordinal);
        Assert.Contains("activate: () => showScope", organizationScript);
        Assert.Contains("new AbortController()", organizationScript);
    }

    [Fact]
    public void AuditRenderingEscapesKeysAndValues()
    {
        var script = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin-organizations.js");

        Assert.Contains("escapeHtml(key)", script);
        Assert.Contains("escapeHtml(value ?? 'null')", script);
        Assert.DoesNotContain("JSON.stringify(item.data)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadinessWarnings_LinkToBudgetAndPricingSetup()
    {
        var script = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin-organizations.js");

        Assert.Contains("label: 'Thiết lập ngân sách'", script);
        Assert.Contains("label: 'Thiết lập bảng giá'", script);
        Assert.Contains("label: 'Cấu hình credential'", script);
        Assert.Contains("data-readiness-action=", script);
        Assert.Contains("await selectTab('usage')", script);
        Assert.Contains("await selectTab('providers')", script);
        Assert.Contains("await showScope('pricing')", script);
        Assert.Contains("data-pricing-provider=", script);
    }

    [Fact]
    public void CostGuide_ExplainsServerBillingAndUsesActiveRatesForExamples()
    {
        var page = ReadRepositoryFile("TOOL-SERVER", "Pages", "Admin", "Index.cshtml");
        var script = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin-organizations.js");

        Assert.Contains("data-organization-scope=\"cost-guide\"", page);
        Assert.Contains("Cơ chế tính chi phí AI", page);
        Assert.Contains("Input ước tính = max(2.000; 1.500 + số ký tự chủ đề / 3)", page);
        Assert.Contains("Output ước tính = min(8.000; 2.000 + số cảnh × 300)", page);
        Assert.Contains("Budget − Actual − Reserved", page);
        Assert.Contains("developers.openai.com/api/docs/models", page);
        Assert.Contains("kling.ai/document-api/pricing/base/video", page);
        Assert.Contains("loadCostGuide", script);
        Assert.Contains("tokenCostForGuide", script);
        Assert.Contains("guideRate(klingModel, 'VideoSecond', 'kling')", script);
        Assert.Contains("metadata.resolution?.toLowerCase() === '720p'", script);
        Assert.Contains("metadata.nativeAudio === true", script);
        Assert.Contains("resolution: '720p', nativeAudio: true", script);
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
