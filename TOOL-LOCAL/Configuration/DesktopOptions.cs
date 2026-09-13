using System.Text.Json;
using System.Text.Json.Nodes;

namespace TOOL_LOCAL.Configuration;

public sealed class DesktopOptions
{
    public TOOL_SHARED.Contracts.Common.ApplicationFeaturePolicy Application { get; init; } = new();
    public ServerOptions Server { get; init; } = new();

    public DatabaseOptions Database { get; init; } = new();

    public StorageOptions Storage { get; init; } = new();

    public MediaToolOptions MediaTools { get; init; } = new();

    public DesktopUpdateOptions Update { get; init; } = new();

    public DesktopFeatureOptions Features { get; init; } = new();

    public DesktopSpeechSynchronizationOptions SpeechSynchronization { get; init; } = new();

    public static DesktopOptions Load() => Load(AppContext.BaseDirectory);

    internal static DesktopOptions Load(string applicationDirectory)
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

        var options = root.Deserialize<DesktopOptions>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Không thể đọc cấu hình Desktop.");

        if (!Uri.TryCreate(options.Server.BaseUrl, UriKind.Absolute, out var serverUri) ||
            serverUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Server:BaseUrl phải là HTTPS URL hợp lệ.");
        }

        if (!options.Application.VietsubLocalOnly && string.IsNullOrWhiteSpace(options.Database.ConnectionString))
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

        return options;
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
    public bool VietsubEnabled { get; init; }

    public bool VietsubOcrEnabled { get; init; }

    public bool VietsubLocalTranslationEnabled { get; init; }

    public bool VietsubLocalVoiceEnabled { get; init; } = true;

    public bool SpeechSynchronizationEnabled { get; init; }
}

public sealed class DesktopSpeechSynchronizationOptions
{
    public decimal MaximumTempoAdjustmentRatio { get; init; } = 0.05m;

    public decimal MinimumVoiceDurationRatio { get; init; } = 0.20m;

    public decimal TargetLoudnessLufs { get; init; } = -16m;

    public int TargetSpeechLeadInMs { get; init; } = 150;

    public int SpeechBoundaryPaddingMs { get; init; } = 80;
}
