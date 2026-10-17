extern alias SetupAssembly;
using System.IO.Compression;
using System.Text.Json;
using SetupAssembly::TOOL_SETUP;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;
using Xunit;

namespace TOOL_TESTS.Updates;

public sealed class StaticSetupInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "static-setup-" + Guid.NewGuid().ToString("N"));
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public StaticSetupInstallerTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void BootstrapCannotCompleteWithUnexpectedComponentsBeyondItsRequiredSet()
    {
        var operation = Guid.NewGuid().ToString("N");
        var components = new[] { "media", "ocr", "qwen", "piper", "webview2", "unexpected" }
            .Select(id => new StaticModelComponentResult(id, true, Hash)).ToArray();
        var result = new StaticModelBootstrapResult(1, operation, "models-20261016-03", true, components);
        Assert.Throws<InvalidDataException>(() => StaticBootstrapRunner.ValidateResult(result, operation, "models-20261016-03", 0, false));
    }

    [Fact]
    public void JobHandshakePublishesOnlyCompleteOperationAndNeverReplacesAnotherOwner()
    {
        var path = Path.Combine(_root, "result.json.jobready"); var operation = Guid.NewGuid().ToString("N");
        StaticBootstrapRunner.PublishJobReady(path, operation);
        Assert.Equal(operation, File.ReadAllText(path));
        Assert.False(File.Exists(path + "." + operation + ".part"));
        Assert.Throws<IOException>(() => StaticBootstrapRunner.PublishJobReady(path, Guid.NewGuid().ToString("N")));
        Assert.Equal(operation, File.ReadAllText(path));
    }

    [Fact]
    public void RecoveryDeletesOnlyTheInterruptedCopyTemporaryFileBoundToItsOperation()
    {
        var target = Path.Combine(_root, "installed");
        var transaction = new StaticSetupTransaction(Path.Combine(_root, "transaction"), target);
        Write(transaction.StageRoot, "a.dll", "new-a"); transaction.Begin(Hash);
        transaction.Snapshot(["a.dll"], [], CancellationToken.None);
        // Simulate a process terminating in CopyTo before atomic promotion.
        var owned = StaticSetupTransaction.CopyTemporaryPath(Path.Combine(target, "a.dll"), transaction.Current!.OperationId);
        var unrelated = StaticSetupTransaction.CopyTemporaryPath(Path.Combine(target, "a.dll"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target); File.WriteAllText(owned, "partial-app-file"); File.WriteAllText(unrelated, "unowned-file");
        new StaticSetupTransaction(transaction.Root, target).Recover();
        Assert.False(File.Exists(owned)); Assert.False(File.Exists(Path.Combine(target, "a.dll")));
        Assert.Equal("unowned-file", File.ReadAllText(unrelated));
    }

    [Fact]
    public void RecoveryCanRestartAfterAnInterruptedBackupRestoreCopy()
    {
        var target = Path.Combine(_root, "installed");
        var transaction = new StaticSetupTransaction(Path.Combine(_root, "transaction"), target);
        Write(target, "a.dll", "old-a"); Write(transaction.StageRoot, "a.dll", "new-a");
        transaction.Begin(Hash); transaction.Snapshot(["a.dll"], ["a.dll"], CancellationToken.None); transaction.Apply(null, CancellationToken.None);
        File.WriteAllText(StaticSetupTransaction.CopyTemporaryPath(Path.Combine(target, "a.dll"), transaction.Current!.OperationId), "incomplete-restore");
        new StaticSetupTransaction(transaction.Root, target).Recover();
        Assert.Equal("old-a", File.ReadAllText(Path.Combine(target, "a.dll")));
    }

    [Fact]
    public void BootstrapRequiresActualFingerprintAndNoContradictoryReadyError()
    {
        var operation = Guid.NewGuid().ToString("N");
        var valid = new StaticModelBootstrapResult(1, operation, "bundle", true,
            new[] { "media", "ocr", "qwen", "piper", "webview2" }.Select(id => new StaticModelComponentResult(id, true, Hash)).ToArray());
        foreach (var fingerprint in new[] { null, "", new string('a', 63), new string('g', 64) })
            Assert.Throws<InvalidDataException>(() => StaticBootstrapRunner.ValidateResult(valid with
            { Components = valid.Components.Select(x => x.ComponentId == "qwen" ? x with { Fingerprint = fingerprint } : x).ToArray() }, operation, "bundle", 0, false));
        Assert.Throws<InvalidDataException>(() => StaticBootstrapRunner.ValidateResult(valid with
        { Components = valid.Components.Select(x => x.ComponentId == "qwen" ? x with { ErrorCode = "probe_failed" } : x).ToArray() }, operation, "bundle", 0, false));
        Assert.Throws<InvalidDataException>(() => StaticBootstrapRunner.ValidateResult(valid with { ErrorCode = "failed" }, operation, "bundle", 0, false));
        Assert.Throws<InvalidDataException>(() => StaticBootstrapRunner.ValidateResult(valid with { Components = [null!] }, operation, "bundle", 0, false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"unknownField\":true}")]
    [InlineData("[]")]
    [InlineData("{\"components\":[{\"componentId\":\"qwen\",\"ready\":true,\"ready\":false}]}")]
    public async Task BootstrapResultFileRejectsMalformedDuplicateAndUnexpectedSchema(string json)
    {
        var path = Path.Combine(_root, "result.json"); await File.WriteAllTextAsync(path, json);
        await Assert.ThrowsAsync<InvalidDataException>(() => StaticBootstrapRunner.ReadResultAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task BootstrapResultFileBoundIsAppliedToTheHandleActuallyRead()
    {
        var path = Path.Combine(_root, "result.json"); var operation = Guid.NewGuid().ToString("N");
        var value = new StaticModelBootstrapResult(1, operation, "bundle", true,
            new[] { "media", "ocr", "qwen", "piper", "webview2" }.Select(id => new StaticModelComponentResult(id, true, Hash)).ToArray());
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await File.WriteAllTextAsync(path, json.PadRight(64 * 1024));
        StaticBootstrapRunner.ValidateResult(await StaticBootstrapRunner.ReadResultAsync(path, CancellationToken.None), operation, "bundle", 0, false);
        await File.AppendAllTextAsync(path, " ");
        await Assert.ThrowsAsync<InvalidDataException>(() => StaticBootstrapRunner.ReadResultAsync(path, CancellationToken.None));
    }

    [Fact]
    public void ProgressDoesNotRegressAcrossPrerequisiteDownloadHashAndProbeStages()
    {
        var values = new List<DesktopUpdateProgress>();
        var progress = new MonotonicSetupProgress(new InlineProgress<DesktopUpdateProgress>(values.Add));
        foreach (var value in new[] { new DesktopUpdateProgress("prerequisite", 26, "prerequisite"), new("models", 30, "download"),
                     new("models", 65, "download complete"), new("models", 30, "hash"), new("probe", 85, "probe"),
                     new("commit", 100, "commit pending"), new("complete", 100, "committed") }) progress.Report(value);
        Assert.Equal(new[] { 26, 30, 65, 65, 85, 99, 100 }, values.Select(x => x.Percent));
        Assert.Equal("hash", values[3].Message);
    }

    [Fact]
    public void ReleaseBindingRejectsDifferentDesktopHashBeforeAnyDownload()
    {
        var options = Options();
        var release = Release();
        LauncherInstallerService.ValidateStaticRelease(release, options);
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateStaticRelease(release with { Sha256 = new string('b', 64) }, options));
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateStaticRelease(release with { Version = "2.0.0" }, options));
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateStaticRelease(release with { BuildNumber = 8 }, options));
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateStaticRelease(release with { DownloadUrl = "https://outside.invalid/app.zip" }, options));
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateStaticRelease(release with { DownloadUrl = "https://app.invalid:8443/app.zip" }, options));
    }

    [Theory]
    [InlineData("../other.bin")]
    [InlineData("/absolute.bin")]
    [InlineData("folder/../../other.bin")]
    [InlineData("folder/a.bin:stream")]
    [InlineData("folder/CON.txt")]
    [InlineData("folder/end. ")]
    public void TransactionRejectsTraversalAndWindowsAliases(string relative) =>
        Assert.Throws<InvalidDataException>(() => StaticSetupTransaction.SafePath(_root, relative));

    [Fact]
    public void InterruptedApplicationCopyRecoversAllFilesAndPreservesConfigurationAndMedia()
    {
        var target = Path.Combine(_root, "installed");
        var transaction = new StaticSetupTransaction(Path.Combine(_root, "transaction"), target);
        Write(target, "a.dll", "old-a"); Write(target, "obsolete.dll", "old-obsolete");
        Write(target, "appsettings.json", "user-settings"); Write(target, "media/user.mp4", "user-media");
        Write(transaction.StageRoot, "a.dll", "new-a"); Write(transaction.StageRoot, "new.dll", "new-file");
        Write(transaction.StageRoot, "appsettings.json", "package-settings");
        transaction.Begin(Hash);
        transaction.Snapshot(["a.dll", "new.dll", "appsettings.json"], ["a.dll", "obsolete.dll", "appsettings.json"], CancellationToken.None);
        using var cancel = new CancellationTokenSource();
        var progress = new InlineProgress<DesktopUpdateProgress>(_ => cancel.Cancel());
        Assert.Throws<OperationCanceledException>(() => transaction.Apply(progress, cancel.Token));
        Assert.Equal("Applying", transaction.Current!.State);
        var recovered = new StaticSetupTransaction(transaction.Root, target);
        recovered.Recover();
        Assert.Equal("old-a", File.ReadAllText(Path.Combine(target, "a.dll")));
        Assert.Equal("old-obsolete", File.ReadAllText(Path.Combine(target, "obsolete.dll")));
        Assert.False(File.Exists(Path.Combine(target, "new.dll")));
        Assert.Equal("user-settings", File.ReadAllText(Path.Combine(target, "appsettings.json")));
        Assert.Equal("user-media", File.ReadAllText(Path.Combine(target, "media/user.mp4")));
        Assert.Equal("RolledBack", recovered.Current!.State);
        recovered.Recover(); // recovery is idempotent
    }

    [Fact]
    public void NewManagedFileCannotOverwriteAnExistingUnmanagedUserFile()
    {
        var target = Path.Combine(_root, "installed");
        var transaction = new StaticSetupTransaction(Path.Combine(_root, "transaction"), target);
        Write(target, "new-asset.dat", "user-file");
        Write(transaction.StageRoot, "new-asset.dat", "package-file");
        transaction.Begin(Hash);
        Assert.Throws<InvalidOperationException>(() => transaction.Snapshot(["new-asset.dat"], [], CancellationToken.None));
        Assert.Equal("user-file", File.ReadAllText(Path.Combine(target, "new-asset.dat")));
        Assert.Equal("Staging", transaction.Current!.State);
    }

    [Fact]
    public void CorruptBackupPreventsAnyRecoveryMutation()
    {
        var target = Path.Combine(_root, "installed");
        var transaction = new StaticSetupTransaction(Path.Combine(_root, "transaction"), target);
        Write(target, "a.dll", "old-a"); Write(target, "b.dll", "old-b");
        Write(transaction.StageRoot, "a.dll", "new-a"); Write(transaction.StageRoot, "b.dll", "new-b");
        transaction.Begin(Hash); transaction.Snapshot(["a.dll", "b.dll"], ["a.dll", "b.dll"], CancellationToken.None);
        transaction.Apply(null, CancellationToken.None);
        Write(Path.Combine(transaction.Root, "backup"), "b.dll", "corrupt");
        Assert.Throws<InvalidDataException>(() => new StaticSetupTransaction(transaction.Root, target).Recover());
        Assert.Equal("new-a", File.ReadAllText(Path.Combine(target, "a.dll")));
        Assert.Equal("new-b", File.ReadAllText(Path.Combine(target, "b.dll")));
    }

    [Fact]
    public void CommittedTransactionNeverRollsBackVerifiedApplication()
    {
        var target = Path.Combine(_root, "installed");
        var transaction = new StaticSetupTransaction(Path.Combine(_root, "transaction"), target);
        Write(transaction.StageRoot, "a.dll", "new-a");
        transaction.Begin(Hash); transaction.Snapshot(["a.dll"], [], CancellationToken.None);
        transaction.Apply(null, CancellationToken.None); transaction.Commit();
        new StaticSetupTransaction(transaction.Root, target).Recover();
        Assert.Equal("new-a", File.ReadAllText(Path.Combine(target, "a.dll")));
    }

    [Fact]
    public void CompletionRequiresPendingMarkerForTheExactOperationAndBundle()
    {
        var path = Path.Combine(_root, "pending.json");
        var operation = Guid.NewGuid().ToString("N");
        StaticSetupTransaction.AtomicJson(path, new { schemaVersion = 1, operationId = operation, bundleId = "bundle" });
        LauncherInstallerService.ValidatePendingMarker(path, operation, "bundle");
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidatePendingMarker(path, "other", "bundle"));
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidatePendingMarker(path, operation, "other"));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void BootstrapSuccessRequiresMatchingOperationBundleExitCodeAndEveryComponent()
    {
        var operation = Guid.NewGuid().ToString("N");
        var valid = new StaticModelBootstrapResult(1, operation, "bundle", true,
            new[] { "media", "ocr", "qwen", "piper", "webview2" }.Select(id => new StaticModelComponentResult(id, true, Hash)).ToArray());
        StaticBootstrapRunner.ValidateResult(valid, operation, "bundle", 0, false);
        Assert.Throws<InvalidDataException>(() => StaticBootstrapRunner.ValidateResult(valid, "different-operation", "bundle", 0, false));
        Assert.Throws<InvalidDataException>(() => StaticBootstrapRunner.ValidateResult(valid, operation, "different-bundle", 0, false));
        Assert.Throws<InvalidDataException>(() => StaticBootstrapRunner.ValidateResult(valid, operation, "bundle", 1, false));
        Assert.Throws<InvalidDataException>(() => StaticBootstrapRunner.ValidateResult(valid with { Components = valid.Components.Where(c => c.ComponentId != "qwen").ToArray() }, operation, "bundle", 0, false));
        Assert.Throws<InvalidDataException>(() => StaticBootstrapRunner.ValidateResult(valid with { Components = valid.Components.Append(new("qwen", true)).ToArray() }, operation, "bundle", 0, false));
        Assert.Throws<InvalidDataException>(() => StaticBootstrapRunner.ValidateResult(valid with { Succeeded = false }, operation, "bundle", 0, false));
    }

    [Theory]
    [InlineData("../escape.dll", "normal.dll")]
    [InlineData("folder/same.dll", "FOLDER/SAME.dll")]
    [InlineData("/rooted.dll", "normal.dll")]
    [InlineData("folder/file.dll:stream", "normal.dll")]
    public void DesktopArchiveRejectsUnsafeEntriesBeforeExtraction(string first, string second)
    {
        var path = Path.Combine(_root, "bad.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        { archive.CreateEntry(first); archive.CreateEntry(second); }
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateDesktopArchive(path));
    }

    [Fact]
    public void DesktopArchiveRejectsSymbolicLink()
    {
        var path = Path.Combine(_root, "bad.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create)) archive.CreateEntry("link").ExternalAttributes = unchecked((int)0xA1FF0000);
        Assert.Throws<InvalidDataException>(() => LauncherInstallerService.ValidateDesktopArchive(path));
    }

    private static InstallerOptions Options() => new() { ServerBaseUrl = "https://app.invalid/", Channel = "Stable", ModelDeliveryMode = "StaticArchive", DesktopVersion = "1.0.0", DesktopBuildNumber = 7, DesktopSha256 = Hash };
    private static DesktopReleaseResponse Release() => new(Guid.NewGuid(), "VideoMaker", "1.0.0", 7, "Stable", "win-x64", null, null, DateTime.UtcNow, "app.zip", "/app.zip", 100, Hash,
        InstallMode: DesktopInstallModes.StaticArchive, DistributionProtocol: DesktopDistributionProtocol.Current, ModelBundleId: ApprovedStaticModelBundle.Current.BundleId);
    private static void Write(string root, string relative, string value)
    { var path = Path.Combine(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, value); }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}
