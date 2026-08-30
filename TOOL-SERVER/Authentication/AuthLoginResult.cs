using TOOL_SHARED.Contracts.Authentication;

namespace TOOL_SERVER.Authentication;

public sealed record AuthFailure(
    int StatusCode,
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]>? Errors = null);

public sealed class AuthLoginResult
{
    private AuthLoginResult(AuthTokenResponse? response, AuthFailure? failure)
    {
        Response = response;
        Failure = failure;
    }

    public AuthTokenResponse? Response { get; }

    public AuthFailure? Failure { get; }

    public static AuthLoginResult Success(AuthTokenResponse response) =>
        new(response ?? throw new ArgumentNullException(nameof(response)), null);

    public static AuthLoginResult Rejected(AuthFailure failure) =>
        new(null, failure ?? throw new ArgumentNullException(nameof(failure)));
}
