using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Controllers;

namespace TOOL_TESTS.Providers;

public sealed class AdminProvidersControllerTests
{
    [Fact]
    public void LicenseController_RequiresAdminRole()
    {
        var attribute = Assert.Single(
            typeof(AdminLicensesController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>());

        Assert.Equal("Admin", attribute.Roles);
    }

    [Fact]
    public void AiPricingController_RequiresGlobalAdminRole()
    {
        var attribute = Assert.Single(
            typeof(AdminAiPricingController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>());

        Assert.Equal("Admin", attribute.Roles);
    }

    [Fact]
    public void OrganizationGenerationController_IsAuthenticatedAndDiscoverable()
    {
        Assert.Empty(typeof(GenerationController).GetCustomAttributes(typeof(NonControllerAttribute), inherit: true));
        Assert.NotEmpty(typeof(GenerationController).GetCustomAttributes(typeof(ApiControllerAttribute), inherit: true));
        Assert.NotEmpty(typeof(GenerationController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("{}", 1)]
    [InlineData("{\"maxConcurrentSessions\":3}", 3)]
    [InlineData("{\"maxConcurrentSessions\":999}", 100)]
    [InlineData("invalid", 1)]
    public void LicensePolicy_ReadsConcurrentSessionLimit(string? json, int expected)
    {
        Assert.Equal(expected, LicensePolicy.GetMaxConcurrentSessions(json));
    }

    [Fact]
    public void LicensePolicy_MergesConcurrentSessionLimitWithoutLosingFlags()
    {
        var json = LicensePolicy.MergeMaxConcurrentSessions("{\"featureA\":true}", 2);

        Assert.Contains("\"featureA\":true", json);
        Assert.Contains("\"maxConcurrentSessions\":2", json);
    }
}
