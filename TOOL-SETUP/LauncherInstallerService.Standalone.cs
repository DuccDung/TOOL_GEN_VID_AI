using System.Net;
using System.Net.Http.Json;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_SETUP;

internal sealed partial class LauncherInstallerService
{
    public async Task<IReadOnlyList<DesktopStandaloneReleaseResponse>> GetStandaloneVersionsAsync(CancellationToken token)
    {
        if (!options.IsSharedBootstrapper || !options.SupportedCatalogProtocols.Split(',').Contains("5", StringComparer.Ordinal)) return [];
        var path = $"api/launcher-distribution/standalone/versions?profile={Uri.EscapeDataString(options.Profile)}" +
            $"&channel={Uri.EscapeDataString(options.Channel)}&platform={Uri.EscapeDataString(options.Platform)}" +
            $"&catalogProtocol=5&setupVersion={Uri.EscapeDataString(options.BootstrapperVersion)}";
        using var response = await _httpClient.GetAsync(path, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DesktopStandaloneReleaseListResponse>(token)
            ?? throw new InvalidDataException("Server không trả catalog độc lập hợp lệ.");
        if (result.Releases is null || result.Releases.Count > 1000 || result.Releases.Any(x => x is null)) throw new InvalidDataException("Catalog vượt giới hạn.");
        return result.Releases.Where(IsCompatibleStandalone).OrderByDescending(x => Version.Parse(x.Version)).ThenByDescending(x => x.DeclaredBuildNumber).ToArray();
    }

    private bool IsCompatibleStandalone(DesktopStandaloneReleaseResponse release)
    {
        try { ValidateStandaloneRelease(release, options); return true; }
        catch (Exception error) when (error is InvalidDataException or ArgumentException) { return false; }
    }

    public async Task<DesktopStandaloneReleaseResponse?> GetStandaloneReleaseAsync(DesktopStandaloneReleaseResponse snapshot, CancellationToken token)
    {
        ValidateStandaloneRelease(snapshot, options);
        var path = $"api/launcher-distribution/standalone/versions/{snapshot.ReleaseId:D}?profile={Uri.EscapeDataString(options.Profile)}" +
            $"&channel={Uri.EscapeDataString(options.Channel)}&platform={Uri.EscapeDataString(options.Platform)}&catalogProtocol=5";
        using var response = await _httpClient.GetAsync(path, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var approved = await response.Content.ReadFromJsonAsync<DesktopStandaloneReleaseResponse>(token)
            ?? throw new InvalidDataException("Catalog không trả đúng snapshot yêu cầu.");
        ValidateStandaloneSnapshot(snapshot, approved, options); return approved;
    }

    public Task InstallStandaloneAsync(DesktopStandaloneReleaseResponse release, string directory, bool desktopShortcut, bool launch,
        IProgress<DesktopUpdateProgress>? progress, CancellationToken token) =>
        InstallStaticCoreAsync(null, null, directory, desktopShortcut, launch,
            progress is null ? null : new MonotonicSetupProgress(progress), token, release);

    internal static void ValidateStandaloneRelease(DesktopStandaloneReleaseResponse release, InstallerOptions installer)
    {
        if (!installer.IsSharedBootstrapper || !installer.SupportedCatalogProtocols.Split(',').Contains("5", StringComparer.Ordinal))
            throw new InvalidDataException("Catalog độc lập cần Setup 1.3.0 trở lên.");
        DesktopStandaloneCatalogPolicy.ValidateRelease(release, new Uri(installer.ServerBaseUrl), installer.Profile);
        if (release.Channel != installer.Channel || release.Platform != installer.Platform
            || DesktopReleaseVersionPolicy.CompareVersions(installer.BootstrapperVersion, release.MinimumSetupVersion) < 0)
            throw new InvalidDataException("Setup không hỗ trợ API/profile/giao thức của bản phát hành.");
    }

    internal static void ValidateStandaloneSnapshot(DesktopStandaloneReleaseResponse requested, DesktopStandaloneReleaseResponse approved, InstallerOptions installer)
    {
        ValidateStandaloneRelease(approved, installer);
        if (requested != approved) throw new InvalidDataException("Catalog đã đổi snapshot hoặc bị thu hồi. Chưa thay ứng dụng đang dùng.");
    }

    internal static void ValidateStandaloneVersionTransition(DesktopStandaloneReleaseResponse release, string version, int build, string sha256)
    {
        if (release.Version == version && release.DeclaredBuildNumber == build && release.ExpectedSha256.Equals(sha256, StringComparison.OrdinalIgnoreCase)) return;
        if (!DesktopReleaseVersionPolicy.IsSupportedStableVersion(version) || DesktopReleaseVersionPolicy.CompareVersions(release.Version, version) <= 0)
            throw new InvalidDataException("Catalog chỉ cập nhật lên phiên bản mới hơn hoặc sửa đúng package đang cài.");
    }

    internal async Task RevalidateStandaloneCommitAsync(DesktopStandaloneReleaseResponse snapshot, StaticSetupTransaction transaction, string operation, CancellationToken token)
    {
        var approved = await GetStandaloneReleaseAsync(snapshot, token) ?? throw new InvalidDataException("Catalog đã thu hồi phiên bản trong lúc kiểm tra model. Chưa commit cài đặt.");
        ValidateStandaloneSnapshot(snapshot, approved, options);
        if (transaction.Current is not { State: "Probing" } journal || journal.OperationId != operation
            || !journal.ReleaseSha256.Equals(snapshot.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Giao dịch không còn khớp snapshot vừa kiểm tra.");
        token.ThrowIfCancellationRequested();
    }
}
