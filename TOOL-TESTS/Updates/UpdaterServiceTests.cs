using System.Text.Json;
using TOOL_SHARED.Contracts.Updates;
using TOOL_UPDATER;

namespace TOOL_TESTS.Updates;

public sealed class UpdaterServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "VideoMakerUpdaterServiceTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Apply_ReplacesAddsAndRemovesManagedFilesButPreservesSettings()
    {
        var (options, stage, target) = CreateLayout();
        WriteManifest(target, "1.0.0", 1, ["TOOL-LOCAL.exe", "old.txt", "appsettings.json", "update-manifest.json"]);
        File.WriteAllText(Path.Combine(target, "TOOL-LOCAL.exe"), "old launcher");
        File.WriteAllText(Path.Combine(target, "old.txt"), "obsolete");
        File.WriteAllText(Path.Combine(target, "appsettings.json"), "user settings");
        WriteManifest(stage, "1.0.1", 2,
        [
            "TOOL-LOCAL.exe",
            "new.txt",
            "appsettings.json",
            .. RequiredMediaFiles,
            "update-manifest.json"
        ]);
        File.WriteAllText(Path.Combine(stage, "TOOL-LOCAL.exe"), "new launcher");
        File.WriteAllText(Path.Combine(stage, "new.txt"), "new file");
        File.WriteAllText(Path.Combine(stage, "appsettings.json"), "package settings");
        WriteRequiredMediaFiles(stage);

        var runtime = new FakeRuntime();
        var result = new UpdaterService(options, runtime).Apply();

        Assert.Equal(0, result);
        Assert.Equal("new launcher", File.ReadAllText(Path.Combine(target, "TOOL-LOCAL.exe")));
        Assert.Equal("new file", File.ReadAllText(Path.Combine(target, "new.txt")));
        Assert.False(File.Exists(Path.Combine(target, "old.txt")));
        Assert.Equal("user settings", File.ReadAllText(Path.Combine(target, "appsettings.json")));
        Assert.True(runtime.Started);
    }

    [Fact]
    public void Apply_RollsBackWhenRestartFails()
    {
        var (options, stage, target) = CreateLayout();
        WriteManifest(target, "1.0.0", 1, ["TOOL-LOCAL.exe", "old.txt", "update-manifest.json"]);
        File.WriteAllText(Path.Combine(target, "TOOL-LOCAL.exe"), "old launcher");
        File.WriteAllText(Path.Combine(target, "old.txt"), "old file");
        WriteManifest(stage, "1.0.1", 2,
        [
            "TOOL-LOCAL.exe",
            "new.txt",
            .. RequiredMediaFiles,
            "update-manifest.json"
        ]);
        File.WriteAllText(Path.Combine(stage, "TOOL-LOCAL.exe"), "new launcher");
        File.WriteAllText(Path.Combine(stage, "new.txt"), "new file");
        WriteRequiredMediaFiles(stage);

        Assert.Throws<InvalidOperationException>(() => new UpdaterService(options, new FakeRuntime { FailStart = true }).Apply());

        Assert.Equal("old launcher", File.ReadAllText(Path.Combine(target, "TOOL-LOCAL.exe")));
        Assert.Equal("old file", File.ReadAllText(Path.Combine(target, "old.txt")));
        Assert.False(File.Exists(Path.Combine(target, "new.txt")));
        var restored = JsonSerializer.Deserialize<DesktopUpdateManifest>(File.ReadAllText(Path.Combine(target, "update-manifest.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(1, restored?.BuildNumber);
    }

    [Fact]
    public void Apply_RejectsPackageWithoutRequiredMediaTools()
    {
        var (options, stage, _) = CreateLayout();
        WriteManifest(stage, "1.0.1", 2, ["TOOL-LOCAL.exe", "update-manifest.json"]);
        File.WriteAllText(Path.Combine(stage, "TOOL-LOCAL.exe"), "new launcher");

        var exception = Assert.Throws<InvalidDataException>(
            () => new UpdaterService(options, new FakeRuntime()).Apply());

        Assert.Contains("media tool", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_RejectsTamperedMediaBundleBeforeStoppingApplication()
    {
        var (options, stage, _) = CreateLayout();
        WriteManifest(stage, "1.0.1", 2,
        [
            "TOOL-LOCAL.exe",
            .. RequiredMediaFiles,
            "update-manifest.json"
        ]);
        File.WriteAllText(Path.Combine(stage, "TOOL-LOCAL.exe"), "new launcher");
        WriteRequiredMediaFiles(stage);
        File.AppendAllText(Path.Combine(stage, "tools", "ffmpeg", "ffmpeg.exe"), "tampered");
        var runtime = new FakeRuntime();

        var exception = Assert.Throws<InvalidDataException>(
            () => new UpdaterService(options, runtime).Apply());

        Assert.Contains("SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(runtime.WaitedForExit);
        Assert.False(runtime.Started);
    }

    private (UpdaterOptions Options, string Stage, string Target) CreateLayout()
    {
        var stage = Path.Combine(_root, "stage");
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(target);
        return (new UpdaterOptions(123, stage, target, "TOOL-LOCAL.exe", Path.Combine(_root, "update.log")), stage, target);
    }

    private static void WriteManifest(string root, string version, int build, string[] files)
    {
        var manifest = new DesktopUpdateManifest("VideoMaker", version, build, "win-x64", files);
        File.WriteAllText(Path.Combine(root, "update-manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static readonly string[] RequiredMediaFiles =
    [
        "tools/ffmpeg/ffmpeg.exe",
        "tools/ffmpeg/ffprobe.exe",
        "tools/ffmpeg/LICENSE.txt",
        "tools/ffmpeg/PROVENANCE.md",
        "tools/ffmpeg/checksums.sha256"
    ];

    private static void WriteRequiredMediaFiles(string root)
    {
        var mediaDirectory = Path.Combine(root, "tools", "ffmpeg");
        Directory.CreateDirectory(mediaDirectory);
        File.WriteAllText(Path.Combine(mediaDirectory, "ffmpeg.exe"), "test ffmpeg");
        File.WriteAllText(Path.Combine(mediaDirectory, "ffprobe.exe"), "test ffprobe");
        File.WriteAllText(Path.Combine(mediaDirectory, "LICENSE.txt"), "test license");
        File.WriteAllText(
            Path.Combine(mediaDirectory, "PROVENANCE.md"),
            $"# Test FFmpeg provenance{Environment.NewLine}{Environment.NewLine}- Approval scope: Release{Environment.NewLine}");
        var checksumLines = new[] { "ffmpeg.exe", "ffprobe.exe", "LICENSE.txt" }
            .Select(fileName =>
            {
                using var stream = File.OpenRead(Path.Combine(mediaDirectory, fileName));
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
                return $"{hash} *{fileName}";
            });
        File.WriteAllLines(Path.Combine(mediaDirectory, "checksums.sha256"), checksumLines);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeRuntime : IUpdaterRuntime
    {
        public bool FailStart { get; init; }
        public bool Started { get; private set; }
        public bool WaitedForExit { get; private set; }
        public void WaitForExit(int processId, TimeSpan timeout) => WaitedForExit = true;
        public void Start(string executablePath, string workingDirectory)
        {
            if (FailStart) throw new InvalidOperationException("restart failed");
            Started = true;
        }
    }
}
