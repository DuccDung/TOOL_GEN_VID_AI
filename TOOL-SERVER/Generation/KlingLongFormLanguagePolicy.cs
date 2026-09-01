using System.Globalization;
using System.Text;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_SERVER.Generation;

internal static class GenerationWorkflowTypes
{
    public const string OpenAiStructuredPlan = "OpenAiStructuredPlan";
    public const string DirectShortVideo = "DirectShortVideo";
}

internal static class KlingLongFormLanguagePolicy
{
    public const string VietnameseLanguageCode = "vi-VN";
    public const string PolicyVersion = "kling-long-form-vietnamese-v1";

    public static bool RequiresVietnamese(string? providerCode, string? structureType) =>
        string.Equals(providerCode, ProviderCodes.Kling, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(structureType, GenerationWorkflowTypes.OpenAiStructuredPlan, StringComparison.Ordinal);

    public static string Resolve(
        string? providerCode,
        string projectLanguageCode,
        string? structureType) =>
        RequiresVietnamese(providerCode, structureType)
            ? VietnameseLanguageCode
            : projectLanguageCode;
}

internal static class KlingVietnameseContentValidator
{
    private static readonly HashSet<char> VietnameseLetters = new(
        "ăâđêôơưĂÂĐÊÔƠƯ" +
        "áàảãạấầẩẫậắằẳẵặéèẻẽẹếềểễệíìỉĩịóòỏõọốồổỗộớờởỡợúùủũụứừửữựýỳỷỹỵ" +
        "ÁÀẢÃẠẤẦẨẪẬẮẰẲẴẶÉÈẺẼẸẾỀỂỄỆÍÌỈĨỊÓÒỎÕỌỐỒỔỖỘỚỜỞỠỢÚÙỦŨỤỨỪỬỮỰÝỲỶỸỴ");

    private static readonly HashSet<string> VietnameseMarkers = new(StringComparer.Ordinal)
    {
        "ai", "anh", "am", "ban", "bep", "bo", "buoc", "cac", "cach", "canh", "chao",
        "chay", "cho", "chung", "co", "cua", "dai", "dang", "den", "di", "duoc", "giua",
        "gai", "gio", "hinh", "hon", "khong", "khi", "la", "lai", "lam", "loi", "mot",
        "moi", "nay", "nen", "nguoi", "nhan", "nhin", "noi", "o", "phong", "quay", "sang",
        "sau", "tao", "thanh", "theo", "tieng", "toi", "tren", "trong", "tu", "va", "vat",
        "voi", "xin", "ruc", "ro", "sac", "mau", "mua", "he"
    };

    public static IReadOnlyList<string> FindPlanViolations(GeneratedContentPlan plan)
    {
        var fields = new List<(string Field, string? Value, bool Required)>
        {
            ("title", plan.Title, true),
            ("hook", plan.Hook, true),
            ("angle", plan.Angle, true),
            ("audience", plan.Audience, true),
            ("call_to_action", plan.CallToAction, true),
            ("script_full_text", plan.ScriptFullText, true),
            ("visual_style", plan.VisualStyle, true),
            ("negative_prompt", plan.NegativePrompt, true)
        };

        for (var index = 0; index < plan.Characters.Count; index++)
        {
            var prefix = $"characters[{index}]";
            var character = plan.Characters[index];
            fields.AddRange([
                // A character name may remain a proper noun such as Maya.
                ($"{prefix}.name", character.Name, true),
                ($"{prefix}.role", character.Role, true),
                ($"{prefix}.gender", character.Gender, true),
                ($"{prefix}.face", character.Face, true),
                ($"{prefix}.hair", character.Hair, true),
                ($"{prefix}.skin", character.Skin, true),
                ($"{prefix}.body", character.Body, true),
                ($"{prefix}.clothing", character.Clothing, true),
                ($"{prefix}.accessories", character.Accessories, true),
                ($"{prefix}.visual_identity", character.VisualIdentity, true)
            ]);
            fields.AddRange(character.ImmutableTraits.Select((value, itemIndex) =>
                ($"{prefix}.immutable_traits[{itemIndex}]", (string?)value, true)));
            fields.AddRange(character.ForbiddenChanges.Select((value, itemIndex) =>
                ($"{prefix}.forbidden_changes[{itemIndex}]", (string?)value, true)));
        }

        var assets = plan.Assets ?? [];
        for (var index = 0; index < assets.Count; index++)
        {
            fields.Add(($"assets[{index}].name", assets[index].Name, true));
            fields.Add(($"assets[{index}].canonical_description", assets[index].CanonicalDescription, true));
        }

        for (var index = 0; index < plan.Scenes.Count; index++)
        {
            var prefix = $"scenes[{index}]";
            var scene = plan.Scenes[index];
            var speechRequired = !string.Equals(scene.SpeechMode, KlingSpeechModes.None, StringComparison.Ordinal);
            fields.AddRange([
                ($"{prefix}.story_purpose", scene.StoryPurpose, true),
                ($"{prefix}.spoken_text", scene.Narration, speechRequired),
                ($"{prefix}.visual_prompt", scene.VisualPrompt, true),
                ($"{prefix}.voice_style", scene.VoiceStyle, true),
                ($"{prefix}.ambient_audio", scene.AmbientAudio, true),
                ($"{prefix}.sound_effects", scene.SoundEffects, true)
            ]);
        }

        return FindViolations(fields);
    }

    public static IReadOnlyList<string> FindViolations(
        IEnumerable<(string Field, string? Value, bool Required)> fields)
    {
        var violations = new List<string>();
        foreach (var (field, value, required) in fields)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (required)
                {
                    violations.Add(field);
                }
                continue;
            }

            if (RequiresVietnameseText(field) && !ContainsHighConfidenceVietnamese(value))
            {
                violations.Add(field);
            }
        }

        return violations.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static bool ContainsHighConfidenceVietnamese(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Normalize(NormalizationForm.FormC);
        var hasVietnameseLetters = normalized.Any(VietnameseLetters.Contains);
        var tokens = TokenizeForDetection(normalized);
        if (tokens.Count == 0)
        {
            return false;
        }

        var markerCount = tokens.Count(VietnameseMarkers.Contains);
        if (hasVietnameseLetters)
        {
            return true;
        }

        // Accept Vietnamese prose typed without diacritics while avoiding one-off
        // English words that happen to overlap a Vietnamese token.
        return markerCount >= 2 && markerCount * 2 >= Math.Min(tokens.Count, 10);
    }

    private static bool RequiresVietnameseText(string field) =>
        !(field.EndsWith(".name", StringComparison.Ordinal) &&
          (field.StartsWith("character.", StringComparison.Ordinal) ||
           field.StartsWith("characters[", StringComparison.Ordinal)));

    private static IReadOnlyList<string> TokenizeForDetection(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetter(character))
            {
                builder.Append(character is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(' ');
            }
        }

        return builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

}
