using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;

namespace TOOL_TESTS.Authentication;

public sealed class PasswordResetConfigurationTests
{
    [Fact]
    public void PasswordResetOptions_RejectsUnsafeOtpLimits()
    {
        Assert.False(PasswordResetOptions.IsValid(new PasswordResetOptions
        {
            OtpLifetimeMinutes = 60,
            MaxFailedAttempts = 5
        }));
        Assert.False(PasswordResetOptions.IsValid(new PasswordResetOptions
        {
            OtpLifetimeMinutes = 10,
            MaxFailedAttempts = 20
        }));
    }

    [Fact]
    public void SmtpOptions_AcceptsGmailStartTlsShape()
    {
        var options = CreateGmailOptions();

        Assert.True(options.IsConfigured);
        Assert.True(SmtpOptions.IsValidOrDisabled(options));
        Assert.Equal("sender@gmail.com", options.EffectiveFromAddress);
    }

    [Fact]
    public void SmtpOptions_RequiresStartTlsWhenPasswordIsConfigured()
    {
        var options = CreateGmailOptions(useStartTls: false);

        Assert.False(options.IsConfigured);
        Assert.False(SmtpOptions.IsValidOrDisabled(options));
    }

    [Fact]
    public void OtpGenerator_CreatesSixDigitsAndUsesFixedHashComparison()
    {
        var otp = PasswordResetOtpGenerator.Generate();
        var hash = PasswordResetOtpGenerator.Hash(otp);

        Assert.Equal(6, otp.Length);
        Assert.All(otp, character => Assert.InRange(character, '0', '9'));
        Assert.True(PasswordResetOtpGenerator.Matches(otp, hash));
        Assert.False(PasswordResetOtpGenerator.Matches("000000", hash));
    }

    private static SmtpOptions CreateGmailOptions(bool useStartTls = true) =>
        new()
        {
            Host = "smtp.gmail.com",
            Port = 587,
            UseStartTls = useStartTls,
            User = "sender@gmail.com",
            Pass = "app-password",
            TimeoutSeconds = 30,
            FromName = "VideoMaker"
        };
}
