using System.Diagnostics;
using System.Text;

namespace TOOL_LOCAL.Media;

public sealed record ProcessExecutionResult(int ExitCode, string StandardOutput, string StandardError);

public interface IExternalProcessRunner
{
    Task<ProcessExecutionResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed class ExternalProcessRunner : IExternalProcessRunner
{
    private const int MaximumCapturedCharacters = 200_000;

    public async Task<ProcessExecutionResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Không thể khởi động {executable}.");
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new FileNotFoundException($"Không tìm thấy hoặc không thể chạy media tool: {executable}", executable, exception);
        }

        var stdoutTask = ReadLimitedAsync(process.StandardOutput, cancellationToken);
        var stderrTask = ReadLimitedAsync(process.StandardError, cancellationToken);
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            if (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Media tool exceeded timeout {timeout}.");
            }

            throw;
        }

        return new ProcessExecutionResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var result = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                return result.ToString();
            }

            if (result.Length < MaximumCapturedCharacters)
            {
                result.Append(buffer, 0, Math.Min(read, MaximumCapturedCharacters - result.Length));
            }
        }
    }
}
