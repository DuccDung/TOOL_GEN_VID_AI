using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class LipSyncOptionsTests
{
    [Fact]
    public void IsValid_AllowsDisabledSafeDefault()
    {
        Assert.True(LipSyncOptions.IsValid(new LipSyncOptions()));
    }

    [Theory]
    [InlineData("http://video.example.com/")]
    [InlineData("https://video.example.com:8443/")]
    [InlineData("https://localhost/")]
    [InlineData("https://video.example.com/base/")]
    [InlineData("https://user:pass@video.example.com/")]
    [InlineData("https://video.example.com/?secret=value")]
    public void IsValid_RejectsUnsafeOrAmbiguousPublicBaseUrl(string publicBaseUrl)
    {
        Assert.False(LipSyncOptions.IsValid(CreateEnabled(publicBaseUrl)));
    }

    [Fact]
    public void IsValid_AcceptsPublicHttpsPort443RootUrl()
    {
        Assert.True(LipSyncOptions.IsValid(CreateEnabled("https://video.example.com/")));
    }

    [Fact]
    public void StartupValidation_AllowsAdminToConfigureEnabledFeatureAfterStartup()
    {
        var options = CreateEnabled(string.Empty);

        Assert.True(LipSyncOptions.IsStartupConfigurationValid(options));
        Assert.False(LipSyncOptions.IsValid(options));
    }

    [Fact]
    public void TryNormalizePublicBaseUrl_AddsRootSlash()
    {
        var valid = LipSyncOptions.TryNormalizePublicBaseUrl(
            " https://video.example.com ",
            out var normalized);

        Assert.True(valid);
        Assert.Equal("https://video.example.com/", normalized);
    }

    private static LipSyncOptions CreateEnabled(string publicBaseUrl) =>
        new()
        {
            Enabled = true,
            PublicBaseUrl = publicBaseUrl
        };
}
