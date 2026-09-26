using System.Text.Json;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Voice;

namespace TOOL_LOCAL.SystemSetup;

// Explicit maintenance operation, isolated from deployment settings, login and SQL.
internal static class PiperOfflineCommand
{
    internal static bool Matches(string[] args) => args.Length > 0
        && args[0] is "--prepare-piper-offline" or "--verify-piper-offline";

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length != 3 || args[1] != "--workspace" || !Path.IsPathFullyQualified(args[2]))
                throw new ArgumentException("An explicit absolute diagnostic workspace is required.");
            var workspace = Path.GetFullPath(args[2]);
            PiperOfflineBundle.AssertSafePath(workspace);
            var marker = Path.Combine(workspace, ".piper-offline-diagnostic");
            if (!File.Exists(marker))
            {
                if (args[0] != "--prepare-piper-offline"
                    || Directory.Exists(workspace) && Directory.EnumerateFileSystemEntries(workspace).Any())
                    throw new ArgumentException("Use a new, empty diagnostic workspace.");
                SystemSetupPaths.EnsureSafeDirectory(workspace);
                File.WriteAllText(marker, "Piper offline diagnostic workspace v1");
            }
            using var store = new VietsubVoiceComponentStore(new VietsubAppPaths(workspace), true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var status = args[0] == "--prepare-piper-offline"
                ? await store.InstallAsync(null, timeout.Token) : await store.VerifyAsync(timeout.Token);
            Console.WriteLine(JsonSerializer.Serialize(new { PiperOfflineReady = status.Ready,
                BundleVersion = PiperOfflineBundle.ApprovedDefinition().BundleVersion, status.ErrorCode,
                NetworkDownloadsAllowed = false, ServerAccessChecked = false }));
            return status.Ready ? 0 : 2;
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { PiperOfflineReady = false,
                ErrorCode = error is VietsubVoiceException voice ? voice.Code : "piper_offline_diagnostics_failed" }));
            return 2;
        }
    }
}
