using System.Text.RegularExpressions;

namespace TOOL_LOCAL.Vietsub.Domain;

internal static class VietsubSubtitleStylePresetIds
{
    public const string Readable = "READABLE";
    public const string Vertical = "VERTICAL";
    public const string Outline = "OUTLINE";
    public const string TikTok = "TIKTOK";
    public const string Cinema = "CINEMA";
    public const string Yellow = "YELLOW";
    public const string Minimal = "MINIMAL";
    public const string Custom = "CUSTOM";

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        Readable,
        Vertical,
        Outline,
        TikTok,
        Cinema,
        Yellow,
        Minimal,
        Custom
    };
}

internal static class VietsubSubtitleVerticalPositions
{
    public const string Top = "TOP";
    public const string Middle = "MIDDLE";
    public const string Bottom = "BOTTOM";
    public const string Custom = "CUSTOM";

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        Top,
        Middle,
        Bottom,
        Custom
    };
}

internal static class VietsubSubtitleAlignments
{
    public const string BottomLeft = "BOTTOM_LEFT";
    public const string BottomCenter = "BOTTOM_CENTER";
    public const string BottomRight = "BOTTOM_RIGHT";

    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal)
    {
        BottomLeft,
        BottomCenter,
        BottomRight
    };
}

internal sealed partial class VietsubSubtitleStyle
{
    private static readonly IReadOnlyDictionary<string, string> SupportedFonts =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Arial"] = "Arial",
            ["Segoe UI"] = "Segoe UI",
            ["Tahoma"] = "Tahoma",
            ["Verdana"] = "Verdana",
            ["Times New Roman"] = "Times New Roman"
        };

    public string PresetId { get; set; } = VietsubSubtitleStylePresetIds.Readable;

    public string FontFamily { get; set; } = "Arial";

    public double FontSizePercent { get; set; } = 4.4;

    public bool Bold { get; set; } = true;

    public bool Italic { get; set; }

    public string TextColor { get; set; } = "#FFFFFF";

    public double TextOpacity { get; set; } = 1;

    public string OutlineColor { get; set; } = "#000000";

    public double OutlineOpacity { get; set; } = 0.95;

    public double OutlineWidthPercent { get; set; } = 0.18;

    public string ShadowColor { get; set; } = "#000000";

    public double ShadowOpacity { get; set; } = 0.65;

    public double ShadowOffsetPercent { get; set; } = 0.16;

    public bool BackgroundEnabled { get; set; } = true;

    public string BackgroundColor { get; set; } = "#04080D";

    public double BackgroundOpacity { get; set; } = 0.58;

    public string Alignment { get; set; } = VietsubSubtitleAlignments.BottomCenter;

    public string VerticalPosition { get; set; } = VietsubSubtitleVerticalPositions.Bottom;

    public double PositionXPercent { get; set; } = 50;

    public double PositionYPercent { get; set; } = 93;

    public double BottomMarginPercent { get; set; } = 7;

    public double HorizontalMarginPercent { get; set; } = 6;

    public double MaxWidthPercent { get; set; } = 88;

    public double LineHeight { get; set; } = 1.25;

    public int MaxLines { get; set; } = 2;

    public static VietsubSubtitleStyle CreateDefault() => new();

    public VietsubSubtitleStyle Copy() => new()
    {
        PresetId = PresetId,
        FontFamily = FontFamily,
        FontSizePercent = FontSizePercent,
        Bold = Bold,
        Italic = Italic,
        TextColor = TextColor,
        TextOpacity = TextOpacity,
        OutlineColor = OutlineColor,
        OutlineOpacity = OutlineOpacity,
        OutlineWidthPercent = OutlineWidthPercent,
        ShadowColor = ShadowColor,
        ShadowOpacity = ShadowOpacity,
        ShadowOffsetPercent = ShadowOffsetPercent,
        BackgroundEnabled = BackgroundEnabled,
        BackgroundColor = BackgroundColor,
        BackgroundOpacity = BackgroundOpacity,
        Alignment = Alignment,
        VerticalPosition = VerticalPosition,
        PositionXPercent = PositionXPercent,
        PositionYPercent = PositionYPercent,
        BottomMarginPercent = BottomMarginPercent,
        HorizontalMarginPercent = HorizontalMarginPercent,
        MaxWidthPercent = MaxWidthPercent,
        LineHeight = LineHeight,
        MaxLines = MaxLines
    };

    public void Normalize()
    {
        PresetId = (PresetId ?? string.Empty).Trim().ToUpperInvariant();
        if (!VietsubSubtitleStylePresetIds.Supported.Contains(PresetId))
        {
            throw new ArgumentException("Mẫu thiết kế phụ đề không hợp lệ.", nameof(PresetId));
        }

        var requestedFont = (FontFamily ?? string.Empty).Trim();
        if (!SupportedFonts.TryGetValue(requestedFont, out var safeFont))
        {
            throw new ArgumentException("Phông chữ phụ đề không nằm trong danh sách được hỗ trợ.", nameof(FontFamily));
        }
        FontFamily = safeFont;

        Alignment = (Alignment ?? string.Empty).Trim().ToUpperInvariant();
        if (!VietsubSubtitleAlignments.Supported.Contains(Alignment))
        {
            throw new ArgumentException("Vị trí phụ đề không hợp lệ.", nameof(Alignment));
        }


        VerticalPosition = (VerticalPosition ?? string.Empty).Trim().ToUpperInvariant();
        if (!VietsubSubtitleVerticalPositions.Supported.Contains(VerticalPosition))
        {
            throw new ArgumentException("Vị trí dọc của phụ đề không hợp lệ.", nameof(VerticalPosition));
        }

        TextColor = NormalizeColor(TextColor, nameof(TextColor));
        OutlineColor = NormalizeColor(OutlineColor, nameof(OutlineColor));
        ShadowColor = NormalizeColor(ShadowColor, nameof(ShadowColor));
        BackgroundColor = NormalizeColor(BackgroundColor, nameof(BackgroundColor));

        ValidateRange(FontSizePercent, 2, 10, nameof(FontSizePercent));
        ValidateRange(TextOpacity, 0, 1, nameof(TextOpacity));
        ValidateRange(OutlineOpacity, 0, 1, nameof(OutlineOpacity));
        ValidateRange(OutlineWidthPercent, 0, 1, nameof(OutlineWidthPercent));
        ValidateRange(ShadowOpacity, 0, 1, nameof(ShadowOpacity));
        ValidateRange(ShadowOffsetPercent, 0, 1.5, nameof(ShadowOffsetPercent));
        ValidateRange(BackgroundOpacity, 0, 1, nameof(BackgroundOpacity));
        ValidateRange(PositionXPercent, 2, 98, nameof(PositionXPercent));
        ValidateRange(PositionYPercent, 2, 98, nameof(PositionYPercent));
        ValidateRange(BottomMarginPercent, 0, 30, nameof(BottomMarginPercent));
        ValidateRange(HorizontalMarginPercent, 0, 30, nameof(HorizontalMarginPercent));
        ValidateRange(MaxWidthPercent, 40, 100, nameof(MaxWidthPercent));
        ValidateRange(LineHeight, 1, 2, nameof(LineHeight));
        if (MaxLines is < 1 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxLines), "Số dòng phụ đề mục tiêu phải từ 1 đến 3.");
        }

        if ((HorizontalMarginPercent * 2) + MaxWidthPercent > 100.001)
        {
            throw new ArgumentException(
                "Lề ngang và chiều rộng tối đa của phụ đề vượt quá khung hình.",
                nameof(MaxWidthPercent));
        }
    }

    private static string NormalizeColor(string? value, string parameterName)
    {
        var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (!HexColorPattern().IsMatch(normalized))
        {
            throw new ArgumentException("Màu phụ đề phải có định dạng #RRGGBB.", parameterName);
        }
        return normalized;
    }

    private static void ValidateRange(double value, double minimum, double maximum, string parameterName)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Giá trị thiết kế phụ đề phải nằm trong khoảng {minimum} đến {maximum}.");
        }
    }

    [GeneratedRegex("^#[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorPattern();
}
