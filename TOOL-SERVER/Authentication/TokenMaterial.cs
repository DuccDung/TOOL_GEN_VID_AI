namespace TOOL_SERVER.Authentication;

public sealed record AccessTokenMaterial(
    string Token,
    string JwtId,
    DateTime ExpiresAtUtc);

public sealed record RefreshTokenMaterial(
    string PlainTextToken,
    byte[] TokenHash,
    string TokenPrefix,
    DateTime ExpiresAtUtc);
