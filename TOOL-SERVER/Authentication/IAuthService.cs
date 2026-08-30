using TOOL_SHARED.Contracts.Authentication;

namespace TOOL_SERVER.Authentication;

public interface IAuthService
{
    Task<AuthTokenResponse> RegisterAsync(
        RegisterRequest request,
        ClientRequestContext client,
        CancellationToken cancellationToken);

    Task<AuthLoginResult> LoginAsync(
        LoginRequest request,
        ClientRequestContext client,
        CancellationToken cancellationToken);

    Task<AuthTokenResponse> RefreshAsync(
        RefreshTokenRequest request,
        ClientRequestContext client,
        CancellationToken cancellationToken);

    Task LogoutAsync(
        string userId,
        Guid currentSessionId,
        LogoutRequest request,
        ClientRequestContext client,
        CancellationToken cancellationToken);

    Task<UserProfileResponse> GetProfileAsync(string userId, CancellationToken cancellationToken);
}
