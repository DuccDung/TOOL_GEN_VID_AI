using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace TOOL_SERVER.TikTok;

public interface ITikTokAppCredentialProtector
{
    string Protect(Guid credentialId, string clientKey, string clientSecret);

    TikTokAppCredentialMaterial Unprotect(Guid credentialId, string protectedPayload, bool pendingVerification);
}

public sealed record TikTokAppCredentialMaterial(
    Guid? CredentialId,
    string ClientKey,
    string ClientSecret,
    bool PendingVerification);

public sealed class TikTokAppCredentialProtector(IDataProtectionProvider dataProtectionProvider)
    : ITikTokAppCredentialProtector
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "TOOL_SERVER.TikTokAppCredentials.v1");

    public string Protect(Guid credentialId, string clientKey, string clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientKey) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new ArgumentException("TikTok credential không hợp lệ.");
        }

        return ForCredential(credentialId).Protect(
            JsonSerializer.Serialize(new CredentialPayload(clientKey, clientSecret)));
    }

    public TikTokAppCredentialMaterial Unprotect(
        Guid credentialId,
        string protectedPayload,
        bool pendingVerification)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<CredentialPayload>(
                ForCredential(credentialId).Unprotect(protectedPayload));
            if (payload is null ||
                string.IsNullOrWhiteSpace(payload.ClientKey) ||
                string.IsNullOrWhiteSpace(payload.ClientSecret))
            {
                throw new CryptographicException("TikTok credential payload không hợp lệ.");
            }

            return new TikTokAppCredentialMaterial(
                credentialId,
                payload.ClientKey,
                payload.ClientSecret,
                pendingVerification);
        }
        catch (Exception exception) when (exception is not CryptographicException)
        {
            throw new CryptographicException("Không thể giải mã TikTok credential.", exception);
        }
    }

    private IDataProtector ForCredential(Guid credentialId) =>
        _protector.CreateProtector(credentialId.ToString("N"));

    private sealed record CredentialPayload(string ClientKey, string ClientSecret);
}
