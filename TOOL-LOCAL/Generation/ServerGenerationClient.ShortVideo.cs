using System.Net.Http.Headers;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_LOCAL.Generation;

internal interface IShortVideoOutfitClient
{
    Task<ShortVideoQuote> QuoteShortTextVideoAsync(ShortVideoQuoteRequest request, CancellationToken ct) => throw new NotSupportedException();
    Task<ShortVideoVeoMigrationResponse> MigrateShortVideoToVeoAsync(ShortVideoVeoMigrationRequest request, CancellationToken ct) => throw new NotSupportedException();
    Task<ShortVideoState> GetOutfitAsync(Guid projectId, CancellationToken ct) => throw new NotSupportedException();
    Task<ShortVideoState> SaveOutfitAsync(ShortVideoSettingsRequest request, CancellationToken ct) => throw new NotSupportedException();
    Task<ShortVideoQuote> QuoteOutfitAsync(ShortVideoQuoteRequest request, CancellationToken ct) => throw new NotSupportedException();
    Task<ShortVideoComposition> ComposeOutfitAsync(ShortVideoComposeRequest request, CancellationToken ct) => throw new NotSupportedException();
    Task<ShortVideoState> ApproveOutfitAsync(ShortVideoApprovalRequest request, CancellationToken ct) => throw new NotSupportedException();
    Task<byte[]> DownloadOutfitAsync(ShortVideoComposition image, CancellationToken ct) => throw new NotSupportedException();
}

internal sealed partial class ServerGenerationClient
{
    public Task<ShortVideoQuote> QuoteShortTextVideoAsync(ShortVideoQuoteRequest request, CancellationToken ct) =>
        SendAsync<ShortVideoQuote>(HttpMethod.Post, "api/generation/short-video/quote-text-video", request, ct);
    public Task<ShortVideoVeoMigrationResponse> MigrateShortVideoToVeoAsync(ShortVideoVeoMigrationRequest request, CancellationToken ct) =>
        SendAsync<ShortVideoVeoMigrationResponse>(HttpMethod.Post, "api/generation/short-video/migrate-veo", request, ct);
    public async Task<ShortVideoState> GetOutfitAsync(Guid projectId, CancellationToken ct) =>
        await SendAsync<ShortVideoState>(HttpMethod.Get, $"api/generation/short-video/state?projectId={projectId:D}&organizationId={await GetOrganizationIdAsync(ct):D}", null, ct);
    public Task<ShortVideoState> SaveOutfitAsync(ShortVideoSettingsRequest request, CancellationToken ct) =>
        SendAsync<ShortVideoState>(HttpMethod.Post, "api/generation/short-video/settings", request, ct);
    public Task<ShortVideoQuote> QuoteOutfitAsync(ShortVideoQuoteRequest request, CancellationToken ct) =>
        SendAsync<ShortVideoQuote>(HttpMethod.Post, "api/generation/short-video/quote", request, ct);
    public Task<ShortVideoComposition> ComposeOutfitAsync(ShortVideoComposeRequest request, CancellationToken ct) =>
        SendAsync<ShortVideoComposition>(HttpMethod.Post, "api/generation/short-video/compose", request, ct);
    public Task<ShortVideoState> ApproveOutfitAsync(ShortVideoApprovalRequest request, CancellationToken ct) =>
        SendAsync<ShortVideoState>(HttpMethod.Post, "api/generation/short-video/approval", request, ct);
    public async Task<byte[]> DownloadOutfitAsync(ShortVideoComposition image, CancellationToken ct)
    {
        await licenseManager.EnsureAccessAsync(ct);
        var path = $"/api/generation/short-video/images/{image.CompositionId:D}";
        if (image.ContentUrl != path || image.SizeBytes is <= 0 or > MaximumImageBytes)
            throw new InvalidDataException("Metadata ảnh không hợp lệ.");
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await sessionManager.GetValidAccessTokenAsync(ct));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);
        if (response.Content.Headers.ContentType?.MediaType != image.MimeType ||
            response.Content.Headers.ContentLength is { } size && size != image.SizeBytes)
            throw new InvalidDataException("Nội dung ảnh không khớp metadata.");
        using var target = new MemoryStream();
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[65536];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            if (target.Length + read > image.SizeBytes) throw new InvalidDataException("Ảnh vượt dung lượng cho phép.");
            target.Write(buffer, 0, read);
        }
        var bytes = target.ToArray();
        ShortVideoWorkflowService.CheckImage(bytes, new(image.Sha256, image.MimeType, image.SizeBytes, image.Width, image.Height));
        return bytes;
    }
}
