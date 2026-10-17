extern alias SetupAssembly;
using System.Text.Json;
using System.Text.Json.Nodes;
using SetupAssembly::TOOL_SETUP;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;

namespace TOOL_TESTS.Updates;

public sealed class StaticSetupRepairTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "setup-repair-" + Guid.NewGuid().ToString("N"));
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly string[] Files = ["TOOL-LOCAL.exe", "app.dll", "update-manifest.json", "static-setup-receipt.json"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrCorruptManifestRepairsOnlyCommittedOwnedFilesAndPreservesUnmanagedData(bool corrupt)
    {
        var target = Path.Combine(_root, "installed"); var metadata = Path.Combine(_root, "metadata");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "retired.dll"), "old managed file");
        var transaction = new StaticSetupTransaction(Path.Combine(metadata, "installer-transactions", StaticSetupTransaction.TargetKey(target)), target);
        Stage(transaction, "old"); transaction.Begin(Hash); transaction.Snapshot(Files, ["retired.dll"], CancellationToken.None);
        transaction.Apply(null, CancellationToken.None); transaction.Commit(); WriteReceipt(target, transaction.Current!.OperationId);
        var priorCommit = transaction.Current!;
        var manifest = Path.Combine(target, "update-manifest.json");
        if (corrupt) File.WriteAllText(manifest, "{broken manifest"); else File.Delete(manifest);
        File.WriteAllText(Path.Combine(target, "user.mp4"), "unmanaged media");
        File.WriteAllText(Path.Combine(target, "user.dll"), "unmanaged collision");

        var owned = LauncherInstallerService.ResolvePreviousManagedFiles(target, transaction);
        Assert.Equal(Files.Order(), owned.Order());
        Assert.DoesNotContain("retired.dll", owned); Assert.DoesNotContain("user.dll", owned);
        Stage(transaction, "repaired"); File.WriteAllText(Path.Combine(transaction.StageRoot, "user.dll"), "must not overwrite");
        transaction.Begin(Hash, preserveStagedPayload: true);
        Assert.Throws<InvalidOperationException>(() => transaction.Snapshot(Files.Append("user.dll"), owned, CancellationToken.None));
        Assert.Equal("unmanaged collision", File.ReadAllText(Path.Combine(target, "user.dll")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "TOOL-LOCAL.exe")));
        transaction.RestoreCommittedBeforeMutation(priorCommit);

        transaction.Begin(Hash, preserveStagedPayload: true); transaction.Snapshot(Files, owned, CancellationToken.None);
        transaction.Apply(null, CancellationToken.None); transaction.Commit(); WriteReceipt(target, transaction.Current!.OperationId);
        Assert.Equal("repaired", File.ReadAllText(Path.Combine(target, "TOOL-LOCAL.exe")));
        Assert.Equal("repaired", File.ReadAllText(Path.Combine(target, "app.dll")));
        using var repairedManifest = JsonDocument.Parse(File.ReadAllText(manifest));
        Assert.Equal("VideoMaker", repairedManifest.RootElement.GetProperty("product").GetString());
        Assert.Equal("unmanaged media", File.ReadAllText(Path.Combine(target, "user.mp4")));
        Assert.Equal("unmanaged collision", File.ReadAllText(Path.Combine(target, "user.dll")));
        Assert.False(File.Exists(Path.Combine(target, "retired.dll")));
        SetupMaintenanceHandoff.ValidateOwnership(target, "1.0.0", 7, Hash, metadata);
    }

    [Fact]
    public void ManifestFallbackRejectsAReceiptFromAnotherCommittedOperation()
    {
        var target = Path.Combine(_root, "installed"); var metadata = Path.Combine(_root, "metadata");
        var transaction = new StaticSetupTransaction(Path.Combine(metadata, "installer-transactions", StaticSetupTransaction.TargetKey(target)), target);
        Stage(transaction, "old"); transaction.Begin(Hash); transaction.Snapshot(Files, [], CancellationToken.None);
        transaction.Apply(null, CancellationToken.None); transaction.Commit();
        WriteReceipt(target, Guid.NewGuid().ToString("N")); File.Delete(Path.Combine(target, "update-manifest.json"));
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ResolvePreviousManagedFiles(target, transaction));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "TOOL-LOCAL.exe")));
    }

    [Fact]
    public void ValidManifestCannotGrantOwnershipOfAnUnmanagedFile()
    {
        var (target, metadata, transaction, priorCommit) = CommittedInstallation();
        File.WriteAllText(Path.Combine(target, "user.dll"), "unmanaged collision");
        StaticSetupTransaction.AtomicJson(Path.Combine(target, "update-manifest.json"),
            new DesktopUpdateManifest("VideoMaker", "1.0.0", 7, "win-x64", [.. Files, "user.dll"]));

        var owned = LauncherInstallerService.ResolvePreviousManagedFiles(target, transaction);
        Assert.Equal(Files.Order(), owned.Order());
        Assert.DoesNotContain("user.dll", owned);
        Stage(transaction, "repaired");
        File.WriteAllText(Path.Combine(transaction.StageRoot, "user.dll"), "must not overwrite");
        transaction.Begin(Hash, preserveStagedPayload: true, previousCommitted: priorCommit);
        Assert.Throws<InvalidOperationException>(() =>
            transaction.Snapshot(Files.Append("user.dll"), owned, CancellationToken.None));
        Assert.Equal("unmanaged collision", File.ReadAllText(Path.Combine(target, "user.dll")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "TOOL-LOCAL.exe")));

        transaction.Recover();
        Assert.Equal(priorCommit.OperationId, transaction.Current!.OperationId);
        Assert.Equal("Committed", transaction.Current.State);
        SetupMaintenanceHandoff.ValidateOwnership(target, "1.0.0", 7, Hash, metadata);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedProbeRestoresCommittedOwnershipAndAllowsAnotherManifestRepair(bool corrupt)
    {
        var (target, metadata, transaction, priorCommit) = CommittedInstallation();
        var receiptPath = Path.Combine(target, "static-setup-receipt.json");
        var oldReceipt = File.ReadAllBytes(receiptPath);
        var manifestPath = Path.Combine(target, "update-manifest.json");
        if (corrupt) File.WriteAllText(manifestPath, "{broken manifest"); else File.Delete(manifestPath);
        File.WriteAllText(Path.Combine(target, "user.mp4"), "unmanaged media");
        var pendingPath = Path.Combine(_root, "separate-runtime", "static-model-install.pending.json");
        Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
        File.WriteAllText(pendingPath, "pending marker outside the app transaction");
        var pendingBytes = File.ReadAllBytes(pendingPath);

        var owned = LauncherInstallerService.ResolvePreviousManagedFiles(target, transaction);
        Stage(transaction, "failed probe payload");
        transaction.Begin(Hash, preserveStagedPayload: true, previousCommitted: priorCommit);
        transaction.Snapshot(Files, owned, CancellationToken.None);
        transaction.Apply(null, CancellationToken.None);
        Assert.Equal("Probing", transaction.Current!.State);
        Assert.Equal("failed probe payload", File.ReadAllText(Path.Combine(target, "TOOL-LOCAL.exe")));

        // A fresh transaction instance represents restart after the probe failed.
        var restarted = new StaticSetupTransaction(transaction.Root, target);
        restarted.Recover();
        Assert.Equal("Committed", restarted.Current!.State);
        Assert.Equal(priorCommit.OperationId, restarted.Current.OperationId);
        Assert.Equal(priorCommit.ReleaseSha256, restarted.Current.ReleaseSha256);
        Assert.Equal(priorCommit.Changes.ToArray(), restarted.Current.Changes.ToArray());
        Assert.Equal(oldReceipt, File.ReadAllBytes(receiptPath));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "TOOL-LOCAL.exe")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "app.dll")));
        Assert.Equal(corrupt, File.Exists(manifestPath));
        if (corrupt) Assert.Equal("{broken manifest", File.ReadAllText(manifestPath));
        Assert.Equal(pendingBytes, File.ReadAllBytes(pendingPath));
        Assert.Equal("unmanaged media", File.ReadAllText(Path.Combine(target, "user.mp4")));
        SetupMaintenanceHandoff.ValidateOwnership(target, "1.0.0", 7, Hash, metadata);

        var retryOwned = LauncherInstallerService.ResolvePreviousManagedFiles(target, restarted);
        Assert.Equal(Files.Order(), retryOwned.Order());
        Stage(restarted, "repaired after rollback");
        restarted.Begin(Hash, preserveStagedPayload: true, previousCommitted: restarted.Current);
        restarted.Snapshot(Files, retryOwned, CancellationToken.None);
        restarted.Apply(null, CancellationToken.None);
        WriteReceipt(target, restarted.Current!.OperationId);
        restarted.Commit();
        Assert.NotEqual(priorCommit.OperationId, restarted.Current!.OperationId);
        Assert.Equal("repaired after rollback", File.ReadAllText(Path.Combine(target, "TOOL-LOCAL.exe")));
        using var repairedManifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        Assert.Equal("VideoMaker", repairedManifest.RootElement.GetProperty("product").GetString());
        Assert.Equal("unmanaged media", File.ReadAllText(Path.Combine(target, "user.mp4")));
        Assert.Equal(pendingBytes, File.ReadAllBytes(pendingPath));
        SetupMaintenanceHandoff.ValidateOwnership(target, "1.0.0", 7, Hash, metadata);
    }

    [Theory]
    [InlineData("operation")]
    [InlineData("receipt")]
    [InlineData("missing")]
    public void InvalidPriorCommitEvidenceCannotRestoreOrCommitAppliedPayload(string mutation)
    {
        var (target, _, transaction, priorCommit) = CommittedInstallation();
        File.WriteAllText(Path.Combine(target, "user.mp4"), "unmanaged media");
        Stage(transaction, "unverified payload");
        transaction.Begin(Hash, preserveStagedPayload: true, previousCommitted: priorCommit);
        transaction.Snapshot(Files, Files, CancellationToken.None);
        transaction.Apply(null, CancellationToken.None);
        var operation = transaction.Current!.OperationId;
        var appliedReceipt = File.ReadAllBytes(Path.Combine(target, "static-setup-receipt.json"));
        if (mutation == "operation")
        {
            var path = Path.Combine(transaction.Root, "previous-committed.json");
            var evidence = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            evidence["operationId"] = Guid.NewGuid().ToString("N");
            File.WriteAllText(path, evidence.ToJsonString());
        }
        else if (mutation == "missing") File.Delete(Path.Combine(transaction.Root, "previous-committed.json"));
        else File.WriteAllText(Path.Combine(transaction.Root, "backup", "static-setup-receipt.json"), "corrupt backup");

        var restarted = new StaticSetupTransaction(transaction.Root, target);
        Assert.Throws<InvalidDataException>(restarted.Recover);
        Assert.Equal("Probing", restarted.Current!.State);
        Assert.Equal(operation, restarted.Current.OperationId);
        Assert.Equal("unverified payload", File.ReadAllText(Path.Combine(target, "TOOL-LOCAL.exe")));
        Assert.Equal(appliedReceipt, File.ReadAllBytes(Path.Combine(target, "static-setup-receipt.json")));
        Assert.Equal("unmanaged media", File.ReadAllText(Path.Combine(target, "user.mp4")));
        Assert.Equal(mutation != "missing", File.Exists(Path.Combine(transaction.Root, "previous-committed.json")));
    }

    [Fact]
    public void MissingPriorCommitEvidenceCannotAuthorizeStagingRecovery()
    {
        var (target, _, transaction, priorCommit) = CommittedInstallation();
        var receiptPath = Path.Combine(target, "static-setup-receipt.json");
        var oldReceipt = File.ReadAllBytes(receiptPath);
        transaction.Begin(Hash, preserveStagedPayload: true, previousCommitted: priorCommit);
        var operation = transaction.Current!.OperationId;
        File.Delete(Path.Combine(transaction.Root, "previous-committed.json"));

        var restarted = new StaticSetupTransaction(transaction.Root, target);
        Assert.Throws<InvalidDataException>(restarted.Recover);
        Assert.Equal("Staging", restarted.Current!.State);
        Assert.Equal(operation, restarted.Current.OperationId);
        Assert.NotEqual(priorCommit.OperationId, restarted.Current.OperationId);
        Assert.Equal(oldReceipt, File.ReadAllBytes(receiptPath));
        Assert.Equal("old", File.ReadAllText(Path.Combine(target, "TOOL-LOCAL.exe")));
    }

    private (string Target, string Metadata, StaticSetupTransaction Transaction, StaticSetupTransaction.Journal Commit) CommittedInstallation()
    {
        var target = Path.Combine(_root, "installed"); var metadata = Path.Combine(_root, "metadata");
        var transaction = new StaticSetupTransaction(Path.Combine(metadata, "installer-transactions", StaticSetupTransaction.TargetKey(target)), target);
        Stage(transaction, "old"); transaction.Begin(Hash); transaction.Snapshot(Files, [], CancellationToken.None);
        transaction.Apply(null, CancellationToken.None); transaction.Commit();
        WriteReceipt(target, transaction.Current!.OperationId);
        return (target, metadata, transaction, transaction.Current!);
    }

    private static void Stage(StaticSetupTransaction transaction, string content)
    {
        Directory.CreateDirectory(transaction.StageRoot);
        File.WriteAllText(Path.Combine(transaction.StageRoot, "TOOL-LOCAL.exe"), content);
        File.WriteAllText(Path.Combine(transaction.StageRoot, "app.dll"), content);
        StaticSetupTransaction.AtomicJson(Path.Combine(transaction.StageRoot, "update-manifest.json"),
            new DesktopUpdateManifest("VideoMaker", "1.0.0", 7, "win-x64", Files));
        File.WriteAllText(Path.Combine(transaction.StageRoot, "static-setup-receipt.json"), "pending");
    }

    private static void WriteReceipt(string target, string operation) => StaticSetupTransaction.AtomicJson(Path.Combine(target, "static-setup-receipt.json"),
        new { schemaVersion = 1, bundleId = ApprovedStaticModelBundle.Current.BundleId, targetRoot = target, version = "1.0.0",
            buildNumber = 7, packageSha256 = Hash, operationId = operation, completedAtUtc = DateTime.UtcNow });

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
