using TOOL_SERVER.Domain.Accounts;

namespace TOOL_SERVER.Authentication;

public interface ITokenFactory
{
    AccessTokenMaterial CreateAccessToken(
        ApplicationUser user,
        IEnumerable<string> roles,
        Guid sessionId,
        Guid deviceId);

    RefreshTokenMaterial CreateRefreshToken();

    byte[] HashRefreshToken(string refreshToken);
}
