using Microsoft.AspNetCore.Identity;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Domain.Accounts;

namespace TOOL_SERVER.Infrastructure;

public static class AdminBootstrapper
{
    public static async Task EnsureAsync(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger)
    {
        await using var scope = services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roleManager.RoleExistsAsync("Admin"))
        {
            var result = await roleManager.CreateAsync(new IdentityRole("Admin"));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("Không thể khởi tạo role Admin.");
            }
        }

        var options = configuration.GetSection(AdminOptions.SectionName).Get<AdminOptions>();
        if (string.IsNullOrWhiteSpace(options?.BootstrapEmail))
        {
            return;
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(options.BootstrapEmail.Trim());
        if (user is null)
        {
            logger.LogWarning("Admin bootstrap email is configured but the account does not exist.");
            return;
        }

        if (!await userManager.IsInRoleAsync(user, "Admin"))
        {
            var result = await userManager.AddToRoleAsync(user, "Admin");
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("Không thể gán role Admin cho tài khoản bootstrap.");
            }

            logger.LogInformation("Admin bootstrap role was assigned to the configured account.");
        }
    }
}

