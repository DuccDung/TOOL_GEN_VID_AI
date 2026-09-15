using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TOOL_LOCAL.SystemSetup;

internal static class SystemSetupPaths
{
    public static string MetadataRoot => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "ToolGenPostVideo", "system-setup");
    public static string ComponentsRoot => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "ToolGenPostVideo", "components");

    public static void EnsureSafeDirectory(string path)
    {
        var full = Path.GetFullPath(path);
        for (var item = new DirectoryInfo(full); item is not null; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Component directory cannot be a reparse point.");
        Directory.CreateDirectory(full);
    }

    // This is readiness evidence only, never an authorization identity or a value sent to the DOM.
    public static string MachineFingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Environment.MachineName + "|" + Environment.UserDomainName + "|" + Environment.UserName + "|" +
        System.Runtime.InteropServices.RuntimeInformation.OSArchitecture)));
}

internal sealed class SystemSetupJournal(string root)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string FileFor(string userId, Guid organizationId) => Path.Combine(root,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId + "|" + organizationId))) + ".json");
    public SetupOperation? Read(string userId, Guid organizationId)
    {
        try
        {
            var path = FileFor(userId, organizationId);
            if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024) return null;
            var value = JsonSerializer.Deserialize<SetupOperation>(File.ReadAllText(path), Json);
            if (value is null || value.ComponentIds is not { Length: > 0 and <= 4 }
                || value.ComponentIds.Any(id => id is not ("ocr" or "media" or "qwen" or "piper"))) return null;
            return value with { State = value.State is "Accepted" or "Running" ? "Interrupted" : value.State,
                AllSelectedReady = false, AllRequiredReady = false, Sequence = value.Sequence + 1 };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    public void Write(string userId, Guid organizationId, SetupOperation operation)
    {
        SystemSetupPaths.EnsureSafeDirectory(root);
        var destination = FileFor(userId, organizationId);
        var part = destination + ".part";
        if (new[] { destination, part }.Any(p => File.Exists(p) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("Invalid journal file.");
        File.WriteAllText(part, JsonSerializer.Serialize(operation, Json));
        File.Move(part, destination, true);
    }
}
