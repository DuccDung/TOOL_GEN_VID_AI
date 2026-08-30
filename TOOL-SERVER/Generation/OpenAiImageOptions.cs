namespace TOOL_SERVER.Generation;

internal sealed class OpenAiImageOptions
{
    public const string SectionName = "Generation:OpenAiImage";

    public string Quality { get; set; } = "medium";

    public int RetentionHours { get; set; } = 24;

    public int MaximumBytes { get; set; } = 10 * 1024 * 1024;

    public long EstimatedInputTokens { get; set; } = 1_000;

    public long EstimatedOutputTokens { get; set; } = 16_000;

    public void Validate()
    {
        if (Quality is not ("low" or "medium" or "high"))
        {
            throw new InvalidOperationException("Generation:OpenAiImage:Quality chỉ nhận low, medium hoặc high.");
        }
        if (RetentionHours is < 1 or > 168)
        {
            throw new InvalidOperationException("Generation:OpenAiImage:RetentionHours phải nằm trong khoảng 1-168 giờ.");
        }
        if (MaximumBytes is < 1024 or > 10 * 1024 * 1024)
        {
            throw new InvalidOperationException("Generation:OpenAiImage:MaximumBytes phải nằm trong khoảng 1 KB-10 MB.");
        }
        if (EstimatedInputTokens <= 0 || EstimatedOutputTokens <= 0)
        {
            throw new InvalidOperationException("Ước tính token cho GPT-Image-2 phải lớn hơn 0.");
        }
    }
}
