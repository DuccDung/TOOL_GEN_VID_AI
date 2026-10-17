using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Providers;

namespace TOOL_SERVER.Organizations;

internal static class OrganizationCredentialWriter
{
    // Caller must authorize, probe the transient key and own a Serializable transaction.
    // Shared by advanced management and guided setup to preserve running-task snapshots.
    public static async Task<OrganizationProviderCredential> RotateAsync(DbContext db, Guid orgId, Guid providerId,
        string providerName, string? name, string apiKey, string actor, DateTime now,
        IProviderCredentialProtector protector, CancellationToken ct)
    {
        var credentials = db.Set<OrganizationProviderCredential>();
        var current = await credentials.Where(x => x.OrganizationId == orgId && x.ProviderId == providerId && x.Status == ProviderCredentialStatuses.Active).ToListAsync(ct);
        foreach (var c in current) { c.Status = ProviderCredentialStatuses.Retiring; c.RetiredAtUtc = now; c.UpdatedAtUtc = now; }
        if (current.Count > 0) await db.SaveChangesAsync(ct); // Release the filtered unique Active slot.
        var version = (await credentials.Where(x => x.OrganizationId == orgId && x.ProviderId == providerId).MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1;
        var added = new OrganizationProviderCredential { OrganizationProviderCredentialId = Guid.NewGuid(), OrganizationId = orgId,
            ProviderId = providerId, Version = version, Name = string.IsNullOrWhiteSpace(name) ? $"{providerName} v{version}" : name,
            EncryptedPayload = protector.Protect(apiKey), SecretHint = $"••••{apiKey[^4..]}", Status = ProviderCredentialStatuses.Active,
            CreatedByUserId = actor, CreatedAtUtc = now, UpdatedAtUtc = now };
        credentials.Add(added); return added;
    }
}
