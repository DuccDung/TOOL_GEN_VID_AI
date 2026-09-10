using System.Globalization;
using System.Text;
using TOOL_LOCAL.Vietsub.Domain;

namespace TOOL_LOCAL.Vietsub.Subtitles;

internal static class VietsubAssSubtitleBuilder
{
    private const int MaximumRenderDimension = 8192;
    private const int MinimumRenderDimension = 128;

    public static string BuildTranslated(
        IReadOnlyList<VietsubSubtitleCue> cues,
        VietsubSubtitleStyle style,
        int videoWidth,
        int videoHeight)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(style);
        if (videoWidth is < MinimumRenderDimension or > MaximumRenderDimension
            || videoHeight is < MinimumRenderDimension or > MaximumRenderDimension)
        {
            throw new ArgumentOutOfRangeException(
                nameof(videoWidth),
                "Kích thước khung hình ASS không hợp lệ.");
        }
        if (cues.Count > VietsubSubtitleService.MaximumCueCount)
        {
            throw new ArgumentException("Track phụ đề vượt quá giới hạn số câu.", nameof(cues));
        }

        var safeStyle = style.Copy();
        safeStyle.Normalize();
        var fontSize = videoHeight * safeStyle.FontSizePercent / 100;
        var outline = videoHeight * safeStyle.OutlineWidthPercent / 100;
        var shadow = videoHeight * safeStyle.ShadowOffsetPercent / 100;
        var horizontalMarginPercent = Math.Max(
            safeStyle.HorizontalMarginPercent,
            (100 - safeStyle.MaxWidthPercent) / 2);
        var horizontalMargin = (int)Math.Round(videoWidth * horizontalMarginPercent / 100);
        var verticalMargin = 0;
        var alignment = ResolveAlignment(safeStyle);
        var positionOverride = FormattableString.Invariant(
            $"{{\\an{alignment}\\pos({FormatDecimal(videoWidth * safeStyle.PositionXPercent / 100)},{FormatDecimal(videoHeight * safeStyle.PositionYPercent / 100)})}}");
        var estimatedCharactersPerLine = Math.Max(
            8,
            (int)Math.Floor(
                videoWidth * safeStyle.MaxWidthPercent / 100
                / Math.Max(1, fontSize * 0.55)));

        var builder = new StringBuilder(4 * 1024);
        builder.AppendLine("[Script Info]");
        builder.AppendLine("ScriptType: v4.00+");
        builder.AppendLine("WrapStyle: 0");
        builder.AppendLine("ScaledBorderAndShadow: yes");
        builder.Append("PlayResX: ").Append(videoWidth).AppendLine();
        builder.Append("PlayResY: ").Append(videoHeight).AppendLine();
        builder.AppendLine();
        builder.AppendLine("[V4+ Styles]");
        builder.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");

        if (safeStyle.BackgroundEnabled)
        {
            AppendStyle(
                builder,
                "VietsubBox",
                safeStyle.FontFamily,
                fontSize,
                ToAssColor(safeStyle.TextColor, 0),
                ToAssColor(safeStyle.BackgroundColor, safeStyle.BackgroundOpacity),
                ToAssColor(safeStyle.BackgroundColor, 0),
                safeStyle.Bold,
                safeStyle.Italic,
                borderStyle: 3,
                outline: Math.Max(1, videoHeight * 0.008),
                shadow: 0,
                alignment,
                horizontalMargin,
                verticalMargin);
        }

        AppendStyle(
            builder,
            "VietsubText",
            safeStyle.FontFamily,
            fontSize,
            ToAssColor(safeStyle.TextColor, safeStyle.TextOpacity),
            ToAssColor(safeStyle.OutlineColor, safeStyle.OutlineOpacity),
            ToAssColor(safeStyle.ShadowColor, safeStyle.ShadowOpacity),
            safeStyle.Bold,
            safeStyle.Italic,
            borderStyle: 1,
            outline,
            shadow,
            alignment,
            horizontalMargin,
            verticalMargin);

        builder.AppendLine();
        builder.AppendLine("[Events]");
        builder.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
        foreach (var cue in cues.OrderBy(item => item.StartMilliseconds).ThenBy(item => item.EndMilliseconds))
        {
            var text = cue.TranslatedText?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }
            if (cue.StartMilliseconds < 0 || cue.EndMilliseconds <= cue.StartMilliseconds)
            {
                throw new InvalidDataException("Mốc thời gian của câu phụ đề không hợp lệ.");
            }

