using TOOL_LOCAL.Configuration;
using TOOL_LOCAL.Media;

namespace TOOL_TESTS.Media;

public sealed class MediaToolPreflightServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "VideoMakerMediaToolTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void Resolver_PrefersBundledExecutables()
    {
        var bundle = Path.Combine(_root, "tools", "ffmpeg");
        Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(bundle, "ffmpeg.exe"), "test");
        File.WriteAllText(Path.Combine(bundle, "ffprobe.exe"), "test");

        var result = new MediaToolPathResolver(new MediaToolOptions(), _root).Resolve();

        Assert.Equal(Path.Combine(bundle, "ffmpeg.exe"), result.FfmpegPath);
        Assert.Equal(Path.Combine(bundle, "ffprobe.exe"), result.FfprobePath);
    }

    [Fact]
    public async Task GetStatusAsync_ChecksBothExecutablesAndCachesResult()
    {
        var runner = new FakeProcessRunner(executable =>
            Task.FromResult(new ProcessExecutionResult(
                0,
                executable.Contains("ffprobe", StringComparison.OrdinalIgnoreCase)
                    ? "ffprobe version 7.1"
                    : "ffmpeg version 7.1",
                string.Empty)));
        var service = new MediaToolPreflightService(
            new MediaToolPaths("bundle/ffmpeg.exe", "bundle/ffprobe.exe"),
            runner,
            TimeProvider.System);

        var first = await service.GetStatusAsync(force: false, CancellationToken.None);
        var cached = await service.GetStatusAsync(force: false, CancellationToken.None);

        Assert.True(first.Ready);
        Assert.Equal("ffmpeg version 7.1", first.FfmpegVersion);
        Assert.Equal("ffprobe version 7.1", first.FfprobeVersion);
        Assert.Same(first, cached);
        Assert.Equal(2, runner.CallCount);
    }

    [Fact]
    public async Task RequireReadyAsync_ReturnsStableCodeWhenFfprobeIsMissing()
    {
        var runner = new FakeProcessRunner(executable =>
        {
            if (executable.Contains("ffprobe", StringComparison.OrdinalIgnoreCase))
            {
                throw new FileNotFoundException("missing", executable);
            }

            return Task.FromResult(new ProcessExecutionResult(0, "ffmpeg version 7.1", string.Empty));
        });
        var service = new MediaToolPreflightService(
            new MediaToolPaths("bundle/ffmpeg.exe", "bundle/ffprobe.exe"),
            runner,
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<MediaToolUnavailableException>(
            () => service.RequireReadyAsync(CancellationToken.None));

        Assert.Equal("ffprobe_not_found", exception.Code);
        Assert.Equal(2, runner.CallCount);
    }

    [Fact]
    public async Task GetStatusAsync_DistinguishesExistingButInvalidExecutable()
    {
        Directory.CreateDirectory(_root);
        var ffmpegPath = Path.Combine(_root, "ffmpeg.exe");
        File.WriteAllText(ffmpegPath, "not a Windows executable");
        var runner = new FakeProcessRunner(executable =>
            throw new FileNotFoundException("cannot execute", executable));
        var service = new MediaToolPreflightService(
            new MediaToolPaths(ffmpegPath, Path.Combine(_root, "ffprobe.exe")),
            runner,
            TimeProvider.System);

        var status = await service.GetStatusAsync(force: true, CancellationToken.None);

        Assert.False(status.Ready);
        Assert.Equal("media_tool_not_executable", status.ErrorCode);
        Assert.Equal(1, runner.CallCount);
    }

    [Fact]
    public async Task GetStatusAsync_RejectsMismatchedFfmpegAndFfprobeVersions()
    {
        var runner = new FakeProcessRunner(executable =>
            Task.FromResult(new ProcessExecutionResult(
                0,
                executable.Contains("ffprobe", StringComparison.OrdinalIgnoreCase)
                    ? "ffprobe version 7.0"
                    : "ffmpeg version 7.1",
                string.Empty)));
        var service = new MediaToolPreflightService(
            new MediaToolPaths("bundle/ffmpeg.exe", "bundle/ffprobe.exe"),
            runner,
            TimeProvider.System);

        var status = await service.GetStatusAsync(force: true, CancellationToken.None);

        Assert.False(status.Ready);
        Assert.Equal("media_tool_version_mismatch", status.ErrorCode);
        Assert.Contains("không cùng phiên bản", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStatusAsync_RejectsIncompleteBundledProfileBeforeExecutingTools()
    {
        var bundle = Path.Combine(_root, "tools", "ffmpeg");
        Directory.CreateDirectory(bundle);
        var ffmpegPath = Path.Combine(bundle, "ffmpeg.exe");
        var ffprobePath = Path.Combine(bundle, "ffprobe.exe");
        File.WriteAllText(ffmpegPath, "test");
        File.WriteAllText(ffprobePath, "test");
        var runner = new FakeProcessRunner(_ =>
            Task.FromResult(new ProcessExecutionResult(0, "unused", string.Empty)));
        var service = new MediaToolPreflightService(
            new MediaToolPaths(ffmpegPath, ffprobePath, bundle),
            runner,
            TimeProvider.System);

        var status = await service.GetStatusAsync(force: true, CancellationToken.None);

        Assert.False(status.Ready);
        Assert.Equal("media_tool_bundle_invalid", status.ErrorCode);
        Assert.Contains("cài lại", status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public void DesktopOptions_UserFileOverridesOnlyMediaToolPaths()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "appsettings.json"), """
            {
              "Server": { "BaseUrl": "https://localhost:7202/" },
              "Database": { "ConnectionString": "Server=test;Database=test" },
              "Storage": { "WorkspaceRoot": "workspace" },
              "MediaTools": {
                "FfmpegPath": "tools/ffmpeg/ffmpeg.exe",
                "FfprobePath": "tools/ffmpeg/ffprobe.exe"
              },
              "Update": {
                "Enabled": true,
                "Channel": "Stable",
                "Platform": "win-x64",
                "CheckIntervalSeconds": 120
              }
            }
            """);
        File.WriteAllText(Path.Combine(_root, "appsettings.user.json"), """
            {
              "MediaTools": {
                "FfmpegPath": "D:/MediaTools/ffmpeg.exe",
                "FfprobePath": "D:/MediaTools/ffprobe.exe"
              }
            }
            """);

        var options = DesktopOptions.Load(_root);

        Assert.Equal("https://localhost:7202/", options.Server.BaseUrl);
        Assert.Equal("D:/MediaTools/ffmpeg.exe", options.MediaTools.FfmpegPath);
        Assert.Equal("D:/MediaTools/ffprobe.exe", options.MediaTools.FfprobePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeProcessRunner(
        Func<string, Task<ProcessExecutionResult>> handler) : IExternalProcessRunner
    {
        public int CallCount { get; private set; }

        public Task<ProcessExecutionResult> RunAsync(
            string executable,
            IEnumerable<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            Assert.Equal(["-version"], arguments);
            return handler(executable);
        }
    }
}
