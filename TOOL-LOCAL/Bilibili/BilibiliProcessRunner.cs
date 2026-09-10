using System.Diagnostics;
using System.Text;

namespace TOOL_LOCAL.Bilibili;

internal interface IBilibiliProcessRunner
{
    Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory,
        Action<string> output, Action<string> error, TimeSpan timeout, CancellationToken cancellationToken);
}

internal sealed class BilibiliProcessRunner : IBilibiliProcessRunner
{
    public async Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory,
        Action<string> output, Action<string> error, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = workingDirectory
        };
        // Do not inherit app secrets, proxies, Python configuration or yt-dlp plugins.
        var environment = start.Environment.Where(pair => pair.Key.Equals("SystemRoot", StringComparison.OrdinalIgnoreCase)
            || pair.Key.Equals("WINDIR", StringComparison.OrdinalIgnoreCase)
            || pair.Key.Equals("TEMP", StringComparison.OrdinalIgnoreCase)
            || pair.Key.Equals("TMP", StringComparison.OrdinalIgnoreCase)).ToArray();
        start.Environment.Clear();
        foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        start.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new BilibiliException("bilibili_tool_start_failed", "Không khởi động được công cụ tải.");
        using var kill = linked.Token.Register(() => Kill(process));
        var stdout = PumpAsync(process.StandardOutput, output, linked.Token);
        var stderr = PumpAsync(process.StandardError, error, linked.Token);
        // A malformed/oversized line or failed callback must also stop the child process.
        _ = stdout.ContinueWith(_ => Kill(process), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        _ = stderr.ContinueWith(_ => Kill(process), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            return process.ExitCode;
        }
        catch
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); } catch { }
            if (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                throw new BilibiliException("bilibili_timeout", "Bilibili phản hồi quá lâu. Bạn có thể thử lại sau.");
            throw;
        }
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> consume, CancellationToken token)
    {
        var buffer = new char[4096];
        var line = new StringBuilder();
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (count == 0) break;
            for (var i = 0; i < count; i++)
            {
                if (buffer[i] == '\n') { consume(line.ToString().TrimEnd('\r')); line.Clear(); }
                else
                {
                    if (line.Length >= 2 * 1024 * 1024)
                        throw new BilibiliException("bilibili_response_too_large", "Phản hồi Bilibili vượt giới hạn an toàn.");
                    line.Append(buffer[i]);
                }
            }
        }
        if (line.Length > 0) consume(line.ToString());
    }
}
