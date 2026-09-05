using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using TOOL_LOCAL.Media;

namespace TOOL_LOCAL.Vietsub.Ocr;

internal sealed record VietsubRawVideoFrame(
    long FrameIndex,
    long TimestampMilliseconds,
    int Width,
    int Height,
    byte[] Bgr24Pixels);

internal interface IVietsubOcrFrameReader
{
    IAsyncEnumerable<VietsubRawVideoFrame> ReadAsync(
        string sourcePath,
        int sourceWidth,
        int sourceHeight,
        int rotationDegrees,
        VietsubNormalizedRegion normalizedRegion,
        VietsubOcrProfile profile,
        long startMilliseconds = 0,
        CancellationToken cancellationToken = default);
}

internal sealed class VietsubFfmpegFrameReader(
    string ffmpegPath,
    IMediaToolPreflightService preflight) : IVietsubOcrFrameReader
{
    private const int MaximumDiagnosticCharacters = 8 * 1024;
    private const int FrameBufferCapacity = 4;

    public async IAsyncEnumerable<VietsubRawVideoFrame> ReadAsync(
        string sourcePath,
        int sourceWidth,
        int sourceHeight,
        int rotationDegrees,
        VietsubNormalizedRegion normalizedRegion,
        VietsubOcrProfile profile,
        long startMilliseconds = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var absolutePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(absolutePath))
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.VideoNotReady,
                "Không tìm thấy video nguồn để đọc frame.");
        }
        if (startMilliseconds < 0)
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.TimestampInvalid,
                "Timestamp bắt đầu OCR không hợp lệ.");
        }

        try
        {
            await preflight.RequireReadyAsync(cancellationToken);
        }
        catch (MediaToolUnavailableException exception)
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.RuntimeNotInstalled,
                exception.Message,
                exception);
        }

        var pixelRegion = VietsubOcrRegionResolver.Resolve(
            sourceWidth,
            sourceHeight,
            rotationDegrees,
            normalizedRegion,
            profile.MaximumWidth);
        var arguments = BuildArguments(absolutePath, pixelRegion, profile, startMilliseconds);
        using var process = new Process
        {
            StartInfo = CreateStartInfo(ffmpegPath, arguments),
            EnableRaisingEvents = true
        };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException();
            }
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.RuntimeNotInstalled,
                "Không thể khởi động FFmpeg để đọc frame OCR.",
                exception);
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var channel = Channel.CreateBounded<VietsubRawVideoFrame>(new BoundedChannelOptions(FrameBufferCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        var stderrTask = DrainDiagnosticAsync(process.StandardError);
        var producerTask = ProduceFramesAsync(
            process,
            channel.Writer,
            pixelRegion,
            profile.SampleIntervalMilliseconds,
            startMilliseconds,
            linkedCancellation.Token);

        try
        {
            await foreach (var frame in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return frame;
            }
            await producerTask;
            var diagnostic = await stderrTask;
            if (process.ExitCode != 0)
            {
                throw new VietsubOcrException(
                    VietsubOcrErrorCodes.FrameExtractionFailed,
                    string.IsNullOrWhiteSpace(diagnostic)
                        ? "FFmpeg không thể trích xuất frame OCR."
                        : "FFmpeg không thể trích xuất frame OCR; hãy kiểm tra lại video nguồn.");
            }
        }
        finally
        {
            linkedCancellation.Cancel();
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
            }
            try
            {
                await producerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
            {
            }
            try
            {
                await stderrTask.ConfigureAwait(false);
            }
            catch (IOException)
            {
            }
        }
    }

    internal static IReadOnlyList<string> BuildArguments(
        string absolutePath,
        VietsubOcrPixelRegion region,
        VietsubOcrProfile profile,
        long startMilliseconds)
    {
        var intervalSeconds = profile.SampleIntervalMilliseconds / 1000d;
        var filter = string.Create(
            CultureInfo.InvariantCulture,
            $"fps=1/{intervalSeconds:0.###},crop={region.Width}:{region.Height}:{region.X}:{region.Y}," +
            $"scale={region.OutputWidth}:{region.OutputHeight}:flags=bilinear,format=bgr24");
        var arguments = new List<string>
        {
            "-hide_banner",
            "-loglevel", "error",
            "-nostdin"
        };
        if (startMilliseconds > 0)
        {
            arguments.Add("-ss");
            arguments.Add((startMilliseconds / 1000d).ToString("0.###", CultureInfo.InvariantCulture));
        }
        arguments.AddRange(
        [
            "-i", absolutePath,
            "-map", "0:v:0",
            "-an", "-sn", "-dn",
            "-vf", filter,
            "-f", "rawvideo",
            "-pix_fmt", "bgr24",
            "pipe:1"
        ]);
        return arguments;
    }

    private static ProcessStartInfo CreateStartInfo(
        string executable,
        IReadOnlyList<string> arguments)
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
        return startInfo;
    }

    private static async Task ProduceFramesAsync(
        Process process,
        ChannelWriter<VietsubRawVideoFrame> writer,
        VietsubOcrPixelRegion region,
        int sampleIntervalMilliseconds,
        long startMilliseconds,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            var frameSize = checked(region.OutputWidth * region.OutputHeight * 3);
            var index = 0L;
            while (true)
            {
                var pixels = GC.AllocateUninitializedArray<byte>(frameSize);
                if (!await ReadFrameAsync(process.StandardOutput.BaseStream, pixels, cancellationToken))
                {
                    break;
                }
                await writer.WriteAsync(
                    new VietsubRawVideoFrame(
                        index,
                        checked(startMilliseconds + index * sampleIntervalMilliseconds),
                        region.OutputWidth,
                        region.OutputHeight,
                        pixels),
                    cancellationToken);
                index++;
            }
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                failure = new VietsubOcrException(
                    VietsubOcrErrorCodes.FrameExtractionFailed,
                    "FFmpeg không thể trích xuất frame OCR.");
            }
        }
        catch (OperationCanceledException exception)
        {
            failure = exception;
        }
        catch (Exception exception) when (exception is IOException or OverflowException or InvalidDataException)
        {
            failure = new VietsubOcrException(
                VietsubOcrErrorCodes.FrameExtractionFailed,
                "Luồng frame OCR từ FFmpeg không hợp lệ.",
                exception);
        }
        finally
        {
            writer.TryComplete(failure);
        }
    }

    private static async Task<bool> ReadFrameAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                if (offset == 0)
                {
                    return false;
                }
                throw new InvalidDataException("FFmpeg trả về frame raw không đủ byte.");
            }
            offset += read;
        }
        return true;
    }

    private static async Task<string> DrainDiagnosticAsync(StreamReader reader)
    {
        var result = new StringBuilder(MaximumDiagnosticCharacters);
        var buffer = new char[1024];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory());
            if (read == 0)
            {
                return SanitizeDiagnostic(result.ToString());
            }
            if (result.Length < MaximumDiagnosticCharacters)
            {
                result.Append(buffer, 0, Math.Min(read, MaximumDiagnosticCharacters - result.Length));
            }
        }
    }

    private static string SanitizeDiagnostic(string value)
    {
        var singleLine = string.Join(
            ' ',
            value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return singleLine.Length <= MaximumDiagnosticCharacters
            ? singleLine
            : singleLine[..MaximumDiagnosticCharacters];
    }
}
