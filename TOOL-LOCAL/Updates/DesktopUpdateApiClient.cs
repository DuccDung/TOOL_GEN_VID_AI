using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TOOL_LOCAL.Authentication;
using TOOL_LOCAL.Configuration;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_LOCAL.Updates;

internal sealed partial class DesktopUpdateApiClient(
    HttpClient httpClient,
    AccountSessionManager sessionManager,
    DesktopUpdateOptions options)
{
    public async Task<DesktopUpdateCheckResponse> CheckAsync(CancellationToken cancellationToken)
    {
        var standalone = await CheckStandaloneAsync(cancellationToken);
        if (standalone is { IsUpdateAvailable: true, Release: not null })
            return new(true, standalone.IsMandatory, null, null, standalone.Release);
        var direct = await CheckDirectAsync(cancellationToken);
        if (direct is { IsUpdateAvailable: true, Release: not null })
            return new(true, direct.IsMandatory, null, direct.Release);
        var query = $"version={Uri.EscapeDataString(DesktopBuildInfo.Version)}" +
                    $"&buildNumber={DesktopBuildInfo.BuildNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                    $"&channel={Uri.EscapeDataString(options.Channel)}" +
                    $"&platform={Uri.EscapeDataString(options.Platform)}&distributionProtocol={DesktopBuildInfo.DistributionProtocol}" +
                    $"&profile={Uri.EscapeDataString(DesktopBuildInfo.Profile)}";
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
        var result = await response.Content.ReadFromJsonAsync<DesktopUpdateCheckResponse>(cancellationToken)
            ?? throw new InvalidDataException("Server trả về thông tin cập nhật không hợp lệ.");
        if (result.Release is not null) ValidateIncomingRelease(result.Release);
        if (result.DirectRelease is not null) ValidateIncomingDirectRelease(result.DirectRelease);
        return result;
    }

    private string DirectQuery => $"version={Uri.EscapeDataString(DesktopBuildInfo.Version)}&buildNumber={DesktopBuildInfo.BuildNumber}" +
        $"&channel={Uri.EscapeDataString(options.Channel)}&platform={Uri.EscapeDataString(options.Platform)}" +
        $"&profile={Uri.EscapeDataString(DesktopBuildInfo.Profile)}&catalogProtocol=4";

    private async Task<HttpResponseMessage> SendDirectAsync(string path, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await sessionManager.GetValidAccessTokenAsync(token));
        var response = await httpClient.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose(); await sessionManager.InvalidateAsync(CancellationToken.None);
            throw new AccountClientException("session_expired", AccountSessionManager.SessionExpiredMessage, 401);
        }
        return response;
    }

    private async Task<DesktopDirectUpdateCheckResponse?> CheckDirectAsync(CancellationToken token)
    {
        using var response = await SendDirectAsync("api/desktop-updates/direct/check?" + DirectQuery, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DesktopDirectUpdateCheckResponse>(token)
            ?? throw new InvalidDataException("Server trả catalog cập nhật không hợp lệ.");
        if (result.Release is not null) ValidateIncomingDirectRelease(result.Release);
        if (result.IsUpdateAvailable && (result.Release is null ||
            DesktopReleaseVersionPolicy.CompareVersions(result.Release.Version, DesktopBuildInfo.Version) <= 0))
            throw new InvalidDataException("Catalog cập nhật không chọn phiên bản mới hơn bản đang chạy.");
        return result;
    }

    public async Task<DesktopDirectReleaseResponse?> GetDirectRepairReleaseAsync(CancellationToken token)
    {
        using var response = await SendDirectAsync("api/desktop-updates/direct/repair?" + DirectQuery, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw await DesktopRepairErrors.FromResponseAsync(response, token);
        var result = await response.Content.ReadFromJsonAsync<DesktopDirectReleaseResponse>(token)
            ?? throw new InvalidDataException("Server trả catalog sửa chữa không hợp lệ.");
        ValidateIncomingDirectRelease(result);
        if (result.Version != DesktopBuildInfo.Version || result.DeclaredBuildNumber != DesktopBuildInfo.BuildNumber)
            throw new InvalidDataException("Catalog sửa chữa không khớp phiên bản/build đang cài.");
        return result;
    }

    internal void ValidateIncomingDirectRelease(DesktopDirectReleaseResponse release) =>
        DesktopDirectCatalogPolicy.ValidateRelease(release,
            httpClient.BaseAddress ?? throw new InvalidOperationException("Thiếu API cập nhật."), DesktopBuildInfo.Profile);

    public async Task<DesktopReleaseResponse> GetRepairReleaseAsync(CancellationToken cancellationToken)
    {
        var query = $"version={Uri.EscapeDataString(DesktopBuildInfo.Version)}" +
                    $"&buildNumber={DesktopBuildInfo.BuildNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                    $"&channel={Uri.EscapeDataString(options.Channel)}" +
                    $"&platform={Uri.EscapeDataString(options.Platform)}&distributionProtocol={DesktopBuildInfo.DistributionProtocol}" +
                    $"&profile={Uri.EscapeDataString(DesktopBuildInfo.Profile)}";
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
        if (!response.IsSuccessStatusCode)
            throw await DesktopRepairErrors.FromResponseAsync(response, cancellationToken);
        var release = await response.Content.ReadFromJsonAsync<DesktopReleaseResponse>(cancellationToken)
            ?? throw new InvalidDataException("Server trả về package sửa chữa không hợp lệ.");
        ValidateIncomingRelease(release);
        return release;
    }

    internal void ValidateIncomingRelease(DesktopReleaseResponse release)
    {
        if (release.DistributionProtocol == DesktopDistributionProtocol.Shared)
            DesktopSharedBootstrapperPolicy.ValidateRelease(release,
                httpClient.BaseAddress ?? throw new InvalidOperationException("Thiếu địa chỉ API cập nhật."), DesktopBuildInfo.Profile);
    }
}
