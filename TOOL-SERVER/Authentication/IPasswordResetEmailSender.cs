namespace TOOL_SERVER.Authentication;

public interface IPasswordResetEmailSender
{
    bool IsConfigured { get; }

    Task SendAsync(
        string recipientEmail,
        string? displayName,
        string otp,
        TimeSpan otpLifetime,
        CancellationToken cancellationToken);
}
