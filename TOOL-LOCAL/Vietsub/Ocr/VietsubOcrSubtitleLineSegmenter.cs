using OpenCvSharp;

namespace TOOL_LOCAL.Vietsub.Ocr;

internal static class VietsubOcrSubtitleLineSegmenter
{
    internal static bool TrySegment(
        Mat image,
        out IReadOnlyList<Mat> lines,
        out bool hasTextCandidates)
    {
        lines = [];
        hasTextCandidates = false;
        if (image.Empty() || image.Rows < 24 || image.Cols < 80 || image.Rows > image.Cols)
        {
            return false;
        }

        using var glyphMask = BuildGlyphMask(image);
        var minimumActivePixels = Math.Max(3, (int)Math.Ceiling(image.Cols * 0.006));
        var activeRows = new bool[image.Rows];
        for (var row = 0; row < image.Rows; row++)
        {
            activeRows[row] = Cv2.CountNonZero(glyphMask.Row(row)) >= minimumActivePixels;
        }

        BridgeSmallGaps(activeRows, Math.Max(1, image.Rows / 40));
        var bands = FindBands(activeRows)
            .Where(band => band.Height >= Math.Max(3, image.Rows / 25))
            .ToList();
        if (bands.Count < 1)
        {
            return false;
        }

        var segmented = new List<Mat>(bands.Count);
        try
        {
            foreach (var band in bands)
            {
                var peakPixels = Enumerable.Range(band.Top, band.Height)
                    .Max(row => Cv2.CountNonZero(glyphMask.Row(row)));
                if (peakPixels < Math.Max(8, image.Cols / 50))
                {
                    continue;
                }

                var bandRect = new Rect(0, band.Top, image.Cols, band.Height);
                using var bandMask = new Mat(glyphMask, bandRect);
                using var points = new Mat();
                Cv2.FindNonZero(bandMask, points);
                if (points.Empty())
                {
                    continue;
                }

                var glyphBounds = Cv2.BoundingRect(points);
                glyphBounds.Y += band.Top;
                if (glyphBounds.Width < Math.Max(24, image.Cols / 10))
                {
                    continue;
                }

                var crop = ExpandAndClamp(
                    glyphBounds,
                    Math.Max(4, image.Cols / 80),
                    Math.Max(2, glyphBounds.Height / 8),
                    image.Size());
                if (crop.Width < 24 || crop.Height < 8 || crop.Width / (double)crop.Height < 1.4)
                {
                    continue;
                }

                using var cropMask = new Mat(glyphMask, crop);
                var occupancy = Cv2.CountNonZero(cropMask) / (double)(crop.Width * crop.Height);
                if (occupancy is < 0.008 or > 0.45)
                {
                    continue;
                }

                segmented.Add(new Mat(image, crop).Clone());
                hasTextCandidates = true;
                if (segmented.Count > 2)
                {
                    return false;
                }
            }

            if (segmented.Count == 0)
            {
                return false;
            }
            lines = segmented;
            return true;
        }
        finally
        {
            if (lines.Count == 0)
            {
                foreach (var line in segmented)
                {
                    line.Dispose();
                }
            }
        }
    }

    internal static Mat BuildGlyphMask(Mat image)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(image, hsv, ColorConversionCodes.BGR2HSV);
        using var value = new Mat();
        Cv2.ExtractChannel(hsv, value, 2);
        Cv2.GaussianBlur(value, value, new OpenCvSharp.Size(3, 3), 0);

        using var bright = new Mat();
        Cv2.Threshold(value, bright, 165, 255, ThresholdTypes.Binary);
        using var localContrast = new Mat();
        var contrastKernelWidth = EnsureOdd(Math.Clamp(image.Cols / 45, 7, 17));
        var contrastKernelHeight = EnsureOdd(Math.Clamp(image.Rows / 20, 3, 7));
        using (var kernel = Cv2.GetStructuringElement(
                   MorphShapes.Rect,
                   new OpenCvSharp.Size(contrastKernelWidth, contrastKernelHeight)))
        {
            Cv2.MorphologyEx(value, localContrast, MorphTypes.TopHat, kernel);
        }

        using var contrast = new Mat();
        Cv2.Threshold(localContrast, contrast, 14, 255, ThresholdTypes.Binary);
        var glyphMask = new Mat();
        Cv2.BitwiseAnd(bright, contrast, glyphMask);
        using (var closeKernel = Cv2.GetStructuringElement(
                   MorphShapes.Rect,
                   new OpenCvSharp.Size(3, 2)))
        {
            Cv2.MorphologyEx(glyphMask, glyphMask, MorphTypes.Close, closeKernel);
        }
        return glyphMask;
    }

    private static int EnsureOdd(int value) => value % 2 == 0 ? value + 1 : value;

    private static void BridgeSmallGaps(bool[] rows, int maximumGap)
    {
        var lastActive = -1;
        for (var index = 0; index < rows.Length; index++)
        {
            if (!rows[index])
            {
                continue;
            }
            if (lastActive >= 0 && index - lastActive - 1 <= maximumGap)
            {
                for (var fill = lastActive + 1; fill < index; fill++)
                {
                    rows[fill] = true;
                }
            }
            lastActive = index;
        }
    }

    private static IReadOnlyList<RowBand> FindBands(bool[] rows)
    {
        var bands = new List<RowBand>();
        var start = -1;
        for (var index = 0; index <= rows.Length; index++)
        {
            var active = index < rows.Length && rows[index];
            if (active && start < 0)
            {
                start = index;
            }
            else if (!active && start >= 0)
            {
                bands.Add(new RowBand(start, index));
                start = -1;
            }
        }
        return bands;
    }

    private static Rect ExpandAndClamp(
        Rect rect,
        int horizontal,
        int vertical,
        OpenCvSharp.Size bounds)
    {
        var left = Math.Max(0, rect.Left - horizontal);
        var top = Math.Max(0, rect.Top - vertical);
        var right = Math.Min(bounds.Width, rect.Right + horizontal);
        var bottom = Math.Min(bounds.Height, rect.Bottom + vertical);
        return new Rect(left, top, right - left, bottom - top);
    }

    private readonly record struct RowBand(int Top, int Bottom)
    {
        public int Height => Bottom - Top;
    }
}
