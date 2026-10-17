using TOOL_SHARED.Contracts.Updates;

namespace TOOL_SETUP;

/// <summary>Native UI boundary; a fixture can exercise the form without network or model mutation.</summary>
internal interface IInstallerSession : IDisposable
{
    Func<string, CancellationToken, Task<bool>>? ConfirmResourceWarningAsync { get; set; }
    Task<IReadOnlyList<DesktopReleaseResponse>> GetVersionsAsync(CancellationToken cancellationToken);
    async Task<DesktopReleaseResponse?> GetReleaseAsync(DesktopReleaseResponse snapshot, CancellationToken cancellationToken) =>
        (await GetVersionsAsync(cancellationToken)).SingleOrDefault(release => release.ReleaseId == snapshot.ReleaseId);
    Task InstallAsync(DesktopReleaseResponse release, string installDirectory, bool createDesktopShortcut,
        bool launchAfterInstall, IProgress<DesktopUpdateProgress>? progress, CancellationToken cancellationToken);
    Task<IReadOnlyList<DesktopDirectReleaseResponse>> GetDirectVersionsAsync(CancellationToken token) =>
        Task.FromResult<IReadOnlyList<DesktopDirectReleaseResponse>>([]);
    async Task<DesktopDirectReleaseResponse?> GetDirectReleaseAsync(DesktopDirectReleaseResponse snapshot, CancellationToken token) =>
        (await GetDirectVersionsAsync(token)).SingleOrDefault(release => release.ReleaseId == snapshot.ReleaseId);
    Task InstallDirectAsync(DesktopDirectReleaseResponse release, string directory, bool desktopShortcut, bool launch,
        IProgress<DesktopUpdateProgress>? progress, CancellationToken token) =>
        throw new InvalidOperationException("Setup này chưa hỗ trợ catalog tải trực tiếp.");
    Task<IReadOnlyList<DesktopStandaloneReleaseResponse>> GetStandaloneVersionsAsync(CancellationToken token) =>
        Task.FromResult<IReadOnlyList<DesktopStandaloneReleaseResponse>>([]);
    async Task<DesktopStandaloneReleaseResponse?> GetStandaloneReleaseAsync(DesktopStandaloneReleaseResponse snapshot, CancellationToken token) =>
        (await GetStandaloneVersionsAsync(token)).SingleOrDefault(release => release.ReleaseId == snapshot.ReleaseId);
    Task InstallStandaloneAsync(DesktopStandaloneReleaseResponse release, string directory, bool desktopShortcut, bool launch,
        IProgress<DesktopUpdateProgress>? progress, CancellationToken token) =>
        throw new InvalidOperationException("Setup này chưa hỗ trợ catalog độc lập.");
}
