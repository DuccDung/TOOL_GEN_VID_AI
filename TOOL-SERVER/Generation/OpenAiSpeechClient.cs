using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace TOOL_SERVER.Generation;

internal sealed record ValidatedGeneratedVoice(
    byte[] Bytes,
    string MimeType,
    string Sha256,
    long DurationMs,
    int SampleRate,
    byte Channels);

internal sealed record OpenAiSpeechResult(ValidatedGeneratedVoice Voice, string ProviderRequestId);

internal interface IOpenAiSpeechClient
{
    Task<OpenAiSpeechResult> GenerateAsync(
        ProviderRuntimeConfiguration provider,
        string narration,
        string providerVoiceCode,
        string instructions,
        decimal speakingRate,
        CancellationToken cancellationToken);
}

internal sealed class OpenAiSpeechClient(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiSpeechOptions> options) : IOpenAiSpeechClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OpenAiSpeechOptions _options = ValidateOptions(options.Value);

    public async Task<OpenAiSpeechResult> GenerateAsync(
        ProviderRuntimeConfiguration provider,
        string narration,
        string providerVoiceCode,
        string instructions,
        decimal speakingRate,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(provider.ProviderCode, ProviderCodes.OpenAi, StringComparison.Ordinal) ||
            !string.Equals(provider.ModelCode, "gpt-4o-mini-tts", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Runtime tạo giọng phải dùng đúng openai/gpt-4o-mini-tts.");
        }
        if (string.IsNullOrWhiteSpace(narration) || narration.Length > _options.MaximumInputCharacters)
        {
            throw new ArgumentException("Lời đọc trống hoặc vượt quá giới hạn OpenAI Speech.", nameof(narration));
        }
        if (string.IsNullOrWhiteSpace(providerVoiceCode) || providerVoiceCode.Length > 100 ||
            speakingRate < _options.MinimumSpeakingRate || speakingRate > _options.MaximumSpeakingRate)
        {
            throw new ArgumentException("Cấu hình giọng đọc không hợp lệ.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(provider.BaseUri, "audio/speech"))
        {
            Content = JsonContent.Create(new
            {
                model = "gpt-4o-mini-tts",
                input = narration,
                voice = providerVoiceCode,
                instructions,
                response_format = "wav",
                speed = speakingRate
            }, options: JsonOptions)
        };
        OpenAiContentClient.ApplyAuthentication(request, provider);

        using var response = await httpClientFactory.CreateClient("OpenAiRuntime")
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadLimitedErrorAsync(response.Content, cancellationToken);
            throw NormalizeProviderError(response.StatusCode, error);
        }

        var mimeType = response.Content.Headers.ContentType?.MediaType;
        if (mimeType is not ("audio/wav" or "audio/x-wav" or "application/octet-stream"))
        {
            throw InvalidResponse("voice_audio_invalid", "OpenAI trả về Content-Type âm thanh không hợp lệ.");
        }
        var bytes = await ReadLimitedBytesAsync(response.Content, _options.MaximumBytes, cancellationToken);
        var requestId = response.Headers.TryGetValues("x-request-id", out var values)
            ? values.FirstOrDefault() ?? string.Empty
            : string.Empty;
        return new OpenAiSpeechResult(WaveAudioValidator.Validate(bytes, _options.MaximumBytes), requestId);
    }

    private static async Task<byte[]> ReadLimitedBytesAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0 && content.Headers.ContentLength > maximumBytes)
        {
            throw InvalidResponse("voice_audio_too_large", "Audio OpenAI vượt quá giới hạn dung lượng.");
        }
        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream(Math.Min(maximumBytes, 1024 * 1024));
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (destination.Length + read > maximumBytes)
            {
                throw InvalidResponse("voice_audio_too_large", "Audio OpenAI vượt quá giới hạn dung lượng.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return destination.ToArray();
    }

    private static async Task<string> ReadLimitedErrorAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var bytes = await ReadLimitedBytesAsync(content, 64 * 1024, cancellationToken);
        return Encoding.UTF8.GetString(bytes);
    }

    private static ProviderHttpException NormalizeProviderError(HttpStatusCode statusCode, string responseJson)
    {
        var error = ProviderHttpException.FromResponse(ProviderCodes.OpenAi, statusCode, responseJson);
        var code = statusCode switch
        {
            HttpStatusCode.TooManyRequests => "openai_voice_rate_limited",
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "openai_voice_permission_denied",
            HttpStatusCode.BadRequest => "openai_voice_request_rejected",
            _ => "openai_voice_generation_failed"
        };
        var message = statusCode == HttpStatusCode.TooManyRequests
            ? "OpenAI đang giới hạn tần suất tạo giọng. Vui lòng thử lại sau."
            : $"OpenAI từ chối yêu cầu tạo giọng, HTTP {(int)statusCode}.";
        return new ProviderHttpException(ProviderCodes.OpenAi, code, message, statusCode: error.StatusCode ?? statusCode);
    }

    private static ProviderHttpException InvalidResponse(string code, string message) =>
        new(ProviderCodes.OpenAi, code, message);

    private static OpenAiSpeechOptions ValidateOptions(OpenAiSpeechOptions options)
    {
        options.Validate();
        return options;
    }
}

internal static class WaveAudioValidator
{
    public static ValidatedGeneratedVoice Validate(byte[] bytes, int maximumBytes)
    {
        if (bytes.Length < 44 || bytes.Length > maximumBytes ||
            !bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) ||
            !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        {
            throw Invalid("Audio OpenAI không có chữ ký RIFF/WAVE hợp lệ.");
        }

        ushort format = 0;
        ushort channels = 0;
        int sampleRate = 0;
        int byteRate = 0;
        int dataBytes = 0;
        var offset = 12;
        while (offset + 8 <= bytes.Length)
        {
            var chunkId = bytes.AsSpan(offset, 4);
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            var payloadOffset = offset + 8;
            if (chunkSize > int.MaxValue || payloadOffset + (long)chunkSize > bytes.Length)
            {
                throw Invalid("Cấu trúc chunk WAV không hợp lệ.");
            }
            if (chunkId.SequenceEqual("fmt "u8) && chunkSize >= 16)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(payloadOffset, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(payloadOffset + 2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(payloadOffset + 4, 4));
                byteRate = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(payloadOffset + 8, 4));
            }
            else if (chunkId.SequenceEqual("data"u8))
            {
                dataBytes = checked((int)chunkSize);
            }
            offset = checked(payloadOffset + (int)chunkSize + ((int)chunkSize & 1));
        }

        if (format is not (1 or 3) || channels is < 1 or > 2 || sampleRate is < 8_000 or > 192_000 ||
            byteRate <= 0 || dataBytes <= 0)
        {
            throw Invalid("Metadata PCM WAV của OpenAI không hợp lệ.");
        }
        var durationMs = dataBytes * 1000L / byteRate;
        if (durationMs <= 0)
        {
            throw Invalid("Thời lượng WAV của OpenAI không hợp lệ.");
        }
        return new ValidatedGeneratedVoice(
            bytes,
            "audio/wav",
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            durationMs,
            sampleRate,
            checked((byte)channels));
    }

    private static ProviderHttpException Invalid(string message) =>
        new(ProviderCodes.OpenAi, "voice_audio_invalid", message);
}
