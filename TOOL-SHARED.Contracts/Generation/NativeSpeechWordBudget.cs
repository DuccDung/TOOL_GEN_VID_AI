namespace TOOL_SHARED.Contracts.Generation;

/// <summary>
/// Quy ước giới hạn lời nói đồng bộ theo thời lượng một cảnh video native audio.
/// Đây là contract dùng chung giữa gateway và desktop; server vẫn là nơi thực thi
/// quyết định cuối cùng trước khi gọi provider.
/// </summary>
public static class NativeSpeechWordBudget
{
    public static int CountWords(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? 0
            : value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    public static int MaximumWordsForDurationSeconds(int durationSeconds) => durationSeconds switch
    {
        <= 5 => 8,
        <= 10 => 18,
        <= 15 => 28,
        _ => (int)Math.Ceiling(durationSeconds * 1.9m)
    };
}