            var preparedText = WrapToTargetLines(text, safeStyle.MaxLines, estimatedCharactersPerLine);
            var safeText = positionOverride + EscapeDialogueText(preparedText);
            if (safeStyle.BackgroundEnabled)
            {
                AppendDialogue(builder, 0, cue, "VietsubBox", safeText);
            }
            AppendDialogue(builder, 1, cue, "VietsubText", safeText);
        }
        return builder.ToString();
    }

    private static void AppendStyle(
        StringBuilder builder,
        string name,
        string fontFamily,
        double fontSize,
        string primaryColor,
        string outlineColor,
        string backColor,
        bool bold,
        bool italic,
        int borderStyle,
        double outline,
        double shadow,
        int alignment,
        int horizontalMargin,
        int verticalMargin)
    {
        builder.Append("Style: ").Append(name).Append(',')
            .Append(fontFamily).Append(',')
            .Append(FormatDecimal(fontSize)).Append(',')
            .Append(primaryColor).Append(',')
            .Append(primaryColor).Append(',')
            .Append(outlineColor).Append(',')
            .Append(backColor).Append(',')
            .Append(bold ? -1 : 0).Append(',')
            .Append(italic ? -1 : 0)
            .Append(",0,0,100,100,0,0,")
            .Append(borderStyle).Append(',')
            .Append(FormatDecimal(outline)).Append(',')
            .Append(FormatDecimal(shadow)).Append(',')
            .Append(alignment).Append(',')
            .Append(horizontalMargin).Append(',')
            .Append(horizontalMargin).Append(',')
            .Append(verticalMargin)
            .AppendLine(",1");
    }

    private static void AppendDialogue(
        StringBuilder builder,
        int layer,
        VietsubSubtitleCue cue,
        string styleName,
        string text)
    {
        builder.Append("Dialogue: ").Append(layer).Append(',')
            .Append(FormatTime(cue.StartMilliseconds)).Append(',')
            .Append(FormatTime(cue.EndMilliseconds)).Append(',')
            .Append(styleName)
            .Append(",,0,0,0,,")
            .Append(text)
            .AppendLine();
    }

    private static string FormatTime(long milliseconds)
    {
        var centiseconds = milliseconds / 10;
        var hours = centiseconds / 360_000;
        var minutes = (centiseconds / 6_000) % 60;
        var seconds = (centiseconds / 100) % 60;
        var remainder = centiseconds % 100;
        return FormattableString.Invariant($"{hours}:{minutes:00}:{seconds:00}.{remainder:00}");
    }

    private static string FormatDecimal(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    private static int ResolveAlignment(VietsubSubtitleStyle style)
    {
        var horizontalOffset = style.Alignment switch
        {
            VietsubSubtitleAlignments.BottomLeft => 0,
            VietsubSubtitleAlignments.BottomRight => 2,
            _ => 1
        };
        var rowStart = style.VerticalPosition switch
        {
            VietsubSubtitleVerticalPositions.Top => 7,
            VietsubSubtitleVerticalPositions.Bottom => 1,
            _ => 4
        };
        return rowStart + horizontalOffset;
    }

    private static string WrapToTargetLines(string text, int maximumLines, int charactersPerLine)
    {
        var paragraphs = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => string.Join(' ', line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
            .Where(line => line.Length > 0)
            .ToArray();
        if (paragraphs.Length == 0)
        {
            return string.Empty;
        }

        var greedyLines = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = new StringBuilder();
            foreach (var word in words)
            {
                if (current.Length > 0 && current.Length + 1 + word.Length > charactersPerLine)
                {
                    greedyLines.Add(current.ToString());
                    current.Clear();
                }
                if (current.Length > 0) current.Append(' ');
                current.Append(word);
            }
            if (current.Length > 0) greedyLines.Add(current.ToString());
        }
        if (greedyLines.Count <= maximumLines)
        {
            return string.Join('\n', greedyLines);
        }

        var allWords = paragraphs
            .SelectMany(paragraph => paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToArray();
        var totalCharacters = allWords.Sum(word => word.Length) + Math.Max(0, allWords.Length - 1);
        var targetCharacters = Math.Max(charactersPerLine, (int)Math.Ceiling(totalCharacters / (double)maximumLines));
        var balanced = new List<string>(maximumLines);
        var wordIndex = 0;
        for (var lineIndex = 0; lineIndex < maximumLines && wordIndex < allWords.Length; lineIndex++)
        {
            if (lineIndex == maximumLines - 1)
            {
                balanced.Add(string.Join(' ', allWords[wordIndex..]));
                break;
            }
            var remainingLines = maximumLines - lineIndex - 1;
            var line = new StringBuilder();
            while (wordIndex < allWords.Length - remainingLines)
            {
                var word = allWords[wordIndex];
                var proposedLength = line.Length + (line.Length > 0 ? 1 : 0) + word.Length;
                if (line.Length > 0 && proposedLength > targetCharacters) break;
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
                wordIndex++;
            }
            balanced.Add(line.ToString());
        }
        return string.Join('\n', balanced);
    }

    private static string ToAssColor(string hex, double opacity)
    {
        var alpha = 255 - (int)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        var red = hex[1..3];
        var green = hex[3..5];
        var blue = hex[5..7];
        return $"&H{alpha:X2}{blue}{green}{red}";
    }

    private static string EscapeDialogueText(string text)
    {
        var builder = new StringBuilder(text.Length + 16);
        foreach (var character in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'))
        {
            builder.Append(character switch
            {
                '\n' => "\\N",
                '\\' => "＼",
                '{' => "｛",
                '}' => "｝",
                _ when char.IsControl(character) => string.Empty,
                _ => character.ToString()
            });
        }
        return builder.ToString();
    }
}
