namespace TOOL_UPDATER;

internal sealed record UpdaterOptions(
    int LauncherProcessId,
    string StageDirectory,
    string TargetDirectory,
    string RestartExecutableName,
    string LogPath)
{
    public static UpdaterOptions Parse(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Count; index += 2)
        {
            if (index + 1 >= args.Count || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Tham số updater không hợp lệ.");
            }

            values[args[index][2..]] = args[index + 1];
        }

        if (!values.TryGetValue("pid", out var pidText) || !int.TryParse(pidText, out var pid) || pid <= 0 ||
            !values.TryGetValue("stage", out var stage) ||
            !values.TryGetValue("target", out var target) ||
            !values.TryGetValue("restart", out var restart) ||
            !values.TryGetValue("log", out var log))
        {
            throw new ArgumentException("Updater thiếu tham số bắt buộc.");
        }

        var stagePath = Path.GetFullPath(stage);
        var targetPath = Path.GetFullPath(target);
        if (!Directory.Exists(stagePath) || string.Equals(stagePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Thư mục staging hoặc thư mục đích không hợp lệ.");
        }

        var restartName = Path.GetFileName(restart);
        if (string.IsNullOrWhiteSpace(restartName) || !string.Equals(restartName, restart, StringComparison.Ordinal))
        {
            throw new ArgumentException("Tên executable khởi động lại không hợp lệ.");
        }

        return new UpdaterOptions(pid, stagePath, targetPath, restartName, Path.GetFullPath(log));
    }
}
