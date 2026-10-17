using System.Net;
using System.Net.Http.Json;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_LOCAL.Updates;

internal sealed partial class DesktopUpdateApiClient
{
    private string StandaloneQuery => $"version={Uri.EscapeDataString(DesktopBuildInfo.Version)}&buildNumber={DesktopBuildInfo.BuildNumber}" +
        $"&channel={Uri.EscapeDataString(options.Channel)}&platform={Uri.EscapeDataString(options.Platform)}" +
        $"&profile={Uri.EscapeDataString(DesktopBuildInfo.Profile)}&catalogProtocol=5";

    private async Task<DesktopStandaloneUpdateCheckResponse?> CheckStandaloneAsync(CancellationToken token)
    {
        using var response = await SendDirectAsync("api/desktop-updates/standalone/check?" + StandaloneQuery, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DesktopStandaloneUpdateCheckResponse>(token)
            ?? throw new InvalidDataException("Server trả catalog cập nhật độc lập không hợp lệ.");
        if (result.Release is not null) ValidateIncomingStandaloneRelease(result.Release);
        if (result.IsUpdateAvailable && (result.Release is null ||
            DesktopReleaseVersionPolicy.CompareVersions(result.Release.Version, DesktopBuildInfo.Version) <= 0))
            throw new InvalidDataException("Catalog cập nhật không chọn phiên bản mới hơn bản đang chạy.");
        return result;
    }

    public async Task<DesktopStandaloneReleaseResponse?> GetStandaloneRepairReleaseAsync(CancellationToken token)
    {
        using var response = await SendDirectAsync("api/desktop-updates/standalone/repair?" + StandaloneQuery, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw await DesktopRepairErrors.FromResponseAsync(response, token);
        var result = await response.Content.ReadFromJsonAsync<DesktopStandaloneReleaseResponse>(token)
            ?? throw new InvalidDataException("Server trả catalog sửa chữa độc lập không hợp lệ.");
        ValidateIncomingStandaloneRelease(result);
        if (result.Version != DesktopBuildInfo.Version || result.DeclaredBuildNumber != DesktopBuildInfo.BuildNumber)
            throw new InvalidDataException("Catalog sửa chữa không khớp phiên bản/build đang cài.");
        return result;
    }

    internal void ValidateIncomingStandaloneRelease(DesktopStandaloneReleaseResponse release) =>
        DesktopStandaloneCatalogPolicy.ValidateRelease(release,
            httpClient.BaseAddress ?? throw new InvalidOperationException("Thiếu API cập nhật."), DesktopBuildInfo.Profile);
}
