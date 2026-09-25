using System.Text.Json;
using System.Text.Json.Nodes;

namespace TOOL_LOCAL.Configuration;

internal static class DesktopUserSettingsStore
{
    private const string LegacySettingsFileName = "appsettings.user.json";
    private const string PreferencesFileName = "preferences.json";

    public static string PreferencesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ToolGenPostVideo", "settings");

    public static bool ReadSpeechSynchronizationEnabled(
        string applicationDirectory,
        bool fallbackValue,
        string? preferencesDirectory = null)
    {
        var path = Path.Combine(preferencesDirectory ?? PreferencesDirectory, PreferencesFileName);
        if (!File.Exists(path))
            path = Path.Combine(applicationDirectory, LegacySettingsFileName);
        if (!File.Exists(path))
        {
            return fallbackValue;
        }

        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidOperationException("Không thể đọc tùy chọn Desktop dành cho người dùng.");
        return root["Features"]?["SpeechSynchronizationEnabled"]?.GetValue<bool>()
            ?? fallbackValue;
    }

    public static void WriteSpeechSynchronizationEnabled(
        string applicationDirectory,
        bool enabled,
        string? preferencesDirectory = null)
    {
        // Preferences are deliberately separate from deployment settings. Never migrate
        // a database connection, server endpoint or machine-specific paths from the legacy file.
        var directory = preferencesDirectory ?? PreferencesDirectory;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, PreferencesFileName);
        var root = new JsonObject
        {
            ["SchemaVersion"] = 1,
            ["Features"] = new JsonObject { ["SpeechSynchronizationEnabled"] = enabled }
        };

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
