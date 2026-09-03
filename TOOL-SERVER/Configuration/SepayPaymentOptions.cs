using System.Text.RegularExpressions;

namespace TOOL_SERVER.Configuration;

public sealed partial class SepayPaymentOptions
{
    public const string SectionName = "Payments:Sepay";

    public bool Enabled { get; init; }

    public string QrBaseUrl { get; init; } = "https://qr.sepay.vn/img";

    public string ReceiverBankCode { get; init; } = string.Empty;

    public string ReceiverAccountNumber { get; init; } = string.Empty;

    public string ReceiverAccountName { get; init; } = string.Empty;

    public string TransferCodePrefix { get; init; } = "VM";

    public int PaymentExpireMinutes { get; init; } = 15;

    public bool IsReady => Enabled && IsValid(this);

    public bool CanProcessWebhooks => IsValid(this);

    public static bool IsValidOrDisabled(SepayPaymentOptions options) =>
        !options.Enabled || IsValid(options);

    private static bool IsValid(SepayPaymentOptions options) =>
        Uri.TryCreate(options.QrBaseUrl, UriKind.Absolute, out var qrUri) &&
        qrUri.Scheme == Uri.UriSchemeHttps &&
        qrUri.IsDefaultPort &&
        string.IsNullOrEmpty(qrUri.UserInfo) &&
        string.IsNullOrEmpty(qrUri.Query) &&
        string.IsNullOrEmpty(qrUri.Fragment) &&
        IsAllowedQrHost(qrUri.Host) &&
        BankCodeRegex().IsMatch(options.ReceiverBankCode.Trim()) &&
        AccountNumberRegex().IsMatch(options.ReceiverAccountNumber.Trim()) &&
        !string.IsNullOrWhiteSpace(options.ReceiverAccountName) &&
        options.ReceiverAccountName.Trim().Length <= 200 &&
        PrefixRegex().IsMatch(options.TransferCodePrefix.Trim()) &&
        options.PaymentExpireMinutes is >= 5 and <= 60;

    private static bool IsAllowedQrHost(string host) =>
        host.Equals("vietqr.app", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("qr.sepay.vn", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Za-z0-9_-]{2,50}$", RegexOptions.CultureInvariant)]
    private static partial Regex BankCodeRegex();

    [GeneratedRegex("^[A-Za-z0-9]{4,50}$", RegexOptions.CultureInvariant)]
    private static partial Regex AccountNumberRegex();

    [GeneratedRegex("^[A-Za-z0-9]{2,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixRegex();
}
