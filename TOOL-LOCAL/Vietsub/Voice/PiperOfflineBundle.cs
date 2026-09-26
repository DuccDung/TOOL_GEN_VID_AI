using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TOOL_LOCAL.SystemSetup;

namespace TOOL_LOCAL.Vietsub.Voice;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record PiperOfflineDefinition(int SchemaVersion, string BundleVersion, string Platform,
    int ProtocolVersion, long ArchiveSize, string ArchiveSha256, long ManifestSize, string ManifestSha256,
    long ExpandedBytes, long MinimumFreeDiskBytes, string WorkerSha256, string RequirementsSha256);
internal sealed record PiperBundleFile(string Path, long Size, string Sha256);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record PiperBundleManifest(int SchemaVersion, string BundleVersion,
    PiperBundleFile[] Files, PiperBundleFile[] InstalledFiles);

// The identity comes from the application resource, never from a file beside an untrusted ZIP.
internal sealed class PiperOfflineBundle(string root, PiperOfflineDefinition definition)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, (long Length, DateTime Written, bool Valid)> _hashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private PiperBundleManifest? _manifest;
    public PiperOfflineDefinition Definition { get; } = definition;
    public string Root { get; } = Path.GetFullPath(root);

    internal static PiperOfflineDefinition ApprovedDefinition()
    {
        using var source = typeof(PiperOfflineBundle).Assembly.GetManifestResourceStream("PiperOffline.ApprovedDefinition")
            ?? throw Invalid("Thiếu định nghĩa gói giọng Việt trong ứng dụng.");
        return JsonSerializer.Deserialize<PiperOfflineDefinition>(source, Json)
            ?? throw Invalid("Định nghĩa gói giọng Việt không hợp lệ.");
    }

    internal static PiperOfflineBundle ForApplication(string? appRoot = null)
    {
        var approved = ApprovedDefinition();
        return new(Path.Combine(appRoot ?? AppContext.BaseDirectory, "components", "piper", approved.BundleVersion), approved);
    }

    internal bool Available(out string? errorCode)
    {
        try { VerifyArchive(); errorCode = null; return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or VietsubVoiceException)
        { errorCode = e is VietsubVoiceException voice ? voice.Code : VietsubVoiceErrorCodes.BundleInvalid; return false; }
    }

    internal PiperBundleManifest ReadManifest()
    {
        lock (_sync)
        {
            ValidateDefinition();
            var path = Path.Combine(Root, "manifest.json");
            if (!File.Exists(path)) throw Missing();
            if (!CheckFile(path, Definition.ManifestSize, Definition.ManifestSha256)) throw Invalid();
            if (_manifest is not null) return _manifest;
            // Hash the same bytes that are parsed, even if the sidecar was replaced
            // between the metadata check and opening it.
            using var manifestStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (manifestStream.Length != Definition.ManifestSize) throw Invalid();
            var bytes = new byte[checked((int)Definition.ManifestSize)];
            manifestStream.ReadExactly(bytes);
            if (bytes.LongLength != Definition.ManifestSize || !Matches(SHA256.HashData(bytes), Definition.ManifestSha256)) throw Invalid();
            var manifest = JsonSerializer.Deserialize<PiperBundleManifest>(bytes, Json)
                ?? throw Invalid();
            if (manifest.SchemaVersion != 1 || manifest.BundleVersion != Definition.BundleVersion
                || manifest.Files is not { Length: > 0 and <= 12000 }
                || manifest.InstalledFiles is not { Length: > 0 and <= 12000 }) throw Invalid();
            ValidateEntries(manifest.Files);
            ValidateEntries(manifest.InstalledFiles);
            if (manifest.Files.Sum(x => x.Size) != Definition.ExpandedBytes
                || new[] { "uv.exe", "python/python.exe", "model/vi_VN-vais1000-medium.onnx",
                    "model/vi_VN-vais1000-medium.onnx.json" }.Any(p => !manifest.Files.Any(x => x.Path == p))
                || !manifest.Files.Any(x => x.Path.StartsWith("wheels/", StringComparison.Ordinal) && x.Path.EndsWith(".whl", StringComparison.Ordinal))
                || !manifest.InstalledFiles.Any(x => x.Path == ".venv/Scripts/python.exe")
                || manifest.InstalledFiles.Any(x => !x.Path.StartsWith(".venv/", StringComparison.Ordinal))) throw Invalid();
            _manifest = manifest;
            return manifest;
        }
    }

    internal void ClearVerificationCache() { lock (_sync) { _hashes.Clear(); _manifest = null; } }

    private void ValidateDefinition()
    {
        if (Definition.SchemaVersion != 1 || Definition.Platform != "win-x64" || Definition.ProtocolVersion != 1
            || string.IsNullOrEmpty(Definition.BundleVersion) || !Regex.IsMatch(Definition.BundleVersion, "^[a-z0-9][a-z0-9.-]{1,100}$")
            || Definition.ArchiveSize is <= 0 or > 2_000_000_000 || Definition.ManifestSize is <= 0 or > 4_000_000
            || Definition.ExpandedBytes is <= 0 or > 4_000_000_000
            || Definition.MinimumFreeDiskBytes < Definition.ExpandedBytes
            || new[] { Definition.ArchiveSha256, Definition.ManifestSha256, Definition.WorkerSha256, Definition.RequirementsSha256 }
                .Any(x => !ValidHash(x))) throw Invalid();
    }

    internal static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 240 || path.Contains('\\') || path.Contains(':')
            || path.Any(c => c < 32 || c == 127 || "<>\"|?*".Contains(c))) throw Invalid();
        foreach (var part in path.Split('/'))
            if (part is "" or "." or ".." || part.EndsWith('.') || part.EndsWith(' ')
                || Regex.IsMatch(part.Split('.')[0], "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase)) throw Invalid();
    }

    private static void ValidateEntries(PiperBundleFile[] entries)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry is null || entry.Size is < 0 or > 1_000_000_000 || !ValidHash(entry.Sha256)) throw Invalid();
            ValidateRelativePath(entry.Path);
            if (!paths.Add(entry.Path)) throw Invalid();
        }
    }

    private static bool ValidHash(string? value) => value is not null && Regex.IsMatch(value, "^[a-f0-9]{64}$");

    internal void VerifyArchive(bool force = false)
    {
        lock (_sync)
        {
            if (force) ClearVerificationCache();
            _ = ReadManifest();
            var path = Path.Combine(Root, "piper-offline.zip");
            if (!File.Exists(path)) throw Missing();
            if (!CheckFile(path, Definition.ArchiveSize, Definition.ArchiveSha256)) throw Invalid();
        }
    }

    internal void VerifyWorker(string worker, string requirements)
    {
        if (!CheckHash(worker, Definition.WorkerSha256) || !CheckHash(requirements, Definition.RequirementsSha256))
            throw Invalid("Worker hoặc lockfile không khớp gói giọng Việt đi kèm.");
    }

    // Extract a verified archive into a NEW owned staging directory. Keep the same open
    // handle during checksum validation and extraction to prevent replacing the source.
    internal async Task ExtractAsync(string destination, Action<double> progress, CancellationToken token)
    {
        var manifest = ReadManifest();
        AssertSafePath(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw Invalid("Thư mục chuẩn bị gói đã tồn tại.");
        var archivePath = Path.Combine(Root, "piper-offline.zip");
        AssertSafePath(archivePath);
        await using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != Definition.ArchiveSize
            || !Matches(await SHA256.HashDataAsync(stream, token), Definition.ArchiveSha256)) throw Invalid();
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count != manifest.Files.Length) throw Invalid();
        var expected = manifest.Files.ToDictionary(x => x.Path, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            ValidateRelativePath(entry.FullName);
            var kind = (entry.ExternalAttributes >> 16) & 0xF000;
            if (!seen.Add(entry.FullName) || !expected.TryGetValue(entry.FullName, out var file)
                || file.Path != entry.FullName || file.Size != entry.Length
                || (kind != 0 && kind != 0x8000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw Invalid();
        }
        SystemSetupPaths.EnsureSafeDirectory(destination);
        long completed = 0;
        double reported = -1;
        try
        {
            var buffer = new byte[128 * 1024];
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                var file = expected[entry.FullName];
                var target = ChildPath(destination, entry.FullName);
                SystemSetupPaths.EnsureSafeDirectory(Path.GetDirectoryName(target)!);
                AssertSafePath(target);
                using var input = entry.Open();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    buffer.Length, FileOptions.Asynchronous);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long written = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, token)) != 0)
                {
                    written += read;
                    if (written > file.Size) throw Invalid();
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                }
                if (written != file.Size || !Matches(hash.GetHashAndReset(), file.Sha256)) throw Invalid();
                completed += written;
                var percent = completed * 100d / Definition.ExpandedBytes;
                if (percent - reported >= 1 || completed == Definition.ExpandedBytes)
                {
                    progress(percent);
                    reported = percent;
                }
            }
        }
        catch { DeleteOwnedDirectory(destination, Path.GetDirectoryName(destination)!); throw; }
    }

    internal bool VerifyInstalled(string runtime, bool force = false)
    {
        lock (_sync)
        {
            if (force) ClearVerificationCache();
            var manifest = ReadManifest();
            AssertSafePath(runtime);
            // Walk the tree once to reject reparse points, instead of querying the
            // same parent directories again for every Python/module file.
            var actualFiles = EnumerateSafeFiles(runtime).ToArray();
            foreach (var file in manifest.Files.Concat(manifest.InstalledFiles))
                if (!CheckFile(ChildPath(runtime, file.Path), file.Size, file.Sha256, checkParents: false)) return false;
            // Reject additional importable code/native libraries. Generated metadata,
            // logs, request WAVs and unused command-line entry points are not imported.
            var known = manifest.Files.Concat(manifest.InstalledFiles).Select(x => x.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in actualFiles)
            {
                var relative = Path.GetRelativePath(runtime, file).Replace('\\', '/');
                if (relative.StartsWith("cache/", StringComparison.OrdinalIgnoreCase)
                    || relative.StartsWith("requests/", StringComparison.OrdinalIgnoreCase)) continue;
                if (new[] { ".py", ".pyc", ".pyo", ".pyd", ".dll", ".pth", ".zip", ".egg" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                    && !known.Contains(relative)) return false;
            }
            return true;
        }
    }

    private bool CheckFile(string path, long size, string sha256, bool checkParents = true)
    {
        lock (_sync)
        {
            if (checkParents) AssertSafePath(path);
            var file = new FileInfo(path);
            if (!file.Exists || file.Length != size || (file.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            if (_hashes.TryGetValue(path, out var prior) && prior.Length == file.Length && prior.Written == file.LastWriteTimeUtc)
                return prior.Valid;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var valid = Matches(SHA256.HashData(stream), sha256);
            _hashes[path] = (file.Length, file.LastWriteTimeUtc, valid);
            return valid;
        }
    }

    private static bool CheckHash(string path, string hash)
    {
        AssertSafePath(path);
        if (!File.Exists(path)) return false;
        using var stream = File.OpenRead(path);
        return Matches(SHA256.HashData(stream), hash);
    }
    private static bool Matches(byte[] hash, string hex) => CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(hex));

    internal static string ChildPath(string root, string relative)
    {
        ValidateRelativePath(relative);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw Invalid();
        return path;
    }

    internal static void AssertSafePath(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw Invalid("Đường dẫn thành phần giọng Việt không hợp lệ.");
    }

    internal static IEnumerable<string> EnumerateSafeFiles(string root)
    {
        AssertSafePath(root);
        return Walk(root);

        static IEnumerable<string> Walk(string directory)
        {
            foreach (var item in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(item);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw Invalid();
                if ((attributes & FileAttributes.Directory) != 0)
                    foreach (var child in Walk(item)) yield return child;
                else yield return item;
            }
        }
    }

    internal static void DeleteOwnedDirectory(string path, string parent)
    {
        var full = Path.GetFullPath(path);
        var allowed = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || full == allowed.TrimEnd(Path.DirectorySeparatorChar)) throw Invalid();
        AssertSafePath(full);
        if (!Directory.Exists(full)) return;
        _ = EnumerateSafeFiles(full).Count(); // Reject every reparse point before a recursive deletion.
        Directory.Delete(full, recursive: true);
    }

    internal static VietsubVoiceException Missing() => new(VietsubVoiceErrorCodes.BundleMissing,
        "Bộ ứng dụng thiếu gói giọng Việt offline. Hãy dùng bản ZIP đầy đủ đúng phiên bản.");
    internal static VietsubVoiceException Invalid(string message = "Gói giọng Việt offline không khớp bản ứng dụng hoặc đã bị thay đổi.") =>
        new(VietsubVoiceErrorCodes.BundleInvalid, message);
}
