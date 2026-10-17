using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.ModelDistribution;

namespace TOOL_SHARED.Distribution.StaticModels;

/// <summary>Verifies release-approved ZIP bytes and imports only model payload plus attribution.
/// It never installs a runtime or creates a READY marker.</summary>
public sealed class StaticModelArchiveStore : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly StaticModelBundleDescriptor _descriptor;
    private readonly Action<string, long> _checkDisk;
    private readonly StaticModelArchiveDownload _download;

    public StaticModelArchiveStore()
    {
        _descriptor = ApprovedStaticModelBundle.Current;
        foreach (var artifact in _descriptor.Artifacts) ApprovedModelArtifactPins.Validate(artifact);
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            UseDefaultCredentials = false,
            Credentials = null,
            CheckCertificateRevocationList = true
        };
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _ownsHttp = true;
        _checkDisk = StaticModelFileSafety.EnsureDisk;
        _download = new(_http, _descriptor);
    }

    // Friend test assembly only. Production cannot supply an arbitrary descriptor, handler or disk bypass.
    internal StaticModelArchiveStore(HttpClient http, StaticModelBundleDescriptor descriptor,
        Action<string, long>? checkDisk = null, Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        TimeSpan? inactivityTimeout = null)
    {
        _http = http;
        _descriptor = descriptor;
        _checkDisk = checkDisk ?? StaticModelFileSafety.EnsureDisk;
        _download = new(http, descriptor, inactivityTimeout, delayAsync, _checkDisk);
    }

    /// <summary>Read-only cache estimate. Completed files are credited only after hashing.
    /// The extraction bound is deliberately conservative; runtime/app/backup space is the caller's responsibility.
    /// The actual operation locks the cache and rechecks free space before each large write.</summary>
    public async Task<long> EstimateAdditionalDiskBytesAsync(string cacheRoot, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        StaticModelFileSafety.AssertSafePath(cacheRoot);
        var payload = _descriptor.Describe(cacheRoot);
        if (await IsPayloadValidAsync(payload.PayloadDirectory, token)) return 0;
        var archivePath = Path.Combine(Path.GetFullPath(cacheRoot), _descriptor.ArchiveSha256, "archive.zip");
        return checked(await _download.EstimateRemainingDownloadBytesAsync(archivePath, token) + _descriptor.MaximumExpandedBytes);
    }

    public async Task<VerifiedStaticModelPayload> EnsurePayloadAsync(string cacheRoot,
        IProgress<StaticModelSetupProgress>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        StaticModelFileSafety.EnsureDirectory(cacheRoot);
        var bundleRoot = Path.Combine(Path.GetFullPath(cacheRoot), _descriptor.ArchiveSha256);
        StaticModelFileSafety.EnsureDirectory(bundleRoot);
        var lockPath = Path.Combine(bundleRoot, "setup.lock");
        StaticModelFileSafety.AssertSafePath(lockPath);
        using var lease = AcquireLock(lockPath);
        var payload = _descriptor.Describe(cacheRoot);
        progress?.Report(new(StaticModelSetupStages.Verifying, 0, 0, "Kiểm tra model đã có trong cache."));
        if (await IsPayloadValidAsync(payload.PayloadDirectory, token)) return payload;
        StaticModelFileSafety.DeleteOwnedTree(payload.PayloadDirectory);
        // Only abandoned directories in this bundle's private cache are eligible for cleanup.
        foreach (var oldStage in Directory.EnumerateDirectories(bundleRoot, "stage-*"))
            StaticModelFileSafety.DeleteOwnedTree(oldStage);

        var archive = await _download.EnsureAsync(Path.Combine(bundleRoot, "archive.zip"), progress, token);
        var staging = Path.Combine(bundleRoot, "stage-" + Guid.NewGuid().ToString("N"));
        StaticModelFileSafety.EnsureDirectory(staging);
        try
        {
            await ExtractAsync(archive, staging, progress, token);
            token.ThrowIfCancellationRequested();
            StaticModelFileSafety.AssertSafePath(payload.PayloadDirectory);
            Directory.Move(staging, payload.PayloadDirectory);
            return payload;
        }
        finally { StaticModelFileSafety.DeleteOwnedTree(staging); }
    }

    private static FileStream AcquireLock(string path)
    {
        try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException)
        { throw new ModelDistributionException(ModelDistributionErrors.Busy, "Một lượt cài model khác đang chạy. Chờ lượt đó kết thúc rồi thử lại."); }
    }

    private async Task ExtractAsync(string archivePath, string staging,
        IProgress<StaticModelSetupProgress>? progress, CancellationToken token)
    {
        StaticModelFileSafety.AssertSafePath(archivePath);
        await using var archiveFile = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            StaticModelFileSafety.BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        // Verify the very handle that is parsed; a path/mtime receipt is never enough to trust archive contents.
        if (archiveFile.Length != _descriptor.ArchiveSizeBytes ||
            !HashEquals(await SHA256.HashDataAsync(archiveFile, token), _descriptor.ArchiveSha256)) throw Invalid();
        archiveFile.Position = 0;
        using var zip = new ZipArchive(archiveFile, ZipArchiveMode.Read, leaveOpen: true);
        var entries = ValidateZipStructure(zip);
        if (!entries.TryGetValue("inventory.json", out var inventoryEntry)) throw Invalid();
        var inventoryBytes = await ReadBoundedAsync(inventoryEntry, 1024 * 1024, token);
        var inventory = ReadInventory(inventoryBytes);
        if (entries.Count != inventory.Files.Length + 2 || !entries.TryGetValue("checksums.sha256", out var checksumsEntry)) throw Invalid();
        var checksums = await ReadBoundedAsync(checksumsEntry, 32 * 1024, token);
        ValidateChecksums(checksums, inventory, _descriptor.InventorySha256);
        foreach (var file in inventory.Files)
            if (!entries.TryGetValue(file.Path, out var entry) || entry.FullName != file.Path || entry.Length != file.SizeBytes)
                throw Invalid();
        if (!entries.TryGetValue("compatibility.json", out var compatibilityEntry)) throw Invalid();
        ValidateCompatibility(await ReadBoundedAsync(compatibilityEntry, 64 * 1024, token));

        var keptBytes = inventory.Files.Where(f => IsKept(f.Path)).Aggregate(0L, (sum, file) => checked(sum + file.SizeBytes));
        _checkDisk(staging, checked(keptBytes + inventoryBytes.LongLength + checksums.LongLength));
        await File.WriteAllBytesAsync(Path.Combine(staging, "inventory.json"), inventoryBytes, token);
        await File.WriteAllBytesAsync(Path.Combine(staging, "checksums.sha256"), checksums, token);
        var processed = 0L;
        var total = inventory.Files.Aggregate(0L, (sum, file) => checked(sum + file.SizeBytes));
        foreach (var file in inventory.Files)
        {
            token.ThrowIfCancellationRequested();
            await using var input = entries[file.Path].Open();
            FileStream? output = null;
            try
            {
                if (IsKept(file.Path))
                {
                    var target = StaticModelFileSafety.ResolveUnder(staging, MapOutput(file.Path));
                    StaticModelFileSafety.EnsureDirectory(Path.GetDirectoryName(target)!);
                    output = new(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        StaticModelFileSafety.BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
                }
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[StaticModelFileSafety.BufferBytes];
                var written = 0L;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, token);
                    if (read == 0) break;
                    if (read > file.SizeBytes - written) throw Invalid();
                    hash.AppendData(buffer, 0, read);
                    if (output is not null) await output.WriteAsync(buffer.AsMemory(0, read), token);
                    written += read;
                    processed = checked(processed + read);
                    progress?.Report(new(StaticModelSetupStages.Extracting, processed, total,
                        "Kiểm tra inventory và giải nén Qwen/Piper."));
                }
                if (written != file.SizeBytes || !HashEquals(hash.GetHashAndReset(), file.Sha256)) throw Invalid();
                if (output is not null) await output.FlushAsync(token);
            }
            finally { if (output is not null) await output.DisposeAsync(); }
        }
    }

    private Dictionary<string, ZipArchiveEntry> ValidateZipStructure(ZipArchive zip)
    {
        if (zip.Entries.Count is < 5 || zip.Entries.Count > _descriptor.MaximumEntryCount) throw Invalid();
        var result = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        var expanded = 0L;
        foreach (var entry in zip.Entries)
        {
            StaticModelFileSafety.SafeRelativePath(entry.FullName);
            var unixType = (entry.ExternalAttributes >> 16) & 0xf000;
            if (unixType is not (0 or 0x8000) || (entry.ExternalAttributes & 0x410) != 0 ||
                !result.TryAdd(entry.FullName, entry) || entry.Length < 0 || entry.CompressedLength < 0 ||
                entry.Length > _descriptor.MaximumExpandedBytes ||
                (entry.Length > 0 && (entry.CompressedLength == 0 || entry.Length / entry.CompressedLength > 250))) throw Invalid();
            expanded = checked(expanded + entry.Length);
            if (expanded > _descriptor.MaximumExpandedBytes) throw Invalid();
        }
        return result;
    }

    private Inventory ReadInventory(byte[] bytes)
    {
        if (!HashEquals(SHA256.HashData(bytes), _descriptor.InventorySha256)) throw Invalid();
        Inventory inventory;
        try { inventory = JsonSerializer.Deserialize<Inventory>(bytes, Json) ?? throw Invalid(); }
        catch (JsonException) { throw Invalid(); }
        if (inventory.SchemaVersion != 1 || inventory.CandidateId != _descriptor.BundleId || inventory.Files is not { Length: > 0 } ||
            inventory.Files.Length > _descriptor.MaximumEntryCount - 2) throw Invalid();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sum = 0L;
        foreach (var file in inventory.Files)
        {
            StaticModelFileSafety.SafeRelativePath(file.Path);
            if (file.Path is "inventory.json" or "checksums.sha256" || !paths.Add(file.Path) ||
                !StaticModelFileSafety.IsHash(file.Sha256) || file.SizeBytes < 0) throw Invalid();
            sum = checked(sum + file.SizeBytes);
            if (sum > _descriptor.MaximumExpandedBytes) throw Invalid();
        }
        foreach (var pin in _descriptor.Artifacts)
        {
            var file = inventory.Files.SingleOrDefault(f => f.Path == pin.RelativePath);
            if (file is null || file.SizeBytes != pin.SizeBytes || file.Sha256 != pin.Sha256) throw Invalid();
        }
        // Prevent two inventory paths mapping onto the same import destination (case-insensitive on Windows).
        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "inventory.json", "checksums.sha256" };
        foreach (var file in inventory.Files.Where(f => IsKept(f.Path)))
            if (!mapped.Add(MapOutput(file.Path))) throw Invalid();
        return inventory;
    }

    private static void ValidateChecksums(byte[] bytes, Inventory inventory, string inventoryHash)
    {
        var expected = inventory.Files.ToDictionary(f => f.Path, f => f.Sha256, StringComparer.Ordinal);
        expected.Add("inventory.json", inventoryHash);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var lines = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 67 || line.Substring(64, 2) != "  ") throw Invalid();
            var path = line[66..];
            if (!seen.Add(path) || !expected.TryGetValue(path, out var hash) || line[..64] != hash) throw Invalid();
        }
        if (seen.Count != inventory.Files.Length + 1 || !seen.Contains("inventory.json")) throw Invalid();
    }

    private async Task<bool> IsPayloadValidAsync(string root, CancellationToken token)
    {
        StaticModelFileSafety.AssertSafePath(root);
        if (!Directory.Exists(root)) return false;
        try
        {
            var inventoryPath = StaticModelFileSafety.ResolveUnder(root, "inventory.json");
            var checksumsPath = StaticModelFileSafety.ResolveUnder(root, "checksums.sha256");
            if (!File.Exists(inventoryPath) || new FileInfo(inventoryPath).Length > 1024 * 1024 ||
                !File.Exists(checksumsPath) || new FileInfo(checksumsPath).Length > 32 * 1024) return false;
            var inventory = ReadInventory(await File.ReadAllBytesAsync(inventoryPath, token));
            ValidateChecksums(await File.ReadAllBytesAsync(checksumsPath, token), inventory, _descriptor.InventorySha256);
            var compatibilityPath = StaticModelFileSafety.ResolveUnder(root, "compatibility.json");
            if (!File.Exists(compatibilityPath) || new FileInfo(compatibilityPath).Length > 64 * 1024) return false;
            ValidateCompatibility(await File.ReadAllBytesAsync(compatibilityPath, token));
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "inventory.json", "checksums.sha256" };
            foreach (var file in inventory.Files.Where(f => IsKept(f.Path)))
            {
                var relative = MapOutput(file.Path);
                expected.Add(relative);
                if (!await StaticModelFileSafety.MatchesAsync(StaticModelFileSafety.ResolveUnder(root, relative), file.SizeBytes, file.Sha256, token))
                    return false;
            }
            var directories = new List<string> { root };
            var files = 0;
            for (var i = 0; i < directories.Count; i++)
            {
                foreach (var item in Directory.EnumerateFileSystemEntries(directories[i]))
                {
                    StaticModelFileSafety.AssertSafePath(item);
                    if (Directory.Exists(item))
                    {
                        if (directories.Count >= 128) return false;
                        directories.Add(item);
                    }
                    else
                    {
                        files++;
                        if (!expected.Contains(Path.GetRelativePath(root, item).Replace('\\', '/'))) return false;
                    }
                }
            }
            return files == expected.Count;
        }
        catch (InvalidDataException) { return false; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private bool IsKept(string path) => _descriptor.Artifacts.Any(a => a.RelativePath == path) ||
        !path.StartsWith("artifacts/", StringComparison.Ordinal);

    private void ValidateCompatibility(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
                root.GetProperty("candidateId").GetString() != _descriptor.BundleId) throw Invalid();
            var desktop = root.GetProperty("desktop");
            var qwenPin = _descriptor.GetArtifact(ApprovedModelArtifactPins.QwenArtifactId);
            var piperPin = _descriptor.GetArtifact(ApprovedModelArtifactPins.PiperArchiveArtifactId);
            if (desktop.GetProperty("platform").GetString() != "win-x64" ||
                desktop.GetProperty("version").GetString() != qwenPin.MinimumDesktopVersion ||
                desktop.GetProperty("buildNumber").GetInt32() != qwenPin.MinimumDesktopBuildNumber) throw Invalid();
            var components = root.GetProperty("components").EnumerateArray().ToArray();
            if (components.Length != 3 || components.Select(c => c.GetProperty("componentId").GetString())
                .Distinct(StringComparer.Ordinal).Count() != 3) throw Invalid();
            var qwen = components.Single(c => c.GetProperty("componentId").GetString() == qwenPin.ComponentId);
            var piper = components.Single(c => c.GetProperty("componentId").GetString() == piperPin.ComponentId);
            var ocr = components.Single(c => c.GetProperty("componentId").GetString() == "paddleocr-en-zh-win-x64");
            if (qwen.GetProperty("componentVersion").GetString() != qwenPin.ComponentVersion ||
                qwen.GetProperty("modelSha256").GetString() != qwenPin.Sha256 ||
                qwen.GetProperty("workerVersion").GetString() != _descriptor.RequiredWorkerVersion ||
                qwen.GetProperty("protocolVersion").GetInt32() != _descriptor.RequiredWorkerProtocolVersion ||
                piper.GetProperty("componentVersion").GetString() != piperPin.ComponentVersion ||
                piper.GetProperty("protocolVersion").GetInt32() != 1 ||
                !piper.GetProperty("createFreshVenvOnTarget").GetBoolean() ||
                ocr.GetProperty("componentVersion").GetString() != "3.3.1" ||
                ocr.GetProperty("runtimeDelivery").GetString() != "matching-desktop-package" ||
                ocr.GetProperty("independentRuntimeDownloadSupported").GetBoolean()) throw Invalid();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { throw Invalid(); }
    }

    private string MapOutput(string path)
    {
        var artifact = _descriptor.Artifacts.FirstOrDefault(a => a.RelativePath == path);
        if (artifact is null) return path;
        return artifact.ArtifactId == ApprovedModelArtifactPins.QwenArtifactId ? "qwen/" + artifact.FileName : "piper/" + artifact.FileName;
    }

    private static async Task<byte[]> ReadBoundedAsync(ZipArchiveEntry entry, int maxBytes, CancellationToken token)
    {
        if (entry.Length <= 0 || entry.Length > maxBytes) throw Invalid();
        var bytes = new byte[(int)entry.Length];
        await using var stream = entry.Open();
        await stream.ReadExactlyAsync(bytes, token);
        var extra = new byte[1];
        if (await stream.ReadAsync(extra, token) != 0) throw Invalid();
        return bytes;
    }

    private static bool HashEquals(byte[] hash, string expected) => Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
    private static InvalidDataException Invalid() => new("ZIP hoặc inventory model không khớp bộ đã được phê duyệt.");
    private sealed record Inventory(int SchemaVersion, string CandidateId, InventoryFile[] Files);
    private sealed record InventoryFile(string Path, long SizeBytes, string Sha256);

    public void Dispose() { if (_ownsHttp) _http.Dispose(); }
}
