namespace TOOL_SERVER.Configuration;

public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    public int OtpLifetimeMinutes { get; init; } = 10;

    public int MaxFailedAttempts { get; init; } = 5;

    public static bool IsValid(PasswordResetOptions options) =>
        options.OtpLifetimeMinutes is >= 5 and <= 30 &&
        options.MaxFailedAttempts is >= 3 and <= 10;
}
