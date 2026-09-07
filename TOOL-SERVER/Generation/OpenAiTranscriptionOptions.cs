namespace TOOL_SERVER.Generation;

internal sealed class OpenAiTranscriptionOptions
{
    public const string SectionName = "Generation:OpenAiTranscription";

    public string ModelCode { get; set; } = "whisper-1";

    public int MaximumBytes { get; set; } = 25 * 1024 * 1024;

    public int MaximumDurationSeconds { get; set; } = 120;

    public decimal PassedWordErrorRate { get; set; } = 0.10m;

    public decimal PassedCharacterErrorRate { get; set; } = 0.08m;

    public decimal PassedRequiredTermRecall { get; set; } = 0.95m;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ModelCode) || ModelCode.Length > 200)
        {
            throw new InvalidOperationException("Generation:OpenAiTranscription:ModelCode không hợp lệ.");
        }
        if (MaximumBytes is < 1024 or > 25 * 1024 * 1024)
        {
            throw new InvalidOperationException("Generation:OpenAiTranscription:MaximumBytes phải nằm trong khoảng 1 KB-25 MB.");
        }
        if (MaximumDurationSeconds is < 1 or > 900)
        {
            throw new InvalidOperationException("Generation:OpenAiTranscription:MaximumDurationSeconds phải nằm trong khoảng 1-900 giây.");
        }
        if (PassedWordErrorRate is < 0 or > 1 ||
            PassedCharacterErrorRate is < 0 or > 1 ||
            PassedRequiredTermRecall is < 0 or > 1)
        {
            throw new InvalidOperationException("Ngưỡng speech verification phải nằm trong khoảng 0-1.");
        }
    }
}
