using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Subtitles;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubSubtitleMaskTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-mask-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("x")]
    [InlineData("height")]
    [InlineData("outside")]
    [InlineData("nan")]
    [InlineData("infinity")]
    [InlineData("blur")]
    [InlineData("mode")]
    [InlineData("color")]
    [InlineData("opacity")]
    [InlineData("opacity_nan")]
    public void InvalidSettings_AreRejectedEvenWhenDisabled(string invalid)
    {
        var settings = new VietsubSubtitleMaskSettings();
        switch (invalid)
        {
            case "x": settings.X = -0.1; break;
            case "height": settings.Height = 0; break;
            case "outside": settings.Width = 1; break;
            case "nan": settings.X = double.NaN; break;
            case "infinity": settings.BlurPercent = double.PositiveInfinity; break;
            case "blur": settings.BlurPercent = 10; break;
            case "mode": settings.Mode = "crop=1:1"; break;
            case "color": settings.Color = "#000000;movie=file"; break;
            case "opacity": settings.Opacity = 1.01; break;
            case "opacity_nan": settings.Opacity = double.NaN; break;
        }
        Assert.ThrowsAny<ArgumentException>(settings.Normalize);
    }

    [Fact]
    public void Copy_IsIndependent_AndFilterUsesInvariantNumbersAndFrameBounds()
    {
        var original = new VietsubVideoTransformSettings();
        var copy = original.Copy();
        copy.SubtitleMask.Enabled = true;
        copy.SubtitleMask.Mode = "BLUR";
        copy.SubtitleMask.X = 0.98;
        copy.SubtitleMask.Width = 0.02;
        copy.SubtitleMask.Y = 0.98;
        copy.SubtitleMask.Height = 0.02;
        Assert.False(original.SubtitleMask.Enabled);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
            var filter = VietsubSubtitleMaskFilter.Build(copy.SubtitleMask, 720, 1280)!;
            Assert.Contains("crop=w=16:h=26:x=704:y=1254", filter);
            Assert.Contains("sigma=19.2", filter);
        }
        finally { CultureInfo.CurrentCulture = previous; }
        Assert.Null(VietsubSubtitleMaskFilter.Build(original.SubtitleMask, 0, 0));
    }

    [Fact]
    public void DefaultMaskUsesBlur_LegacySolidRemainsOpaque_AlphaRoundTrips()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var fresh = new VietsubSubtitleMaskSettings();
        fresh.Normalize();
        Assert.Equal("BLUR", fresh.Mode);
        Assert.Equal(.35, fresh.Opacity);
        var legacy = JsonSerializer.Deserialize<VietsubSubtitleMaskSettings>("""{"mode":"SOLID","enabled":true}""", options)!;
        legacy.Normalize();
        Assert.Equal(1, legacy.Opacity);
        legacy.Opacity = .25;
        var roundTrip = JsonSerializer.Deserialize<VietsubSubtitleMaskSettings>(JsonSerializer.Serialize(legacy, options), options)!;
        roundTrip.Normalize();
        Assert.Equal(.25, roundTrip.Opacity);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
            Assert.Contains("color=0x000000@0.25", VietsubSubtitleMaskFilter.Build(roundTrip, 720, 1280));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(.35)]
    [InlineData(1)]
    public async Task RealFfmpeg_ColorOpacityKeepsExpectedBackgroundBrightness(double opacity)
    {
        Directory.CreateDirectory(_root);
        var ffmpeg = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
        var runner = new ExternalProcessRunner();
        var filter = VietsubSubtitleMaskFilter.Build(new()
        {
            Enabled = true, Mode = "SOLID", X = .25, Y = .25, Width = .5, Height = .5, Opacity = opacity
        }, 128, 128)!;
        var framePath = Path.Combine(_root, "alpha.gray");
        var result = await runner.RunAsync(ffmpeg,
            ["-v", "error", "-f", "lavfi", "-i", "color=c=white:s=128x128:d=1", "-vf", filter,
                "-frames:v", "1", "-pix_fmt", "gray", "-f", "rawvideo", "-y", framePath], TimeSpan.FromSeconds(30));
        Assert.True(result.ExitCode == 0, result.StandardError);
        var frame = await File.ReadAllBytesAsync(framePath);
        Assert.Equal(128 * 128, frame.Length);
        Assert.InRange(frame[10 * 128 + 10], (byte)250, (byte)255);
        Assert.InRange((double)frame[64 * 128 + 64], Math.Max(0, 255 * (1 - opacity) - 4), Math.Min(255, 255 * (1 - opacity) + 4));
    }

    [Fact]
    public async Task Manifest7_MigratesMaskDisabled_PreservesFlips_AndRoundTripsMask()
    {
        var paths = new VietsubAppPaths(_root);
        var projects = new VietsubProjectStore(paths, new VietsubSubtitleStore(paths));
        var project = await projects.CreateAsync(Guid.NewGuid(), "owner", "Mask migration");
        var path = paths.GetProjectPath(project.ProjectId, "project.json");
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["schemaVersion"] = 7;
        json["videoTransformSettings"]!["flipVertical"] = true;
        json["videoTransformSettings"]!.AsObject().Remove("subtitleMask");
        await File.WriteAllTextAsync(path, json.ToJsonString());
        var loaded = await projects.OpenAsync(project.ProjectId, project.OrganizationId, "owner");
        Assert.Equal(8, loaded.SchemaVersion);
        Assert.True(loaded.VideoTransformSettings.FlipVertical);
        Assert.False(loaded.VideoTransformSettings.SubtitleMask.Enabled);
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.False(persisted.RootElement.GetProperty("videoTransformSettings").GetProperty("subtitleMask").GetProperty("enabled").GetBoolean());
        loaded.VideoTransformSettings.SubtitleMask = new() { Enabled = true, Mode = "BLUR", X = 0.2, Width = 0.7 };
        await projects.SaveAsync(loaded);
        var reopened = await projects.OpenAsync(project.ProjectId, project.OrganizationId, "owner");
        Assert.True(reopened.VideoTransformSettings.SubtitleMask.Enabled);
        Assert.Equal("BLUR", reopened.VideoTransformSettings.SubtitleMask.Mode);
        Assert.Equal(0.2, reopened.VideoTransformSettings.SubtitleMask.X);
        Assert.True(reopened.VideoTransformSettings.FlipVertical);
    }

    [Theory]
    [InlineData("SOLID", false, false, false)]
    [InlineData("SOLID", true, false, false)]
    [InlineData("SOLID", false, true, true)]
    [InlineData("SOLID", true, true, false)]
    [InlineData("BLUR", false, false, true)]
    [InlineData("BLUR", true, false, false)]
    [InlineData("BLUR", false, true, false)]
    [InlineData("BLUR", true, true, true)]
    public async Task RealFfmpeg_MasksSourceBeforeFlipsAndVietnamese_PreservesAudio(
        string mode, bool horizontal, bool vertical, bool portrait)
    {
        Directory.CreateDirectory(_root);
        var ffmpeg = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffmpeg.exe");
        var ffprobe = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "ffprobe.exe");
        Assert.True(File.Exists(ffmpeg) && File.Exists(ffprobe));
        var runner = new ExternalProcessRunner();
        async Task Run(IEnumerable<string> args)
        {
            var result = await runner.RunAsync(ffmpeg, args, TimeSpan.FromSeconds(30));
            Assert.True(result.ExitCode == 0, result.StandardError);
        }
        var width = portrait ? 128 : 224;
        var height = portrait ? 224 : 128;
        var raw = new byte[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            raw[y * width + x] = x > width * .1 && x < width * .9 && y > height * .6 && y < height * .95
                ? (byte)(x % 6 < 3 ? 240 : 24) : (byte)24;
        var rawPath = Path.Combine(_root, "source.gray");
        await File.WriteAllBytesAsync(rawPath, raw);
        var source = Path.Combine(_root, "source.mp4");
        await Run(["-v", "error", "-f", "rawvideo", "-pixel_format", "gray", "-video_size", $"{width}x{height}",
            "-framerate", "1", "-i", rawPath, "-f", "lavfi", "-i", "sine=frequency=440:duration=1",
            "-t", "1", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-y", source]);
        var style = VietsubSubtitleStyle.CreateDefault();
        style.FontSizePercent = 10;
        style.BackgroundEnabled = false;
        var assPath = Path.Combine(_root, "subtitles.ass");
        await File.WriteAllTextAsync(assPath, VietsubAssSubtitleBuilder.BuildTranslated([
            new() { StartMilliseconds = 0, EndMilliseconds = 1000, TranslatedText = "VIET" }
        ], style, width, height));
        var escaped = assPath.Replace("\\", "/").Replace(":", "\\:");
        var destination = Path.Combine(_root, "masked.mp4");
        var transform = new VietsubVideoTransformSettings
        {
            FlipHorizontal = horizontal, FlipVertical = vertical,
            SubtitleMask = new() { Enabled = true, Mode = mode, X = .1, Y = .6, Width = .8, Height = .35, BlurPercent = 4 }
        };
        var args = VietsubVideoExportService.BuildRenderArguments(source, source, destination,
            $"subtitles=filename='{escaped}'", 1, true, VietsubAudioMixSettings.CreateDefault(), transform, width, height);
        // Include the voice input to exercise blur's video branches alongside the audio mix graph.
        await Run(args);
        var metadata = await new FfprobeService(ffprobe, runner).ProbeAsync(destination);
        Assert.Equal(width, metadata.Width);
        Assert.Equal(height, metadata.Height);
        Assert.True(metadata.HasAudio);
        Assert.InRange(metadata.DurationSeconds, .9m, 1.2m);
        var framePath = Path.Combine(_root, "output.gray");
        await Run(["-v", "error", "-i", destination, "-frames:v", "1", "-pix_fmt", "gray", "-f", "rawvideo", "-y", framePath]);
        var frame = await File.ReadAllBytesAsync(framePath);
        Assert.Equal(raw.Length, frame.Length);
        byte SourcePixel(int x, int y) => frame[(vertical ? height - 1 - y : y) * width + (horizontal ? width - 1 - x : x)];
        var sample = Enumerable.Range((int)(width * .2), (int)(width * .6)).Select(x => (double)SourcePixel(x, (int)(height * .65))).ToArray();
        if (mode == "SOLID") Assert.All(sample, value => Assert.InRange(value, 0, 12));
        else
        {
            var mean = sample.Average();
            var deviation = Math.Sqrt(sample.Average(value => (value - mean) * (value - mean)));
            Assert.InRange(mean, 60, 180);
            Assert.True(deviation < 25, $"Original stripes remain legible: deviation {deviation}.");
        }
        Assert.InRange(SourcePixel(width / 2, height / 2), (byte)18, (byte)30);
        var subtitlePixels = Enumerable.Range((int)(height * .78), (int)(height * .17))
            .SelectMany(y => Enumerable.Range(width / 3, width / 3).Select(x => frame[y * width + x])).Count(value => value > 210);
        Assert.True(subtitlePixels > 8, "Vietnamese text must stay sharp above the mask and keep its orientation.");
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        return ValueTask.CompletedTask;
    }
}
