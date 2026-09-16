using System.Drawing;

namespace TOOL_LOCAL.Authentication;

internal static class BrandIdentity
{
    public const string DisplayName = "taphoatool";

    public static Image Logo { get; } = LoadLogo();
    public static Icon WindowIcon { get; } = LoadWindowIcon();

    private static Image LoadLogo()
    {
        using var stream = typeof(BrandIdentity).Assembly.GetManifestResourceStream("TapHoaTool.Logo")
            ?? throw new InvalidOperationException("Không tìm thấy logo taphoatool trong ứng dụng.");
        using var source = new Bitmap(stream);
        return new Bitmap(source);
    }

    private static Icon LoadWindowIcon()
    {
        using var stream = typeof(BrandIdentity).Assembly.GetManifestResourceStream("TapHoaTool.WindowIcon")
            ?? throw new InvalidOperationException("Không tìm thấy icon taphoatool trong ứng dụng.");
        using var source = new Icon(stream);
        return (Icon)source.Clone();
    }
}
