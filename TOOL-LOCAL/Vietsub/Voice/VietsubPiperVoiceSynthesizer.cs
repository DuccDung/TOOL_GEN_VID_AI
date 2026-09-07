using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TOOL_LOCAL.Vietsub.Voice;

internal sealed record VietsubPiperWorkerEvent(
    int ProtocolVersion,
    string RequestId,
    string Type,
    int? Index = null,
    int? Written = null,
    int? Total = null);

internal sealed class VietsubPiperVoiceSynthesizer(VietsubVoiceComponentStore components)
    : IVietsubVoiceSynthesizer
{
    private const int ProtocolVersion = 1;
    private const int MaximumEventCharacters = 16 * 1024;
    private const int MaximumErrorCharacters = 32 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SynthesizeIncrementallyAsync(
        IReadOnlyList<VietsubVoiceSynthesisItem> items,
        Func<VietsubVoiceSynthesisItem, ValueTask> onCompleted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(onCompleted);
        if (items.Count == 0) return;
        var paths = components.RequireReady();
        var requestId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(paths.RequestDirectory);
        var requestPath = Path.Combine(paths.RequestDirectory, $"{requestId}.request.json");
        var request = new
        {
            protocolVersion = ProtocolVersion,
            requestId,
            modelPath = paths.ModelPath,
            configPath = paths.ConfigPath,
            volume = 1.0,
            lengthScale = 1.0,
            items = items.Select(item => new { item.Index, item.Text, item.OutputPath }).ToArray()
        };
        await File.WriteAllTextAsync(
            requestPath,
            JsonSerializer.Serialize(request, JsonOptions),
            new UTF8Encoding(false),
            cancellationToken);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = paths.PythonPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            startInfo.ArgumentList.Add("-X");
            startInfo.ArgumentList.Add("utf8");
            startInfo.ArgumentList.Add("-I");
            startInfo.ArgumentList.Add(paths.WorkerPath);
            startInfo.ArgumentList.Add(requestPath);
            startInfo.Environment["PYTHONUTF8"] = "1";
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            startInfo.Environment["PYTHONNOUSERSITE"] = "1";
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new VietsubVoiceException(VietsubVoiceErrorCodes.WorkerFailed, "Không thể khởi động Piper worker.", true);
            }

            using var inactivity = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            inactivity.CancelAfter(TimeSpan.FromMinutes(10));
            var stderrTask = ReadLimitedAsync(process.StandardError, MaximumErrorCharacters, CancellationToken.None);
            Dictionary<int, VietsubVoiceSynthesisItem> itemsByIndex;
            try
            {
                itemsByIndex = items.ToDictionary(item => item.Index);
            }
            catch (ArgumentException exception)
            {
                throw new VietsubVoiceException(
                    VietsubVoiceErrorCodes.WorkerProtocolInvalid,
                    "Danh sách phrase gửi Piper có index trùng.",
                    innerException: exception);
            }
            var completed = new HashSet<int>();
            var workerCompleted = false;
            try
            {
                while (true)
                {
                    var line = await process.StandardOutput.ReadLineAsync(inactivity.Token);
                    if (line is null) break;
                    inactivity.CancelAfter(TimeSpan.FromMinutes(10));
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.Length > MaximumEventCharacters) throw ProtocolError();
                    VietsubPiperWorkerEvent workerEvent;
                    try
                    {
                        workerEvent = JsonSerializer.Deserialize<VietsubPiperWorkerEvent>(line, JsonOptions)
                            ?? throw new JsonException();
                    }
                    catch (JsonException exception)
                    {
                        throw new VietsubVoiceException(VietsubVoiceErrorCodes.WorkerProtocolInvalid, "Piper worker trả về event không hợp lệ.", innerException: exception);
                    }
                    if (workerEvent.ProtocolVersion != ProtocolVersion
                        || !string.Equals(workerEvent.RequestId, requestId, StringComparison.Ordinal))
                    {
                        throw ProtocolError();
                    }
                    if (workerEvent.Type == "item_completed")
                    {
                        var index = workerEvent.Index ?? -1;
                        if (index < 0 || !itemsByIndex.TryGetValue(index, out var item) || !completed.Add(index))
                        {
                            throw ProtocolError();
                        }
                        _ = VietsubWavInspector.Inspect(item.OutputPath, analyzeSilence: false);
                        await onCompleted(item);
                    }
                    else if (workerEvent.Type == "completed")
                    {
                        workerCompleted = true;
                    }
                }
                await process.WaitForExitAsync(inactivity.Token);
                var error = await stderrTask;
                if (process.ExitCode != 0)
                {
                    throw new VietsubVoiceException(VietsubVoiceErrorCodes.WorkerFailed, "Piper worker thất bại: " + LastLine(error), true);
                }
                if (!workerCompleted || completed.Count != items.Count) throw ProtocolError();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Kill(process);
                throw new VietsubVoiceException(VietsubVoiceErrorCodes.WorkerTimeout, "Piper worker không phản hồi trong thời gian cho phép.", true);
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                throw;
            }
            catch
            {
                Kill(process);
                throw;
            }
        }
        finally
        {
            TryDelete(requestPath);
        }
    }

    private static VietsubVoiceException ProtocolError() =>
        new(VietsubVoiceErrorCodes.WorkerProtocolInvalid, "Piper worker trả về protocol thiếu, thừa hoặc sai thứ tự.");

    private static async Task<string> ReadLimitedAsync(StreamReader reader, int maximum, CancellationToken cancellationToken)
    {
        var buffer = new char[2048];
        var result = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) return result.ToString();
            if (result.Length < maximum) result.Append(buffer, 0, Math.Min(read, maximum - result.Length));
        }
    }

    private static string LastLine(string value) => value
        .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .LastOrDefault() is { } line
            ? line[..Math.Min(300, line.Length)]
            : "không có thông tin lỗi.";

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
