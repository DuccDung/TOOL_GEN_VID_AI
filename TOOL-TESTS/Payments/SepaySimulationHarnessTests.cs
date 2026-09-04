using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TOOL_SERVER.Controllers;
using TOOL_SERVER.Payments;

namespace TOOL_TESTS.Payments;

public sealed class SepaySimulationHarnessTests
{
    [Fact]
    public void WebhookEndpoint_RemainsAnonymousBoundedAndRateLimited()
    {
        var controller = typeof(SepayWebhookController);
        var webhook = controller.GetMethod(nameof(SepayWebhookController.Webhook))!;
        var requestSizeLimit = webhook.CustomAttributes.Single(x =>
            x.AttributeType == typeof(RequestSizeLimitAttribute));

        Assert.NotNull(controller.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("api/payments/sepay", controller.GetCustomAttribute<RouteAttribute>()?.Template);
        Assert.Equal(64L * 1024, requestSizeLimit.ConstructorArguments.Single().Value);
        Assert.Equal("sepay-webhook", webhook.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName);
    }

    [Fact]
    public void WebhookPayload_SerializesWithSepayFieldNames()
    {
        var payload = new SepayWebhookPayload(
            123,
            "TESTBANK",
            "2026-09-04 12:00:00",
            "123456789",
            string.Empty,
            "VMTESTCODE",
            "TEST VMTESTCODE",
            "in",
            "Simulated webhook",
            132_000,
            132_000,
            "SIM-123");

        var json = JsonSerializer.Serialize(payload);

        foreach (var propertyName in new[]
        {
            "id",
            "gateway",
            "transactionDate",
            "accountNumber",
            "subAccount",
            "code",
            "content",
            "transferType",
            "description",
            "transferAmount",
            "accumulated",
            "referenceCode"
        })
        {
            Assert.Contains($"\"{propertyName}\":", json, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("\"TransactionDate\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"TransferAmount\":", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SimulationScript_RequiresExplicitMutationApprovalAndKeepsTokensOutOfArguments()
    {
        var script = ReadRepositoryFile("scripts", "Test-SepayOrganizationProvisioning.ps1");

        Assert.Contains("ValidateSet('SEPAY_TEST_ONLY')", script, StringComparison.Ordinal);
        Assert.Contains("if (-not $BaseUrl.IsLoopback)", script, StringComparison.Ordinal);
        Assert.Contains("if (-not $AllowRemote)", script, StringComparison.Ordinal);
        Assert.Contains("A remote test target must use HTTPS", script, StringComparison.Ordinal);
        Assert.Contains("VIDEOMAKER_TEST_USER_TOKEN", script, StringComparison.Ordinal);
        Assert.Contains("VIDEOMAKER_TEST_ADMIN_TOKEN", script, StringComparison.Ordinal);
        Assert.Contains("[Environment]::GetEnvironmentVariable", script, StringComparison.Ordinal);
        Assert.DoesNotContain("[string]$UserAccessToken", script, StringComparison.Ordinal);
        Assert.DoesNotContain("[string]$AdminAccessToken", script, StringComparison.Ordinal);
        Assert.Contains("api/payments/sepay/webhook", script, StringComparison.Ordinal);
        Assert.Contains("[System.Threading.Tasks.Task]::WaitAll", script, StringComparison.Ordinal);
        Assert.Contains("api/admin/licenses/payments?search=", script, StringComparison.Ordinal);
        Assert.Contains("api/admin/organization-pools/assignments?take=200", script, StringComparison.Ordinal);
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
