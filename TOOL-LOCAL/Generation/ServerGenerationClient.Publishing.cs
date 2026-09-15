using System.Net.Http.Headers;
using System.Security.Cryptography;
using TOOL_SHARED.Contracts.Publishing;

namespace TOOL_LOCAL.Generation;

internal sealed partial class ServerGenerationClient
{
    public Task<PublishingState> GetPublishingStateAsync(Guid organizationId, CancellationToken ct) =>
        SendAsync<PublishingState>(HttpMethod.Get, $"api/publishing/state?organizationId={organizationId:D}", null, ct);
    public Task<PublishingImage> UploadPublishingImageAsync(UploadPublishingImageRequest request, CancellationToken ct) =>
        SendAsync<PublishingImage>(HttpMethod.Post, "api/publishing/images", request, ct);
    public Task<PublishingScheduleSummary> SavePublishingScheduleAsync(SavePublishingScheduleRequest request, CancellationToken ct) =>
        SendAsync<PublishingScheduleSummary>(HttpMethod.Post, "api/publishing/schedules", request, ct);
    public Task<PublishingScheduleSummary> ChangePublishingScheduleAsync(ChangePublishingScheduleRequest request, CancellationToken ct) =>
        SendAsync<PublishingScheduleSummary>(HttpMethod.Post, "api/publishing/schedules/change", request, ct);
    public Task<StartPublishingOAuthResponse> StartPublishingOAuthAsync(StartPublishingOAuthRequest request, CancellationToken ct) =>
        SendAsync<StartPublishingOAuthResponse>(HttpMethod.Post, "api/publishing/oauth/start", request, ct);
    public Task PublishingRunActionAsync(PublishingRunActionRequest request, CancellationToken ct) => SendWithoutResponseAsync(HttpMethod.Post, "api/publishing/runs/action", request, ct);
    public Task ReviewPublishingRunAsync(ApprovePublishingRunRequest request, CancellationToken ct) => SendWithoutResponseAsync(HttpMethod.Post, "api/publishing/runs/review", request, ct);
    public Task DisconnectPublishingAsync(Guid connectionId, CancellationToken ct) => SendWithoutResponseAsync(HttpMethod.Delete, $"api/publishing/connections/{connectionId:D}", null, ct);

    public async Task DownloadPublishingPreviewAsync(Guid organizationId, Guid runId, string sha256, string path, CancellationToken ct)
    {
        const long maximum = 200L * 1024 * 1024;
        if (runId == Guid.Empty || organizationId != SelectedOrganizationId || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Metadata video xem trước không hợp lệ.");
        await licenseManager.EnsureAccessAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/publishing/runs/{runId:D}/content?organizationId={organizationId:D}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await sessionManager.GetValidAccessTokenAsync(ct));
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);
        if (response.Content.Headers.ContentType?.MediaType != "video/mp4" || response.Content.Headers.ContentLength is not (> 0 and <= maximum) ||
            !string.Equals(response.Headers.ETag?.Tag, $"\"{sha256}\"", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Server trả về video không khớp phiên bản xem trước.");
        var part = path + ".part";
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var destination = new FileStream(part, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                var buffer = new byte[65536]; int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                { if (destination.Length + read > maximum) throw new InvalidDataException("Video vượt giới hạn xem trước."); await destination.WriteAsync(buffer.AsMemory(0, read), ct); }
                if (destination.Length != response.Content.Headers.ContentLength) throw new InvalidDataException("Video chưa được tải đủ.");
                destination.Position = 0; var header = new byte[12]; await destination.ReadExactlyAsync(header, ct);
                if (!header.AsSpan(4, 4).SequenceEqual("ftyp"u8)) throw new InvalidDataException("File xem trước không phải MP4.");
                destination.Position = 0;
                if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(destination, ct)), sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Checksum video xem trước không khớp.");
                await destination.FlushAsync(ct);
            }
            File.Move(part, path, true);
        }
        finally { if (File.Exists(part)) File.Delete(part); }
    }
}
