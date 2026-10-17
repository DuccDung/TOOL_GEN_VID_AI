using System.Reflection;
using System.Text.Json;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;

namespace TOOL_SETUP;

internal sealed class InstallerOptions
{
    public string ServerBaseUrl { get; init; } = string.Empty;

    public string Channel { get; init; } = "Stable";

    public string Platform { get; init; } = "win-x64";

    public string ModelDeliveryMode { get; init; } = "Bundled";
    public string SetupMode { get; init; } = "ExactRelease";
    public string Profile { get; init; } = "Production";
    public string BootstrapperVersion { get; init; } = "1.3.0";
    public string DesktopVersion { get; init; } = string.Empty;
    public int DesktopBuildNumber { get; init; }
    public string DesktopSha256 { get; init; } = string.Empty;
    public long WebView2InstallerSizeBytes { get; init; }
    public string WebView2InstallerSha256 { get; init; } = string.Empty;
    public string WebView2PublisherThumbprint { get; init; } = string.Empty;
    public bool IsStaticArchive => ModelDeliveryMode.Equals("StaticArchive", StringComparison.Ordinal);
    public bool IsSharedBootstrapper => SetupMode == "SharedBootstrapper";
    public int DistributionProtocol => IsSharedBootstrapper ? 3 : DesktopDistributionProtocol.Current;
    public string SupportedCatalogProtocols => IsSharedBootstrapper &&
        DesktopReleaseVersionPolicy.CompareVersions(BootstrapperVersion, DesktopStandaloneCatalogProtocol.MinimumSetupVersion) >= 0 ? "3,4,5"
        : IsSharedBootstrapper && DesktopReleaseVersionPolicy.CompareVersions(BootstrapperVersion, DesktopDirectCatalogProtocol.MinimumBootstrapperVersion) >= 0 ? "3,4" : "3";

    internal DesktopBootstrapperBindingInfo InspectSharedBootstrapper() => new(1, BootstrapperVersion,
        ServerBaseUrl, Profile, Channel, Platform, DistributionProtocol, ApprovedStaticModelBundle.Current.BundleId, SupportedCatalogProtocols);

    internal DesktopSetupBindingInfo InspectBinding() => new(1, DesktopVersion, DesktopBuildNumber, DesktopSha256,
        Channel, Platform, ServerBaseUrl, IsStaticArchive ? DesktopInstallModes.StaticArchive : DesktopInstallModes.Legacy,
        DesktopDistributionProtocol.Current, ApprovedStaticModelBundle.Current.BundleId);

    public static InstallerOptions Load()
    {
        var metadata = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Value is not null)
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value!, StringComparer.OrdinalIgnoreCase);
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("TOOL_SETUP.setupsettings.json")
            ?? throw new InvalidOperationException("Không tìm thấy cấu hình VideoMaker Setup.");
        var options = JsonSerializer.Deserialize<InstallerOptions>(
            stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Cấu hình VideoMaker Setup không hợp lệ.");
        options = new InstallerOptions
        {
            ServerBaseUrl = metadata.GetValueOrDefault("SetupServerBaseUrl", options.ServerBaseUrl),
            Channel = metadata.GetValueOrDefault("SetupChannel", options.Channel),
            Platform = metadata.GetValueOrDefault("SetupPlatform", options.Platform),
            ModelDeliveryMode = metadata.GetValueOrDefault("SetupModelDeliveryMode", options.ModelDeliveryMode),
            SetupMode = metadata.GetValueOrDefault("SetupMode", options.SetupMode),
            Profile = metadata.GetValueOrDefault("SetupProfile", options.Profile),
            BootstrapperVersion = metadata.GetValueOrDefault("SetupBootstrapperVersion", options.BootstrapperVersion),
            DesktopVersion = metadata.GetValueOrDefault("SetupDesktopVersion", options.DesktopVersion),
            DesktopBuildNumber = int.TryParse(metadata.GetValueOrDefault("SetupDesktopBuildNumber"), out var build) ? build : 0,
            DesktopSha256 = metadata.GetValueOrDefault("SetupDesktopSha256", options.DesktopSha256),
            WebView2InstallerSizeBytes = long.TryParse(metadata.GetValueOrDefault("SetupWebView2InstallerSizeBytes"), out var size) ? size : 0,
            WebView2InstallerSha256 = metadata.GetValueOrDefault("SetupWebView2InstallerSha256", string.Empty),
            WebView2PublisherThumbprint = metadata.GetValueOrDefault("SetupWebView2InstallerPublisherThumbprint", string.Empty)
        };
        if (!Uri.TryCreate(options.ServerBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("ServerBaseUrl của Setup phải là HTTPS URL.");
        }

        if (options.SetupMode is not ("ExactRelease" or "SharedBootstrapper") ||
            options.IsSharedBootstrapper && (!options.IsStaticArchive || options.Platform != "win-x64" ||
                string.IsNullOrWhiteSpace(options.Profile) || options.Profile.Length > 50 ||
                !DesktopReleaseVersionPolicy.IsSupportedStableVersion(options.BootstrapperVersion) || uri.IsLoopback || uri.UserInfo.Length != 0 ||
                uri.Query.Length != 0 || uri.Fragment.Length != 0))
            throw new InvalidOperationException("Profile hoặc capability của Setup dùng chung không hợp lệ.");
        if (options.ModelDeliveryMode is not ("Bundled" or "Server" or "StaticArchive"))
            throw new InvalidOperationException("Chế độ bộ cài không hợp lệ.");
        if (options.IsStaticArchive && !options.IsSharedBootstrapper && (string.IsNullOrWhiteSpace(options.DesktopVersion) || options.DesktopBuildNumber < 1 ||
            options.DesktopSha256.Length != 64 || options.DesktopSha256.Any(c => !Uri.IsHexDigit(c))))
            throw new InvalidOperationException("Setup chưa được ghép với package ứng dụng đã duyệt.");

        if (options.IsSharedBootstrapper)
            options.ValidateSharedDescription(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description);

        return options;
    }

    internal void ValidateSharedDescription(string? description)
    {
        const string prefix = "VideoMaker.SharedBootstrapper.v1:";
        if (description is null || description.Length > 8192 || !description.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Setup thiếu metadata profile được đóng trong binary.");
        try
        {
            var binding = JsonSerializer.Deserialize<DesktopBootstrapperBindingInfo>(Convert.FromBase64String(description[prefix.Length..]),
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false });
            if (binding != InspectSharedBootstrapper())
                throw new InvalidOperationException("Metadata đóng gói không khớp API/profile/capability của Setup đang chạy.");
        }
        catch (Exception error) when (error is JsonException or FormatException)
        { throw new InvalidOperationException("Metadata profile của Setup không hợp lệ.", error); }
    }
}
