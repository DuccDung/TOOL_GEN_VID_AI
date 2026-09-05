using System.Diagnostics;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using OpenCvSharp;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub.Ocr;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubPaddleOcrIntegrationTests
{
    [Fact]
    [Trait("Category", "OcrIntegration")]
    public async Task PaddleRuntime_RecognizesGeneratedEnglishSubtitleFrame()
    {
        const int width = 1200;
        const int height = 240;
        var pixels = new byte[width * height * 3];
        Array.Fill(pixels, (byte)255);
        using (var image = Mat.FromPixelData(height, width, MatType.CV_8UC3, pixels))
        {
            Cv2.PutText(
                image,
                "HELLO SUBVID",
                new OpenCvSharp.Point(90, 155),
                HersheyFonts.HersheyDuplex,
                2.2,
                Scalar.Black,
                5,
                LineTypes.AntiAlias);
        }

        await using var recognizer = new PaddleVietsubOcrRecognizer();
        var runtime = await recognizer.GetRuntimeStatusAsync(CancellationToken.None);
        Assert.True(runtime.Ready, runtime.Message);
        Assert.Contains(VietsubOcrLanguageCodes.English, runtime.AvailableLanguages);
        Assert.Contains(VietsubOcrLanguageCodes.Chinese, runtime.AvailableLanguages);

        var result = await recognizer.RecognizeAsync(
            new VietsubRawVideoFrame(0, 0, width, height, pixels),
            VietsubOcrLanguageCodes.English,
            CancellationToken.None);

        Assert.True(result.Confidence >= 0.45f, $"Low confidence: {result.Confidence}; text: {result.Text}");
        Assert.Contains("SUBVID", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "OcrIntegration")]
    public async Task PaddleRuntime_RecognizesChineseSubtitleFixture()
    {
        const int width = 1000;
        const int height = 200;
        var fontPath = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "msyh.ttc"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "simhei.ttf"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "simsun.ttc")
            }
            .FirstOrDefault(File.Exists);
        Assert.True(fontPath is not null, "A Windows CJK font is required by the Chinese OCR integration fixture.");

        var pixels = new byte[width * height * 3];
        using (var fontCollection = new PrivateFontCollection())
        using (var bitmap = new System.Drawing.Bitmap(width, height, PixelFormat.Format24bppRgb))
        {
            fontCollection.AddFontFile(fontPath);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            using var font = new System.Drawing.Font(
                fontCollection.Families[0],
                72,
                System.Drawing.FontStyle.Bold,
                System.Drawing.GraphicsUnit.Pixel);
            graphics.Clear(System.Drawing.Color.White);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.DrawString(
                "\u4E2D\u6587\u5B57\u5E55",
                font,
                System.Drawing.Brushes.Black,
                110,
                45);
            var data = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);
            try
            {
                Assert.Equal(width * 3, data.Stride);
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        await using var recognizer = new PaddleVietsubOcrRecognizer();
        var runtime = await recognizer.GetRuntimeStatusAsync(CancellationToken.None);
        Assert.True(runtime.Ready, runtime.Message);
        Assert.Contains(VietsubOcrLanguageCodes.Chinese, runtime.AvailableLanguages);

        var result = await recognizer.RecognizeAsync(
            new VietsubRawVideoFrame(0, 0, width, height, pixels),
            VietsubOcrLanguageCodes.Chinese,
            CancellationToken.None);

        Assert.True(result.Confidence >= 0.45f, $"Low confidence: {result.Confidence}; text: {result.Text}");
        Assert.Contains("\u6587\u5B57\u5E55", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "OcrIntegration")]
    public async Task LocalPipeline_RecognizesHardSubtitleFromRealVideoFrame()
    {
        var ffmpegPath = Path.Combine(
            FindRepositoryRoot(),
            "third_party",
            "ffmpeg",
            "win-x64",
            "ffmpeg.exe");
        Assert.True(File.Exists(ffmpegPath), $"FFmpeg test bundle was not found: {ffmpegPath}");
        var tempRoot = Path.Combine(Path.GetTempPath(), "VIDEOMAKER_OCR_PIPELINE_TEST", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            const int width = 1280;
            const int height = 720;
            var imagePath = Path.Combine(tempRoot, "subtitle.png");
            using (var image = new Mat(new OpenCvSharp.Size(width, height), MatType.CV_8UC3, Scalar.Black))
            {
                Cv2.PutText(
                    image,
                    "HELLO SUBVID",
                    new OpenCvSharp.Point(300, 620),
                    HersheyFonts.HersheyDuplex,
                    2.2,
                    Scalar.White,
                    5,
                    LineTypes.AntiAlias);
                Assert.True(Cv2.ImWrite(imagePath, image));
            }

            var videoPath = Path.Combine(tempRoot, "hard-subtitle.mp4");
            await RunProcessAsync(
                ffmpegPath,
                [
                    "-y", "-v", "error",
                    "-loop", "1", "-i", imagePath,
                    "-t", "1", "-r", "10",
                    "-c:v", "mpeg4", "-pix_fmt", "yuv420p",
                    videoPath
                ]);

            var frameReader = new VietsubFfmpegFrameReader(ffmpegPath, new ReadyMediaPreflight());
            VietsubRawVideoFrame? extractedFrame = null;
            await foreach (var frame in frameReader.ReadAsync(
                               videoPath,
                               width,
                               height,
                               0,
                               VietsubNormalizedRegion.Default,
                               VietsubOcrProfile.Resolve(VietsubOcrProfileNames.Balanced)))
            {
                extractedFrame = frame;
                break;
            }
            Assert.NotNull(extractedFrame);

            await using var recognizer = new PaddleVietsubOcrRecognizer();
            var result = await recognizer.RecognizeAsync(
                extractedFrame,
                VietsubOcrLanguageCodes.English,
                CancellationToken.None);

            Assert.True(result.Confidence >= 0.45f, $"Low confidence: {result.Confidence}; text: {result.Text}");
            Assert.Contains("SUBVID", result.Text, StringComparison.OrdinalIgnoreCase);
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
            ?? throw new InvalidOperationException("Could not start the FFmpeg test process.");
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
        throw new DirectoryNotFoundException("Could not locate the VideoMaker repository root from test output.");
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
