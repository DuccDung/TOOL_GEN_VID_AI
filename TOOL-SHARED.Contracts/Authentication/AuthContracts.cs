namespace TOOL_SHARED.Contracts.Authentication;

public sealed record DeviceRegistrationRequest(
    string Fingerprint,
    string DeviceName,
    string? OperatingSystem,
    string? ApplicationVersion);

public sealed record RegisterRequest(
    string Email,
    string Password,
    string? DisplayName,
    DeviceRegistrationRequest Device);

public sealed record LoginRequest(
    string Email,
    string Password,
    DeviceRegistrationRequest Device);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ForgotPasswordResponse(string Message);

public sealed record ResetPasswordRequest(
    string Email,
    string Otp,
    string NewPassword);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record LogoutRequest(
    string? RefreshToken,
    bool RevokeAllSessions = false);

public sealed record AuthTokenResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    Guid SessionId,
    Guid DeviceId,
    UserProfileResponse User);

public sealed record UserProfileResponse(
    string UserId,
    string Email,
    string? DisplayName,
    string AccountStatus,
    IReadOnlyCollection<string> Roles);
