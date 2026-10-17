using System.Drawing;
using System.Runtime.InteropServices;

namespace TOOL_LOCAL.Authentication;

internal static class BrandIdentity
{
    public const string DisplayName = "taphoatool";

    public static Image Logo { get; } = LoadLogo();
    public static Icon WindowIcon { get; } = LoadWindowIcon();
    private static Icon SmallWindowIcon { get; } = new(WindowIcon, SystemInformation.SmallIconSize);

    public static void ApplyWindowIcon(Form window)
    {
        window.Icon = WindowIcon;
        window.HandleCreated += WindowOnHandleCreated;
        if (window.IsHandleCreated) ApplyWindowClassIcons(window.Handle);
    }

    private static void WindowOnHandleCreated(object? sender, EventArgs args) =>
        ApplyWindowClassIcons(((Form)sender!).Handle);

    private static void ApplyWindowClassIcons(nint handle)
    {
        // The shell can fall back to the window class instead of Form.Icon.
        // Keep these icons alive for the process lifetime, including recreated windows.
        if (Environment.Is64BitProcess)
        {
            SetClassLongPtr(handle, -14 /* GCLP_HICON */, WindowIcon.Handle);
            SetClassLongPtr(handle, -34 /* GCLP_HICONSM */, SmallWindowIcon.Handle);
        }
        else
        {
            SetClassLong(handle, -14, WindowIcon.Handle.ToInt32());
            SetClassLong(handle, -34, SmallWindowIcon.Handle.ToInt32());
        }
    }

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

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern nint SetClassLongPtr(nint window, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "SetClassLongW")]
    private static extern uint SetClassLong(nint window, int index, int value);
}
