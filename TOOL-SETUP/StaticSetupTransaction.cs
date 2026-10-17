using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TOOL_SETUP;

/// <summary>Persistent app-file transaction. It never owns user configuration, media or workspaces.</summary>
internal sealed class StaticSetupTransaction
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal sealed record FileChange(string RelativePath, bool HadOriginal, long OriginalSize, string? OriginalSha256, bool RemoveOnly);
    internal sealed record Journal(int SchemaVersion, string OperationId, string TargetRoot, string ReleaseSha256,
        string State, IReadOnlyList<FileChange> Changes, bool PreviousCommitRequired = false);
    private sealed record PreviousCommit(int SchemaVersion, string OperationId, Journal Committed, long ReceiptSize, string ReceiptSha256);
    private readonly string _root;
    private readonly string _target;
    private readonly string _journalPath;
    private readonly string _previousCommitPath;
    internal Journal? Current { get; private set; }
    public string StageRoot => Path.Combine(_root, "stage");
    public string PackagePath => Path.Combine(_root, "package.zip");
    public string ResultPath => Path.Combine(_root, "bootstrap-result.json");
    public string Root => _root;

    public StaticSetupTransaction(string root, string target)
    {
        _root = Path.GetFullPath(root);
        _target = Path.GetFullPath(target);
        _journalPath = Path.Combine(_root, "transaction.json");
        _previousCommitPath = Path.Combine(_root, "previous-committed.json");
        EnsureSafeDirectory(_root);
        if (File.Exists(_journalPath))
        {
            AssertSafeFile(_journalPath);
            if (new FileInfo(_journalPath).Length > 8 * 1024 * 1024) throw new InvalidDataException("Nhật ký Setup quá lớn.");
            using var document = SetupMaintenanceHandoff.ReadOwnedJson(_journalPath, 8 * 1024 * 1024);
            Current = document.RootElement.Deserialize<Journal>(Json);
            ValidateJournal(Current);
        }
    }

    private void ValidateJournal(Journal? journal)
    {
        if (journal is null || journal.SchemaVersion != 1 || !Guid.TryParseExact(journal.OperationId, "N", out _) ||
            !string.Equals(journal.TargetRoot, _target, StringComparison.OrdinalIgnoreCase) ||
            journal.ReleaseSha256 is null || journal.ReleaseSha256.Length != 64 || journal.ReleaseSha256.Any(c => !Uri.IsHexDigit(c)) ||
            journal.State is not ("Staging" or "Prepared" or "Applying" or "Probing" or "Committed" or "RolledBack") ||
            journal.Changes is null || journal.Changes.Count > 200_000 ||
            journal.Changes.Any(change => change is null || change.OriginalSize < 0 ||
                change.HadOriginal && (change.OriginalSha256?.Length != 64 || change.OriginalSha256.Any(c => !Uri.IsHexDigit(c)))))
            throw new InvalidDataException("Nhật ký Setup không khớp thư mục cài đặt. Giữ nguyên bản sao để kiểm tra.");
        if (journal.Changes.Select(change => NormalizeRelative(change.RelativePath)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != journal.Changes.Count)
            throw new InvalidDataException("Nhật ký Setup có đường dẫn trùng.");
    }

    public void Recover()
    {
        if (Current is null || Current.State is "Committed" or "RolledBack") return;
        var previous = ReadPreviousCommit();
        if (Current.State == "Staging")
        {
            if (previous is not null)
            {
                ValidatePreviousReceipt(Path.Combine(_target, "static-setup-receipt.json"), previous);
                Save(previous.Committed);
            }
            return;
        }
        // Validate every backup before the first restore. An incomplete/corrupt backup must not
        // produce an apparently successful recovery and must never be silently discarded.
        foreach (var change in Current.Changes.Where(x => x.HadOriginal))
        {
            var backup = SafePath(Path.Combine(_root, "backup"), change.RelativePath);
            AssertSafeFile(backup);
            if (!File.Exists(backup) || new FileInfo(backup).Length != change.OriginalSize || HashFile(backup) != change.OriginalSha256)
                throw new InvalidDataException("Không thể khôi phục tự động: bản sao Setup thiếu hoặc sai checksum. Nhật ký được giữ nguyên.");
        }
        if (previous is not null)
        {
            if (!Current.Changes.Any(change => change.HadOriginal && change.RelativePath.Equals("static-setup-receipt.json", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Thiếu snapshot receipt để khôi phục quyền sở hữu trước repair.");
            ValidatePreviousReceipt(SafePath(Path.Combine(_root, "backup"), "static-setup-receipt.json"), previous);
        }
        foreach (var change in Current.Changes.Reverse())
        {
            var destination = SafePath(_target, change.RelativePath);
            AssertSafeFile(destination);
            DeleteOwnedFile(CopyTemporaryPath(destination, Current.OperationId));
            if (change.HadOriginal) AtomicCopy(SafePath(Path.Combine(_root, "backup"), change.RelativePath), destination, Current.OperationId);
            else if (File.Exists(destination)) File.Delete(destination);
        }
        if (previous is not null)
        {
            ValidatePreviousReceipt(Path.Combine(_target, "static-setup-receipt.json"), previous);
            // Restores ownership only. The global pending gate remains until a new real probe succeeds.
            Save(previous.Committed);
        }
        else Save(Current with { State = "RolledBack" });
    }

    public void Begin(string releaseSha256, string? operationId = null, bool preserveStagedPayload = false, Journal? previousCommitted = null)
    {
        if (operationId is not null && !Guid.TryParseExact(operationId, "N", out _))
            throw new InvalidDataException("Mã lượt cài đặt không hợp lệ.");
        if (Current is { State: not ("Committed" or "RolledBack" or "Staging") })
            throw new InvalidOperationException("Phải khôi phục lượt cài cũ trước khi bắt đầu lại.");
        // Existing package remains hash-keyed by the immutable snapshot; it is not trusted until verified.
        if (!preserveStagedPayload && Current is not null && Current.ReleaseSha256 != releaseSha256)
        {
            DeleteOwnedDirectory(StageRoot);
            DeleteOwnedFile(PackagePath);
            DeleteOwnedFile(PackagePath + ".part");
            DeleteOwnedFile(PackagePath + ".receipt.json");
        }
        var operation = operationId ?? Guid.NewGuid().ToString("N");
        if (previousCommitted is not null)
        {
            ValidateJournal(previousCommitted);
            if (previousCommitted.State != "Committed" || Current is not { State: "Committed" }
                || Current.OperationId != previousCommitted.OperationId || Current.ReleaseSha256 != previousCommitted.ReleaseSha256
                || Current.PreviousCommitRequired != previousCommitted.PreviousCommitRequired
                || operation == previousCommitted.OperationId
                || !Current.Changes.SequenceEqual(previousCommitted.Changes))
                throw new InvalidDataException("Chỉ giữ quyền sở hữu từ chính commit hiện hành đã xác minh.");
            LauncherInstallerService.ValidateExistingInstallation(_target);
            var receiptPath = Path.Combine(_target, "static-setup-receipt.json");
            var previous = new PreviousCommit(1, operation, previousCommitted, new FileInfo(receiptPath).Length, HashFile(receiptPath));
            ValidatePreviousReceipt(receiptPath, previous);
            AtomicJson(_previousCommitPath, previous);
        }
        else DeleteOwnedFile(_previousCommitPath);
        Save(new Journal(1, operation, _target, releaseSha256, "Staging", [], PreviousCommitRequired: previousCommitted is not null));
    }

    private PreviousCommit? ReadPreviousCommit()
    {
        AssertSafeFile(_previousCommitPath);
        if (!File.Exists(_previousCommitPath))
        {
            if (Current!.PreviousCommitRequired || Current.Changes.Any(change => change.HadOriginal
                && change.RelativePath.Equals("static-setup-receipt.json", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Thiếu bằng chứng commit trước; chưa khôi phục file để giữ nguyên quyền sở hữu bản cài.");
            return null;
        }
        using var document = SetupMaintenanceHandoff.ReadOwnedJson(_previousCommitPath, 8 * 1024 * 1024 + 4096);
        var previous = document.RootElement.Deserialize<PreviousCommit>(Json);
        if (previous is null || previous.SchemaVersion != 1 || previous.OperationId != Current!.OperationId
            || previous.ReceiptSize is <= 0 or > 64 * 1024 || previous.ReceiptSha256 is not { Length: 64 }
            || previous.ReceiptSha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Bằng chứng commit trước không thuộc lượt khôi phục hiện hành.");
        ValidateJournal(previous.Committed);
        if (previous.Committed.State != "Committed" || previous.Committed.OperationId == previous.OperationId)
            throw new InvalidDataException("Bằng chứng commit trước không hợp lệ.");
        return previous;
    }

    private void ValidatePreviousReceipt(string path, PreviousCommit previous)
    {
        using var document = SetupMaintenanceHandoff.ReadOwnedJson(path, 64 * 1024);
        if (new FileInfo(path).Length != previous.ReceiptSize || !HashFile(path).Equals(previous.ReceiptSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Receipt commit trước không còn nguyên vẹn.");
        var value = document.RootElement;
        if (!value.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1
            || !value.TryGetProperty("operationId", out var operation) || operation.GetString() != previous.Committed.OperationId
            || !value.TryGetProperty("targetRoot", out var target) || !string.Equals(target.GetString(), _target, StringComparison.OrdinalIgnoreCase)
            || !value.TryGetProperty("packageSha256", out var hash) || !string.Equals(hash.GetString(), previous.Committed.ReleaseSha256, StringComparison.OrdinalIgnoreCase)
            || !value.TryGetProperty("bundleId", out var bundle) || bundle.GetString() != TOOL_SHARED.Distribution.StaticModels.ApprovedStaticModelBundle.Current.BundleId)
            throw new InvalidDataException("Receipt không chứng minh được quyền sở hữu của commit trước.");
    }

    // Only valid before a pending marker or application/model mutation. The caller holds
    // the usage barrier. Never restore old approval after Apply/Probing has started.
    internal void RestoreCommittedBeforeMutation(Journal previous)
    {
        if (previous.State != "Committed" || !string.Equals(previous.TargetRoot, _target, StringComparison.OrdinalIgnoreCase)
            || Current is null || Current.State is not ("Staging" or "Prepared" or "Committed"))
            throw new InvalidOperationException("Không thể phục hồi commit cũ sau mutation.");
        if (Current.State == "Committed")
        {
            if (Current.OperationId != previous.OperationId || Current.ReleaseSha256 != previous.ReleaseSha256)
                throw new InvalidOperationException("Không thay commit mới bằng approval cũ.");
            return;
        }
        Save(previous);
    }

    public void Snapshot(IEnumerable<string> newFiles, IEnumerable<string> oldFiles, CancellationToken token)
    {
        if (Current is null || Current.State != "Staging") throw new InvalidOperationException("Trạng thái Setup không hợp lệ.");
        var newSet = newFiles.Select(NormalizeRelative).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previousSet = oldFiles.Select(NormalizeRelative).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changes = new List<FileChange>();
        var backupRoot = Path.Combine(_root, "backup");
        DeleteOwnedDirectory(backupRoot);
        foreach (var relative in previousSet.Union(newSet, StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            var destination = SafePath(_target, relative);
            AssertSafeFile(destination);
            if (IsConfiguration(relative) && File.Exists(destination)) continue;
            var exists = File.Exists(destination);
            if (exists && newSet.Contains(relative) && !previousSet.Contains(relative))
                throw new InvalidOperationException("File mới của ứng dụng trùng với dữ liệu chưa thuộc bộ cài. Hãy chọn thư mục mới để giữ nguyên file đó.");
            if (exists)
            {
                var backup = SafePath(backupRoot, relative);
                AtomicCopy(destination, backup);
                changes.Add(new(relative, true, new FileInfo(backup).Length, HashFile(backup), !newSet.Contains(relative)));
            }
            else if (newSet.Contains(relative)) changes.Add(new(relative, false, 0, null, false));
        }
        Save(Current with { State = "Prepared", Changes = changes });
    }

    public void Apply(IProgress<TOOL_SHARED.Contracts.Updates.DesktopUpdateProgress>? progress, CancellationToken token)
    {
        if (Current is null || Current.State != "Prepared") throw new InvalidOperationException("Chưa có snapshot để cài đặt.");
        Save(Current with { State = "Applying" });
        for (var i = 0; i < Current.Changes.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var change = Current.Changes[i];
            var destination = SafePath(_target, change.RelativePath);
            if (change.RemoveOnly) { if (File.Exists(destination)) File.Delete(destination); }
            else AtomicCopy(SafePath(StageRoot, change.RelativePath), destination, Current.OperationId);
            progress?.Report(new("install", 72 + (int)((i + 1L) * 8 / Math.Max(1, Current.Changes.Count)), "Đang cài ứng dụng; bản sao khôi phục vẫn được giữ."));
        }
        Save(Current with { State = "Probing" });
    }

    public void Commit()
    {
        if (Current is null || Current.State != "Probing") throw new InvalidOperationException("Chưa kiểm tra đủ bộ cài.");
        Save(Current with { State = "Committed" });
        // Keeping this last successful backup also makes support/forward recovery reviewable.
    }

    private void Save(Journal journal)
    {
        AtomicJson(_journalPath, journal);
        Current = journal;
    }
    internal static void AtomicJson<T>(string path, T value)
    {
        AssertSafeFile(path);
        EnsureSafeDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(output, value, Json);
                output.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static string CopyTemporaryPath(string destination, string operation)
    {
        if (!Guid.TryParseExact(operation, "N", out _)) throw new InvalidDataException("Định danh lượt copy không hợp lệ.");
        return destination + ".setup-" + operation + ".part";
    }
    internal static void AtomicCopy(string source, string destination, string? operation = null)
    {
        AssertSafeFile(source);
        AssertSafeFile(destination);
        EnsureSafeDirectory(Path.GetDirectoryName(destination)!);
        var temporary = CopyTemporaryPath(destination, operation ?? Guid.NewGuid().ToString("N"));
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { input.CopyTo(output); output.Flush(true); }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static string HashFile(string path)
    { using var input = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(); }
    internal static string NormalizeRelative(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new InvalidDataException("Đường dẫn file Setup không an toàn.");
        var segments = relative.Replace('\\', '/').Split('/');
        if (segments.Any(s => s is "" or "." or ".." || s.EndsWith('.') || s.EndsWith(' ') || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || IsReservedName(s)))
            throw new InvalidDataException("Đường dẫn file Setup không an toàn.");
        return Path.Combine(segments);
    }
    private static bool IsReservedName(string segment)
    {
        var name = segment.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" ||
            name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) && name[3] is >= '1' and <= '9';
    }
    internal static string SafePath(string root, string relative)
    {
        var normalized = NormalizeRelative(relative);
        var baseRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var result = Path.GetFullPath(Path.Combine(baseRoot, normalized));
        if (!result.StartsWith(baseRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("File Setup nằm ngoài thư mục sở hữu.");
        return result;
    }
    internal static void EnsureSafeDirectory(string path)
    {
        AssertSafeAncestors(path);
        Directory.CreateDirectory(path);
        AssertSafeAncestors(path);
    }
    internal static void AssertSafeFile(string path)
    {
        AssertSafeAncestors(path);
        if (Directory.Exists(path)) throw new InvalidDataException("File Setup bị thay bằng thư mục.");
    }
    internal static void AssertSafeAncestors(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Setup không cài qua liên kết hoặc reparse point.");
            current = Path.GetDirectoryName(current);
        }
    }
    internal static bool IsConfiguration(string relative) => relative.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase) || relative.Equals("appsettings.user.json", StringComparison.OrdinalIgnoreCase);
    internal static void DeleteOwnedFile(string path) { AssertSafeFile(path); if (File.Exists(path)) File.Delete(path); }
    internal static void DeleteOwnedDirectory(string path)
    {
        AssertSafeAncestors(path);
        if (!Directory.Exists(path)) return;
        foreach (var child in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories)) AssertSafeAncestors(child);
        Directory.Delete(path, true);
    }
    internal static string TargetKey(string target) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(target).TrimEnd('\\').ToUpperInvariant()))).ToLowerInvariant();
}
