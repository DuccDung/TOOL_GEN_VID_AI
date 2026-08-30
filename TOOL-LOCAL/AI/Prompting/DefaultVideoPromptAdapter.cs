using TOOL_LOCAL.AI.Contracts;
using TOOL_LOCAL.Providers;

namespace TOOL_LOCAL.AI.Prompting;

public interface IVideoPromptAdapter
{
    string ProviderCode { get; }

    VideoGenerationRequest Adapt(
        CanonicalVideoPromptContract prompt,
        VideoProviderCapabilities capabilities,
        string? firstFramePath,
        string? lastFramePath,
        string? idempotencyKey);
}

public sealed class DefaultVideoPromptAdapter(string providerCode) : IVideoPromptAdapter
{
    public string ProviderCode { get; } = providerCode;

    public VideoGenerationRequest Adapt(
        CanonicalVideoPromptContract prompt,
        VideoProviderCapabilities capabilities,
        string? firstFramePath,
        string? lastFramePath,
        string? idempotencyKey)
    {
        var duration = capabilities.SupportedDurationsSeconds
            .Where(x => x >= prompt.DurationSeconds)
            .DefaultIfEmpty(capabilities.SupportedDurationsSeconds.Max())
            .Min();

        return new VideoGenerationRequest(
            $"{prompt.PositivePrompt} {prompt.ContinuityInstruction}",
            capabilities.SupportsNegativePrompt ? prompt.NegativePrompt : null,
            Math.Min(prompt.Width, capabilities.MaximumWidth),
            Math.Min(prompt.Height, capabilities.MaximumHeight),
            duration,
            prompt.FramesPerSecond,
            capabilities.SupportsReferenceImages ? prompt.ReferenceImagePaths : null,
            capabilities.SupportsFirstFrame ? firstFramePath : null,
            capabilities.SupportsLastFrame ? lastFramePath : null,
            null,
            idempotencyKey);
    }
}
