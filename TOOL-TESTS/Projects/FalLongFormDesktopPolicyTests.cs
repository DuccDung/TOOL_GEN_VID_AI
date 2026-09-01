using TOOL_LOCAL.Projects;

namespace TOOL_TESTS.Projects;

public sealed class FalLongFormDesktopPolicyTests
{
    [Theory]
    [InlineData("fal", "OpenAiStructuredPlan", true)]
    [InlineData("fal", "DirectShortVideo", false)]
    [InlineData("kling", "OpenAiStructuredPlan", true)]
    [InlineData("byteplus", "OpenAiStructuredPlan", false)]
    public void VietnamesePolicy_IsScopedToSupportedLongFormProviders(
        string providerCode,
        string structureType,
        bool expected)
    {
        Assert.Equal(
            expected,
            KlingLongFormVietnameseValidator.RequiresVietnamese(providerCode, structureType));
    }

    [Theory]
    [InlineData("fal", "fal-veo-long-form-vietnamese-v1")]
    [InlineData("kling", "kling-long-form-vietnamese-v1")]
    public void IdempotencyPolicyVersion_RemainsProviderSpecific(string providerCode, string expected)
    {
        Assert.Equal(expected, KlingLongFormVietnameseValidator.ResolvePolicyVersion(providerCode));
    }
}
