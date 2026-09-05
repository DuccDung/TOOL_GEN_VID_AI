using System.Text.Json;
using TOOL_LOCAL.Vietsub.Domain;

namespace TOOL_LOCAL.Vietsub.Ocr;

internal interface IVietsubOcrSourceResolver
{
    Task<string> ResolveVerifiedSourcePathAsync(
        Guid projectId,
        VietsubMediaReference media,
        CancellationToken cancellationToken = default);
}

internal static class VietsubOcrLanguageCodes
{
    public const string English = "en";
    public const string Chinese = "zh";

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        English => English,
        Chinese or "zh-cn" or "zh-hans" => Chinese,
        _ => throw new VietsubOcrException(
            VietsubOcrErrorCodes.LanguageNotSupported,
            "OCR V1 chỉ hỗ trợ tiếng Anh và tiếng Trung.")
    };
}

internal static class VietsubOcrProfileNames
{
    public const string Fast = "FAST";
    public const string Balanced = "BALANCED";
    public const string Accurate = "ACCURATE";

    public static string Normalize(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        Fast => Fast,
        Balanced => Balanced,
        Accurate => Accurate,
        _ => throw new VietsubOcrException(
            VietsubOcrErrorCodes.ProfileInvalid,
            "Profile OCR phải là Fast, Balanced hoặc Accurate.")
    };
}

internal static class VietsubOcrErrorCodes
{
    public const string RuntimeNotInstalled = "OCR_RUNTIME_NOT_INSTALLED";
    public const string RuntimeInvalid = "OCR_RUNTIME_INVALID";
    public const string LanguageNotSupported = "OCR_LANGUAGE_NOT_SUPPORTED";
    public const string ProfileInvalid = "OCR_PROFILE_INVALID";
    public const string RegionInvalid = "OCR_REGION_INVALID";
    public const string TimestampInvalid = "OCR_TIMESTAMP_INVALID";
    public const string VideoNotReady = "OCR_VIDEO_NOT_READY";
    public const string SourceChanged = "OCR_SOURCE_CHANGED";
    public const string FrameExtractionFailed = "OCR_FRAME_EXTRACTION_FAILED";
    public const string ModelLoadFailed = "OCR_MODEL_LOAD_FAILED";
    public const string InferenceFailed = "OCR_INFERENCE_FAILED";
    public const string TextNotDetected = "OCR_TEXT_NOT_DETECTED";
    public const string JobAlreadyActive = "OCR_JOB_ALREADY_ACTIVE";
    public const string JobNotResumable = "OCR_JOB_NOT_RESUMABLE";
    public const string JobCancelled = "OCR_JOB_CANCELLED";
    public const string AccessDenied = "OCR_ACCESS_DENIED";
    public const string LicenseRequired = "OCR_LICENSE_REQUIRED";
}

internal sealed class VietsubOcrException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

internal sealed record VietsubNormalizedRegion(double X, double Y, double Width, double Height)
{
    public const double MinimumWidth = 0.05;
    public const double MinimumHeight = 0.04;

    public static VietsubNormalizedRegion Default { get; } = new(0, 0.6, 1, 0.4);

    public VietsubNormalizedRegion Validate()
    {
        if (!double.IsFinite(X)
            || !double.IsFinite(Y)
            || !double.IsFinite(Width)
            || !double.IsFinite(Height)
            || X < 0
            || Y < 0
            || Width < MinimumWidth
            || Height < MinimumHeight
            || X + Width > 1.000001
            || Y + Height > 1.000001)
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.RegionInvalid,
                "Vùng OCR phải nằm trong video và có kích thước tối thiểu 5% x 4%.");
        }
        return this with
        {
            X = Math.Clamp(X, 0, 1),
            Y = Math.Clamp(Y, 0, 1),
            Width = Math.Clamp(Width, MinimumWidth, 1),
            Height = Math.Clamp(Height, MinimumHeight, 1)
        };
    }
}

internal sealed record VietsubOcrProfile(
    string Name,
    int SampleIntervalMilliseconds,
    int SafetyRefreshMilliseconds,
    int MaximumWidth,
    double ChangeThreshold)
{
    public static VietsubOcrProfile Resolve(string? name) =>
        VietsubOcrProfileNames.Normalize(name) switch
        {
            VietsubOcrProfileNames.Fast => new(VietsubOcrProfileNames.Fast, 500, 10_000, 960, 0.025),
            VietsubOcrProfileNames.Accurate => new(VietsubOcrProfileNames.Accurate, 200, 4_000, 1280, 0.006),
            _ => new(VietsubOcrProfileNames.Balanced, 250, 8_000, 1080, 0.015)
        };
}

