namespace TOOL_LOCAL.Vietsub.Ocr;

internal enum VietsubOcrFrameDecisionKind
{
    Hold,
    Reuse,
    Recognize
}

internal readonly record struct VietsubOcrFrameDecision(
    VietsubOcrFrameDecisionKind Kind,
    long TimestampMilliseconds);

internal sealed class VietsubOcrFrameChangeTracker(
    int maximumReuseFrames,
    double changeThresholdRatio)
{
    private readonly int _maximumReuseFrames = Math.Max(1, maximumReuseFrames);
    private readonly double _changeThresholdRatio = Math.Clamp(changeThresholdRatio, 0.001, 0.05);
    private byte[]? _previousRawSignature;
    private byte[]? _recognizedStableSignature;
    private long _pendingTimestamp;
    private bool _changePending;
    private int _framesSinceRecognition;

    public VietsubOcrFrameDecision Analyze(byte[] rawSignature, long timestampMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(rawSignature);
        if (rawSignature.Length == 0)
        {
            throw new ArgumentException("Signature frame OCR không được để trống.", nameof(rawSignature));
        }
        if (_previousRawSignature is null)
        {
            _previousRawSignature = (byte[])rawSignature.Clone();
            _pendingTimestamp = timestampMilliseconds;
            _changePending = true;
            return new(VietsubOcrFrameDecisionKind.Hold, timestampMilliseconds);
        }
        if (_previousRawSignature.Length != rawSignature.Length)
        {
            throw new ArgumentException("Các signature OCR phải có cùng kích thước.", nameof(rawSignature));
        }

        var stableSignature = BuildTemporallyStableSignature(_previousRawSignature, rawSignature);
        _previousRawSignature = (byte[])rawSignature.Clone();
        if (_recognizedStableSignature is null)
        {
            UpdateRecognizedSignature(stableSignature);
            var firstTimestamp = _pendingTimestamp;
            _changePending = false;
            return new(VietsubOcrFrameDecisionKind.Recognize, firstTimestamp);
        }

        var unchanged = CalculateDifferenceRatio(_recognizedStableSignature, stableSignature)
            <= _changeThresholdRatio;
        if (_framesSinceRecognition >= _maximumReuseFrames)
        {
            UpdateRecognizedSignature(stableSignature);
            _changePending = false;
            return new(VietsubOcrFrameDecisionKind.Recognize, timestampMilliseconds);
        }
        if (!_changePending)
        {
            if (unchanged)
            {
                _framesSinceRecognition++;
                return new(VietsubOcrFrameDecisionKind.Reuse, timestampMilliseconds);
            }
            _changePending = true;
            _pendingTimestamp = timestampMilliseconds;
            return new(VietsubOcrFrameDecisionKind.Hold, timestampMilliseconds);
        }
        if (unchanged)
        {
            _changePending = false;
            _framesSinceRecognition++;
            return new(VietsubOcrFrameDecisionKind.Reuse, timestampMilliseconds);
        }

        UpdateRecognizedSignature(stableSignature);
        var effectiveTimestamp = _pendingTimestamp;
        _changePending = false;
        return new(VietsubOcrFrameDecisionKind.Recognize, effectiveTimestamp);
    }

    public static int ResolveMaximumReuseFrames(VietsubOcrProfile profile) =>
        Math.Max(1, profile.SafetyRefreshMilliseconds / profile.SampleIntervalMilliseconds - 1);

    public static byte[] BuildSignature(VietsubRawVideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var expectedLength = checked(frame.Width * frame.Height * 3);
        if (frame.Width < 1 || frame.Height < 1 || frame.Bgr24Pixels.Length != expectedLength)
        {
            throw new ArgumentException("Frame BGR24 không đúng kích thước.", nameof(frame));
        }

        const int signatureWidth = 160;
        var signatureHeight = Math.Clamp(
            (int)Math.Round(frame.Height * signatureWidth / (double)frame.Width),
            16,
            96);
        var luminance = new byte[signatureWidth * signatureHeight];
        for (var y = 0; y < signatureHeight; y++)
        {
            var sourceY = Math.Min(frame.Height - 1, y * frame.Height / signatureHeight);
            for (var x = 0; x < signatureWidth; x++)
            {
                var sourceX = Math.Min(frame.Width - 1, x * frame.Width / signatureWidth);
                var offset = (sourceY * frame.Width + sourceX) * 3;
                var blue = frame.Bgr24Pixels[offset];
                var green = frame.Bgr24Pixels[offset + 1];
                var red = frame.Bgr24Pixels[offset + 2];
                luminance[y * signatureWidth + x] = (byte)((29 * blue + 150 * green + 77 * red) >> 8);
            }
        }

        var signature = new byte[luminance.Length];
        for (var y = 1; y < signatureHeight - 1; y++)
        {
            for (var x = 1; x < signatureWidth - 1; x++)
            {
                var index = y * signatureWidth + x;
                var center = luminance[index];
                var surrounding = (
                    luminance[index - signatureWidth] +
                    luminance[index + signatureWidth] +
                    luminance[index - 1] +
                    luminance[index + 1]) / 4;
                signature[index] = center >= 150 && center - surrounding >= 12 ? (byte)1 : (byte)0;
            }
        }
        return signature;
    }

    internal static double CalculateDifferenceRatio(byte[] left, byte[] right)
    {
        if (left.Length != right.Length || left.Length == 0)
        {
            throw new ArgumentException("Các signature OCR phải có cùng kích thước khác 0.");
        }
        var different = 0;
        for (var index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                different++;
            }
        }
        return different / (double)left.Length;
    }

    internal static byte[] BuildTemporallyStableSignature(byte[] previous, byte[] current)
    {
        if (previous.Length != current.Length)
        {
            throw new ArgumentException("Các signature OCR phải có cùng kích thước.");
        }
        var stable = new byte[current.Length];
        for (var index = 0; index < current.Length; index++)
        {
            var left = Math.Max(0, index - 1);
            var right = Math.Min(current.Length - 1, index + 1);
            var previousNear = previous[left] != 0 || previous[index] != 0 || previous[right] != 0;
            var currentNear = current[left] != 0 || current[index] != 0 || current[right] != 0;
            stable[index] = (current[index] != 0 && previousNear)
                || (previous[index] != 0 && currentNear)
                    ? (byte)1
                    : (byte)0;
        }
        return stable;
    }

    private void UpdateRecognizedSignature(byte[] signature)
    {
        _recognizedStableSignature = (byte[])signature.Clone();
        _framesSinceRecognition = 0;
    }
}
