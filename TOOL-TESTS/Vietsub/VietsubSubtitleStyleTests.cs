using System.Text.Json;
using System.Text.Json.Nodes;
using TOOL_LOCAL.Vietsub;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Subtitles;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubSubtitleStyleTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"videomaker-vietsub-style-{Guid.NewGuid():N}");

    [Fact]
    public void Style_NormalizesSafeValues_AndRejectsFontPathOrInvalidGeometry()
    {
        var style = new VietsubSubtitleStyle
        {
            PresetId = "custom",
            FontFamily = "segoe ui",
            TextColor = "#abcdef",
            Alignment = "bottom_right"
        };

        style.Normalize();

        Assert.Equal(VietsubSubtitleStylePresetIds.Custom, style.PresetId);
        Assert.Equal("Segoe UI", style.FontFamily);
        Assert.Equal("#ABCDEF", style.TextColor);
        Assert.Equal(VietsubSubtitleAlignments.BottomRight, style.Alignment);

        var unsafeFont = VietsubSubtitleStyle.CreateDefault();
        unsafeFont.FontFamily = "../../fonts/unknown.ttf";
        Assert.Throws<ArgumentException>(unsafeFont.Normalize);

        var invalidGeometry = VietsubSubtitleStyle.CreateDefault();
        invalidGeometry.HorizontalMarginPercent = 20;
        invalidGeometry.MaxWidthPercent = 88;
        Assert.Throws<ArgumentException>(invalidGeometry.Normalize);
    }

    [Fact]
    public void AudioMix_NormalizesSupportedRange_AndRejectsInvalidGain()
    {
        var settings = new VietsubAudioMixSettings
        {
            OriginalVolume = 0.4,
            TranslatedVoiceVolume = 1.5,
            AutoDuckOriginal = true
        };

        settings.Normalize();

        Assert.Equal(0.4, settings.OriginalVolume);
        Assert.Equal(1.5, settings.TranslatedVoiceVolume);
        settings.TranslatedVoiceVolume = 1.51;
        Assert.Throws<ArgumentOutOfRangeException>(settings.Normalize);
    }

    [Fact]
    public async Task ManifestV3_MigratesAndPersistsDefaultSubtitleStyle()
    {
        var (paths, projects) = CreateStores();
        var organizationId = Guid.NewGuid();
        var created = await projects.CreateAsync(organizationId, "owner", "Style migration");
        var manifestPath = paths.GetProjectPath(created.ProjectId, "project.json");
        var root = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
        root["schemaVersion"] = 3;
        root.Remove("subtitleStyle");
        await File.WriteAllTextAsync(
            manifestPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var migrated = await projects.OpenAsync(created.ProjectId, organizationId, "owner");

        Assert.Equal(VietsubProjectManifest.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Equal(VietsubSubtitleStylePresetIds.Readable, migrated.SubtitleStyle.PresetId);
        Assert.Equal("Arial", migrated.SubtitleStyle.FontFamily);
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        Assert.Equal(
            VietsubProjectManifest.CurrentSchemaVersion,
            persisted.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            "READABLE",
            persisted.RootElement.GetProperty("subtitleStyle").GetProperty("presetId").GetString());
    }

    [Fact]
    public async Task ManifestV4_MigratesLegacyMarginsToPercentageCoordinates()
    {
        var (paths, projects) = CreateStores();
        var organizationId = Guid.NewGuid();
        var created = await projects.CreateAsync(organizationId, "owner", "Position migration");
        var manifestPath = paths.GetProjectPath(created.ProjectId, "project.json");
        var root = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
        root["schemaVersion"] = 4;
        var style = root["subtitleStyle"]!.AsObject();
        style["alignment"] = VietsubSubtitleAlignments.BottomRight;
        style["bottomMarginPercent"] = 12;
        style["horizontalMarginPercent"] = 8;
        style["maxWidthPercent"] = 84;
        style.Remove("verticalPosition");
        style.Remove("positionXPercent");
        style.Remove("positionYPercent");
        style.Remove("maxLines");
        await File.WriteAllTextAsync(manifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var migrated = await projects.OpenAsync(created.ProjectId, organizationId, "owner");

        Assert.Equal(VietsubProjectManifest.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Equal(VietsubSubtitleVerticalPositions.Bottom, migrated.SubtitleStyle.VerticalPosition);
        Assert.Equal(92, migrated.SubtitleStyle.PositionXPercent);
        Assert.Equal(88, migrated.SubtitleStyle.PositionYPercent);
        Assert.Equal(2, migrated.SubtitleStyle.MaxLines);
        Assert.Equal(0.25, migrated.AudioMixSettings.OriginalVolume);
        Assert.Equal(1, migrated.AudioMixSettings.TranslatedVoiceVolume);
        Assert.True(migrated.AudioMixSettings.AutoDuckOriginal);
    }

    [Fact]
    public async Task ManifestV6_MigratesAndPersistsDefaultVideoTransform()
    {
        var (paths, projects) = CreateStores();
        var organizationId = Guid.NewGuid();
        var created = await projects.CreateAsync(organizationId, "owner", "Video transform migration");
        var manifestPath = paths.GetProjectPath(created.ProjectId, "project.json");
        var root = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
        root["schemaVersion"] = 6;
        root.Remove("videoTransformSettings");
        await File.WriteAllTextAsync(manifestPath, root.ToJsonString());

        var migrated = await projects.OpenAsync(created.ProjectId, organizationId, "owner");

        Assert.Equal(VietsubProjectManifest.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.False(migrated.VideoTransformSettings.FlipHorizontal);
        Assert.False(migrated.VideoTransformSettings.FlipVertical);
        using var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        Assert.Equal(VietsubProjectManifest.CurrentSchemaVersion, persisted.RootElement.GetProperty("schemaVersion").GetInt32());
        var videoTransform = persisted.RootElement.GetProperty("videoTransformSettings");
        Assert.False(videoTransform.GetProperty("flipHorizontal").GetBoolean());
        Assert.False(videoTransform.GetProperty("flipVertical").GetBoolean());
    }

    [Fact]
    public void AssBuilder_MapsStyleAndNeutralizesOverrideTagInjection()
    {
        var style = VietsubSubtitleStyle.CreateDefault();
        style.Alignment = VietsubSubtitleAlignments.BottomRight;
        var cue = new VietsubSubtitleCue
        {
            StartMilliseconds = 1_230,
            EndMilliseconds = 3_450,
            OriginalText = "Do not export original",
            TranslatedText = "{\\pos(0,0)}Xin chào\\N\ndòng hai"
        };

        var ass = VietsubAssSubtitleBuilder.BuildTranslated([cue], style, 1920, 1080);

        Assert.Contains("PlayResX: 1920", ass, StringComparison.Ordinal);
        Assert.Contains("Style: VietsubBox", ass, StringComparison.Ordinal);
        Assert.Contains("Style: VietsubText", ass, StringComparison.Ordinal);
        Assert.Contains("Dialogue: 0,0:00:01.23,0:00:03.45,VietsubBox", ass, StringComparison.Ordinal);
        Assert.Contains("Dialogue: 1,0:00:01.23,0:00:03.45,VietsubText", ass, StringComparison.Ordinal);
        Assert.Contains("{\\an3\\pos(960,1004.4)}", ass, StringComparison.Ordinal);
        Assert.Contains("｛＼pos(0,0)｝Xin chào＼N\\Ndòng hai", ass, StringComparison.Ordinal);
        Assert.DoesNotContain("{\\pos(0,0)}", ass, StringComparison.Ordinal);
        Assert.DoesNotContain("Do not export original", ass, StringComparison.Ordinal);
    }

    [Fact]
    public void AssBuilder_MapsCustomCoordinatesAndVerticalAnchor()
    {
        var style = VietsubSubtitleStyle.CreateDefault();
        style.PresetId = VietsubSubtitleStylePresetIds.Custom;
        style.Alignment = VietsubSubtitleAlignments.BottomLeft;
        style.VerticalPosition = VietsubSubtitleVerticalPositions.Custom;
        style.PositionXPercent = 25;
        style.PositionYPercent = 40;
        var cue = new VietsubSubtitleCue
        {
            StartMilliseconds = 0,
            EndMilliseconds = 1_000,
            TranslatedText = "Vị trí tự do"
        };

        var ass = VietsubAssSubtitleBuilder.BuildTranslated([cue], style, 1280, 720);

        Assert.Contains("{\\an4\\pos(320,288)}Vị trí tự do", ass, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bridge_UpdatesStyleWithStableContract_AndPersistsIt()
    {
        var (_, projects) = CreateStores();
        var organizationId = Guid.NewGuid();
        const string owner = "style-owner";
        var project = await projects.CreateAsync(organizationId, owner, "Style bridge");
        var responses = new List<string>();
        var currentOrganizationId = organizationId;
        using var bridge = new VietsubWebBridge(
            enabled: true,
            responses.Add,
            projects,
            () => new VietsubUserContext(owner, currentOrganizationId));
        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.project.open",
            requestId = "open-style",
            payload = new { projectId = project.ProjectId }
        }));
        responses.Clear();

        var style = VietsubSubtitleStyle.CreateDefault();
        style.PresetId = VietsubSubtitleStylePresetIds.Custom;
        style.FontFamily = "Segoe UI";
        style.FontSizePercent = 5.2;
        style.BackgroundEnabled = false;
        var audioMixSettings = new VietsubAudioMixSettings
        {
            OriginalVolume = 0.4,
            TranslatedVoiceVolume = 1.2,
            OriginalMuted = false,
            TranslatedVoiceMuted = false,
            AutoDuckOriginal = false
        };
        var videoTransformSettings = new VietsubVideoTransformSettings
        {
            FlipHorizontal = true,
            FlipVertical = true,
            SubtitleMask = new() { Enabled = true, Mode = "BLUR", X = 0.1, Width = 0.8, BlurPercent = 2 }
        };
        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.subtitle.style.update",
            requestId = "update-style",
            payload = new { style, audioMixSettings, videoTransformSettings }
        }));

        var updatedResponse = responses.Single(response =>
            response.Contains("vietsub.subtitle.style.updated", StringComparison.Ordinal));
        using var updated = JsonDocument.Parse(updatedResponse);
        Assert.Equal(
            "Segoe UI",
            updated.RootElement.GetProperty("payload").GetProperty("fontFamily").GetString());
        Assert.Contains(responses, response => response.Contains("vietsub.operation.completed", StringComparison.Ordinal));
        Assert.Contains(responses, response => response.Contains("vietsub.audio.mix.updated", StringComparison.Ordinal));
        var transformResponse = responses.Single(response =>
            response.Contains("vietsub.video.transform.updated", StringComparison.Ordinal));
        using var transform = JsonDocument.Parse(transformResponse);
        Assert.True(transform.RootElement.GetProperty("payload").GetProperty("flipHorizontal").GetBoolean());
        Assert.True(transform.RootElement.GetProperty("payload").GetProperty("flipVertical").GetBoolean());
        Assert.True(transform.RootElement.GetProperty("payload").GetProperty("subtitleMask").GetProperty("enabled").GetBoolean());
        Assert.DoesNotContain(responses, response => response.Contains("vietsub.error", StringComparison.Ordinal));

        var persisted = await projects.OpenAsync(project.ProjectId, organizationId, owner);
        Assert.Equal("Segoe UI", persisted.SubtitleStyle.FontFamily);
        Assert.Equal(5.2, persisted.SubtitleStyle.FontSizePercent);
        Assert.False(persisted.SubtitleStyle.BackgroundEnabled);
        Assert.Equal(0.4, persisted.AudioMixSettings.OriginalVolume);
        Assert.Equal(1.2, persisted.AudioMixSettings.TranslatedVoiceVolume);
        Assert.False(persisted.AudioMixSettings.AutoDuckOriginal);
        Assert.True(persisted.VideoTransformSettings.FlipHorizontal);
        Assert.True(persisted.VideoTransformSettings.FlipVertical);
        Assert.Equal("BLUR", persisted.VideoTransformSettings.SubtitleMask.Mode);
        Assert.Equal(0.1, persisted.VideoTransformSettings.SubtitleMask.X);
        Assert.Equal(2, persisted.VideoTransformSettings.SubtitleMask.BlurPercent);

        // A malformed mask must reject the complete update without changing the existing design.
        responses.Clear();
        videoTransformSettings.SubtitleMask.Color = "#000000;movie=secret";
        style.FontFamily = "Arial";
        await bridge.TryHandleAsync(JsonSerializer.Serialize(new
        {
            type = "vietsub.subtitle.style.update", requestId = "invalid-mask",
            payload = new { style, audioMixSettings, videoTransformSettings }
        }));
        Assert.Contains(responses, response => response.Contains("vietsub.error", StringComparison.Ordinal));
        Assert.DoesNotContain(responses, response => response.Contains("vietsub.video.transform.updated", StringComparison.Ordinal));
        var unchanged = await projects.OpenAsync(project.ProjectId, organizationId, owner);
        Assert.Equal("Segoe UI", unchanged.SubtitleStyle.FontFamily);
        Assert.Equal("#000000", unchanged.VideoTransformSettings.SubtitleMask.Color);

        responses.Clear();
        currentOrganizationId = Guid.NewGuid();
        await bridge.TryHandleAsync("""
            {"type":"vietsub.subtitle.style.get","requestId":"get-style-wrong-org","payload":{}}
            """);
        using var rejected = JsonDocument.Parse(Assert.Single(responses));
        Assert.Equal(
            "vietsub_access_denied",
            rejected.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private (VietsubAppPaths Paths, VietsubProjectStore Projects) CreateStores()
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        return (paths, new VietsubProjectStore(paths, subtitles));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