internal sealed class VietsubOcrSettings
{
    public string LanguageCode { get; set; } = VietsubOcrLanguageCodes.English;

    public string Profile { get; set; } = VietsubOcrProfileNames.Balanced;

    public VietsubNormalizedRegion Region { get; set; } = VietsubNormalizedRegion.Default;

    public void Normalize()
    {
        LanguageCode = VietsubOcrLanguageCodes.Normalize(LanguageCode);
        Profile = VietsubOcrProfileNames.Normalize(Profile);
        Region = (Region ?? VietsubNormalizedRegion.Default).Validate();
    }
}

internal sealed record VietsubOcrJobParameters(
    int StrategyVersion,
    Guid MediaId,
    string SourceSha256,
    decimal DurationSeconds,
    int SourceWidth,
    int SourceHeight,
    int RotationDegrees,
    string LanguageCode,
    VietsubOcrProfile Profile,
    VietsubNormalizedRegion Region)
{
    public const int CurrentStrategyVersion = 2;

    public static VietsubOcrJobParameters Create(
        Guid mediaId,
        string sourceSha256,
        decimal durationSeconds,
        int sourceWidth,
        int sourceHeight,
        int rotationDegrees,
        VietsubOcrSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();
        if (mediaId == Guid.Empty
            || string.IsNullOrWhiteSpace(sourceSha256)
            || durationSeconds <= 0
            || sourceWidth <= 0
            || sourceHeight <= 0)
        {
            throw new VietsubOcrException(
                VietsubOcrErrorCodes.VideoNotReady,
                "Video nguồn chưa đủ metadata để chạy OCR.");
        }
        return new(
            CurrentStrategyVersion,
            mediaId,
            sourceSha256.Trim().ToLowerInvariant(),
            durationSeconds,
            sourceWidth,
            sourceHeight,
            VietsubVideoRotation.Normalize(rotationDegrees),
            settings.LanguageCode,
            VietsubOcrProfile.Resolve(settings.Profile),
            settings.Region);
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

internal static class VietsubVideoRotation
{
    public static int Normalize(int rotationDegrees)
    {
        var normalized = ((rotationDegrees % 360) + 360) % 360;
        return normalized switch
        {
            < 45 => 0,
            < 135 => 90,
            < 225 => 180,
            < 315 => 270,
            _ => 0
        };
    }

    public static (int Width, int Height) GetDisplaySize(int sourceWidth, int sourceHeight, int rotationDegrees) =>
        Normalize(rotationDegrees) is 90 or 270
            ? (sourceHeight, sourceWidth)
            : (sourceWidth, sourceHeight);
}

internal sealed record VietsubOcrPixelRegion(
    int X,
    int Y,
    int Width,
    int Height,
    int OutputWidth,
    int OutputHeight);

internal static class VietsubOcrRegionResolver
{
    public static VietsubOcrPixelRegion Resolve(
        int sourceWidth,
        int sourceHeight,
        int rotationDegrees,
        VietsubNormalizedRegion normalizedRegion,
        int maximumOutputWidth)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || maximumOutputWidth < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        }
        var region = normalizedRegion.Validate();
        var display = VietsubVideoRotation.GetDisplaySize(sourceWidth, sourceHeight, rotationDegrees);
        var x = ClampEven((int)Math.Floor(region.X * display.Width), 0, display.Width - 2);
        var y = ClampEven((int)Math.Floor(region.Y * display.Height), 0, display.Height - 2);
        var width = ClampEven((int)Math.Round(region.Width * display.Width), 2, display.Width - x);
        var height = ClampEven((int)Math.Round(region.Height * display.Height), 2, display.Height - y);
        var scale = Math.Min(1d, maximumOutputWidth / (double)width);
        var outputWidth = Math.Max(2, MakeEven((int)Math.Round(width * scale)));
        var outputHeight = Math.Max(2, MakeEven((int)Math.Round(height * scale)));
        return new(x, y, width, height, outputWidth, outputHeight);
    }

    private static int ClampEven(int value, int minimum, int maximum)
    {
        var clamped = Math.Clamp(value, minimum, maximum);
        return MakeEven(clamped);
    }

    private static int MakeEven(int value) => value - Math.Abs(value % 2);
}
