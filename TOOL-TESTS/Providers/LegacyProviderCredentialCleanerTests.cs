using TOOL_LOCAL.Providers;

namespace TOOL_TESTS.Providers;

public sealed class LegacyProviderCredentialCleanerTests
{
    [Fact]
    public void Remove_DeletesOnlyLegacyCredentialFiles()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "videomaker-legacy-credential-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var credential = Path.Combine(directory, "provider-secrets.bin");
        var temporary = credential + ".tmp";
        var preserved = Path.Combine(directory, "appsettings.user.json");
        try
        {
            File.WriteAllText(credential, "legacy-secret");
            File.WriteAllText(temporary, "legacy-temporary-secret");
            File.WriteAllText(preserved, "keep");

            LegacyProviderCredentialCleaner.RemoveFromDirectory(directory);

            Assert.False(File.Exists(credential));
            Assert.False(File.Exists(temporary));
            Assert.True(File.Exists(preserved));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
