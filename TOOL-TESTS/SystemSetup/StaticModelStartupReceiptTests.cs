using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.ModelDistribution;
using TOOL_LOCAL.SystemSetup;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.ModelDistribution;
using TOOL_SHARED.Distribution.StaticModels;

namespace TOOL_TESTS.SystemSetup;

// Isolated protocol/crash fixtures only: no real AppData, weights, native runtime or network.
public sealed class StaticModelStartupReceiptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sbr-" + Guid.NewGuid().ToString("N")[..12]);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string Metadata => Path.Combine(_root, "m");
    private string Application => Path.Combine(_root, "a");
    private string Pending => Path.Combine(Metadata, "static-model-install.pending.json");
    private string ModelReceipt => Path.Combine(Metadata, "static-model-install.json");
    private string AppReceipt => Path.Combine(Application, "static-setup-receipt.json");
    private string Journal => Path.Combine(Metadata, "installer-transactions", Sha(Path.GetFullPath(Application).TrimEnd('\\').ToUpperInvariant()), "transaction.json");
    private readonly string _operation = Guid.NewGuid().ToString("N");
    private readonly string _packageHash = Sha("package");
    private StaticModelStartupContext Context => new(Metadata, Application, Sha("windows-user"), Sha("application"), "1.0.0", 7, true);

    public StaticModelStartupReceiptTests()
    {
        Directory.CreateDirectory(Metadata);
        Directory.CreateDirectory(Application);
    }

    [Fact]
    public void Component_success_without_app_receipt_stays_blocked_when_pending_and_journal_are_missing()
    {
        WriteModel();
        Assert.False(File.Exists(Pending));
        Assert.False(File.Exists(Journal));
        Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
    }

    [Fact]
    public void App_receipt_before_journal_commit_cannot_unlock_even_if_pending_is_lost()
    {
        WriteModel(); WriteApp();
        Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
        WriteJournal("Probing");
        Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
        WriteJournal("Committed");
        Assert.NotNull(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
        File.Delete(Journal);
        Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
    }

    [Fact]
    public void Long_unicode_owned_metadata_journal_is_read_without_relaxing_cli_path_limits()
    {
        var segment = "T có dấu" + new string('m', Math.Max(0, 150 - _root.Length - 1 - "T có dấu".Length));
        var metadata = Path.Combine(_root, segment);
        var context = Context with { MetadataRoot = metadata };
        var key = Sha(Path.GetFullPath(Application).TrimEnd('\\').ToUpperInvariant());
        var journalPath = Path.Combine(metadata, "installer-transactions", key, "transaction.json");
        Assert.InRange(metadata.Length, 1, 220);
        Assert.True(journalPath.Length > 220);
        Write(Path.Combine(metadata, "static-model-install.json"), Receipt());
        WriteApp();
        Write(journalPath, new { schemaVersion = 1, operationId = _operation, targetRoot = Application,
            releaseSha256 = _packageHash, state = "Committed", changes = Array.Empty<object>() });
        Assert.NotNull(StaticModelRuntimePolicy.ReadReadyReceipt(context));
        Assert.Throws<ArgumentException>(() => StaticModelRuntimePolicy.ValidateLocalPath(journalPath));
        var args = new[] { "--prepare-static-models", "--operation", _operation, "--result", journalPath,
            "--cache-root", Path.Combine(_root, "cache"), "--parent-pid", Environment.ProcessId.ToString() };
        Assert.Throws<ArgumentException>(() => StaticModelBootstrapCommand.ParseOptions(args));
    }

    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("operationId")]
    [InlineData("targetRoot")]
    [InlineData("releaseSha256")]
    [InlineData("changes")]
    public void Incomplete_or_mismatched_committed_journal_never_authorizes_startup(string property)
    {
        WriteModel(); WriteApp(); WriteJournal("Committed");
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Journal))!.AsObject();
        if (property == "schemaVersion") root[property] = 2;
        else if (property == "changes") root.Remove(property);
        else root[property] = "mismatch";
        File.WriteAllText(Journal, root.ToJsonString());
        Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
    }

    [Fact]
    public void Pending_always_blocks_after_commit_and_corrupt_or_directory_marker_does_not_clear_itself()
    {
        WriteComplete();
        Assert.NotNull(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
        File.WriteAllText(Pending, "corrupt-but-still-pending");
        Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
        Assert.Equal("corrupt-but-still-pending", File.ReadAllText(Pending));
        File.Delete(Pending);
        Directory.CreateDirectory(Pending);
        Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
    }

    [Fact]
    public void App_receipt_must_match_operation_bundle_release_build_target_and_package()
    {
        WriteComplete();
        var valid = Read<StaticApplicationReceipt>(AppReceipt);
        foreach (var invalid in new[]
        {
            valid with { OperationId = Guid.NewGuid().ToString("N") }, valid with { BundleId = "other-bundle" },
            valid with { Version = "1.1.0" }, valid with { BuildNumber = 8 },
            valid with { TargetRoot = Path.Combine(_root, "other") }, valid with { PackageSha256 = Sha("other-package") }
        })
        {
            Write(AppReceipt, invalid);
            Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
        }
        Write(AppReceipt, valid);
        Assert.NotNull(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
    }

    [Fact]
    public void Release_gate_does_not_trust_receipt_claiming_it_is_a_standalone_diagnostic()
    {
        WriteModel(Receipt() with { SetupManaged = false });
        Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
        Assert.NotNull(StaticModelRuntimePolicy.ReadReadyReceipt(Context with { RequireApplicationCommit = false }));
    }

    [Fact]
    public void Receipt_from_another_machine_user_root_or_worker_fingerprint_is_rejected()
    {
        WriteComplete();
        Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context with { MachineFingerprint = Sha("other-user") }));
        Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context with { ApplicationFingerprint = Sha("other-worker") }));
        Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context with { ApplicationRoot = Path.Combine(_root, "other") }));
        WriteModel(Receipt() with { SchemaVersion = 1 });
        Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-sha256")]
    public void Missing_component_evidence_cannot_be_promoted_by_ready_booleans(string? fingerprint)
    {
        var receipt = Receipt();
        var result = receipt.Result with
        {
            Components = receipt.Result.Components.Select(x => x.ComponentId == "qwen" ? x with { Fingerprint = fingerprint } : x).ToArray()
        };
        WriteModel(receipt with { Result = result }); WriteApp(); WriteJournal("Committed");
        Assert.False(StaticModelRuntimePolicy.HasAllRequiredComponents(result));
        Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
    }

    [Fact]
    public void Missing_receipts_invalid_json_and_oversized_receipts_fail_closed_without_mutation()
    {
        WriteComplete();
        File.WriteAllText(ModelReceipt, "{");
        Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
        Assert.Equal("{", File.ReadAllText(ModelReceipt));
        File.WriteAllText(ModelReceipt, new string(' ', 64 * 1024 + 1));
        Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
        File.Delete(ModelReceipt);
        Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
    }

    [Fact]
    public void Duplicate_commit_fields_are_rejected_and_equivalent_hash_casing_is_accepted()
    {
        WriteComplete();
        var receipt = Read<StaticApplicationReceipt>(AppReceipt);
        Write(AppReceipt, receipt with { PackageSha256 = receipt.PackageSha256.ToUpperInvariant() });
        Assert.NotNull(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
        var committed = File.ReadAllText(Journal);
        File.WriteAllText(Journal, committed[..^1] + ",\"state\":\"Committed\"}");
        Assert.Null(StaticModelRuntimePolicy.ReadReadyReceipt(Context));
    }

    [Fact]
    public void Fingerprint_changes_for_actual_file_or_verified_profile_changes_and_rejects_missing_file()
    {
        var directory = Path.Combine(_root, "e");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "probe.json");
        File.WriteAllText(path, "verified-first-probe");
        var initial = StaticModelComponentEvidence.DigestFiles("qwen", directory, ["probe.json"], ["profile-one"]);
        Assert.True(StaticModelRuntimePolicy.IsSha256(initial));
        Assert.NotEqual(initial, StaticModelComponentEvidence.DigestFiles("qwen", directory, ["probe.json"], ["profile-two"]));
        File.WriteAllText(path, "verified-other-probe");
        Assert.NotEqual(initial, StaticModelComponentEvidence.DigestFiles("qwen", directory, ["probe.json"], ["profile-one"]));
        File.Delete(path);
        Assert.Throws<FileNotFoundException>(() => StaticModelComponentEvidence.DigestFiles("qwen", directory, ["probe.json"]));
        Assert.Throws<InvalidDataException>(() => StaticModelComponentEvidence.DigestFiles("qwen", directory, ["../outside"]));
    }

    [Fact]
    public void Desktop_read_leases_coexist_but_exclude_setup_and_release_on_dispose()
    {
        var path = Path.Combine(Metadata, "usage.lock");
        using (StaticModelRuntimePolicy.AcquireUsageLease(path, false))
        using (StaticModelRuntimePolicy.AcquireUsageLease(path, false))
            Assert.Throws<IOException>(() => StaticModelRuntimePolicy.AcquireUsageLease(path, true));
        using (StaticModelRuntimePolicy.AcquireUsageLease(path, true))
        {
            Assert.Throws<IOException>(() => StaticModelRuntimePolicy.AcquireUsageLease(path, false));
            Assert.Throws<IOException>(() => StaticModelRuntimePolicy.AcquireUsageLease(path, true));
        }
        using var reopened = StaticModelRuntimePolicy.AcquireUsageLease(path, false);
        Assert.True(reopened.CanRead);
    }

    [Fact]
    public void Readonly_policy_reports_known_revocation_and_corrupt_trust_before_setup_downloads()
    {
        var revoked = StaticModelBootstrapCommand.InspectPolicy(() => throw new ModelDistributionException(
            ModelDistributionErrors.ArtifactNotApproved, "fixture revoked"));
        Assert.False(revoked.ModelUseAllowed);
        Assert.Equal(ModelDistributionErrors.ArtifactNotApproved, revoked.ErrorCode);
        var corrupt = StaticModelBootstrapCommand.InspectPolicy(() => throw new InvalidDataException("fixture corrupt"));
        Assert.False(corrupt.ModelUseAllowed);
        Assert.Equal("static_model_trust_state_invalid", corrupt.ErrorCode);
        Assert.True(StaticModelBootstrapCommand.InspectPolicy(() => { }).ModelUseAllowed);
        Assert.Empty(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Prerequisite_checks_known_revocation_before_native_or_model_preparation()
    {
        var resultPath = Path.Combine(_root, "result.json");
        var cache = Path.Combine(_root, "cache");
        var calls = 0;
        var exit = await StaticModelBootstrapCommand.RunAsync(["--check-static-prerequisites", "--operation", _operation,
            "--result", resultPath, "--cache-root", cache, "--parent-pid", Environment.ProcessId.ToString()], () =>
        {
            calls++;
            throw new ModelDistributionException(ModelDistributionErrors.ArtifactNotApproved, "fixture revoked");
        });
        Assert.Equal(2, exit);
        Assert.Equal(1, calls);
        var result = Read<StaticModelBootstrapResult>(resultPath);
        Assert.False(result.Succeeded);
        Assert.Equal(_operation, result.OperationId);
        Assert.Equal(ModelDistributionErrors.ArtifactNotApproved, result.ErrorCode);
        Assert.Empty(result.Components);
        Assert.False(Directory.Exists(cache));
        Assert.False(File.Exists(ModelReceipt));
        Assert.False(File.Exists(Pending));
    }

    private StaticModelInstallReceipt Receipt() => new(2, Path.Combine(_root, "cache"), Context.MachineFingerprint,
        Context.ApplicationFingerprint, DateTime.UtcNow, new StaticModelBootstrapResult(1, _operation,
            ApprovedStaticModelBundle.Current.BundleId, true, StaticModelRuntimePolicy.RequiredComponents
                .Select(id => new StaticModelComponentResult(id, true, Sha("fixture:" + id))).ToArray()), true, Application);
    private void WriteModel(StaticModelInstallReceipt? value = null) => Write(ModelReceipt, value ?? Receipt());
    private void WriteApp() => Write(AppReceipt, new StaticApplicationReceipt(1, ApprovedStaticModelBundle.Current.BundleId,
        Application, Context.DesktopVersion, Context.DesktopBuildNumber, _packageHash, _operation, DateTime.UtcNow));
    private void WriteJournal(string state) => Write(Journal, new { schemaVersion = 1, operationId = _operation,
        targetRoot = Application, releaseSha256 = _packageHash, state, changes = Array.Empty<object>() });
    private void WriteComplete() { WriteModel(); WriteApp(); WriteJournal("Committed"); }
    private static void Write<T>(string path, T value)
    { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(value, Json)); }
    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)!;
    private static string Sha(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public void Dispose()
    {
        var target = Path.GetFullPath(_root);
        var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid fixture cleanup root.");
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }
}
