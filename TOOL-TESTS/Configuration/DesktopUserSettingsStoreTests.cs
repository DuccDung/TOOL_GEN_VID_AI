using System.Text.Json.Nodes;
using TOOL_LOCAL.Configuration;

namespace TOOL_TESTS.Configuration;

public sealed class DesktopUserSettingsStoreTests
{
    [Fact]
    public void WriteSpeechSynchronizationEnabled_LeavesDeploymentSettingsUntouched()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "appsettings.user.json");
            File.WriteAllText(path, """
                {
                  "MediaTools": {
                    "FfmpegPath": "C:\\Tools\\ffmpeg.exe"
                  }
                }
                """);

            var original = File.ReadAllText(path);
            var preferences = Path.Combine(directory, "user-data");
            DesktopUserSettingsStore.WriteSpeechSynchronizationEnabled(directory, true, preferences);

            Assert.Equal(original, File.ReadAllText(path));
            var root = JsonNode.Parse(File.ReadAllText(Path.Combine(preferences, "preferences.json")))!.AsObject();
            Assert.Null(root["MediaTools"]);
            Assert.True(root["Features"]!["SpeechSynchronizationEnabled"]!.GetValue<bool>());
            Assert.True(DesktopUserSettingsStore.ReadSpeechSynchronizationEnabled(directory, false, preferences));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReadSpeechSynchronizationEnabled_UsesActiveValueWhenUserHasNoOverride()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var preferences = Path.Combine(directory, "user-data");
            Assert.True(DesktopUserSettingsStore.ReadSpeechSynchronizationEnabled(directory, true, preferences));
            Assert.False(DesktopUserSettingsStore.ReadSpeechSynchronizationEnabled(directory, false, preferences));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void PreferenceOverridesLegacyWithoutChangingLegacyOrCopyingItsOtherKeys()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var preferences = Path.Combine(directory, "user-data");
            var legacy = Path.Combine(directory, "appsettings.user.json");
            File.WriteAllText(legacy, """{"Features":{"SpeechSynchronizationEnabled":true},"Server":{"BaseUrl":"https://legacy.invalid/"}}""");
            Assert.True(DesktopUserSettingsStore.ReadSpeechSynchronizationEnabled(directory, false, preferences));
            DesktopUserSettingsStore.WriteSpeechSynchronizationEnabled(directory, false, preferences);
            Assert.False(DesktopUserSettingsStore.ReadSpeechSynchronizationEnabled(directory, true, preferences));
            var root = JsonNode.Parse(File.ReadAllText(Path.Combine(preferences, "preferences.json")))!;
            Assert.Null(root["Server"]);
            Assert.True(JsonNode.Parse(File.ReadAllText(legacy))!["Features"]!["SpeechSynchronizationEnabled"]!.GetValue<bool>());
            Assert.Empty(Directory.EnumerateFiles(preferences, "*.tmp"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void SavingPreferenceDoesNotRequireApplicationDirectoryToBeWritableOrPresent()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var application = Path.Combine(directory, "application-not-mounted");
            var preferences = Path.Combine(directory, "user-data");
            DesktopUserSettingsStore.WriteSpeechSynchronizationEnabled(application, true, preferences);
            Assert.False(Directory.Exists(application));
            Assert.True(DesktopUserSettingsStore.ReadSpeechSynchronizationEnabled(application, false, preferences));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void LoadOnlyAppliesAllowedPreferenceAndRetainsDeploymentEndpoint()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var preferences = Path.Combine(directory, "user-data");
            Directory.CreateDirectory(preferences);
            File.WriteAllText(Path.Combine(directory, "appsettings.json"), """
                {"Server":{"BaseUrl":"https://deployment.invalid/"},"Database":{"ConnectionString":"test-only"},
                 "Storage":{"WorkspaceRoot":"workspace"},"Features":{"VietsubLocalTranslationEnabled":false}}
                """);
            File.WriteAllText(Path.Combine(preferences, "preferences.json"), """
                {"Server":{"BaseUrl":"https://untrusted.invalid/"},"Database":{"ConnectionString":"untrusted"},
                 "Features":{"SpeechSynchronizationEnabled":true,"VietsubLocalTranslationEnabled":true}}
                """);
            var options = DesktopOptions.Load(directory, preferences);
            Assert.Equal("https://deployment.invalid/", options.Server.BaseUrl);
            Assert.Equal("test-only", options.Database.ConnectionString);
            Assert.True(options.Features.SpeechSynchronizationEnabled);
            Assert.False(options.Features.VietsubLocalTranslationEnabled);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"videomaker-desktop-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
