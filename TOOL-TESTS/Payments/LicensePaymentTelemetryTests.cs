using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using TOOL_SERVER.Payments;

namespace TOOL_TESTS.Payments;

public sealed class LicensePaymentTelemetryTests
{
    [Fact]
    public void LifecycleEvents_ArePublishedWithoutHighCardinalityIdentifiers()
    {
        var measurements = new ConcurrentQueue<Measurement>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, currentListener) =>
        {
            if (instrument.Meter.Name == LicensePaymentTelemetry.MeterName &&
                instrument.Name == LicensePaymentTelemetry.EventCounterName)
            {
                currentListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var values = tags.ToArray().ToDictionary(x => x.Key, x => x.Value?.ToString());
            measurements.Enqueue(new Measurement(
                value,
                values.GetValueOrDefault("event"),
                values.GetValueOrDefault("reason")));
        });
        listener.Start();
        var telemetry = new LicensePaymentTelemetry();

        telemetry.RecordCreated();
        telemetry.RecordFulfilled();
        telemetry.RecordExpired();
        telemetry.RecordDuplicateWebhook();
        telemetry.RecordUnmatchedWebhook(LicensePaymentWebhookMismatchReason.PaymentDetails);

        Assert.Contains(measurements, x => x.Value == 1 && x.Event == "created" && x.Reason is null);
        Assert.Contains(measurements, x => x.Value == 1 && x.Event == "fulfilled" && x.Reason is null);
        Assert.Contains(measurements, x => x.Value == 1 && x.Event == "expired" && x.Reason is null);
        Assert.Contains(measurements, x => x.Value == 1 && x.Event == "duplicate" && x.Reason is null);
        Assert.Contains(measurements, x => x.Value == 1 && x.Event == "unmatched" && x.Reason == "payment_details");
        Assert.All(measurements, measurement =>
        {
            Assert.DoesNotContain("user", measurement.Event ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("order", measurement.Event ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("payment_id", measurement.Event ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        });
    }

    private sealed record Measurement(long Value, string? Event, string? Reason);
}
