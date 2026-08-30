namespace TOOL_SERVER.Generation;

internal sealed class OpenAiSpeechOptions
{
    public const string SectionName = "Generation:OpenAiSpeech";

    public int RetentionHours { get; set; } = 24;

    public int MaximumBytes { get; set; } = 50 * 1024 * 1024;

    public int MaximumInputCharacters { get; set; } = 4_096;

    public decimal MinimumSpeakingRate { get; set; } = 0.5m;

    public decimal MaximumSpeakingRate { get; set; } = 2m;

    public decimal EstimatedCharactersPerSecond { get; set; } = 12m;

    public long EstimatedOutputTokensPerSecond { get; set; } = 50;

    public Dictionary<string, string> VoiceAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["female-sweet"] = "shimmer",
        ["male-warm"] = "onyx"
    };

    public Dictionary<string, string> InstructionsByLanguage { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vi-VN"] = "Speak natural Vietnamese clearly, warmly, and at an educational narration pace.",
        ["en-US"] = "Speak clearly and warmly at an educational narration pace."
    };

    public string ResolveProviderVoice(string voiceCode)
    {
        if (string.IsNullOrWhiteSpace(voiceCode) || !VoiceAliases.TryGetValue(voiceCode, out var providerVoice) ||
            string.IsNullOrWhiteSpace(providerVoice))
        {
            throw new ArgumentException("Giọng đọc của project chưa được cấu hình hợp lệ.");
        }
        return providerVoice.Trim();
    }

    public string ResolveInstructions(string languageCode) =>
        InstructionsByLanguage.TryGetValue(languageCode, out var instructions) && !string.IsNullOrWhiteSpace(instructions)
            ? instructions.Trim()
            : "Speak clearly and naturally.";

    public void Validate()
    {
        if (RetentionHours is < 1 or > 168)
        {
            throw new InvalidOperationException("Generation:OpenAiSpeech:RetentionHours phải nằm trong khoảng 1-168 giờ.");
        }
        if (MaximumBytes is < 1024 or > 50 * 1024 * 1024)
        {
            throw new InvalidOperationException("Generation:OpenAiSpeech:MaximumBytes phải nằm trong khoảng 1 KB-50 MB.");
        }
        if (MaximumInputCharacters is < 1 or > 20_000)
        {
            throw new InvalidOperationException("Generation:OpenAiSpeech:MaximumInputCharacters không hợp lệ.");
        }
        if (MinimumSpeakingRate <= 0 || MaximumSpeakingRate < MinimumSpeakingRate || MaximumSpeakingRate > 4m)
        {
            throw new InvalidOperationException("Khoảng tốc độ OpenAI Speech không hợp lệ.");
        }
        if (EstimatedCharactersPerSecond <= 0 || EstimatedOutputTokensPerSecond <= 0)
        {
            throw new InvalidOperationException("Chính sách ước tính usage OpenAI Speech không hợp lệ.");
        }
        if (VoiceAliases.Count == 0 || VoiceAliases.Any(x => string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Value)))
        {
            throw new InvalidOperationException("Generation:OpenAiSpeech:VoiceAliases chưa được cấu hình hợp lệ.");
        }
    }
}
