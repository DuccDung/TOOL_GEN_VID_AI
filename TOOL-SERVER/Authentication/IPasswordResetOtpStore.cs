using TOOL_SERVER.Domain.Accounts;

namespace TOOL_SERVER.Authentication;

public interface IPasswordResetOtpStore
{
    Task SaveAsync(
        ApplicationUser user,
        string otp,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken);

    Task<PasswordResetOtpValidation> ValidateAsync(
        ApplicationUser user,
        string otp,
        int maxFailedAttempts,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task RemoveAsync(ApplicationUser user, CancellationToken cancellationToken);
}

public enum PasswordResetOtpValidation
{
    Valid,
    Invalid,
    Expired,
    AttemptsExceeded
}
