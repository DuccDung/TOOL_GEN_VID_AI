using System.Diagnostics;
using System.Text.Json;
using TOOL_LOCAL.Configuration;

namespace TOOL_LOCAL.LocalVoice;

// Component maintenance only: no login, database, project mutation or media conversion.
internal static class LocalVoiceMaintenance
{
    internal static bool IsMaintenanceCommand(string[] args) => args.Length == 1 &&
        args[0] is "--prepare-local-voice" or "--verify-local-voice" or "--check-local-voice";

    public static async Task<int> RunAsync(string command)
    {
        try
        {
            var options = DesktopOptions.Load();
            var workspace = Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.Storage.WorkspaceRoot));
            var runtime = new LocalVoiceRuntime(workspace, options.Features.VeoLocalVoiceConsistencyEnabled,
                componentRootOverride: options.LocalVoice.ComponentRoot, temporaryRootOverride: options.LocalVoice.TemporaryRoot);
            if (!runtime.FeatureEnabled) throw new InvalidOperationException("Tính năng đồng nhất giọng local đang tắt.");
            var timer = Stopwatch.StartNew();
            if (command == "--prepare-local-voice") await runtime.InstallAsync(CancellationToken.None);
            else if (command == "--verify-local-voice") await runtime.ProbeInstalledAsync(CancellationToken.None);
            var status = runtime.GetStatus();
            Console.WriteLine(JsonSerializer.Serialize(new { status.Status, status.Message,
                elapsedSeconds = Math.Round(timer.Elapsed.TotalSeconds, 1), peakProcessBytes = runtime.LastProcessPeakBytes,
                processCpuSeconds = runtime.LastProcessCpuSeconds }));
            return status.Status == "READY" ? 0 : 2;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or
            ArgumentException or JsonException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            Console.Error.WriteLine("Không chuẩn bị/xác minh được runtime local. Kiểm tra cờ, đường dẫn, dung lượng, mạng và checksum.");
            return 1;
        }
    }
}
