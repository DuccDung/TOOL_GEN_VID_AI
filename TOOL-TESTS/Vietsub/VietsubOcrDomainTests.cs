using System.Text.Json;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Ocr;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubOcrDomainTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"videomaker-vietsub-ocr-domain-{Guid.NewGuid():N}");

    [Fact]
    public async Task LegacyManifest_LoadsWithBalancedBottomRegionDefaults()
    {
        var paths = new VietsubAppPaths(_root);
        var subtitles = new VietsubSubtitleStore(paths);
        var store = new VietsubProjectStore(paths, subtitles);
        var organizationId = Guid.NewGuid();
        var project = await store.CreateAsync(organizationId, "owner", "Legacy OCR");
        var manifestPath = paths.GetProjectPath(project.ProjectId, "project.json");
        var json = await File.ReadAllTextAsync(manifestPath);
        using var document = JsonDocument.Parse(json);
        var legacy = document.RootElement.EnumerateObject()
            .Where(property => property.Name != "ocrSettings")
            .ToDictionary(property => property.Name, property => property.Value.Clone());
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(legacy));

        var loaded = await store.OpenAsync(project.ProjectId, organizationId, "owner");

        Assert.Equal(VietsubOcrProfileNames.Balanced, loaded.OcrSettings.Profile);
        Assert.Equal(VietsubOcrLanguageCodes.English, loaded.OcrSettings.LanguageCode);
        Assert.Equal(VietsubNormalizedRegion.Default, loaded.OcrSettings.Region);
    }

    [Fact]
    public void RegionResolver_UsesDisplayCoordinatesForRotatedVideo()
    {
        var resolved = VietsubOcrRegionResolver.Resolve(
            1920,
            1080,
            90,
            VietsubNormalizedRegion.Default,
            1080);

        Assert.Equal(1080, resolved.Width);
        Assert.Equal(768, resolved.Height);
        Assert.Equal(0, resolved.X);
        Assert.Equal(1152, resolved.Y);
        Assert.Equal(1080, resolved.OutputWidth);
        Assert.Equal(768, resolved.OutputHeight);
    }

    [Theory]
    [InlineData(0, 0, 0.04, 0.5)]
    [InlineData(0, 0, 0.5, 0.03)]
    [InlineData(0.8, 0, 0.3, 0.5)]
    [InlineData(double.NaN, 0, 0.5, 0.5)]
    public void Region_RejectsInvalidNormalizedValues(double x, double y, double width, double height)
    {
        var exception = Assert.Throws<VietsubOcrException>(() =>
            new VietsubNormalizedRegion(x, y, width, height).Validate());

        Assert.Equal(VietsubOcrErrorCodes.RegionInvalid, exception.Code);
    }

    [Fact]
    public void JobParameters_AreImmutableSnapshotOfSettings()
    {
        var settings = new VietsubOcrSettings
        {
            LanguageCode = "zh-cn",
            Profile = VietsubOcrProfileNames.Fast,
            Region = new VietsubNormalizedRegion(0.1, 0.7, 0.8, 0.2)
        };
        var snapshot = VietsubOcrJobParameters.Create(
            Guid.NewGuid(),
            new string('a', 64),
            30,
            1280,
            720,
            -90,
            settings);

        settings.Profile = VietsubOcrProfileNames.Accurate;
        settings.Region = VietsubNormalizedRegion.Default;

        Assert.Equal(VietsubOcrLanguageCodes.Chinese, snapshot.LanguageCode);
        Assert.Equal(VietsubOcrProfileNames.Fast, snapshot.Profile.Name);
        Assert.Equal(500, snapshot.Profile.SampleIntervalMilliseconds);
        Assert.Equal(270, snapshot.RotationDegrees);
        Assert.Equal(0.1, snapshot.Region.X);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
