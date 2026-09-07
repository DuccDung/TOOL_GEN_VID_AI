using System.Buffers.Binary;
using System.Text;
using TOOL_LOCAL.Vietsub.Translation;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubTranslationWorkerSafetyTests
{
    private const ulong GiB = 1024UL * 1024 * 1024;

    [Fact]
    public void Resource_gate_distinguishes_total_physical_available_physical_and_commit()
    {
        var requirements = new VietsubTranslationResourceRequirements(8 * GiB, 4 * GiB, 6 * GiB);

        var total = VietsubTranslationResourceGate.Evaluate(
            new FixedMemoryProbe(Snapshot(7 * GiB, 6 * GiB, 10 * GiB)),
            requirements);
        var physical = VietsubTranslationResourceGate.Evaluate(
            new FixedMemoryProbe(Snapshot(16 * GiB, 3 * GiB, 10 * GiB)),
            requirements);
        var commit = VietsubTranslationResourceGate.Evaluate(
            new FixedMemoryProbe(Snapshot(16 * GiB, 8 * GiB, 5 * GiB)),
            requirements);
        var allowed = VietsubTranslationResourceGate.Evaluate(
            new FixedMemoryProbe(Snapshot(16 * GiB, 4 * GiB, 6 * GiB)),
            requirements);

        Assert.Equal(VietsubTranslationResourceFailure.TotalPhysicalBelowMinimum, total.Failure);
        Assert.True(total.RequiresConfirmation);
        Assert.Equal(VietsubTranslationResourceFailure.AvailablePhysicalBelowMinimum, physical.Failure);
        Assert.True(physical.RequiresConfirmation);
        Assert.Contains("vẫn có thể tiếp tục", physical.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(VietsubTranslationResourceFailure.AvailableCommitBelowMinimum, commit.Failure);
        Assert.True(commit.RequiresConfirmation);
        Assert.True(allowed.CanLoad);
    }

    [Fact]
    public void Resource_gate_turns_an_unavailable_memory_snapshot_into_a_confirmable_warning()
    {
        var result = VietsubTranslationResourceGate.Evaluate(
            new FailedMemoryProbe(),
            new VietsubTranslationResourceRequirements(1, 1, 1));

        Assert.False(result.CanLoad);
        Assert.True(result.RequiresConfirmation);
        Assert.False(result.IsBlocking);
        Assert.Equal(VietsubTranslationResourceFailure.SnapshotUnavailable, result.Failure);
    }

    [Fact]
    public void Low_memory_profile_lowers_the_advisory_threshold_and_reduces_scene_footprint()
    {
        var snapshot = Snapshot(6 * GiB, 3 * GiB, 4 * GiB);
        var standard = VietsubTranslationResourceGate.Evaluate(
            new FixedMemoryProbe(snapshot),
            VietsubTranslationResourceRequirements.StandardCpu);
        var lowMemory = VietsubTranslationResourceGate.Evaluate(
            new FixedMemoryProbe(snapshot),
            VietsubTranslationResourceRequirements.LowMemoryCpu);
        var standardProfile = VietsubTranslationWorkerProfiles.CreateStandardCpuRuntimeProfile(8);
        var lowMemoryProfile = VietsubTranslationWorkerProfiles.CreateLowMemoryCpuRuntimeProfile(8);

        Assert.False(standard.CanLoad);
        Assert.True(standard.RequiresConfirmation);
        Assert.True(lowMemory.CanLoad);
        Assert.False(standardProfile.IsLowMemory);
        Assert.True(lowMemoryProfile.IsLowMemory);
        Assert.Equal(4_096, lowMemoryProfile.InferenceConfig.ContextSize);
        Assert.True(lowMemoryProfile.InferenceConfig.BatchSize < standardProfile.InferenceConfig.BatchSize);
        Assert.True(lowMemoryProfile.MaximumTargetCues < standardProfile.MaximumTargetCues);
        Assert.True(lowMemoryProfile.MaximumSourceCharacters < standardProfile.MaximumSourceCharacters);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VietsubTranslationWorkerProfiles.CreateRuntimeProfile("profile-from-webview", 8));
    }

    [Fact]
    public async Task Protocol_round_trips_versioned_strict_utf8_frame()
    {
        await using var stream = new MemoryStream();
        var expected = VietsubTranslationWorkerProtocol.Create(
            VietsubTranslationWorkerProtocol.Progress,
            "request_01",
            new VietsubTranslationWorkerProgress("PROBING_EN", 50, "Đang kiểm tra."));

        await VietsubTranslationWorkerFrameCodec.WriteAsync(stream, expected, CancellationToken.None);
        stream.Position = 0;
        var actual = Assert.IsType<VietsubTranslationWorkerEnvelope>(
            await VietsubTranslationWorkerFrameCodec.ReadAsync(stream, CancellationToken.None));
        var progress = VietsubTranslationWorkerProtocol.ReadPayload<VietsubTranslationWorkerProgress>(actual);

        Assert.Equal(VietsubTranslationWorkerProtocol.Version, actual.ProtocolVersion);
        Assert.Equal(expected.RequestId, actual.RequestId);
        Assert.Equal("Đang kiểm tra.", progress.Message);
    }

    [Fact]
    public async Task Protocol_rejects_oversized_invalid_utf8_and_unknown_schema()
    {
        await using var oversized = new MemoryStream();
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(
            header,
            VietsubTranslationWorkerProtocol.MaximumFrameBytes + 1);
        await oversized.WriteAsync(header);
        oversized.Position = 0;
        await Assert.ThrowsAsync<VietsubTranslationWorkerProtocolException>(() =>
            VietsubTranslationWorkerFrameCodec.ReadAsync(oversized, CancellationToken.None));

        await using var invalidUtf8 = CreateRawFrame([0xff]);
        await Assert.ThrowsAsync<VietsubTranslationWorkerProtocolException>(() =>
            VietsubTranslationWorkerFrameCodec.ReadAsync(invalidUtf8, CancellationToken.None));

        var json = Encoding.UTF8.GetBytes(
            "{\"protocolVersion\":1,\"type\":\"result\",\"requestId\":\"r1\",\"payload\":{},\"errorCode\":null,\"message\":null,\"retryable\":false,\"unexpected\":true}");
        await using var unknownSchema = CreateRawFrame(json);
        await Assert.ThrowsAsync<VietsubTranslationWorkerProtocolException>(() =>
            VietsubTranslationWorkerFrameCodec.ReadAsync(unknownSchema, CancellationToken.None));
    }

    [Fact]
    public void Config_fingerprint_changes_when_native_footprint_changes()
    {
        var baseline = VietsubTranslationWorkerProfiles.CreateSafeCpuProfile(8);
        var changed = baseline with { ContextSize = 8_192 };

        Assert.NotEqual(
            VietsubTranslationWorkerProtocol.ComputeConfigFingerprint(baseline),
            VietsubTranslationWorkerProtocol.ComputeConfigFingerprint(changed));
    }

    [Theory]
    [InlineData(true, true, "avx2")]
    [InlineData(false, true, "avx")]
    [InlineData(false, false, "noavx")]
    public void Avx_policy_selects_only_the_best_supported_cpu_backend(
        bool avx2,
        bool avx,
        string expected)
    {
        Assert.Equal(expected, VietsubTranslationWorkerProfiles.SelectAvxName(avx2, avx));
    }

    [Fact]
    public void Worker_and_protocol_have_no_cloud_credential_or_workflow_database_dependency()
    {
        var workerProject = ReadRepositoryFile(
            "TOOL-VIETSUB-TRANSLATION-WORKER",
            "TOOL-VIETSUB-TRANSLATION-WORKER.csproj");
        var workerSource = string.Join(
            "\n",
            Directory.GetFiles(
                    FindRepositoryDirectory("TOOL-VIETSUB-TRANSLATION-WORKER"),
                    "*.cs",
                    SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));
        var protocol = ReadRepositoryFile(
            "TOOL-LOCAL",
            "Vietsub",
            "Translation",
            "VietsubTranslationWorkerProtocol.cs");
        var desktopProject = ReadRepositoryFile("TOOL-LOCAL", "TOOL-LOCAL.csproj");

        Assert.DoesNotContain(
            "<PackageReference Include=\"LLamaSharp",
            desktopProject,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HttpClient", workerSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api.openai.com", workerSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kling", workerSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", workerSource, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Microsoft.Data.Sqlite", workerProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EntityFramework", workerProject, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connectionString", protocol, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("credential", protocol, StringComparison.OrdinalIgnoreCase);
    }

    private static MemoryStream CreateRawFrame(byte[] payload)
    {
        var stream = new MemoryStream();
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        stream.Write(header);
        stream.Write(payload);
        stream.Position = 0;
        return stream;
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Cannot locate repository file: {Path.Combine(relativeParts)}");
    }

    private static string FindRepositoryDirectory(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Cannot locate repository directory: {Path.Combine(relativeParts)}");
    }

    private static VietsubTranslationMemorySnapshot Snapshot(
        ulong totalPhysical,
        ulong availablePhysical,
        ulong availableCommit) => new(
        totalPhysical,
        availablePhysical,
        32 * GiB,
        availableCommit,
        25,
        DateTime.UtcNow);

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

    private sealed class FailedMemoryProbe : IVietsubTranslationMemoryProbe
    {
        public bool TryCapture(out VietsubTranslationMemorySnapshot snapshot, out string? error)
        {
            snapshot = new VietsubTranslationMemorySnapshot(0, 0, 0, 0, 0, DateTime.UtcNow);
            error = "fixture_failure";
            return false;
        }
    }
}
