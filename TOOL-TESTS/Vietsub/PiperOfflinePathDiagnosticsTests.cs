using System.ComponentModel;
using System.Text.Json;
using TOOL_LOCAL.Vietsub.Voice;

namespace TOOL_TESTS.Vietsub;

public sealed class PiperOfflinePathDiagnosticsTests
{
    private const string PrivatePath = "C:\\PrivateUser\\sensitive-workspace\\runtime";
    private const string PrivateMessage = "provider-secret and transcript must not enter diagnostics";

    [Theory]
    [InlineData((int)PiperInstallPhase.Extract)]
    [InlineData((int)PiperInstallPhase.Python)]
    [InlineData((int)PiperInstallPhase.Packages)]
    public void Actual_path_limit_exception_is_classified_without_returning_private_path(int phaseValue)
    {
        var phase = (PiperInstallPhase)phaseValue;
        var original = new PathTooLongException(PrivateMessage + " " + PrivatePath);
        var error = PiperInstallDiagnostics.Normalize(phase, original, PrivatePath);
        var details = Assert.IsType<PiperInstallFailureDetails>(PiperInstallDiagnostics.GetDetails(error));
        Assert.Equal(VietsubVoiceErrorCodes.RuntimeInstallFailed, error.Code);
        Assert.Equal("PATH_TOO_LONG", details.FailureKind);
        Assert.Equal(206, details.NativeErrorCode);
        Assert.Equal(phase.ToString().ToUpperInvariant(), details.Phase);
        Assert.Equal(PrivatePath.Length, details.RuntimeRootCharacters);
        Assert.Null(details.ExitCode);
        Assert.Null(error.InnerException);
        AssertSafe(error, details);
    }

    [Fact]
    public void Windows_error_206_is_reported_as_path_limit_but_other_native_errors_are_not()
    {
        var longPath = "C:\\" + new string('x', 350);
        var tooLong = PiperInstallDiagnostics.Normalize(PiperInstallPhase.Python,
            new Win32Exception(206, PrivateMessage + PrivatePath), PrivatePath);
        Assert.Equal("PATH_TOO_LONG", PiperInstallDiagnostics.GetDetails(tooLong)!.FailureKind);
        var missing = PiperInstallDiagnostics.Normalize(PiperInstallPhase.Python,
            new Win32Exception(3, PrivateMessage + PrivatePath), longPath);
        var details = PiperInstallDiagnostics.GetDetails(missing)!;
        Assert.Equal("PROCESS_START_FAILED", details.FailureKind);
        Assert.Equal(3, details.NativeErrorCode);
        Assert.DoesNotContain("giới hạn đường dẫn", missing.Message, StringComparison.OrdinalIgnoreCase);
        AssertSafe(missing, details);
    }

    [Fact]
    public void Nonzero_uv_exit_has_numeric_code_and_phase_without_claiming_a_path_cause()
    {
        var longPath = "C:\\" + new string('x', 350);
        var error = PiperInstallDiagnostics.ProcessExited(PiperInstallPhase.Python, 1, longPath);
        var details = PiperInstallDiagnostics.GetDetails(error)!;
        Assert.Equal("PROCESS_EXIT", details.FailureKind);
        Assert.Equal(1, details.ExitCode);
        Assert.Equal("PYTHON", details.Phase);
        Assert.Null(details.NativeErrorCode);
        Assert.DoesNotContain("giới hạn đường dẫn", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(longPath, JsonSerializer.Serialize(details), StringComparison.Ordinal);
    }

    [Fact]
    public void Component_error_code_and_existing_process_diagnostics_are_preserved()
    {
        var original = new VietsubVoiceException(VietsubVoiceErrorCodes.BundleInvalid, "Bundle rejected.");
        var normalized = PiperInstallDiagnostics.Normalize(PiperInstallPhase.Validate, original, PrivatePath);
        Assert.Same(original, normalized);
        Assert.Equal(VietsubVoiceErrorCodes.BundleInvalid, normalized.Code);
        Assert.Equal("COMPONENT_REJECTED", PiperInstallDiagnostics.GetDetails(normalized)!.FailureKind);
        var process = PiperInstallDiagnostics.ProcessExited(PiperInstallPhase.Python, 2, PrivatePath);
        var preserved = PiperInstallDiagnostics.Normalize(PiperInstallPhase.Ready, process, PrivatePath);
        Assert.Same(process, preserved);
        Assert.Equal("PYTHON", PiperInstallDiagnostics.GetDetails(preserved)!.Phase);
        Assert.Equal(2, PiperInstallDiagnostics.GetDetails(preserved)!.ExitCode);
    }

    [Fact]
    public void Access_denied_does_not_become_a_path_limit_or_leak_the_native_exception()
    {
        var error = PiperInstallDiagnostics.Normalize(PiperInstallPhase.Extract,
            new UnauthorizedAccessException(PrivateMessage + PrivatePath), PrivatePath);
        var details = PiperInstallDiagnostics.GetDetails(error)!;
        Assert.Equal("ACCESS_DENIED", details.FailureKind);
        Assert.Null(details.NativeErrorCode);
        Assert.Null(error.InnerException);
        AssertSafe(error, details);
    }

    [Fact]
    public void Diagnostic_lookup_never_serializes_arbitrary_exception_data()
    {
        var error = new IOException(PrivateMessage + PrivatePath);
        error.Data["untrusted"] = PrivateMessage + PrivatePath;
        Assert.Null(PiperInstallDiagnostics.GetDetails(error));
        var process = PiperInstallDiagnostics.ProcessExited(PiperInstallPhase.Packages, -1, PrivatePath);
        var wrapper = new IOException(PrivateMessage, process);
        Assert.Equal(-1, PiperInstallDiagnostics.GetDetails(wrapper)!.ExitCode);
    }

    [Fact]
    public async Task Offline_process_output_is_drained_with_a_fixed_buffer_and_never_accumulated()
    {
        using var reader = new CountingReader(2_000_000);
        await PiperInstallDiagnostics.DrainOutputAsync(reader, CancellationToken.None);
        Assert.Equal(2_000_000, reader.Processed);
        Assert.True(reader.Reads > 1);
        Assert.Equal(PiperInstallDiagnostics.OutputDrainBufferCharacters, reader.MaximumRequested);
    }

    [Fact]
    public async Task Output_drain_observes_cancellation_without_a_process_or_model()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var reader = new CountingReader(2_000_000);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PiperInstallDiagnostics.DrainOutputAsync(reader, cancellation.Token));
        Assert.Equal(0, reader.Processed);
    }

    private static void AssertSafe(Exception error, PiperInstallFailureDetails details)
    {
        var serialized = JsonSerializer.Serialize(details);
        Assert.DoesNotContain(PrivatePath, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateMessage, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(PrivatePath, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(PrivateMessage, error.ToString(), StringComparison.Ordinal);
    }

    private sealed class CountingReader(int remaining) : TextReader
    {
        public int Processed { get; private set; }
        public int Reads { get; private set; }
        public int MaximumRequested { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            MaximumRequested = Math.Max(MaximumRequested, buffer.Length);
            var count = Math.Min(remaining, buffer.Length);
            buffer.Span[..count].Fill('s');
            remaining -= count;
            Processed += count;
            return ValueTask.FromResult(count);
        }
    }
}
