using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;

namespace TOOL_SERVER.Infrastructure;

public static class LicensePlanBootstrapper
{
    public static async Task EnsureAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
        var now = DateTime.UtcNow;
        await EnsurePlanAsync(dbContext, "trial-7", "Dùng thử 7 ngày", 7, 1, now, cancellationToken);
        await EnsurePlanAsync(dbContext, "monthly-30", "Gói 30 ngày", 30, 1, now, cancellationToken);
        await EnsurePlanAsync(dbContext, "half-year-180", "Gói 180 ngày", 180, 1, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsurePlanAsync(
        AccountDbContext dbContext,
        string code,
        string name,
        int durationDays,
        int maxDevices,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (await dbContext.LicensePlans.AnyAsync(x => x.PlanCode == code, cancellationToken))
        {
            return;
        }

        dbContext.LicensePlans.Add(new LicensePlan
        {
            LicensePlanId = Guid.NewGuid(),
            PlanCode = code,
            Name = name,
            Description = $"Quyền sử dụng VideoMaker trong {durationDays} ngày.",
            MaxActivatedDevices = maxDevices,
            OfflineGraceHours = 0,
            DefaultDurationDays = durationDays,
            FeatureFlagsJson = LicensePolicy.MergeMaxConcurrentSessions(null, 1),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
    }
}
