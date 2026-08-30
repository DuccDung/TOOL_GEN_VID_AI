using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace TOOL_SERVER.Providers;

public interface IProviderCredentialProtector
{
    string Protect(string apiKey);

    string Unprotect(string protectedPayload);
}

public sealed class ProviderCredentialProtector(IDataProtectionProvider dataProtectionProvider)
    : IProviderCredentialProtector
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "TOOL_SERVER.OrganizationProviderCredentials.v1");

    public string Protect(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 10_000)
        {
            throw new ArgumentException("API key không hợp lệ.", nameof(apiKey));
        }

        var payload = JsonSerializer.Serialize(new ProviderSecretPayload(apiKey));
        return _protector.Protect(payload);
    }

    public string Unprotect(string protectedPayload)
    {
        var json = _protector.Unprotect(protectedPayload);
        var payload = JsonSerializer.Deserialize<ProviderSecretPayload>(json)
            ?? throw new InvalidOperationException("Credential payload không hợp lệ.");
        return payload.ApiKey;
    }

    private sealed record ProviderSecretPayload(string ApiKey);
}
