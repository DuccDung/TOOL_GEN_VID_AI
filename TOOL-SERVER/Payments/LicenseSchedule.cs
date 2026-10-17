using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Accounts;

namespace TOOL_SERVER.Payments;

internal static class LicenseSchedule
{
    internal static Task<UserLicense?> TailAsync(AccountDbContext db, string userId, DateTime now, CancellationToken ct) =>
        db.UserLicenses.Include(x => x.LicensePlan).Where(x => x.UserId == userId && x.RevokedAtUtc == null &&
            (x.Status == "Active" || x.Status == "Trial") && (x.ExpiresAtUtc == null || x.ExpiresAtUtc > now))
        .OrderByDescending(x => x.ExpiresAtUtc == null).ThenByDescending(x => x.ExpiresAtUtc)
        .ThenByDescending(x => x.StartsAtUtc).ThenBy(x => x.UserLicenseId).FirstOrDefaultAsync(ct);

    internal static bool SameRights(string? first, string? second)
    {
        try { return JsonNode.DeepEquals(JsonNode.Parse(string.IsNullOrWhiteSpace(first) ? "{}" : first),
            JsonNode.Parse(string.IsNullOrWhiteSpace(second) ? "{}" : second)); }
        catch (JsonException) { return false; }
    }
}
