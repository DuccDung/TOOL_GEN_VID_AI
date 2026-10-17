using System.Diagnostics;
using System.Text.Json;
using TOOL_LOCAL.ModelDistribution;
using TOOL_LOCAL.SystemSetup;

namespace TOOL_TESTS.SystemSetup;

// Fixtures and a real CLI rejection: no model processes, real AppData, registry or downloads.
public sealed class InstalledStaticModelQualificationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "isq-" + Guid.NewGuid().ToString("N")[..10]);
    private string Output => Path.Combine(_root, "qa");
    private InstalledStaticModelOptions Options(string engine = "qwen", int count = 30) => new(engine, Output, count);

    [Fact]
    public void Cli_cannot_supply_a_model_executable_runtime_or_unbound_confirmation()
    {
        foreach (var extra in new[] { new[] { "--model", "x" }, new[] { "--worker", "x" },
                     new[] { "--cache-root", "x" }, new[] { "--accept-resource-warning" },
                     new[] { "--confirmed-resource-profile", "standard" } })
            Assert.Throws<ArgumentException>(() => InstalledStaticModelCommand.ParseOptions([
                InstalledStaticModelCommand.Flag, "--engine", "qwen", "--output", Output, "--iterations", "30", .. extra]));
        var valid = InstalledStaticModelCommand.ParseOptions([InstalledStaticModelCommand.Flag,
            "--engine", "piper", "--output", Output, "--iterations", "30"]);
        Assert.Equal("piper", valid.Engine);
        Assert.False(valid.AcceptResourceWarning);
    }

    [Fact]
    public async Task Missing_receipt_or_revocation_fails_before_output_or_inference()
    {
        var calls = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => InstalledStaticModelQualification.RunAsync(Options(),
            (_, _) => { calls++; throw new InvalidDataException("No committed receipt."); }, default));
        Assert.Equal(1, calls);
        Assert.False(Directory.Exists(Output));
    }

    [Fact]
    public async Task Real_wrapper_preflight_rejection_creates_no_output_directory_or_series()
    {
        Assert.False(StaticModelRuntimePolicy.RequiredByRelease);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TOOL_GEN_POST_VIDEO.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var script = Path.Combine(directory.FullName, "scripts", "Test-InstalledStaticModel.ps1");
        // Test output contains copied apphosts/runtimeconfig files and is not a
        // desktop deployment. Use the complete desktop build used by native CLI checks.
        var applicationRoot = Path.Combine(directory.FullName, "TOOL-LOCAL", "bin", "Release", "net10.0-windows", "win-x64");
        Assert.True(File.Exists(Path.Combine(applicationRoot, "TOOL-LOCAL.exe")));
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
                     "-InstallRoot", applicationRoot, "-Engine", "qwen", "-OutputDirectory", Output,
                     "-Iterations", "1", "-ColdStarts", "1" }) info.ArgumentList.Add(argument);
        info.Environment["DOTNET_DISABLE_GUI_ERRORS"] = "1";
        var unicodeTemp = Path.Combine(_root, "T có dấu");
        Directory.CreateDirectory(unicodeTemp);
        info.Environment["TEMP"] = unicodeTemp;
        info.Environment["TMP"] = unicodeTemp;
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var output = await stdout; var error = await stderr;
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("installed_model_release_required", output + error, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Output));
            Assert.False(File.Exists(Path.Combine(Output, "series.json")));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task Existing_output_is_preserved_and_session_lease_is_released()
    {
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, "owner.txt"), "keep");
        var session = new Session();
        await Assert.ThrowsAsync<IOException>(() => Run(Options(), session));
        Assert.True(session.Disposed);
        Assert.Equal(0, session.Calls);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(Output, "owner.txt")));
    }

    [Fact]
    public async Task Installed_evidence_changes_stop_before_next_native_iteration()
    {
        var session = new Session { FailGuardAfterIteration = true };
        var report = await Run(Options(), session);
        Assert.False(report.Succeeded);
        Assert.Empty(report.Samples);
        Assert.Equal(1, session.Calls);
        Assert.True(session.Disposed);
        Assert.Equal("installed_model_evidence_invalid", report.ErrorCode);
    }

    [Fact]
    public async Task Native_failure_preserves_readiness_evidence_and_writes_QA_failure_only()
    {
        Directory.CreateDirectory(_root);
        var marker = Path.Combine(_root, "probe.json");
        var receipt = Path.Combine(_root, "static-model-install.json");
        File.WriteAllText(marker, "committed probe"); File.WriteAllText(receipt, "committed receipt");
        var session = new Session { FailIteration = 3 };
        var report = await Run(Options(), session);
        Assert.False(report.Succeeded);
        Assert.Equal(2, report.CompletedIterations);
        Assert.False(report.ProductionReadinessChanged);
        Assert.Equal("committed probe", File.ReadAllText(marker));
        Assert.Equal("committed receipt", File.ReadAllText(receipt));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Output, "qualification.json")));
        Assert.Equal("installed-static-model-qualification", json.RootElement.GetProperty("kind").GetString());
        Assert.False(json.RootElement.GetProperty("productionReadinessChanged").GetBoolean());
    }

    [Fact]
    public async Task Warm_memory_gate_uses_two_exact_ten_sample_windows_and_fails_at_limit()
    {
        var session = new Session { Growth = InstalledStaticModelQualification.MaximumPrivateMemoryGrowthBytes };
        var report = await Run(Options(), session);
        Assert.False(report.Succeeded);
        Assert.Equal("installed_model_memory_growth", report.ErrorCode);
        Assert.Equal(30, report.CompletedIterations);
        Assert.Equal(InstalledStaticModelQualification.MaximumPrivateMemoryGrowthBytes, report.PrivateMemoryGrowthBytes);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task Piper_success_requires_WAV_hash_duration_and_sample_rate()
    {
        var session = new Session { Piper = true };
        var report = await Run(Options("piper", 2), session);
        Assert.True(report.Succeeded);
        Assert.Equal(2, report.CompletedIterations);
        Assert.Null(report.PrivateMemoryGrowthBytes);
        Assert.False(report.BusinessAccessVerified);
        Assert.False(report.NetworkDownloadsAllowed);
    }

    [Fact]
    public async Task Missing_native_metrics_cannot_be_reported_as_success()
    {
        var report = await Run(Options("qwen", 1), new Session { MissingMetrics = true });
        Assert.False(report.Succeeded);
        Assert.Empty(report.Samples);
    }

    [Fact]
    public void Memory_window_matches_bundled_upper_median_and_uses_the_last_ten()
    {
        var samples = Enumerable.Range(1, 40).Select(i => new InstalledStaticModelSample(i, 1, i * 100L)).ToArray();
        Assert.Equal(2_000L, InstalledStaticModelQualification.MemoryGrowth(samples));
        Assert.Null(InstalledStaticModelQualification.MemoryGrowth(samples.Take(29).ToArray()));
        var uneven = Enumerable.Range(1, 30).Select(i => new InstalledStaticModelSample(i, 1,
            i < 21 ? 1 : i < 26 ? 2 : 10)).ToArray();
        Assert.Equal(9L, InstalledStaticModelQualification.MemoryGrowth(uneven));
    }

    [Fact]
    public void Piper_uses_thirty_distinct_synthetic_cases_with_short_long_numbers_and_diacritics()
    {
        var fixtures = Enumerable.Range(1, 30).Select(InstalledStaticModelSession.PiperFixture).ToArray();
        Assert.Equal(30, fixtures.Distinct().Count());
        Assert.True(fixtures.Min(x => x.Length) < 70);
        Assert.True(fixtures.Max(x => x.Length) > 150);
        Assert.Contains(fixtures, x => x.Contains("150"));
        Assert.All(fixtures, x => Assert.Contains(x, c => c > 127));
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("runtime/qa")]
    [InlineData(".")]
    public void Qa_output_cannot_overlap_or_contain_production_root(string relative)
    {
        var runtime = Path.Combine(_root, "runtime");
        Assert.Throws<ArgumentException>(() => InstalledStaticModelSession.AssertSeparateOutput(
            Path.GetFullPath(Path.Combine(_root, relative)), runtime));
        InstalledStaticModelSession.AssertSeparateOutput(Path.Combine(_root, "qa"), runtime);
    }

    private Task<InstalledStaticModelReport> Run(InstalledStaticModelOptions options, Session session) =>
        InstalledStaticModelQualification.RunAsync(options, (_, _) => Task.FromResult<IInstalledStaticModelSession>(session), default);
    private sealed class Session : IInstalledStaticModelSession
    {
        public InstalledStaticModelBinding Binding { get; } = new(Guid.NewGuid().ToString("N"), new('a', 64), new('b', 64), new('c', 64), "fixture");
        public int Calls; public bool Disposed; public int FailIteration; public long Growth;
        public bool Piper; public bool MissingMetrics; public bool FailGuardAfterIteration;
        public void AssertUnchanged()
        {
            Assert.False(Disposed);
            if (FailGuardAfterIteration && Calls > 0) throw new InvalidDataException("Changed ownership.");
        }
        public Task<InstalledStaticModelSample> RunIterationAsync(int iteration, string outputDirectory, CancellationToken token)
        {
            Assert.False(Disposed); Calls++;
            if (iteration == FailIteration) throw new IOException("Native failure.");
            return Task.FromResult(Piper ? new InstalledStaticModelSample(iteration, 1, null, new('d', 64), 100, 22050)
                : new(iteration, 1, MissingMetrics ? null : 1000 + (iteration > 20 ? Growth : 0)));
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
