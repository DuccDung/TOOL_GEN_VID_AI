using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Domain.Accounts;

namespace TOOL_SERVER.Authentication;

public sealed class JwtTokenFactory(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenFactory
{
    private readonly JwtOptions _options = options.Value;

    public AccessTokenMaterial CreateAccessToken(
        ApplicationUser user,
        IEnumerable<string> roles,
        Guid sessionId,
        Guid deviceId)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAtUtc = now.AddMinutes(_options.AccessTokenMinutes);
        var jwtId = Guid.NewGuid().ToString("N");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, jwtId),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.DisplayName ?? user.UserName ?? user.Id),
            new(AuthClaimTypes.SessionId, sessionId.ToString("D")),
            new(AuthClaimTypes.DeviceId, deviceId.ToString("D"))
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return new AccessTokenMaterial(
            new JwtSecurityTokenHandler().WriteToken(token),
            jwtId,
            expiresAtUtc);
    }

    public RefreshTokenMaterial CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        var plainText = WebEncoders.Base64UrlEncode(bytes);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(plainText));
        var prefix = plainText[..Math.Min(12, plainText.Length)];
        var expiresAtUtc = timeProvider.GetUtcNow().UtcDateTime.AddDays(_options.RefreshTokenDays);

        return new RefreshTokenMaterial(plainText, hash, prefix, expiresAtUtc);
    }

    public byte[] HashRefreshToken(string refreshToken) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
}
