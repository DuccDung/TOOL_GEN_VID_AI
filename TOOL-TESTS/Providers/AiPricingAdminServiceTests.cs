using TOOL_SERVER.Providers;

namespace TOOL_TESTS.Providers;

public sealed class AiPricingAdminServiceTests
{
    [Fact]
    public void EffectiveFrom_DefaultsToCurrentUtcTime()
    {
        var now = new DateTime(2026, 8, 26, 4, 0, 0, DateTimeKind.Utc);

        Assert.Equal(now, AiPricingAdminService.NormalizeEffectiveFrom(null, now));
    }

    [Fact]
    public void EffectiveFrom_RejectsFutureRateWithoutScheduler()
    {
        var now = new DateTime(2026, 8, 26, 4, 0, 0, DateTimeKind.Utc);

        Assert.Throws<ArgumentException>(() =>
            AiPricingAdminService.NormalizeEffectiveFrom(now.AddSeconds(1), now));
    }
}
