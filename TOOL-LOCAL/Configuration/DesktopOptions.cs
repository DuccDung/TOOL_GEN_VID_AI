using System.Text.Json;
using System.Text.Json.Nodes;

namespace TOOL_LOCAL.Configuration;

public sealed class DesktopOptions
{
    public ServerOptions Server { get; init; } = new();

    public DatabaseOptions Database { get; init; } = new();

    public StorageOptions Storage { get; init; } = new();

    public MediaToolOptions MediaTools { get; init; } = new();

    public DesktopUpdateOptions Update { get; init; } = new();

    public DesktopFeatureOptions Features { get; init; } = new();

    public DesktopSpeechSynchronizationOptions SpeechSynchronization { get; init; } = new();

    public DesktopLocalVoiceOptions LocalVoice { get; init; } = new();

    public TOOL_LOCAL.ModelDistribution.DesktopModelDistributionOptions ModelDistribution { get; init; } = new();

    public static DesktopOptions Load() => Load(AppContext.BaseDirectory, DesktopUserSettingsStore.PreferencesDirectory);

    internal static DesktopOptions Load(string applicationDirectory, string? preferencesDirectory = null, bool requireDatabase = true)
    {
        var path = Path.Combine(applicationDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Không tìm thấy file cấu hình: {path}");
        }

        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidOperationException("Không thể đọc cấu hình Desktop.");
        var userPath = Path.Combine(applicationDirectory, "appsettings.user.json");
        if (File.Exists(userPath))
        {
            var userRoot = JsonNode.Parse(File.ReadAllText(userPath)) as JsonObject
                ?? throw new InvalidOperationException("Không thể đọc cấu hình Desktop dành cho người dùng.");
            Merge(root, userRoot);
        }

        if (preferencesDirectory is not null)
        {
            var features = root["Features"] as JsonObject ?? new JsonObject();
            features["SpeechSynchronizationEnabled"] = DesktopUserSettingsStore.ReadSpeechSynchronizationEnabled(
                applicationDirectory, features["SpeechSynchronizationEnabled"]?.GetValue<bool>() ?? false,
                preferencesDirectory);
            root["Features"] = features;
        }

        ApplyStaticModelReleasePolicy(root, TOOL_LOCAL.ModelDistribution.StaticModelRuntimePolicy.RequiredByRelease);

        var options = root.Deserialize<DesktopOptions>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Không thể đọc cấu hình Desktop.");

        if (!Uri.TryCreate(options.Server.BaseUrl, UriKind.Absolute, out var serverUri) ||
            serverUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Server:BaseUrl phải là HTTPS URL hợp lệ.");
        }

        if (requireDatabase && string.IsNullOrWhiteSpace(options.Database.ConnectionString))
        {
            throw new InvalidOperationException("Database:ConnectionString chưa được cấu hình.");
        }

        if (string.IsNullOrWhiteSpace(options.Storage.WorkspaceRoot))
        {
            throw new InvalidOperationException("Storage:WorkspaceRoot chưa được cấu hình.");
        }

        if (options.Update.CheckIntervalSeconds < 30 ||
            string.IsNullOrWhiteSpace(options.Update.Channel) ||
            string.IsNullOrWhiteSpace(options.Update.Platform))
        {
            throw new InvalidOperationException("Cấu hình Update không hợp lệ.");
        }
        if (options.SpeechSynchronization.MaximumTempoAdjustmentRatio is < 0m or > 0.25m ||
            options.SpeechSynchronization.MinimumVoiceDurationRatio is < 0.05m or > 1m ||
            options.SpeechSynchronization.TargetLoudnessLufs is < -24m or > -10m ||
            options.SpeechSynchronization.TargetSpeechLeadInMs is < 0 or > 2000 ||
            options.SpeechSynchronization.SpeechBoundaryPaddingMs is < 0 or > 500)
        {
            throw new InvalidOperationException("Cấu hình SpeechSynchronization của desktop không hợp lệ.");
        }

        options.LocalVoice.Validate();
        return options;
    }

