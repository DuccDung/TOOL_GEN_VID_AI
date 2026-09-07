using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TOOL_SERVER.Generation;

internal sealed record TranscribedWord(string Text, long StartMs, long EndMs);

internal sealed record OpenAiTranscriptionResult(
    string Transcript,
    IReadOnlyList<TranscribedWord> Words,
    string ProviderRequestId);

internal interface IOpenAiTranscriptionClient
{
    Task<OpenAiTranscriptionResult> TranscribeAsync(
        ProviderRuntimeConfiguration provider,
        byte[] wavBytes,
        string languageCode,
        CancellationToken cancellationToken);
}

internal sealed class OpenAiTranscriptionClient(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiTranscriptionOptions> options) : IOpenAiTranscriptionClient
{
    private readonly OpenAiTranscriptionOptions _options = ValidateOptions(options.Value);

    public async Task<OpenAiTranscriptionResult> TranscribeAsync(
        ProviderRuntimeConfiguration provider,
        byte[] wavBytes,
        string languageCode,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(provider.ProviderCode, ProviderCodes.OpenAi, StringComparison.Ordinal) ||
            !string.Equals(provider.ModelCode, _options.ModelCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Runtime transcription không đúng provider/model đã cấu hình.");
        }

        var audio = WaveAudioValidator.Validate(wavBytes, _options.MaximumBytes);
        if (audio.DurationMs > _options.MaximumDurationSeconds * 1000L)
        {
            throw InvalidResponse(
                "speech_audio_too_long",
                "Audio kiểm tra lời nói vượt quá thời lượng tối đa.");
        }

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(provider.ModelCode), "model");
        content.Add(new StringContent("verbose_json"), "response_format");
        content.Add(new StringContent("word"), "timestamp_granularities[]");
        var language = NormalizeLanguage(languageCode);
        if (language is not null)
        {
            content.Add(new StringContent(language), "language");
        }

        var audioContent = new ByteArrayContent(wavBytes);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(audioContent, "file", "scene-speech.wav");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(provider.BaseUri, "audio/transcriptions"))
        {
            Content = content
        };
        OpenAiContentClient.ApplyAuthentication(request, provider);

        using var response = await httpClientFactory.CreateClient("OpenAiRuntime")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var responseBytes = await ReadLimitedBytesAsync(
            response.Content,
            2 * 1024 * 1024,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw NormalizeProviderError(response.StatusCode, Encoding.UTF8.GetString(responseBytes));
        }

        try
        {
            using var document = JsonDocument.Parse(responseBytes);
            var root = document.RootElement;
            var transcript = root.TryGetProperty("text", out var textElement) &&
                             textElement.ValueKind == JsonValueKind.String
                ? textElement.GetString()?.Trim() ?? string.Empty
                : string.Empty;
            var words = new List<TranscribedWord>();
            if (root.TryGetProperty("words", out var wordsElement) &&
                wordsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var word in wordsElement.EnumerateArray())
                {
                    if (!word.TryGetProperty("word", out var wordText) ||
                        wordText.ValueKind != JsonValueKind.String ||
                        !word.TryGetProperty("start", out var start) ||
                        !word.TryGetProperty("end", out var end) ||
                        !start.TryGetDecimal(out var startSeconds) ||
                        !end.TryGetDecimal(out var endSeconds))
                    {
                        continue;
                    }
                    words.Add(new TranscribedWord(
                        wordText.GetString()?.Trim() ?? string.Empty,
                        ToMilliseconds(startSeconds),
                        ToMilliseconds(endSeconds)));
                }
            }

            var requestId = response.Headers.TryGetValues("x-request-id", out var values)
                ? values.FirstOrDefault() ?? string.Empty
                : string.Empty;
            return new OpenAiTranscriptionResult(transcript, words, requestId);
        }
        catch (JsonException exception)
        {
            throw InvalidResponse(
                "speech_transcription_invalid",
                $"OpenAI trả về transcription JSON không hợp lệ: {exception.Message}");
        }
    }

    private static long ToMilliseconds(decimal seconds) =>
        checked((long)Math.Round(seconds * 1000m, MidpointRounding.AwayFromZero));

    private static string? NormalizeLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return null;
        }
        var separator = languageCode.IndexOfAny(['-', '_']);
        var value = separator > 0 ? languageCode[..separator] : languageCode;
        return value.Trim().ToLowerInvariant();
    }

    private static async Task<byte[]> ReadLimitedBytesAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0 && content.Headers.ContentLength > maximumBytes)
        {
            throw InvalidResponse("speech_transcription_too_large", "Phản hồi transcription vượt giới hạn.");
        }
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[32 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            if (destination.Length + read > maximumBytes)
            {
                throw InvalidResponse("speech_transcription_too_large", "Phản hồi transcription vượt giới hạn.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return destination.ToArray();
    }

    private static ProviderHttpException NormalizeProviderError(
        HttpStatusCode statusCode,
        string responseJson)
    {
        _ = ProviderHttpException.FromResponse(
            ProviderCodes.OpenAi,
            statusCode,
            responseJson);
        var code = statusCode switch
        {
            HttpStatusCode.TooManyRequests => "openai_transcription_rate_limited",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "openai_transcription_permission_denied",
            HttpStatusCode.BadRequest => "openai_transcription_request_rejected",
            _ => "openai_transcription_failed"
        };
        return new ProviderHttpException(
            ProviderCodes.OpenAi,
            code,
            $"OpenAI từ chối yêu cầu kiểm tra lời nói, HTTP {(int)statusCode}.",
            statusCode: statusCode);
    }

    private static ProviderHttpException InvalidResponse(string code, string message) =>
        new(ProviderCodes.OpenAi, code, message);

    private static OpenAiTranscriptionOptions ValidateOptions(OpenAiTranscriptionOptions options)
    {
        options.Validate();
        return options;
    }
}
