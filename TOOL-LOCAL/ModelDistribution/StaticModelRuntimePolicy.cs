using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.Configuration;
using TOOL_LOCAL.SystemSetup;
using TOOL_LOCAL.Vietsub.Translation;
using TOOL_LOCAL.Vietsub.Voice;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.ModelDistribution;

namespace TOOL_LOCAL.ModelDistribution;

// Release metadata is compiled into the desktop. A user settings override cannot remove this gate.
internal static class StaticModelRuntimePolicy
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static readonly string[] RequiredComponents = ["media", "ocr", "qwen", "piper", "webview2"];
    internal static bool RequiredByRelease => typeof(StaticModelRuntimePolicy).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Any(x => x.Key == "StaticModelSetupRequired"
            && string.Equals(x.Value, "true", StringComparison.OrdinalIgnoreCase));
    internal static bool IsStaticMode(DesktopOptions options) => RequiredByRelease ||
        string.Equals(options.ModelDistribution.Mode, "StaticArchive", StringComparison.Ordinal);
    internal static string ReceiptPath => Path.Combine(SystemSetupPaths.MetadataRoot, "static-model-install.json");
    internal static string PendingPath => Path.Combine(SystemSetupPaths.MetadataRoot, "static-model-install.pending.json");
    internal static string UsageLockPath => Path.Combine(SystemSetupPaths.MetadataRoot, ".static-model-usage.lock");
    internal static string DefaultCacheRoot => Path.Combine(SystemSetupPaths.ComponentsRoot, "static-model-downloads");
    internal static string TranslationRoot => Path.Combine(SystemSetupPaths.ComponentsRoot, "vietsub-translation");

    internal static void EnsureNotRevoked()
    {
        var channel = typeof(StaticModelRuntimePolicy).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(x => x.Key == "StaticModelReleaseChannel")?.Value ?? "Stable";
        using var legacyTrust = new DesktopModelDistributionClient(new Uri("https://disabled-model-distribution.invalid/"),
            EmbeddedModelTrustRoot.Load(channel, requirePublicKeys: false),
            Path.Combine(SystemSetupPaths.ComponentsRoot, "model-downloads"), downloadsEnabled: false);
        EnsureNotRevoked(legacyTrust);
    }

    internal static void EnsureNotRevoked(DesktopModelDistributionClient legacyTrust)
    {
        foreach (var artifact in ApprovedStaticModelBundle.Current.Artifacts)
            if (legacyTrust.IsKnownRevoked(artifact.Sha256))
                throw new ModelDistributionException(ModelDistributionErrors.ArtifactNotApproved,
                    "Bộ model đã được ghi nhận thu hồi trên máy này. Hãy dùng bản phát hành mới.");
    }

    internal static FileStream AcquireUsageLease(bool installing)
        => AcquireUsageLease(UsageLockPath, installing);

    internal static FileStream AcquireUsageLease(string lockPath, bool installing)
    {
        SystemSetupPaths.EnsureSafeDirectory(Path.GetDirectoryName(lockPath)!);
        AssertSafeFile(lockPath);
        if (!File.Exists(lockPath))
        {
            try { using var seed = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite); }
            catch (IOException) when (File.Exists(lockPath)) { }
        }
        return new FileStream(lockPath, FileMode.Open,
            installing ? FileAccess.ReadWrite : FileAccess.Read, installing ? FileShare.None : FileShare.Read);
    }

    internal static bool HasAllRequiredComponents(StaticModelBootstrapResult? result) => result is { SchemaVersion: 1, Succeeded: true }
        && result.BundleId == ApprovedStaticModelBundle.Current.BundleId
        && result.ErrorCode is null
        && result.Components is { Count: 5 }
        && Guid.TryParseExact(result.OperationId, "N", out var operation) && operation != Guid.Empty
        && RequiredComponents.All(id => result.Components.Count(x => x is not null && x.ComponentId == id
            && x.Ready && IsSha256(x.Fingerprint) && x.ErrorCode is null) == 1);

    internal static StaticModelInstallReceipt? ReadReadyReceipt()
    {
        try
        {
            return ReadReadyReceipt(new StaticModelStartupContext(SystemSetupPaths.MetadataRoot, AppContext.BaseDirectory,
                SystemSetupPaths.MachineFingerprint, ApplicationFingerprint(), Updates.DesktopBuildInfo.Version,
                Updates.DesktopBuildInfo.BuildNumber, RequireApplicationCommit: RequiredByRelease));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    // Purely local fixture roots make crash/commit tests independent of installed models,
    // native runtimes and the operator's real AppData. Production supplies the context above.
    internal static StaticModelInstallReceipt? ReadReadyReceipt(StaticModelStartupContext context)
    {
        try
        {
            var pending = Path.Combine(context.MetadataRoot, "static-model-install.pending.json");
            var receiptPath = Path.Combine(context.MetadataRoot, "static-model-install.json");
            AssertSafeMetadataFile(pending, context.MetadataRoot);
            if (File.Exists(pending)) return null;
            var value = ReadBoundedJson<StaticModelInstallReceipt>(receiptPath, 64 * 1024, context.MetadataRoot);
            if (value is null || value.SchemaVersion != 2 || !HasAllRequiredComponents(value.Result)
                || !IsSha256(value.MachineFingerprint) || !IsSha256(value.ApplicationFingerprint)
                || value.CompletedAtUtc == default || value.CompletedAtUtc > DateTime.UtcNow.AddMinutes(5)
                || value.MachineFingerprint != context.MachineFingerprint
                || value.ApplicationFingerprint != context.ApplicationFingerprint
                || !PathEquals(value.ApplicationRoot, context.ApplicationRoot)) return null;
            ValidateLocalPath(value.CacheRoot);
            if ((value.SetupManaged || context.RequireApplicationCommit) && !HasCommittedApplication(value, context)) return null;
            return value;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or InvalidDataException) { return null; }
    }

    private static bool HasCommittedApplication(StaticModelInstallReceipt receipt, StaticModelStartupContext context)
    {
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(context.ApplicationRoot));
        var application = ReadBoundedJson<StaticApplicationReceipt>(Path.Combine(target, "static-setup-receipt.json"), 64 * 1024, target);
        if (application is null || application.SchemaVersion != 1 || application.BundleId != receipt.Result.BundleId
            || application.OperationId != receipt.Result.OperationId || !PathEquals(application.TargetRoot, target)
            || application.Version != context.DesktopVersion || application.BuildNumber != context.DesktopBuildNumber
            || application.CompletedAtUtc == default || application.CompletedAtUtc > DateTime.UtcNow.AddMinutes(5)
            || !IsSha256(application.PackageSha256)) return false;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target.TrimEnd('\\').ToUpperInvariant()))).ToLowerInvariant();
        var journal = ReadBoundedJson<StaticApplicationJournal>(Path.Combine(context.MetadataRoot,
            "installer-transactions", key, "transaction.json"), 8 * 1024 * 1024, context.MetadataRoot);
        return journal is { SchemaVersion: 1, State: "Committed" } && journal.Changes.ValueKind == JsonValueKind.Array
            && journal.Changes.GetArrayLength() <= 200_000 && journal.OperationId == application.OperationId
            && PathEquals(journal.TargetRoot, target)
            && string.Equals(journal.ReleaseSha256, application.PackageSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static T? ReadBoundedJson<T>(string path, long maximumBytes, string ownerRoot)
    {
        AssertSafeMetadataFile(path, ownerRoot);
        if (!File.Exists(path)) return default;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 || stream.Length > maximumBytes) return default;
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
        RejectDuplicateProperties(document.RootElement);
        return document.RootElement.Deserialize<T>(Json);
    }

    private static void AssertSafeMetadataFile(string path, string ownerRoot)
    {
        // The CLI/native payload limit applies to the configured root. A derived
        // journal adds a fixed hash directory and filename and may exceed 220 chars.
        // It is managed metadata: retain local/reparse checks and root containment
        // without reapplying a native/runtime limit to its derived filename.
        ValidateLocalPath(ownerRoot);
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ownerRoot)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Setup metadata escaped its owner root.");
        AssertSafeFile(full, maximumLength: 30_000);
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Ambiguous setup evidence.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private static bool PathEquals(string? left, string right) => !string.IsNullOrWhiteSpace(left)
        && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    internal static bool IsSha256(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    internal static string ResolveCacheRoot(DesktopOptions options) =>
        ReadReadyReceipt()?.CacheRoot ?? options.ModelDistribution.CacheRoot ?? DefaultCacheRoot;

    internal static string ApplicationFingerprint()
    {
        var worker = VietsubTranslationWorkerProtocol.ComputeWorkerBinaryFingerprint(
            Path.Combine(AppContext.BaseDirectory, "_translation_worker"));
        var definition = PiperOfflineBundle.ApprovedDefinition();
        // Component probes bind their own native/config/model hashes. This additional binding
        // prevents a previous release's successful journal authorizing a changed worker.
        if (string.IsNullOrWhiteSpace(worker)) throw new InvalidDataException("Missing translation worker fingerprint.");
        var value = typeof(StaticModelRuntimePolicy).Assembly.ManifestModule.ModuleVersionId + "|" + worker
            + "|" + definition.WorkerSha256 + "|" + definition.RequirementsSha256
            + "|" + definition.ArchiveSha256 + "|" + ApprovedStaticModelBundle.Current.ArchiveSha256;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    internal static void MarkPending(string operationId)
    {
        SystemSetupPaths.EnsureSafeDirectory(SystemSetupPaths.MetadataRoot);
        WriteAtomic(PendingPath, new { schemaVersion = 1, operationId, bundleId = ApprovedStaticModelBundle.Current.BundleId });
    }

    internal static void Commit(StaticModelBootstrapResult result, string cacheRoot, bool keepPending = false)
    {
        if (!HasAllRequiredComponents(result)) throw new InvalidDataException("Incomplete component bootstrap.");
        WriteCommittedReceipt(ReceiptPath, PendingPath, new StaticModelInstallReceipt(2, cacheRoot, SystemSetupPaths.MachineFingerprint,
            ApplicationFingerprint(), DateTime.UtcNow, result, keepPending, Path.GetFullPath(AppContext.BaseDirectory)), keepPending);
    }

    internal static void WriteCommittedReceipt(string receiptPath, string pendingPath,
        StaticModelInstallReceipt receipt, bool keepPending)
    {
        WriteAtomic(receiptPath, receipt);
        if (!keepPending)
        {
            AssertSafeFile(pendingPath);
            File.Delete(pendingPath);
        }
    }

    internal static void WriteAtomic<T>(string path, T value)
    {
        ValidateLocalPath(path);
        SystemSetupPaths.EnsureSafeDirectory(Path.GetDirectoryName(path)!);
        AssertSafeFile(path);
        var part = path + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using (var file = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, value, Json);
                file.Flush(true);
            }
            AssertSafeFile(path);
            File.Move(part, path, true);
        }
        finally { if (File.Exists(part)) File.Delete(part); }
    }

    internal static void ValidateLocalPath(string path)
        => ValidateLocalPath(path, maximumLength: 220);

    private static void ValidateLocalPath(string path, int maximumLength)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal)
            || path.Length > maximumLength || path.IndexOf(':', 2) >= 0) throw new ArgumentException("An absolute local path is required.");
        var root = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(root) || string.Equals(Path.TrimEndingDirectorySeparator(path),
            Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A dedicated local directory is required.");
        for (var info = new DirectoryInfo(Path.GetFullPath(path)); info is not null; info = info.Parent)
            if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Model setup paths cannot be reparse points.");
    }

    internal static void AssertSafeFile(string path)
        => AssertSafeFile(path, maximumLength: 220);

    private static void AssertSafeFile(string path, int maximumLength)
    {
        ValidateLocalPath(path, maximumLength);
        if (Directory.Exists(path) || (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("Invalid model setup file.");
    }
}

internal sealed record StaticModelInstallReceipt(int SchemaVersion, string CacheRoot, string MachineFingerprint,
    string ApplicationFingerprint, DateTime CompletedAtUtc, StaticModelBootstrapResult Result,
    bool SetupManaged = false, string? ApplicationRoot = null);
internal sealed record StaticModelStartupContext(string MetadataRoot, string ApplicationRoot, string MachineFingerprint,
    string ApplicationFingerprint, string DesktopVersion, int DesktopBuildNumber, bool RequireApplicationCommit = false);
internal sealed record StaticApplicationReceipt(int SchemaVersion, string BundleId, string TargetRoot, string Version,
    int BuildNumber, string PackageSha256, string OperationId, DateTime CompletedAtUtc);
internal sealed record StaticApplicationJournal(int SchemaVersion, string OperationId, string TargetRoot,
    string ReleaseSha256, string State, JsonElement Changes);
