namespace TOOL_LOCAL.Vietsub.Ocr;

internal sealed record VietsubOcrRuntimeStatus(
    bool Ready,
    string? ErrorCode,
    string Message,
    IReadOnlyList<string> AvailableLanguages);

internal sealed record VietsubOcrRecognitionResult(string Text, float Confidence);

internal sealed record VietsubOcrRecognizerDiagnostics(
    long DirectRecognitionFrames,
    long FullDetectionFrames);

internal interface IVietsubOcrRecognizer : IAsyncDisposable
{
    Task<VietsubOcrRuntimeStatus> GetRuntimeStatusAsync(CancellationToken cancellationToken);

    Task<VietsubOcrRecognitionResult> RecognizeAsync(
        VietsubRawVideoFrame frame,
        string languageCode,
        CancellationToken cancellationToken);

    VietsubOcrRecognizerDiagnostics GetDiagnostics() => new(0, 0);
}

internal sealed class UnavailableVietsubOcrRecognizer(
    string errorCode = VietsubOcrErrorCodes.RuntimeNotInstalled,
    string message = "Component OCR local chưa được cài đặt hoặc chưa qua package gate.") : IVietsubOcrRecognizer
{
    public Task<VietsubOcrRuntimeStatus> GetRuntimeStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new VietsubOcrRuntimeStatus(
            false,
            errorCode,
            message,
            []));

    public Task<VietsubOcrRecognitionResult> RecognizeAsync(
        VietsubRawVideoFrame frame,
        string languageCode,
        CancellationToken cancellationToken) =>
        throw new VietsubOcrException(
            errorCode,
            message);

    public VietsubOcrRecognizerDiagnostics GetDiagnostics() => new(0, 0);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
