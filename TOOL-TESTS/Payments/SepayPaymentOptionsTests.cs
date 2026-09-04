using TOOL_SERVER.Configuration;

namespace TOOL_TESTS.Payments;

public sealed class SepayPaymentOptionsTests
{
    [Fact]
    public void DisabledConfiguration_IsValidWithoutSecrets()
    {
        Assert.True(SepayPaymentOptions.IsValidOrDisabled(new SepayPaymentOptions()));
        Assert.False(new SepayPaymentOptions().CanProcessWebhooks);
    }

    [Fact]
    public void EnabledConfiguration_IsValidWithoutWebhookApiKey()
    {
        var options = ReadyOptions();

        Assert.True(SepayPaymentOptions.IsValidOrDisabled(options));
        Assert.True(options.IsReady);
    }

    [Fact]
    public void EnabledConfiguration_RejectsUntrustedQrHost()
    {
        var source = ReadyOptions();
        var options = new SepayPaymentOptions
        {
            Enabled = source.Enabled,
            QrBaseUrl = "https://attacker.example/img",
            ReceiverBankCode = source.ReceiverBankCode,
            ReceiverAccountNumber = source.ReceiverAccountNumber,
            ReceiverAccountName = source.ReceiverAccountName,
            TransferCodePrefix = source.TransferCodePrefix,
            PaymentExpireMinutes = source.PaymentExpireMinutes
        };

        Assert.False(SepayPaymentOptions.IsValidOrDisabled(options));
    }

    [Theory]
    [InlineData("https://qr.sepay.vn:444/img")]
    [InlineData("https://user@qr.sepay.vn/img")]
    [InlineData("https://qr.sepay.vn/img?template=compact")]
    [InlineData("https://qr.sepay.vn/img#fragment")]
    public void EnabledConfiguration_RejectsAmbiguousQrBaseUrl(string qrBaseUrl)
    {
        var source = ReadyOptions();
        var options = new SepayPaymentOptions
        {
            Enabled = source.Enabled,
            QrBaseUrl = qrBaseUrl,
            ReceiverBankCode = source.ReceiverBankCode,
            ReceiverAccountNumber = source.ReceiverAccountNumber,
            ReceiverAccountName = source.ReceiverAccountName,
            TransferCodePrefix = source.TransferCodePrefix,
            PaymentExpireMinutes = source.PaymentExpireMinutes
        };

        Assert.False(SepayPaymentOptions.IsValidOrDisabled(options));
    }

    [Fact]
    public void EnabledConfiguration_AcceptsSepayQrEndpoint()
    {
        var options = ReadyOptions();

        Assert.True(SepayPaymentOptions.IsValidOrDisabled(options));
        Assert.True(options.IsReady);
    }

    [Fact]
    public void DisabledCreation_WithValidConfigurationCanStillProcessExistingWebhooks()
    {
        var source = ReadyOptions();
        var options = new SepayPaymentOptions
        {
            Enabled = false,
            QrBaseUrl = source.QrBaseUrl,
            ReceiverBankCode = source.ReceiverBankCode,
            ReceiverAccountNumber = source.ReceiverAccountNumber,
            ReceiverAccountName = source.ReceiverAccountName,
            TransferCodePrefix = source.TransferCodePrefix,
            PaymentExpireMinutes = source.PaymentExpireMinutes
        };

        Assert.False(options.IsReady);
        Assert.True(options.CanProcessWebhooks);
    }

    private static SepayPaymentOptions ReadyOptions() => new()
    {
        Enabled = true,
        QrBaseUrl = "https://qr.sepay.vn/img",
        ReceiverBankCode = "TPBANK",
        ReceiverAccountNumber = "123456789",
        ReceiverAccountName = "VIDEO MAKER TEST",
        TransferCodePrefix = "VM",
        PaymentExpireMinutes = 15
    };
}
