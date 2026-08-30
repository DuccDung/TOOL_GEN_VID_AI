using System.Reflection;
using System.Text.Json;

namespace TOOL_SETUP;

internal sealed class InstallerOptions
{
    public string ServerBaseUrl { get; init; } = string.Empty;

    public string Channel { get; init; } = "Stable";

    public string Platform { get; init; } = "win-x64";

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
            Platform = metadata.GetValueOrDefault("SetupPlatform", options.Platform)
        };
        if (!Uri.TryCreate(options.ServerBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("ServerBaseUrl của Setup phải là HTTPS URL.");
        }

        return options;
    }
}
