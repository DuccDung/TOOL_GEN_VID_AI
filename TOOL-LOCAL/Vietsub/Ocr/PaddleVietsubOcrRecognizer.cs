using System.Text.RegularExpressions;
using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;

namespace TOOL_LOCAL.Vietsub.Ocr;

internal sealed partial class PaddleVietsubOcrRecognizer : IVietsubOcrRecognizer
{
    private static readonly string[] SupportedLanguages =
        [VietsubOcrLanguageCodes.English, VietsubOcrLanguageCodes.Chinese];
    private readonly Dictionary<string, RecognizerSlot> _recognizers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _runtimeGate = new(1, 1);
    private readonly object _sync = new();
    private VietsubOcrRuntimeStatus? _runtimeStatus;
    private bool _disposed;

    public async Task<VietsubOcrRuntimeStatus> GetRuntimeStatusAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_runtimeStatus is not null)
        {
            return _runtimeStatus;
        }

        await _runtimeGate.WaitAsync(cancellationToken);
        try
        {
            if (_runtimeStatus is not null)
            {
                return _runtimeStatus;
            }

            _runtimeStatus = await Task.Run(ProbeRuntime, cancellationToken);
            return _runtimeStatus;
        }
        finally
        {
            _runtimeGate.Release();
        }
    }

    public async Task<VietsubOcrRecognitionResult> RecognizeAsync(
        VietsubRawVideoFrame frame,
        string languageCode,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        var expectedLength = checked(frame.Width * frame.Height * 3);
        if (frame.Width < 1 || frame.Height < 1 || frame.Bgr24Pixels.Length != expectedLength)
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.InferenceFailed,
                "Frame BGR24 không hợp lệ để nhận dạng OCR.");
        }

        var normalizedLanguage = VietsubOcrLanguageCodes.Normalize(languageCode);
        var runtime = await GetRuntimeStatusAsync(cancellationToken);
        if (!runtime.Ready)
        {
            throw new VietsubOcrException(
                runtime.ErrorCode ?? VietsubOcrErrorCodes.RuntimeInvalid,
                runtime.Message);
        }

        RecognizerSlot recognizer;
        try
        {
            recognizer = GetOrCreateRecognizer(normalizedLanguage);
        }
        catch (Exception exception) when (IsNativeOrModelFailure(exception))
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.ModelLoadFailed,
                $"Không thể nạp model PaddleOCR cho ngôn ngữ {normalizedLanguage}.",
                exception);
        }

        return await recognizer.RecognizeAsync(frame, cancellationToken);
    }

    public VietsubOcrRecognizerDiagnostics GetDiagnostics()
    {
        lock (_sync)
        {
            return new VietsubOcrRecognizerDiagnostics(
                _recognizers.Values.Sum(item => item.DirectRecognitionFrames),
                _recognizers.Values.Sum(item => item.FullDetectionFrames));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _runtimeGate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }
            lock (_sync)
            {
                foreach (var recognizer in _recognizers.Values)
                {
                    recognizer.Dispose();
                }
                _recognizers.Clear();
                _disposed = true;
            }
        }
        finally
        {
            _runtimeGate.Release();
            _runtimeGate.Dispose();
        }
    }

    private VietsubOcrRuntimeStatus ProbeRuntime()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
        {
            return new VietsubOcrRuntimeStatus(
                false,
                VietsubOcrErrorCodes.RuntimeInvalid,
                "PaddleOCR local yêu cầu Windows 64-bit.",
                []);
        }

        try
        {
            _ = Cv2.GetVersionString();
            _ = LocalFullModels.EnglishV5;
            _ = LocalFullModels.ChineseV5;
            _ = GetOrCreateRecognizer(VietsubOcrLanguageCodes.English);
            _ = GetOrCreateRecognizer(VietsubOcrLanguageCodes.Chinese);
            return new VietsubOcrRuntimeStatus(
                true,
                null,
                "PaddleOCR V5 local đã sẵn sàng.",
                SupportedLanguages);
        }
        catch (Exception exception) when (IsNativeOrModelFailure(exception))
        {
            return new VietsubOcrRuntimeStatus(
                false,
                VietsubOcrErrorCodes.RuntimeInvalid,
                $"Không thể khởi tạo PaddleOCR local: {NormalizeDiagnostic(exception.Message)}",
                []);
        }
    }

    private RecognizerSlot GetOrCreateRecognizer(string languageCode)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_recognizers.TryGetValue(languageCode, out var existing))
            {
                return existing;
            }

            var created = new RecognizerSlot(languageCode);
            _recognizers.Add(languageCode, created);
            return created;
        }
    }

    private static bool IsNativeOrModelFailure(Exception exception) => exception is
        DllNotFoundException or
        BadImageFormatException or
        FileNotFoundException or
        FileLoadException or
        TypeInitializationException or
        InvalidOperationException or
        OpenCVException;

    private static string NormalizeDiagnostic(string? message)
    {
        var normalized = string.IsNullOrWhiteSpace(message)
            ? "runtime hoặc model không hợp lệ"
            : WhitespaceRegex().Replace(message, " ").Trim();
        return normalized.Length <= 300 ? normalized : normalized[..300];
    }

    private sealed class RecognizerSlot : IDisposable
    {
        private readonly PaddleOcrAll _engine;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private long _directRecognitionFrames;
        private long _fullDetectionFrames;
        private bool _disposed;

        public RecognizerSlot(string languageCode)
        {
            var model = languageCode switch
            {
                VietsubOcrLanguageCodes.English => LocalFullModels.EnglishV5,
                VietsubOcrLanguageCodes.Chinese => LocalFullModels.ChineseV5,
                _ => throw new ArgumentOutOfRangeException(nameof(languageCode))
            };
            _engine = new PaddleOcrAll(
                model,
                PaddleDevice.OneDnn(
                    cacheCapacity: 10,
                    cpuMathThreadCount: Math.Clamp(Environment.ProcessorCount / 2, 2, 12),
                    memoryOptimized: true,
                    glogEnabled: false))
            {
                AllowRotateDetection = false,
                Enable180Classification = false
            };
        }

        public long DirectRecognitionFrames => Interlocked.Read(ref _directRecognitionFrames);

        public long FullDetectionFrames => Interlocked.Read(ref _fullDetectionFrames);

        public async Task<VietsubOcrRecognitionResult> RecognizeAsync(
            VietsubRawVideoFrame frame,
            CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _gate.WaitAsync(cancellationToken);
            try
            {
                return await Task.Run(
                    () => RecognizeCore(frame, cancellationToken),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (VietsubOcrException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new VietsubOcrException(
                    VietsubOcrErrorCodes.InferenceFailed,
                    "Không thể nhận dạng phụ đề bằng PaddleOCR local.",
                    exception);
            }
            finally
            {
                _gate.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _engine.Dispose();
            _gate.Dispose();
            _disposed = true;
        }

        private VietsubOcrRecognitionResult RecognizeCore(
            VietsubRawVideoFrame frame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var image = Mat.FromPixelData(
                frame.Height,
                frame.Width,
                MatType.CV_8UC3,
                frame.Bgr24Pixels);
            IReadOnlyList<TextLine> lines;
            if (TryRecognizeSubtitleLines(image, cancellationToken, out var directLines))
            {
                Interlocked.Increment(ref _directRecognitionFrames);
                lines = directLines;
            }
            else
            {
                Interlocked.Increment(ref _fullDetectionFrames);
                var result = _engine.Run(image);
                cancellationToken.ThrowIfCancellationRequested();
                lines = result.Regions
                    .Where(region => region.Score >= 0.45f)
                    .Select(region => new TextLine(
                        WhitespaceRegex().Replace(region.Text, " ").Trim(),
                        region.Score))
                    .Where(line => line.Text.Length > 0)
                    .ToArray();
            }

            if (lines.Count == 0)
            {
                return new VietsubOcrRecognitionResult(string.Empty, 0);
            }
            return new VietsubOcrRecognitionResult(
                string.Join('\n', lines.Select(line => line.Text)),
                lines.Average(line => line.Confidence));
        }

        private bool TryRecognizeSubtitleLines(
            Mat image,
            CancellationToken cancellationToken,
            out IReadOnlyList<TextLine> lines)
        {
            lines = [];
            if (!VietsubOcrSubtitleLineSegmenter.TrySegment(
                    image,
                    out var crops,
                    out var hasTextCandidates))
            {
                return !hasTextCandidates;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var results = _engine.Recognizer.Run(crops.ToArray(), batchSize: 2);
                cancellationToken.ThrowIfCancellationRequested();
                var recognized = results
                    .Select(result => new TextLine(
                        WhitespaceRegex().Replace(result.Text, " ").Trim(),
                        result.Score))
                    .Where(line => line.Text.Length > 0)
                    .ToArray();
                if (recognized.Length != crops.Count
                    || recognized.Any(line => line.Confidence < 0.72f))
                {
                    return false;
                }
                lines = recognized;
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
            finally
            {
                foreach (var crop in crops)
                {
                    crop.Dispose();
                }
            }
        }

        private sealed record TextLine(string Text, float Confidence);
    }

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}
