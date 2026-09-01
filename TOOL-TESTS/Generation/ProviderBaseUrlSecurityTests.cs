using TOOL_SERVER.Generation;

namespace TOOL_TESTS.Generation;

public sealed class ProviderBaseUrlSecurityTests
{
    [Theory]
    [InlineData("openai", "https://api.openai.com/v1/", true)]
    [InlineData("kling", "https://api-singapore.klingai.com/", true)]
    [InlineData("byteplus", "https://ark.ap-southeast.bytepluses.com/api/v3/", true)]
    [InlineData("fal", "https://queue.fal.run/", true)]
    [InlineData("openai", "http://api.openai.com/v1/", false)]
    [InlineData("openai", "https://api.openai.com.evil.example/v1/", false)]
    [InlineData("kling", "https://127.0.0.1/", false)]
    [InlineData("byteplus", "https://ark.ap-southeast.bytepluses.com.evil.example/api/v3/", false)]
    [InlineData("fal", "https://queue.fal.run.evil.example/", false)]
    [InlineData("fal", "https://api.fal.ai/v1/", false)]
    [InlineData("unknown", "https://api.openai.com/v1/", false)]
    public void RuntimeProviderBaseUrl_UsesFixedHttpsAllowlist(string providerCode, string url, bool allowed)
    {
        Assert.Equal(allowed, ProviderRuntimeResolver.IsAllowedBaseUri(providerCode, new Uri(url)));
    }
}
