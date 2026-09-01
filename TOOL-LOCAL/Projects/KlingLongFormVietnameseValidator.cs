using System.Globalization;
using System.Text;

namespace TOOL_LOCAL.Projects;

internal static class KlingLongFormVietnameseValidator
{
    public const string OpenAiStructuredPlan = "OpenAiStructuredPlan";
    public const string EffectiveLanguageCode = "vi-VN";
    public const string PolicyVersion = "kling-long-form-vietnamese-v1";
    public const string FalPolicyVersion = "fal-veo-long-form-vietnamese-v1";

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

    public static bool RequiresVietnamese(string? providerCode, string? structureType) =>
        (string.Equals(providerCode, "kling", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(providerCode, "fal", StringComparison.OrdinalIgnoreCase)) &&
        string.Equals(structureType, OpenAiStructuredPlan, StringComparison.Ordinal);

    public static string ResolvePolicyVersion(string? providerCode) =>
        string.Equals(providerCode, "fal", StringComparison.OrdinalIgnoreCase)
            ? FalPolicyVersion
            : PolicyVersion;

    public static bool ContainsHighConfidenceVietnamese(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Normalize(NormalizationForm.FormC);
        var hasVietnameseLetters = normalized.Any(VietnameseLetters.Contains);
        var tokens = Tokenize(normalized);
        var markerCount = tokens.Count(VietnameseMarkers.Contains);
        if (hasVietnameseLetters)
        {
            return true;
        }

        return markerCount >= 2 && markerCount * 2 >= Math.Min(tokens.Count, 10);
    }

    public static bool HasNonVietnameseContent(IEnumerable<string?> values) =>
        values.Any(value => !string.IsNullOrWhiteSpace(value) && !ContainsHighConfidenceVietnamese(value));

    public static void RequireVietnamese(IEnumerable<string?> values, string message)
    {
        if (HasNonVietnameseContent(values))
        {
            throw new ArgumentException(message);
        }
    }

    private static IReadOnlyList<string> Tokenize(string value)
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
