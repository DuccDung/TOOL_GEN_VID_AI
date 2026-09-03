using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace TOOL_SERVER.Payments;

public interface ILicensePaymentTelemetry
{
    void RecordCreated();
    void RecordFulfilled();
    void RecordExpired();
    void RecordDuplicateWebhook();
    void RecordUnmatchedWebhook(LicensePaymentWebhookMismatchReason reason);
}

public enum LicensePaymentWebhookMismatchReason
{
    ReceiverAccount,
    PaymentNotFound,
    PaymentDetails
}

public sealed class LicensePaymentTelemetry : ILicensePaymentTelemetry
{
    public const string MeterName = "VideoMaker.Payments";
    public const string EventCounterName = "videomaker.license_payment.events";

    private static readonly Meter PaymentMeter = new(MeterName, "1.0.0");
    private static readonly Counter<long> PaymentEvents = PaymentMeter.CreateCounter<long>(
        EventCounterName,
        unit: "{event}",
        description: "License payment lifecycle and webhook reconciliation events.");

    public void RecordCreated() => Record("created");

    public void RecordFulfilled() => Record("fulfilled");

    public void RecordExpired() => Record("expired");

    public void RecordDuplicateWebhook() => Record("duplicate");

    public void RecordUnmatchedWebhook(LicensePaymentWebhookMismatchReason reason) => Record(
        "unmatched",
        reason switch
        {
            LicensePaymentWebhookMismatchReason.ReceiverAccount => "receiver_account",
            LicensePaymentWebhookMismatchReason.PaymentNotFound => "payment_not_found",
            LicensePaymentWebhookMismatchReason.PaymentDetails => "payment_details",
            _ => "unknown"
        });

    private static void Record(string eventName, string? reason = null)
    {
        var tags = new TagList
        {
            { "event", eventName }
        };
        if (!string.IsNullOrWhiteSpace(reason))
        {
            tags.Add("reason", reason);
        }
        PaymentEvents.Add(1, tags);
    }
}
