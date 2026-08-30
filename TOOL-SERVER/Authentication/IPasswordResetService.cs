using TOOL_SHARED.Contracts.Authentication;

namespace TOOL_SERVER.Authentication;

public interface IPasswordResetService
{
    Task RequestAsync(
        ForgotPasswordRequest request,
        ClientRequestContext client,
        CancellationToken cancellationToken);

    Task ResetAsync(
        ResetPasswordRequest request,
        ClientRequestContext client,
        CancellationToken cancellationToken);
}
