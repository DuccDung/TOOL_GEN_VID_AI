using System.Text.Json.Nodes;
using TOOL_LOCAL.Configuration;

namespace TOOL_TESTS.Configuration;

public sealed class DesktopUserSettingsStoreTests
{
    [Fact]
    public void WriteSpeechSynchronizationEnabled_PreservesOtherUserSettings()
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

            DesktopUserSettingsStore.WriteSpeechSynchronizationEnabled(directory, true);

            var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            Assert.Equal("C:\\Tools\\ffmpeg.exe", root["MediaTools"]!["FfmpegPath"]!.GetValue<string>());
            Assert.True(root["Features"]!["SpeechSynchronizationEnabled"]!.GetValue<bool>());
            Assert.True(DesktopUserSettingsStore.ReadSpeechSynchronizationEnabled(directory, false));
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
            Assert.True(DesktopUserSettingsStore.ReadSpeechSynchronizationEnabled(directory, true));
            Assert.False(DesktopUserSettingsStore.ReadSpeechSynchronizationEnabled(directory, false));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
