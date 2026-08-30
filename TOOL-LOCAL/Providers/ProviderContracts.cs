namespace TOOL_LOCAL.Providers;

public sealed record VideoGenerationRequest(
    string Prompt,
    string? NegativePrompt,
    int Width,
    int Height,
    int DurationSeconds,
    decimal FramesPerSecond,
    IReadOnlyCollection<string>? ReferenceImagePaths,
    string? FirstFramePath,
    string? LastFramePath,
    int? Seed,
    string? IdempotencyKey = null);

public sealed record VideoProviderCapabilities(
    IReadOnlyCollection<int> SupportedDurationsSeconds,
    int MaximumWidth,
    int MaximumHeight,
    bool SupportsReferenceImages,
    bool SupportsFirstFrame,
    bool SupportsLastFrame,
    bool SupportsSeed,
    bool SupportsNegativePrompt);
