namespace TOOL_LOCAL.Vietsub.Domain;

// Coordinates refer to the displayed source after autorotation, before horizontal/vertical flips.
internal sealed class VietsubSubtitleMaskSettings
{
    public bool Enabled { get; set; }
    public string Mode { get; set; } = "BLUR";
    public double X { get; set; } = 0.05;
    public double Y { get; set; } = 0.78;
    public double Width { get; set; } = 0.9;
    public double Height { get; set; } = 0.12;
    public string Color { get; set; } = "#000000";
    // Missing alpha in existing SOLID masks means the original opaque fill.
    public double? Opacity { get; set; }
    public double BlurPercent { get; set; } = 1.5;

    public VietsubSubtitleMaskSettings Copy() => (VietsubSubtitleMaskSettings)MemberwiseClone();

    public void Normalize()
    {
        if (Mode is not ("SOLID" or "BLUR"))
            throw new ArgumentException("Kiểu che phụ đề phải là phủ màu hoặc làm mờ.");
        if (Color is null || Color.Length != 7 || Color[0] != '#'
            || Color.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF"))
            throw new ArgumentException("Màu vùng che phải có định dạng #RRGGBB.");
        Validate(X, 0, 0.98);
        Validate(Y, 0, 0.98);
        Validate(Width, 0.02, 1);
        Validate(Height, 0.02, 1);
        Validate(BlurPercent, 0.2, 4);
        Opacity ??= Mode == "SOLID" ? 1 : 0.35;
        Validate(Opacity.Value, 0, 1);
        if (X + Width > 1.0000001 || Y + Height > 1.0000001)
            throw new ArgumentException("Vùng che phải nằm trong khung hình video.");
        Color = Color.ToUpperInvariant();
    }

    private static void Validate(double value, double minimum, double maximum)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentException("Vị trí, kích thước hoặc mức mờ của vùng che không hợp lệ.");
    }
}
