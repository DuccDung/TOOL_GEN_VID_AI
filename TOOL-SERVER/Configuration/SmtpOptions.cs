using System.Net.Mail;

namespace TOOL_SERVER.Configuration;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 587;

    public bool UseStartTls { get; init; } = true;

    public string User { get; init; } = string.Empty;

    public string Pass { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 30;

    public string? FromAddress { get; init; }

    public string FromName { get; init; } = "VideoMaker";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Host) &&
        Port is > 0 and <= 65535 &&
        UseStartTls &&
        !string.IsNullOrWhiteSpace(User) &&
        !string.IsNullOrWhiteSpace(Pass) &&
        TimeoutSeconds is >= 5 and <= 120 &&
        MailAddress.TryCreate(EffectiveFromAddress, out _);

    public string EffectiveFromAddress =>
        string.IsNullOrWhiteSpace(FromAddress) ? User.Trim() : FromAddress.Trim();

    public static bool IsValidOrDisabled(SmtpOptions options) =>
        string.IsNullOrWhiteSpace(options.Pass) || options.IsConfigured;
}
