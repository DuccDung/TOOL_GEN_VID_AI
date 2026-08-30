using TOOL_UPDATER;

namespace TOOL_TESTS.Updates;

public sealed class UpdaterOptionsTests : IDisposable
{
    private readonly string _stage = Path.Combine(Path.GetTempPath(), "VideoMakerUpdaterTests", Guid.NewGuid().ToString("N"));

    public UpdaterOptionsTests() => Directory.CreateDirectory(_stage);

    [Fact]
    public void Parse_NormalizesValidArguments()
    {
        var target = Path.Combine(Path.GetTempPath(), "VideoMakerTarget", Guid.NewGuid().ToString("N"));
        var log = Path.Combine(_stage, "update.log");

        var options = UpdaterOptions.Parse([
            "--pid", "123",
            "--stage", _stage,
            "--target", target,
            "--restart", "TOOL-LOCAL.exe",
            "--log", log
        ]);

        Assert.Equal(123, options.LauncherProcessId);
        Assert.Equal(Path.GetFullPath(_stage), options.StageDirectory);
        Assert.Equal("TOOL-LOCAL.exe", options.RestartExecutableName);
    }

    [Fact]
    public void Parse_RejectsRestartPathInsteadOfFileName()
    {
        Assert.Throws<ArgumentException>(() => UpdaterOptions.Parse([
            "--pid", "123",
            "--stage", _stage,
            "--target", Path.Combine(_stage, "target"),
            "--restart", "subdir\\TOOL-LOCAL.exe",
            "--log", Path.Combine(_stage, "update.log")
        ]));
    }

    public void Dispose()
    {
        if (Directory.Exists(_stage)) Directory.Delete(_stage, recursive: true);
    }
}
