namespace TOOL_SHARED.Contracts.TikTok;

public static class TikTokAvatarValidation
{
    public const int MaximumBytes = 1024 * 1024;
    public static bool IsValid(ReadOnlySpan<byte> bytes, string? mimeType) => bytes.Length is > 12 and <= MaximumBytes && mimeType switch
    {
        "image/jpeg" => bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
        "image/png" => bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        "image/webp" => bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8),
        _ => false
    };
}
