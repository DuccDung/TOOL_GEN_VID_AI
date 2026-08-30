namespace TOOL_LOCAL.Providers;

public sealed record ProviderSettingsResponse(
    bool OpenAiConfigured,
    string? OpenAiKeyHint,
    string OpenAiModel,
    bool VideoConfigured,
    string? VideoProviderCode,
    string VideoModel);
