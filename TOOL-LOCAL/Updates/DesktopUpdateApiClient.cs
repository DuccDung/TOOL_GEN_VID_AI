using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Configuration;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_LOCAL.Updates;

internal sealed class DesktopUpdateApiClient(
    HttpClient httpClient,
    AccountSessionManager sessionManager,
    DesktopUpdateOptions options)
{
    public async Task<DesktopUpdateCheckResponse> CheckAsync(CancellationToken cancellationToken)
    {
        var query = $"version={Uri.EscapeDataString(DesktopBuildInfo.Version)}" +
                    $"&buildNumber={DesktopBuildInfo.BuildNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                    $"&channel={Uri.EscapeDataString(options.Channel)}" +
                    $"&platform={Uri.EscapeDataString(options.Platform)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/desktop-updates/check?{query}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await sessionManager.GetValidAccessTokenAsync(cancellationToken));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await sessionManager.InvalidateAsync(CancellationToken.None);
            throw new AccountClientException(
                "session_expired",
                AccountSessionManager.SessionExpiredMessage,
                (int)HttpStatusCode.Unauthorized);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DesktopUpdateCheckResponse>(cancellationToken)
            ?? throw new InvalidDataException("Server trả về thông tin cập nhật không hợp lệ.");
    }

    public async Task<DesktopReleaseResponse> GetRepairReleaseAsync(CancellationToken cancellationToken)
    {
        var query = $"version={Uri.EscapeDataString(DesktopBuildInfo.Version)}" +
                    $"&buildNumber={DesktopBuildInfo.BuildNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                    $"&channel={Uri.EscapeDataString(options.Channel)}" +
                    $"&platform={Uri.EscapeDataString(options.Platform)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/desktop-updates/repair?{query}");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await sessionManager.GetValidAccessTokenAsync(cancellationToken));
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await sessionManager.InvalidateAsync(CancellationToken.None);
            throw new AccountClientException(
                "session_expired",
                AccountSessionManager.SessionExpiredMessage,
                (int)HttpStatusCode.Unauthorized);
        }
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new AccountClientException(
                "media_tool_repair_package_not_found",
                "Không tìm thấy package VideoMaker cùng phiên bản để sửa chữa. Hãy cài lại bản VideoMaker đầy đủ hoặc liên hệ quản trị viên.",
                (int)HttpStatusCode.NotFound);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DesktopReleaseResponse>(cancellationToken)
            ?? throw new InvalidDataException("Server trả về package sửa chữa không hợp lệ.");
    }
}
