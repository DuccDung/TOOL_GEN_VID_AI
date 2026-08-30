namespace TOOL_LOCAL.Providers;

internal static class LegacyProviderCredentialCleaner
{
    public static void Remove()
    {
        var applicationDirectory = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolGenPostVideo"));
        RemoveFromDirectory(applicationDirectory);
    }

    internal static void RemoveFromDirectory(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        applicationDirectory = Path.GetFullPath(applicationDirectory);
        var settingsPath = Path.GetFullPath(Path.Combine(applicationDirectory, "provider-secrets.bin"));
        if (!settingsPath.StartsWith(
                applicationDirectory + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Đường dẫn credential cũ không an toàn.");
        }

        DeleteIfPresent(settingsPath);
        DeleteIfPresent(settingsPath + ".tmp");
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