    // A static release requires these local components independently of preferences
    // preserved from an older installation. This changes only the in-memory options;
    // runtime evidence, license and command authorization remain separate gates.
    internal static void ApplyStaticModelReleasePolicy(JsonObject root, bool requiredByRelease)
    {
        static JsonObject? LastObject(JsonObject value, string name) => value
            .LastOrDefault(x => string.Equals(x.Key, name, StringComparison.OrdinalIgnoreCase)).Value as JsonObject;
        static void Set(JsonObject value, string name, JsonNode node)
        {
            foreach (var key in value.Select(x => x.Key)
                .Where(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase)).ToArray()) value.Remove(key);
            value[name] = node;
        }
        var distribution = LastObject(root, "ModelDistribution");
        var mode = distribution?.LastOrDefault(x => string.Equals(x.Key, "Mode", StringComparison.OrdinalIgnoreCase)).Value;
        if (!requiredByRelease && !(mode is JsonValue modeValue
            && modeValue.TryGetValue<string>(out var sourceMode) && sourceMode == "StaticArchive")) return;
        var features = LastObject(root, "Features") ?? new JsonObject();
        Set(root, "Features", features);
        foreach (var feature in new[] { "VietsubEnabled", "VietsubOcrEnabled", "VietsubLocalTranslationEnabled", "VietsubLocalVoiceEnabled" })
            Set(features, feature, JsonValue.Create(true)!);
        distribution ??= new JsonObject();
        Set(root, "ModelDistribution", distribution);
        Set(distribution, "Mode", JsonValue.Create("StaticArchive")!);
        Set(distribution, "Enabled", JsonValue.Create(false)!);
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var pair in source)
        {
            if (pair.Value is JsonObject sourceObject && target[pair.Key] is JsonObject targetObject)
            {
                Merge(targetObject, sourceObject);
            }
            else
            {
                target[pair.Key] = pair.Value?.DeepClone();
            }
        }
    }
}

public sealed class ServerOptions
{
    public string BaseUrl { get; init; } = string.Empty;
}

public sealed class DatabaseOptions
{
    public string ConnectionString { get; init; } = string.Empty;
}

public sealed class StorageOptions
{
    public string WorkspaceRoot { get; init; } = string.Empty;
}

public sealed class MediaToolOptions
{
    public string FfmpegPath { get; init; } = "tools/ffmpeg/ffmpeg.exe";

    public string FfprobePath { get; init; } = "tools/ffmpeg/ffprobe.exe";
}

public sealed class DesktopUpdateOptions
{
    public bool Enabled { get; init; } = true;

    public string Channel { get; init; } = "Stable";

    public string Platform { get; init; } = "win-x64";

    public int CheckIntervalSeconds { get; init; } = 120;
}

public sealed class DesktopFeatureOptions
{
    public bool ShortVideoCharacterOutfitEnabled { get; init; }
    public bool TikTokEnabled { get; init; } = true;

    public bool VietsubEnabled { get; init; }

    public bool VietsubOcrEnabled { get; init; }

    public bool VietsubLocalTranslationEnabled { get; init; }

    public bool VietsubLocalVoiceEnabled { get; init; } = true;

    public bool SpeechSynchronizationEnabled { get; init; }

    public bool VeoLocalVoiceConsistencyEnabled { get; init; }
}

public sealed class DesktopSpeechSynchronizationOptions
{
    public decimal MaximumTempoAdjustmentRatio { get; init; } = 0.05m;

    public decimal MinimumVoiceDurationRatio { get; init; } = 0.20m;

    public decimal TargetLoudnessLufs { get; init; } = -16m;

    public int TargetSpeechLeadInMs { get; init; } = 150;

    public int SpeechBoundaryPaddingMs { get; init; } = 80;
}

public sealed class DesktopLocalVoiceOptions
{
    public string? ComponentRoot { get; init; }

    public string? TemporaryRoot { get; init; }

    internal void Validate()
    {
        if (!string.IsNullOrWhiteSpace(ComponentRoot))
            LocalVoice.LocalVoiceRuntimePaths.ResolveDirectory(ComponentRoot);
        if (!string.IsNullOrWhiteSpace(TemporaryRoot))
            LocalVoice.LocalVoiceRuntimePaths.ResolveDirectory(TemporaryRoot);
    }
}
