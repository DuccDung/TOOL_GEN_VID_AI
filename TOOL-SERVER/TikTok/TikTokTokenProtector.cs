using Microsoft.AspNetCore.DataProtection;

namespace TOOL_SERVER.TikTok;

public interface ITikTokTokenProtector
{
    string ProtectToken(string userId, string token);
    string UnprotectToken(string userId, string protectedToken);
    string ProtectUploadUrl(string userId, string uploadUrl);
    string UnprotectUploadUrl(string userId, string protectedUploadUrl);
    string ProtectAvatarUrl(string userId, string url) => ProtectToken(userId, url);
    string UnprotectAvatarUrl(string userId, string value) => UnprotectToken(userId, value);
}

public sealed class TikTokTokenProtector(IDataProtectionProvider provider) : ITikTokTokenProtector
{
    private const string TokenPurpose = "TOOL_SERVER.TikTokUserTokens.v1";
    private const string UploadPurpose = "TOOL_SERVER.TikTokUploadUrls.v1";
    private const string AvatarPurpose = "TOOL_SERVER.TikTokAvatarUrls.v1";

    public string ProtectAvatarUrl(string userId, string url) => CreateProtector(AvatarPurpose, userId).Protect(url);
    public string UnprotectAvatarUrl(string userId, string value) => CreateProtector(AvatarPurpose, userId).Unprotect(value);

    public string ProtectToken(string userId, string token) =>
        CreateProtector(TokenPurpose, userId).Protect(token);

    public string UnprotectToken(string userId, string protectedToken) =>
        CreateProtector(TokenPurpose, userId).Unprotect(protectedToken);

    public string ProtectUploadUrl(string userId, string uploadUrl) =>
        CreateProtector(UploadPurpose, userId).Protect(uploadUrl);

    public string UnprotectUploadUrl(string userId, string protectedUploadUrl) =>
        CreateProtector(UploadPurpose, userId).Unprotect(protectedUploadUrl);

    private IDataProtector CreateProtector(string purpose, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return provider.CreateProtector(purpose, userId);
    }
}
