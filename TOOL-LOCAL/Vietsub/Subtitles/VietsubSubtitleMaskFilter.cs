using System.Globalization;
using TOOL_LOCAL.Vietsub.Domain;

namespace TOOL_LOCAL.Vietsub.Subtitles;

internal static class VietsubSubtitleMaskFilter
{
    // All values come from validated settings and probed dimensions, never filter text from the UI.
    public static string? Build(VietsubSubtitleMaskSettings settings, int width, int height)
    {
        settings.Normalize();
        if (!settings.Enabled) return null;
        if (width < 2 || height < 2)
            throw new ArgumentException("Không xác định được kích thước video để che phụ đề.");

        var x = Math.Clamp((int)Math.Floor(settings.X * width / 2) * 2, 0, width - 2);
        var y = Math.Clamp((int)Math.Floor(settings.Y * height / 2) * 2, 0, height - 2);
        var right = Math.Min(width, (int)Math.Ceiling((settings.X + settings.Width) * width / 2) * 2);
        var bottom = Math.Min(height, (int)Math.Ceiling((settings.Y + settings.Height) * height / 2) * 2);
        var w = Math.Max(2, right - x);
        var h = Math.Max(2, bottom - y);
        if (settings.Mode == "SOLID")
        {
            var alpha = settings.Opacity!.Value.ToString("0.####", CultureInfo.InvariantCulture);
            return FormattableString.Invariant($"drawbox=x={x}:y={y}:w={w}:h={h}:color=0x{settings.Color[1..]}@{alpha}:t=fill");
        }

        var sigma = Math.Clamp(height * settings.BlurPercent / 100, 0.5, 512)
            .ToString("0.####", CultureInfo.InvariantCulture);
        return FormattableString.Invariant(
            $"split[mask_base][mask_crop];[mask_crop]crop=w={w}:h={h}:x={x}:y={y}:exact=1,gblur=sigma={sigma}:steps=3[mask_blur];[mask_base][mask_blur]overlay=x={x}:y={y}:format=auto");
    }
}
