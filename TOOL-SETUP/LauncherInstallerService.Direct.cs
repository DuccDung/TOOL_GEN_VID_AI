using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_SETUP;

internal sealed partial class LauncherInstallerService
{
    public async Task<IReadOnlyList<DesktopDirectReleaseResponse>> GetDirectVersionsAsync(CancellationToken token)
    {
        if (!options.IsSharedBootstrapper || !options.SupportedCatalogProtocols.Split(',').Contains("4", StringComparer.Ordinal)) return [];
        var path = $"api/launcher-distribution/direct/versions?profile={Uri.EscapeDataString(options.Profile)}" +
            $"&channel={Uri.EscapeDataString(options.Channel)}&platform={Uri.EscapeDataString(options.Platform)}" +
            $"&catalogProtocol=4&bootstrapperVersion={Uri.EscapeDataString(options.BootstrapperVersion)}";
        using var response = await _httpClient.GetAsync(path, token);
        // A newly downloaded Setup may still be used against a server serving only protocol 3.
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DesktopDirectReleaseListResponse>(token)
            ?? throw new InvalidDataException("Server không trả về catalog tải trực tiếp hợp lệ.");
        if (result.Releases.Count > 1000 || result.Releases.Any(x => x is null)) throw new InvalidDataException("Catalog vượt giới hạn.");
        return result.Releases.Where(x => IsCompatibleDirect(x)).OrderByDescending(x => Version.Parse(x.Version))
            .ThenByDescending(x => x.DeclaredBuildNumber).ToArray();
    }

    private bool IsCompatibleDirect(DesktopDirectReleaseResponse release)
    {
        try { ValidateDirectRelease(release, options); return true; }
        catch (Exception error) when (error is InvalidDataException or ArgumentException) { return false; }
    }

    public async Task<DesktopDirectReleaseResponse?> GetDirectReleaseAsync(DesktopDirectReleaseResponse snapshot, CancellationToken token)
    {
        ValidateDirectRelease(snapshot, options);
        var path = $"api/launcher-distribution/direct/versions/{snapshot.ReleaseId:D}?profile={Uri.EscapeDataString(options.Profile)}" +
            $"&bootstrapperId={snapshot.SharedBootstrapper.BootstrapperId:D}&catalogProtocol=4";
        using var response = await _httpClient.GetAsync(path, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var approved = await response.Content.ReadFromJsonAsync<DesktopDirectReleaseResponse>(token)
            ?? throw new InvalidDataException("Catalog không trả đúng snapshot yêu cầu.");
        ValidateDirectSnapshot(snapshot, approved, options); return approved;
    }

    public Task InstallDirectAsync(DesktopDirectReleaseResponse release, string directory, bool desktopShortcut, bool launch,
        IProgress<DesktopUpdateProgress>? progress, CancellationToken token) =>
        InstallStaticCoreAsync(null, release, directory, desktopShortcut, launch,
            progress is null ? null : new MonotonicSetupProgress(progress), token);

    internal static void ValidateDirectRelease(DesktopDirectReleaseResponse release, InstallerOptions installer)
    {
        if (!installer.IsSharedBootstrapper || !installer.SupportedCatalogProtocols.Split(',').Contains("4", StringComparer.Ordinal))
            throw new InvalidDataException("Catalog tải trực tiếp cần Setup dùng chung 1.2.0 trở lên.");
        DesktopDirectCatalogPolicy.ValidateRelease(release, new Uri(installer.ServerBaseUrl), installer.Profile);
        if (release.SharedBootstrapper.Version != installer.BootstrapperVersion || release.Channel != installer.Channel ||
            release.Platform != installer.Platform)
            throw new InvalidDataException("Setup không khớp identity/profile của catalog tải trực tiếp.");
    }

    internal static void ValidateDirectSnapshot(DesktopDirectReleaseResponse requested, DesktopDirectReleaseResponse approved, InstallerOptions installer)
    {
        ValidateDirectRelease(approved, installer);
        if (requested != approved) throw new InvalidDataException("Catalog đã đổi snapshot hoặc bị thu hồi. Chưa thay ứng dụng đang dùng.");
    }

    internal static async Task ValidateDirectBootstrapperFileAsync(string executable, DesktopDirectReleaseResponse release,
        InstallerOptions installer, CancellationToken token)
    {
        ValidateDirectRelease(release, installer);
        var setup = release.SharedBootstrapper;
        await using var file = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        if (file.Length != setup.SizeBytes || !Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(setup.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("File Setup không khớp checksum đã công bố trong catalog.");
    }

    internal static void ValidateDirectVersionTransition(DesktopDirectReleaseResponse release, string version, int build, string sha256)
    {
        if (release.Version == version && release.DeclaredBuildNumber == build &&
            release.ExpectedSha256.Equals(sha256, StringComparison.OrdinalIgnoreCase)) return;
        if (!DesktopReleaseVersionPolicy.IsSupportedStableVersion(version) || DesktopReleaseVersionPolicy.CompareVersions(release.Version, version) <= 0)
            throw new InvalidDataException("Catalog chỉ cập nhật lên phiên bản mới hơn hoặc sửa đúng package đang cài.");
    }

    internal async Task RevalidateDirectCommitAsync(DesktopDirectReleaseResponse snapshot, StaticSetupTransaction transaction,
        string operation, CancellationToken token)
    {
        var approved = await GetDirectReleaseAsync(snapshot, token)
            ?? throw new InvalidDataException("Catalog đã thu hồi phiên bản trong lúc kiểm tra model. Chưa commit cài đặt.");
        ValidateDirectSnapshot(snapshot, approved, options);
        if (transaction.Current is not { State: "Probing" } journal || journal.OperationId != operation ||
            !journal.ReleaseSha256.Equals(snapshot.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Giao dịch cài đặt không còn khớp snapshot vừa kiểm tra.");
        token.ThrowIfCancellationRequested();
    }
}
