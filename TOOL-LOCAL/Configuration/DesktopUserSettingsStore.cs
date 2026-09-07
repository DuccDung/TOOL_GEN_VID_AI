using System.Text.Json;
using System.Text.Json.Nodes;

namespace TOOL_LOCAL.Configuration;

internal static class DesktopUserSettingsStore
{
    private const string UserSettingsFileName = "appsettings.user.json";

    public static bool ReadSpeechSynchronizationEnabled(
        string applicationDirectory,
        bool fallbackValue)
    {
        var path = Path.Combine(applicationDirectory, UserSettingsFileName);
        if (!File.Exists(path))
        {
            return fallbackValue;
        }

        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidOperationException("Không thể đọc cấu hình Desktop dành cho người dùng.");
        return root["Features"]?["SpeechSynchronizationEnabled"]?.GetValue<bool>()
            ?? fallbackValue;
    }

    public static void WriteSpeechSynchronizationEnabled(
        string applicationDirectory,
        bool enabled)
    {
        var path = Path.Combine(applicationDirectory, UserSettingsFileName);
        var root = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidOperationException("Không thể đọc cấu hình Desktop dành cho người dùng.")
            : new JsonObject();
        var features = root["Features"] as JsonObject ?? new JsonObject();
        features["SpeechSynchronizationEnabled"] = enabled;
        root["Features"] = features;

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
