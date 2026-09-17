namespace TOOL_LOCAL.Vietsub.Translation;

internal static class VietsubTranslationEnginePolicies
{
    public const string NotSelected = "NOT_SELECTED";
    public const string ContextualRequired = "CONTEXTUAL_REQUIRED";
    public const string LocalBasicAllowed = "LOCAL_BASIC_ALLOWED";

    public static string Normalize(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        ContextualRequired => ContextualRequired,
        LocalBasicAllowed => LocalBasicAllowed,
        _ => NotSelected
    };
}

internal sealed class VietsubTranslationGlossarySetting
{
    public Guid EntryId { get; set; } = Guid.NewGuid();

    public string SourceText { get; set; } = string.Empty;

    public string TargetText { get; set; } = string.Empty;

    public string? Note { get; set; }
}

internal sealed class VietsubTranslationSettings
{
    public const int MaximumSummaryLength = 4_000;
    public const int MaximumCharacterInstructionsLength = 4_000;
    public const int MaximumStyleInstructionsLength = 2_000;
    public const int MaximumGlossaryEntries = 200;
    public const int MaximumGlossaryCharacters = 20_000;
    public const int MaximumGlossaryTermLength = 200;
    public const int MaximumGlossaryNoteLength = 300;

    public string SourceLanguageCode { get; set; } = string.Empty;

    public string TargetLanguageCode { get; set; } = "vi";

    public string EnginePolicy { get; set; } = VietsubTranslationEnginePolicies.NotSelected;
    public string ExecutionPolicy { get; set; } = VietsubTranslationExecutionPolicies.Auto;

    public int ContextCueCount { get; set; } = VietsubTranslationLimits.DefaultContextCueCount;

    public int SceneMaximumTargetCues { get; set; } = VietsubTranslationLimits.DefaultMaximumTargetCues;

    public int SceneGapMilliseconds { get; set; } = VietsubTranslationLimits.DefaultSceneGapMilliseconds;

    public double MaximumCharactersPerSecond { get; set; } = VietsubTranslationLimits.DefaultMaximumCharactersPerSecond;

    public string ContextSummary { get; set; } = string.Empty;

    public string CharacterInstructions { get; set; } = string.Empty;

    public string StyleInstructions { get; set; } = string.Empty;

    public List<VietsubTranslationGlossarySetting> Glossary { get; set; } = [];

    public void Normalize(string? projectSourceLanguageCode, string? projectTargetLanguageCode)
    {
        var projectSource = NormalizeOptionalSource(projectSourceLanguageCode);
        var requestedSource = NormalizeOptionalSource(SourceLanguageCode);
        if (projectSource.Length > 0 && requestedSource.Length > 0 && projectSource != requestedSource)
        {
            throw new InvalidDataException("Ngôn ngữ nguồn trong cấu hình dịch mâu thuẫn với project Vietsub.");
        }

        SourceLanguageCode = requestedSource.Length > 0 ? requestedSource : projectSource;
        if (!string.Equals(projectTargetLanguageCode?.Trim(), "vi", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(TargetLanguageCode?.Trim(), "vi", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Dịch local Vietsub chỉ hỗ trợ ngôn ngữ đích tiếng Việt.");
        }

        TargetLanguageCode = "vi";
        EnginePolicy = VietsubTranslationEnginePolicies.Normalize(EnginePolicy);
        if (ExecutionPolicy is not (VietsubTranslationExecutionPolicies.Auto or VietsubTranslationExecutionPolicies.CpuOnly))
            throw new InvalidDataException("Chế độ CPU/GPU của project không hợp lệ.");
        ContextCueCount = Math.Clamp(ContextCueCount, 0, 10);
        SceneMaximumTargetCues = Math.Clamp(SceneMaximumTargetCues, 1, 30);
        SceneGapMilliseconds = Math.Clamp(SceneGapMilliseconds, 1_000, 60_000);
        MaximumCharactersPerSecond = double.IsFinite(MaximumCharactersPerSecond)
            ? Math.Clamp(MaximumCharactersPerSecond, 8, 30)
            : VietsubTranslationLimits.DefaultMaximumCharactersPerSecond;
        ContextSummary = NormalizeText(ContextSummary, MaximumSummaryLength, "tóm tắt ngữ cảnh");
        CharacterInstructions = NormalizeText(
            CharacterInstructions,
            MaximumCharacterInstructionsLength,
            "hướng dẫn nhân vật/xưng hô");
        StyleInstructions = NormalizeText(StyleInstructions, MaximumStyleInstructionsLength, "phong cách dịch");
        Glossary ??= [];
        if (Glossary.Count > MaximumGlossaryEntries)
        {
            throw new InvalidDataException($"Glossary dịch local không được vượt quá {MaximumGlossaryEntries} mục.");
        }

        var entryIds = new HashSet<Guid>();
        var totalCharacters = 0;
        foreach (var entry in Glossary)
        {
            if (entry is null || entry.EntryId == Guid.Empty || !entryIds.Add(entry.EntryId))
            {
                throw new InvalidDataException("Glossary dịch local chứa mục thiếu hoặc trùng mã.");
            }

            entry.SourceText = NormalizeRequiredTerm(entry.SourceText, "nguồn");
            entry.TargetText = NormalizeRequiredTerm(entry.TargetText, "đích");
            entry.Note = string.IsNullOrWhiteSpace(entry.Note)
                ? null
                : NormalizeText(entry.Note, MaximumGlossaryNoteLength, "ghi chú glossary");
            totalCharacters += entry.SourceText.Length + entry.TargetText.Length + (entry.Note?.Length ?? 0);
        }

        if (totalCharacters > MaximumGlossaryCharacters)
        {
            throw new InvalidDataException(
                $"Tổng nội dung glossary dịch local không được vượt quá {MaximumGlossaryCharacters} ký tự.");
        }
    }

    private static string NormalizeOptionalSource(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "en" => "en",
        "zh" or "zh-cn" or "zh-hans" => "zh",
        null or "" or "auto" => string.Empty,
        _ => throw new InvalidDataException("Ngôn ngữ nguồn dịch local chỉ có thể là tiếng Anh hoặc tiếng Trung.")
    };

    private static string NormalizeRequiredTerm(string? value, string field)
    {
        var normalized = NormalizeText(value, MaximumGlossaryTermLength, $"thuật ngữ {field}");
        if (normalized.Length == 0)
        {
            throw new InvalidDataException($"Thuật ngữ {field} không được để trống.");
        }

        return normalized;
    }

    private static string NormalizeText(string? value, int maximumLength, string field)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (normalized.Length > maximumLength
            || normalized.Any(character => char.IsControl(character) && character is not '\n' and not '\t'))
        {
            throw new InvalidDataException($"Nội dung {field} không hợp lệ hoặc vượt quá {maximumLength} ký tự.");
        }

        return normalized;
    }
}
