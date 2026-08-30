using System.Text.Json;

namespace TOOL_SERVER.Generation;

internal sealed record VideoProviderReferenceImage(
    Guid CharacterReferenceId,
    string MimeType,
    string Base64Data,
    string Sha256);

internal sealed record VideoProviderTaskResult(
    string ExternalRequestId,
    string Status,
    decimal ProgressPercent,
    string? OutputUrl,
    string? ErrorCode,
    string? ErrorMessage,
    decimal? ReportedBillingAmount,
    long? CompletionTokens,
    int? ActualDurationSeconds,
    string ResponseJson);

internal interface IVideoProviderClient
{
    string ProviderCode { get; }

    Task<VideoProviderTaskResult> SubmitAsync(
        ProviderRuntimeConfiguration provider,
        string prompt,
        string aspectRatio,
        int durationSeconds,
        string resolution,
        bool nativeAudio,
        string safetyIdentifier,
        VideoProviderReferenceImage? referenceImage,
        CancellationToken cancellationToken);

    Task<VideoProviderTaskResult> GetStatusAsync(
        ProviderRuntimeConfiguration provider,
        string externalRequestId,
        CancellationToken cancellationToken);
}

internal interface IVideoProviderRouter
{
    IVideoProviderClient Resolve(string providerCode);
}

internal sealed class VideoProviderRouter(IEnumerable<IVideoProviderClient> clients) : IVideoProviderRouter
{
    private readonly IReadOnlyDictionary<string, IVideoProviderClient> _clients = clients
        .ToDictionary(x => x.ProviderCode, StringComparer.OrdinalIgnoreCase);

    public IVideoProviderClient Resolve(string providerCode) =>
        _clients.TryGetValue(providerCode, out var client)
            ? client
            : throw new ProviderHttpException(
                providerCode,
                "video_provider_not_supported",
                "Provider video trong snapshot chưa được server hỗ trợ.");
}

internal sealed class KlingVideoProviderAdapter(IKlingVideoClient client) : IVideoProviderClient
{
    public string ProviderCode => ProviderCodes.Kling;

    public async Task<VideoProviderTaskResult> SubmitAsync(
        ProviderRuntimeConfiguration provider,
        string prompt,
        string aspectRatio,
        int durationSeconds,
        string resolution,
        bool nativeAudio,
        string safetyIdentifier,
        VideoProviderReferenceImage? referenceImage,
        CancellationToken cancellationToken)
    {
        var result = await client.SubmitAsync(
            provider,
            prompt,
            aspectRatio,
            durationSeconds,
            resolution,
            nativeAudio,
            safetyIdentifier,
            referenceImage is null
                ? null
                : new KlingReferenceImageData(
                    referenceImage.CharacterReferenceId,
                    referenceImage.MimeType,
                    referenceImage.Base64Data,
                    referenceImage.Sha256),
            cancellationToken);
        return Map(result, durationSeconds);
    }

    public async Task<VideoProviderTaskResult> GetStatusAsync(
        ProviderRuntimeConfiguration provider,
        string externalRequestId,
        CancellationToken cancellationToken)
    {
        var result = await client.GetStatusAsync(provider, externalRequestId, cancellationToken);
        return Map(result, null);
    }

    private static VideoProviderTaskResult Map(KlingTaskResult result, int? durationSeconds)
    {
        // The signed provider URL is deliberately kept only in OutputUrl long
        // enough for the worker to cache it. It must never be persisted in the
        // generic ProviderRequest.ResponseJson payload.
        var safeResponseJson = CreateSafeResponseJson(result, durationSeconds);
        return new(
            result.ExternalRequestId,
            result.Status,
            result.ProgressPercent,
            result.OutputUrl,
            result.ErrorCode,
            result.ErrorMessage,
            result.ReportedBillingAmount,
            null,
            durationSeconds,
            safeResponseJson);
    }

    internal static string CreateSafeResponseJson(KlingTaskResult result, int? durationSeconds) =>
        JsonSerializer.Serialize(new
        {
            taskId = result.ExternalRequestId,
            status = result.Status,
            errorCode = result.ErrorCode,
            reportedBillingAmount = result.ReportedBillingAmount,
            actualDurationSeconds = durationSeconds
        });
}
