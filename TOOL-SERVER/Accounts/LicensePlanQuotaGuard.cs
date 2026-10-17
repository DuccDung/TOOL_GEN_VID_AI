using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Organizations;

namespace TOOL_SERVER.Accounts;

internal static class LicensePlanQuotaGuard
{
    // Missing mappings are repaired separately; do not rewrite existing membership quotas.
    public static async Task EnsureCanChangeAsync(AccountDbContext db, Guid planId,
        LicensePlanOrganizationPool? existing, decimal? next, DateTime now, CancellationToken ct)
    {
        if (existing is null || existing.DefaultMemberMonthlyBudgetLimit == next) return;
        if (await db.LicensePayments.AnyAsync(x => x.LicensePlanId == planId &&
                ((x.Status == "Pending" && x.ExpiresAtUtc > now) || (x.Status == "Paid" && x.FulfilledAtUtc == null)), ct) ||
            await db.OrganizationSeatAssignments.AnyAsync(x => x.LicensePlanId == planId && x.Status == OrganizationSeatAssignmentStatuses.Scheduled, ct))
            throw new AccountApiException(409, "license_plan_quota_in_use",
                "Gói còn đơn chờ thanh toán, đã nhận tiền hoặc đã lên lịch. Chờ xử lý xong trước khi đổi hạn mức mặc định.");
    }
}
