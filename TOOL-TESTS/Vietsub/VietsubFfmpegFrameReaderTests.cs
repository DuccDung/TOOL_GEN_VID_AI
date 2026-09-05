using System.Diagnostics;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub.Ocr;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubFfmpegFrameReaderTests
{
    [Fact]
    public void BuildArguments_UsesRawBgr24_CropScaleAndVideoOnlyMapping()
    {
        var profile = VietsubOcrProfile.Resolve(VietsubOcrProfileNames.Balanced);
        var region = new VietsubOcrPixelRegion(10, 400, 1000, 300, 1000, 300);

        var arguments = VietsubFfmpegFrameReader.BuildArguments(
            @"D:\video test\source.mp4",
            region,
            profile,
            1_250);

        Assert.Contains("0:v:0", arguments);
        Assert.Contains("-an", arguments);
        Assert.Contains("-sn", arguments);
        Assert.Contains("-dn", arguments);
        Assert.Contains("rawvideo", arguments);
        Assert.Contains("bgr24", arguments);
        Assert.Contains("1.25", arguments);
        Assert.Contains(
            "fps=1/0.25,crop=1000:300:10:400,scale=1000:300:flags=bilinear,format=bgr24",
            arguments);
        var materialized = arguments.ToArray();
        Assert.Equal(@"D:\video test\source.mp4", materialized[Array.IndexOf(materialized, "-i") + 1]);
    }

    [Fact]
    [Trait("Category", "OcrIntegration")]
    public async Task ReadAsync_StreamsRawFramesFromApprovedFfmpegBundle()
    {
        var ffmpegPath = Path.Combine(
            FindRepositoryRoot(),
            "third_party",
            "ffmpeg",
            "win-x64",
            "ffmpeg.exe");
        Assert.True(File.Exists(ffmpegPath), $"Không tìm thấy FFmpeg test bundle: {ffmpegPath}");
        var tempRoot = Path.Combine(Path.GetTempPath(), "VIDEOMAKER_OCR_FFMPEG_TEST", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var videoPath = Path.Combine(tempRoot, "source.mp4");
            await RunProcessAsync(
                ffmpegPath,
                [
                    "-y", "-v", "error",
                    "-f", "lavfi",
                    "-i", "testsrc=size=320x180:rate=10:duration=1",
                    "-c:v", "mpeg4",
                    videoPath
                ]);

            var reader = new VietsubFfmpegFrameReader(ffmpegPath, new ReadyMediaPreflight());
            var frames = new List<VietsubRawVideoFrame>();
            await foreach (var frame in reader.ReadAsync(
                               videoPath,
                               320,
                               180,
                               0,
                               VietsubNormalizedRegion.Default,
                               VietsubOcrProfile.Resolve(VietsubOcrProfileNames.Balanced)))
            {
                frames.Add(frame);
                if (frames.Count == 2)
                {
                    break;
                }
            }

            Assert.Equal(2, frames.Count);
            Assert.All(frames, frame =>
            {
                Assert.Equal(320, frame.Width);
                Assert.Equal(72, frame.Height);
                Assert.Equal(frame.Width * frame.Height * 3, frame.Bgr24Pixels.Length);
            });
            Assert.Equal(0, frames[0].TimestampMilliseconds);
            Assert.Equal(250, frames[1].TimestampMilliseconds);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static async Task RunProcessAsync(string executable, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Không thể khởi động FFmpeg test.");
        var diagnostic = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, diagnostic);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TOOL_GEN_POST_VIDEO.slnx")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Không tìm thấy root VideoMaker từ test output.");
    }

    private sealed class ReadyMediaPreflight : IMediaToolPreflightService
    {
        private static readonly MediaToolStatusSummary Ready = new(
            true,
            null,
            "ready",
            "test",
            "test",
            DateTime.UtcNow);

        public Task<MediaToolStatusSummary> GetStatusAsync(bool force, CancellationToken cancellationToken) =>
            Task.FromResult(Ready);

        public Task<MediaToolStatusSummary> RequireReadyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Ready);
    }
}
