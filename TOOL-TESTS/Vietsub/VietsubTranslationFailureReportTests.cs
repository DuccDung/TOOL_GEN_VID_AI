using System.Text.Json;
using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Vietsub.Translation;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubTranslationFailureReportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "qwen-reports-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Worker_crash_preserves_exit_code_and_support_id_without_recording_prompt_or_paths()
    {
        await using var client = Client("crash-after-hello");
        const string secret = "PRIVATE_PROMPT_AND_PATH_Canary";
        var error = await Assert.ThrowsAsync<VietsubTranslationException>(() => client.InferAsync(
            new(secret, secret, secret, 32, 100), null, default));

        Assert.Equal(VietsubTranslationErrorCodes.ProcessCrashed, error.Code);
        var report = Assert.IsType<VietsubTranslationFailureReport>(client.LastFailureReport);
        Assert.Equal(197, report.ExitCode);
        Assert.Equal("0x000000C5", report.ExitCodeHex);
        Assert.Equal("infer", report.Operation);
        Assert.Equal("UNKNOWN", report.Stage);
        Assert.Equal(report.DiagnosticId, error.DiagnosticId);
        var file = Assert.Single(Directory.GetFiles(_root, "*.json"));
        var json = await File.ReadAllTextAsync(file);
        Assert.DoesNotContain(secret, json);
        Assert.DoesNotContain(_root, json);
        Assert.DoesNotContain("stderr", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(report.DiagnosticId, JsonDocument.Parse(json).RootElement.GetProperty("diagnosticId").GetString());
        Assert.Contains(report.DiagnosticId, SetupErrors.Message(error));
        await client.ResetAsync();
        Assert.Single(Directory.GetFiles(_root, "*.json"));
    }

    [Fact]
    public async Task Native_exit_status_and_final_stderr_stage_are_preserved()
    {
        await using var client = Client("crash-with-stage");
        var error = await Assert.ThrowsAsync<VietsubTranslationException>(() => client.InferAsync(
            new("PROBING_RUNTIME", "fixture", "root ::= \"[]\"", 32, 100), null, default));
        var report = Assert.IsType<VietsubTranslationFailureReport>(client.LastFailureReport);
        Assert.Equal(VietsubTranslationErrorCodes.ProcessCrashed, error.Code);
        Assert.Equal("0xC0000005", report.ExitCodeHex);
        Assert.Equal("MODEL_LOAD", report.Stage);
        Assert.Single(Directory.GetFiles(_root, "*.json"));
    }

    [Fact]
    public async Task Report_storage_failure_does_not_replace_the_worker_error()
    {
        Directory.CreateDirectory(_root);
        var occupied = Path.Combine(_root, "occupied");
        await File.WriteAllTextAsync(occupied, "preserve");
        await using var client = Client("crash-after-hello", occupied);
        var error = await Assert.ThrowsAsync<VietsubTranslationException>(() => client.InferAsync(
            new("TEST", "fixture", "root ::= \"[]\"", 32, 100), null, default));
        Assert.Equal(VietsubTranslationErrorCodes.ProcessCrashed, error.Code);
        Assert.NotNull(client.LastFailureReport);
        Assert.Null(error.DiagnosticId);
        Assert.Equal("preserve", await File.ReadAllTextAsync(occupied));
    }

    [Fact]
    public async Task User_cancellation_does_not_create_a_crash_report()
    {
        await using var client = Client("hang-after-hello");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.InferAsync(
            new("TEST", "fixture", "root ::= \"[]\"", 32, 100), null, cancel.Token));
        Assert.Null(client.LastFailureReport);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void Retention_is_bounded_and_preserves_unrelated_files()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "customer.json"), "preserve");
        for (var i = 0; i < 25; i++) Assert.True(VietsubTranslationFailureReports.TryWrite(Report(), _root));
        Assert.Equal(VietsubTranslationFailureReports.MaximumReports, Directory.GetFiles(_root, "qwen-failure-*.json").Length);
        Assert.Equal("preserve", File.ReadAllText(Path.Combine(_root, "customer.json")));
        Assert.Empty(Directory.GetFiles(_root, "*.part"));
    }

    [Fact]
    public void Existing_partial_file_is_not_deleted_or_overwritten()
    {
        Directory.CreateDirectory(_root);
        var report = Report();
        var partial = Path.Combine(_root, "qwen-failure-" + report.DiagnosticId + ".json.part");
        File.WriteAllText(partial, "preserve");
        Assert.False(VietsubTranslationFailureReports.TryWrite(report, _root));
        Assert.Equal("preserve", File.ReadAllText(partial));
    }

    [Theory]
    [InlineData(VietsubTranslationErrorCodes.ProcessCrashed, "dừng bất thường")]
    [InlineData(VietsubTranslationErrorCodes.RuntimeOutOfMemory, "hết bộ nhớ")]
    [InlineData(VietsubTranslationErrorCodes.ProcessTimeout, "thời gian")]
    [InlineData(VietsubTranslationErrorCodes.BackendLoadFailed, "runtime Qwen")]
    public void Setup_explains_the_specific_failure_and_rejects_untrusted_support_ids(string code, string expected)
    {
        var failure = new VietsubTranslationException(code, "private-message") { DiagnosticId = "../../private" };
        Assert.Contains(expected, SetupErrors.Message(failure));
        Assert.DoesNotContain("private", SetupErrors.Message(failure));
    }

    private VietsubTranslationWorkerClient Client(string mode, string? directory = null) => new(
        VietsubTranslationWorkerClientOptions.CreateDefault() with
        {
            EnvironmentVariables = new Dictionary<string, string?> { ["VIDEOMAKER_TRANSLATION_WORKER_TEST_MODE"] = mode },
            DiagnosticDirectory = directory ?? _root,
            CancelGracePeriod = TimeSpan.FromMilliseconds(250)
        }, resourceRequirements: new(0, 0, 0));

    private static VietsubTranslationFailureReport Report() => new(Guid.NewGuid().ToString("N"), DateTime.UtcNow,
        VietsubTranslationErrorCodes.ProcessCrashed, "load", "LOADING", VietsubTranslationWorkerProtocol.WorkerVersion,
        null, "10.0.9", 1, unchecked((int)0xc0000005), null, null, "cpu", null, null);

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
