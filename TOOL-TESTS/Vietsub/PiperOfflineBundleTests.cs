using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Vietsub.Storage;
using TOOL_LOCAL.Vietsub.Voice;

namespace TOOL_TESTS.Vietsub;

public sealed class PiperOfflineBundleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vm-piper-bundle-" + Guid.NewGuid().ToString("N"));
    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private PiperOfflineBundle Bundle(string? extraPath = null, bool duplicate = false, bool link = false)
    {
        Directory.CreateDirectory(_root);
        var contents = new Dictionary<string, byte[]> {
            ["uv.exe"] = [1, 2], ["python/python.exe"] = [3],
            ["model/vi_VN-vais1000-medium.onnx"] = [4], ["model/vi_VN-vais1000-medium.onnx.json"] = [5],
            ["wheels/test.whl"] = [6] };
        if (extraPath is not null) contents.Add(extraPath, [7]);
        var manifest = new PiperBundleManifest(1, "fixture-v1",
            contents.Select(p => new PiperBundleFile(p.Key, p.Value.Length, Hash(p.Value))).ToArray(),
            [new(".venv/Scripts/python.exe", 1, Hash([8]))]);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        File.WriteAllBytes(Path.Combine(_root, "manifest.json"), manifestBytes);
        var archivePath = Path.Combine(_root, "piper-offline.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            foreach (var item in contents)
            {
                var entry = archive.CreateEntry(item.Key);
                if (link) entry.ExternalAttributes = unchecked((int)0xA1FF0000);
                using var stream = entry.Open(); stream.Write(item.Value);
            }
            if (duplicate) { using var stream = archive.CreateEntry("UV.EXE").Open(); stream.Write([1, 2]); }
        }
        var zipBytes = File.ReadAllBytes(archivePath);
        return new(_root, new(1, "fixture-v1", "win-x64", 1, zipBytes.Length, Hash(zipBytes),
            manifestBytes.Length, Hash(manifestBytes), contents.Sum(x => (long)x.Value.Length), 1024, Hash([9]), Hash([10])));
    }

    [Fact]
    public async Task ValidBundle_ExtractsOnlyManifestFiles_AndVerifiesInstalledCode()
    {
        var bundle = Bundle();
        Assert.True(bundle.Available(out _));
        var target = Path.Combine(_root, "target");
        await bundle.ExtractAsync(target, _ => { }, default);
        Directory.CreateDirectory(Path.Combine(target, ".venv", "Scripts"));
        File.WriteAllBytes(Path.Combine(target, ".venv", "Scripts", "python.exe"), [8]);
        Assert.True(bundle.VerifyInstalled(target));
        File.WriteAllText(Path.Combine(target, "python", "sitecustomize.py"), "unexpected code");
        Assert.False(bundle.VerifyInstalled(target, force: true));
    }

    [Theory]
    [InlineData("../escape.exe")]
    [InlineData("/absolute.exe")]
    [InlineData("C:/escape.exe")]
    [InlineData("python\\escape.py")]
    [InlineData("python/CON.txt")]
    [InlineData("python/a:stream")]
    [InlineData("python/end. ")]
    public async Task UnsafePaths_AreRejectedBeforeAnyExtraction(string path)
    {
        var bundle = Bundle(path);
        var target = Path.Combine(_root, "target");
        var error = await Assert.ThrowsAsync<VietsubVoiceException>(() => bundle.ExtractAsync(target, _ => { }, default));
        Assert.Equal(VietsubVoiceErrorCodes.BundleInvalid, error.Code);
        Assert.False(Directory.Exists(target));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task DuplicateOrLinkArchive_IsRejectedBeforeExtraction(bool duplicate, bool link)
    {
        var bundle = Bundle(duplicate: duplicate, link: link);
        var target = Path.Combine(_root, "target");
        await Assert.ThrowsAsync<VietsubVoiceException>(() => bundle.ExtractAsync(target, _ => { }, default));
        Assert.False(Directory.Exists(target));
    }

    [Theory]
    [InlineData("manifest.json")]
    [InlineData("piper-offline.zip")]
    public void ChangedPayloadOrSidecar_CannotReplaceEmbeddedTrust(string file)
    {
        var bundle = Bundle();
        Assert.True(bundle.Available(out _));
        var bytes = File.ReadAllBytes(Path.Combine(_root, file));
        bytes[0] ^= 1; // Same size: the checksum must detect the modification.
        File.WriteAllBytes(Path.Combine(_root, file), bytes);
        Assert.False(bundle.Available(out var code));
        Assert.Equal(VietsubVoiceErrorCodes.BundleInvalid, code);
    }

    [Fact]
    public async Task Cancellation_RemovesOwnedStaging_AndCanRetry()
    {
        var bundle = Bundle();
        var target = Path.Combine(_root, "target");
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => bundle.ExtractAsync(target, _ => cancellation.Cancel(), cancellation.Token));
        Assert.False(Directory.Exists(target));
        await bundle.ExtractAsync(target, _ => { }, default);
        Assert.True(File.Exists(Path.Combine(target, "uv.exe")));
    }

    [Fact]
    public async Task MissingBundle_InstallAndModelInstallNeverUseHttp_OrCreateReadyMarker()
    {
        var bundle = Bundle();
        File.Delete(Path.Combine(_root, "piper-offline.zip"));
        var http = new RejectHttp();
        using var store = new VietsubVoiceComponentStore(new VietsubAppPaths(Path.Combine(_root, "workspace")), true,
            http, offlineBundle: bundle);
        var error = await Assert.ThrowsAsync<VietsubVoiceException>(() => store.InstallAsync(null, default));
        Assert.Equal(VietsubVoiceErrorCodes.BundleMissing, error.Code);
        error = await Assert.ThrowsAsync<VietsubVoiceException>(() => store.InstallModelAsync(VietsubVoiceCatalog.PiperVoiceId, null, default));
        Assert.Equal(VietsubVoiceErrorCodes.BundleMissing, error.Code);
        Assert.Equal(0, http.Calls);
        Assert.False(store.GetStatus().Ready);
        Assert.Empty(Directory.EnumerateFiles(_root, ".ready.json", SearchOption.AllDirectories));
        var component = new PiperSetupAdapter(store, true).Inspect();
        Assert.False(component.CanPrepareOffline);
        Assert.False(component.CanInstall);
        Assert.Equal("REPAIR_REQUIRED", component.State);
    }

    [Fact]
    public async Task InstallLease_RejectsAnotherStore_BeforeExecutingBundle()
    {
        var bundle = Bundle();
        var paths = new VietsubAppPaths(Path.Combine(_root, "workspace"));
        using var store = new VietsubVoiceComponentStore(paths, true, offlineBundle: bundle);
        Directory.CreateDirectory(store.ComponentDirectory);
        using var lease = new FileStream(Path.Combine(store.ComponentDirectory, ".piper-install.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var error = await Assert.ThrowsAsync<VietsubVoiceException>(() => store.InstallAsync(null, default));
        Assert.Equal(VietsubVoiceErrorCodes.RuntimeBusy, error.Code);
        Assert.False(Directory.Exists(store.RuntimeRoot));
    }

    [Fact]
    public async Task CheckingMissingRuntime_IsReadOnly_AndDoesNotPrepareFromValidBundle()
    {
        var bundle = Bundle();
        var paths = new VietsubAppPaths(Path.Combine(_root, "workspace"));
        using var http = new RejectHttp();
        using var store = new VietsubVoiceComponentStore(paths, true, http, offlineBundle: bundle);
        var result = await new PiperSetupAdapter(store, true).RunAsync(false, false, (_, _, _, _) => { }, default);
        Assert.Equal("NOT_INSTALLED", result.State);
        Assert.True(result.CanPrepareOffline);
        Assert.False(Directory.Exists(store.ComponentDirectory));
        Assert.Equal(0, http.Calls);
    }

    [Fact]
    public async Task InsufficientDisk_IsRejectedBeforeExtractingOrExecutingAnyRuntime()
    {
        var fixture = Bundle();
        var definition = fixture.Definition with { MinimumFreeDiskBytes = long.MaxValue,
            WorkerSha256 = Hash(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "workers", "piper_worker.py"))),
            RequirementsSha256 = Hash(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "workers", "piper-requirements.lock"))) };
        var bundle = new PiperOfflineBundle(_root, definition);
        using var store = new VietsubVoiceComponentStore(new VietsubAppPaths(Path.Combine(_root, "workspace")), true, offlineBundle: bundle);
        var error = await Assert.ThrowsAsync<VietsubVoiceException>(() => store.InstallAsync(null, default));
        Assert.Equal(VietsubVoiceErrorCodes.RuntimeInstallFailed, error.Code);
        Assert.Contains("dung lượng", error.Message);
        Assert.False(Directory.Exists(store.RuntimeRoot));
    }

    [Fact]
    public async Task ReparseDestination_CannotWriteOrDeleteOutsideOwnedRuntime()
    {
        var bundle = Bundle();
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "keep.txt"), "keep");
        var link = Path.Combine(_root, "linked");
        // NTFS directory junctions do not require the symbolic-link privilege or
        // Developer Mode. Both paths are private fixture paths passed as arguments.
        var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("New-Item -ItemType Junction -Path $env:VM_JUNCTION_LINK -Target $env:VM_JUNCTION_TARGET | Out-Null");
        start.Environment["VM_JUNCTION_LINK"] = link;
        start.Environment["VM_JUNCTION_TARGET"] = outside;
        using (var process = System.Diagnostics.Process.Start(start)!)
        {
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }
        try
        {
            await Assert.ThrowsAsync<VietsubVoiceException>(() => bundle.ExtractAsync(Path.Combine(link, "target"), _ => { }, default));
            Assert.Throws<VietsubVoiceException>(() => PiperOfflineBundle.DeleteOwnedDirectory(link, _root));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(outside, "keep.txt")));
        }
        finally { Directory.Delete(link); }
    }

    private sealed class RejectHttp : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; throw new InvalidOperationException("Offline preparation attempted HTTP."); }
    }

    [Fact]
    public async Task ClosingWorkerOwnership_StopsTheOwnedProcess()
    {
        var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") {
            UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("Start-Sleep -Seconds 60");
        using var process = System.Diagnostics.Process.Start(start)!;
        try
        {
            using (var ownership = new PiperProcessTree(process)) Assert.False(process.HasExited);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.HasExited);
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }

    public void Dispose() => PiperOfflineBundle.DeleteOwnedDirectory(_root, Path.GetTempPath());
}
