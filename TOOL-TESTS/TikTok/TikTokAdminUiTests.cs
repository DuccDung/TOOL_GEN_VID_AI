namespace TOOL_TESTS.TikTok;

public sealed class TikTokAdminUiTests
{
    [Fact]
    public void AdminPage_ContainsTikTokNavigationAndOneTimeCredentialDialog()
    {
        var page = ReadRepositoryFile("TOOL-SERVER", "Pages", "Admin", "Index.cshtml");

        Assert.Contains("data-view=\"tiktok\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"tiktokAdminConsole\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"tiktokClientKey\" type=\"password\"", page, StringComparison.Ordinal);
        Assert.Contains("id=\"tiktokClientSecret\" type=\"password\"", page, StringComparison.Ordinal);
        Assert.Contains("autocomplete=\"new-password\"", page, StringComparison.Ordinal);
        Assert.Contains("admin/admin-tiktok.js", page, StringComparison.Ordinal);
        Assert.Contains("admin/admin-tiktok-state.js", page, StringComparison.Ordinal);
        Assert.Contains("aria-labelledby=\"tiktokCredentialTitle\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminScript_ClearsSecretsAndRendersOnlyCredentialHints()
    {
        var script = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin-tiktok.js");

        Assert.Contains("document.getElementById('tiktokClientKey').value = '';", script, StringComparison.Ordinal);
        Assert.Contains("document.getElementById('tiktokClientSecret').value = '';", script, StringComparison.Ordinal);
        Assert.Contains("item.clientKeyHint", script, StringComparison.Ordinal);
        Assert.Contains("item.secretHint", script, StringComparison.Ordinal);
        Assert.DoesNotContain("item.clientSecret", script, StringComparison.Ordinal);
        Assert.DoesNotContain("protectedPayload", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localStorage", script, StringComparison.Ordinal);
        Assert.DoesNotContain("sessionStorage", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminScript_RequiresDesktopVerificationBeforeActivation()
    {
        var script = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin-tiktok.js");

        Assert.Contains("/verification`, { method: 'POST' }", script, StringComparison.Ordinal);
        Assert.Contains("verificationRequestedByCurrentAdmin", script, StringComparison.Ordinal);
        var states = ReadRepositoryFile("TOOL-SERVER", "wwwroot", "admin", "admin-tiktok-state.js");
        Assert.Contains("Đang chờ bạn xác minh trên Desktop", states, StringComparison.Ordinal);
        Assert.Contains("confirmAuditApproval", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Desktop_AllowsOnlyPendingAdminVerificationPastLicenseOverlay()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var types = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "features", "tiktok", "types.ts");
        var controller = ReadRepositoryFile("TOOL-SERVER", "Controllers", "TikTokController.cs");

        Assert.Contains("isCredentialVerification?: boolean", types, StringComparison.Ordinal);
        Assert.Contains("dashboard.profile.roles.some((role) => role.toLowerCase() === 'admin')", app, StringComparison.Ordinal);
        Assert.Contains("page === 'tiktok' && tiktokCredentialVerification", app, StringComparison.Ordinal);
        Assert.Contains("onVerifyTikTok={tiktokCredentialVerification", app, StringComparison.Ordinal);
        Assert.Contains("User.IsInRole(\"Admin\")", controller, StringComparison.Ordinal);
        Assert.Contains("tiktok_credential_verification_admin_required", controller, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. pathParts]);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Cannot locate {Path.Combine(pathParts)}.");
    }
}
