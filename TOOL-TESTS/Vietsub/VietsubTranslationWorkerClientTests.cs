using TOOL_LOCAL.Vietsub.Translation;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubTranslationWorkerClientTests
{
    private const ulong GiB = 1024UL * 1024 * 1024;

    [Fact]
    public async Task Fake_worker_completes_handshake_load_and_inference()
    {
        await using var client = CreateClient("echo");

        var loaded = await client.LoadAsync(CreateLoadRequest(), null, CancellationToken.None);
        var inference = await client.InferAsync(
            new VietsubTranslationWorkerInferRequest(
                "TEST",
                "private fixture prompt",
                "root ::= \"[]\"",
                32,
                100),
            null,
            CancellationToken.None);

        Assert.Equal("test-cpu-avx2", loaded.BackendIdentity);
        Assert.Equal("[]", inference.RawOutput);
        Assert.True(client.IsLoaded(loaded.ConfigFingerprint));
    }

    [Theory]
    [InlineData("crash-after-hello", VietsubTranslationErrorCodes.ProcessCrashed)]
    [InlineData("malformed-after-hello", VietsubTranslationErrorCodes.WorkerProtocolInvalid)]
    public async Task Worker_exit_or_malformed_frame_isolated_from_client_process(
        string mode,
        string expectedCode)
    {
        await using var client = CreateClient(mode);

        var exception = await Assert.ThrowsAsync<VietsubTranslationException>(() =>
            client.LoadAsync(CreateLoadRequest(), null, CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
        Assert.False(client.IsLoaded("not-loaded"));
    }

    [Fact]
    public async Task Hung_worker_times_out_and_is_killed_without_orphaning_client()
    {
        await using var client = CreateClient(
            "hang-after-hello",
            loadTimeout: TimeSpan.FromMilliseconds(250));

        var exception = await Assert.ThrowsAsync<VietsubTranslationException>(() =>
            client.LoadAsync(CreateLoadRequest(), null, CancellationToken.None));

        Assert.Equal(VietsubTranslationErrorCodes.ProcessTimeout, exception.Code);
    }

    [Fact]
    public async Task Cancellation_has_grace_period_then_kills_hung_worker_tree()
    {
        await using var client = CreateClient(
            "hang-after-hello",
            loadTimeout: TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.LoadAsync(CreateLoadRequest(), null, cancellation.Token));

        Assert.False(client.IsLoaded("not-loaded"));
    }

    [Fact]
    public async Task Timeout_kills_the_entire_worker_process_tree()
    {
        await using var client = CreateClient(
            "hang-with-child",
            loadTimeout: TimeSpan.FromMilliseconds(350));

        var exception = await Assert.ThrowsAsync<VietsubTranslationException>(() =>
            client.LoadAsync(CreateLoadRequest(), null, CancellationToken.None));
        Assert.Equal(VietsubTranslationErrorCodes.ProcessTimeout, exception.Code);

        var pidLine = client.LastDiagnostics
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Last(line => line.StartsWith("child_pid:", StringComparison.Ordinal));
        var childPid = int.Parse(pidLine["child_pid:".Length..], System.Globalization.CultureInfo.InvariantCulture);
        await Task.Delay(150);
        Assert.False(IsProcessAlive(childPid));
    }

    [Fact]
    public async Task Large_stderr_is_drained_and_retained_in_a_bounded_ring()
    {
        await using var client = CreateClient("stderr-large");

        await client.LoadAsync(CreateLoadRequest(), null, CancellationToken.None);

        Assert.InRange(client.LastDiagnostics.Length, 1, 32 * 1024);
        Assert.Contains("bounded-stderr-test", client.LastDiagnostics, StringComparison.Ordinal);
        Assert.DoesNotContain("private fixture prompt", client.LastDiagnostics, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Low_memory_requires_confirmation_before_worker_spawn()
    {
        var options = CreateOptions("echo", TimeSpan.FromSeconds(2));
        await using var client = new VietsubTranslationWorkerClient(
            options,
            new FixedMemoryProbe(new VietsubTranslationMemorySnapshot(
                16 * GiB,
                1 * GiB,
                32 * GiB,
                2 * GiB,
                90,
                DateTime.UtcNow)));

        var exception = await Assert.ThrowsAsync<VietsubTranslationException>(() =>
            client.LoadAsync(CreateLoadRequest(), null, CancellationToken.None));

        Assert.Equal(VietsubTranslationErrorCodes.ResourceConfirmationRequired, exception.Code);
        Assert.Contains("vẫn có thể tiếp tục", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Confirmed_low_memory_is_allowed_to_start_the_worker()
    {
        var options = CreateOptions("echo", TimeSpan.FromSeconds(2));
        await using var client = new VietsubTranslationWorkerClient(
            options,
            new FixedMemoryProbe(new VietsubTranslationMemorySnapshot(
                16 * GiB,
                1 * GiB,
                32 * GiB,
                2 * GiB,
                90,
                DateTime.UtcNow)));

        var loaded = await client.LoadAsync(
            CreateLoadRequest() with { ResourceWarningAccepted = true },
            null,
            CancellationToken.None);

        Assert.Equal("test-cpu-avx2", loaded.BackendIdentity);
        Assert.True(client.IsLoaded(loaded.ConfigFingerprint));
    }

    [Fact]
    public async Task Cpu_backend_dry_run_reports_selected_avx_and_native_hash()
    {
        await using var client = CreateClient("backend-preflight");

        var result = await client.LoadAsync(CreateLoadRequest(), null, CancellationToken.None);

        Assert.StartsWith("cpu-", result.BackendIdentity, StringComparison.Ordinal);
        Assert.Matches("^[0-9a-f]{64}$", result.NativeLibraryHash);
        Assert.Equal(
            VietsubTranslationWorkerProfiles.SelectAvxName().Replace("noavx", "none", StringComparison.Ordinal),
            result.AvxLevel.ToLowerInvariant());
    }

    [Fact]
    public async Task Missing_native_backend_is_reported_without_crashing_client()
    {
        await using var client = CreateClient("backend-preflight-missing");

        var exception = await Assert.ThrowsAsync<VietsubTranslationException>(() =>
            client.LoadAsync(CreateLoadRequest(), null, CancellationToken.None));

        Assert.Equal(VietsubTranslationErrorCodes.BackendLoadFailed, exception.Code);
    }

    [Fact]
    public async Task Missing_worker_executable_has_a_stable_repair_error()
    {
        var options = CreateOptions("echo", TimeSpan.FromSeconds(2)) with
        {
            WorkerExecutablePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing-worker.exe")
        };
        await using var client = new VietsubTranslationWorkerClient(
            options,
            new FixedMemoryProbe(new VietsubTranslationMemorySnapshot(
                16 * GiB, 10 * GiB, 32 * GiB, 20 * GiB, 25, DateTime.UtcNow)));

        var exception = await Assert.ThrowsAsync<VietsubTranslationException>(() =>
            client.LoadAsync(CreateLoadRequest(), null, CancellationToken.None));

        Assert.Equal(VietsubTranslationErrorCodes.BackendLoadFailed, exception.Code);
    }

    private static VietsubTranslationWorkerClient CreateClient(
        string mode,
        TimeSpan? loadTimeout = null) => new(
        CreateOptions(mode, loadTimeout ?? TimeSpan.FromSeconds(5)),
        new FixedMemoryProbe(new VietsubTranslationMemorySnapshot(
            16 * GiB,
            10 * GiB,
            32 * GiB,
            20 * GiB,
            25,
            DateTime.UtcNow)));

    private static VietsubTranslationWorkerClientOptions CreateOptions(
        string mode,
        TimeSpan loadTimeout) => new(
        Path.Combine(
            AppContext.BaseDirectory,
            "_translation_worker",
            "VideoMaker.Vietsub.TranslationWorker.exe"),
        TimeSpan.FromSeconds(5),
        loadTimeout,
        TimeSpan.FromSeconds(5),
        TimeSpan.FromMilliseconds(250),
        new Dictionary<string, string?>
        {
            ["VIDEOMAKER_TRANSLATION_WORKER_TEST_MODE"] = mode
        });

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static VietsubTranslationWorkerLoadRequest CreateLoadRequest()
    {
        var config = VietsubTranslationWorkerProfiles.CreateSafeCpuProfile(Environment.ProcessorCount);
        return new VietsubTranslationWorkerLoadRequest(
            Path.GetTempPath(),
            Path.Combine(Path.GetTempPath(), "fixture.gguf"),
            "fixture",
            VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km.EngineId,
            VietsubTranslationApprovedComponents.Qwen3_4B_Q4Km.EngineVersion,
            "fixture.gguf",
            1,
            new string('0', 64),
            config,
            VietsubTranslationResourceRequirements.SafeCpuBaseline);
    }

    private sealed class FixedMemoryProbe(VietsubTranslationMemorySnapshot snapshot)
        : IVietsubTranslationMemoryProbe
    {
        public bool TryCapture(out VietsubTranslationMemorySnapshot value, out string? error)
        {
            value = snapshot;
            error = null;
            return true;
        }
    }
}
