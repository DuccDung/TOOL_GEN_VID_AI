using System.Collections.Concurrent;
using System.Text.Json;
using TOOL_LOCAL.Vietsub.Domain;

namespace TOOL_LOCAL.Vietsub.Storage;

internal sealed class VietsubProjectStore
{
    private const string ManifestFileName = "project.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private static readonly TimeSpan[] PublishRetryDelays =
    [
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(400)
    ];
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _projectLocks = new();
    private readonly VietsubAppPaths _paths;
    private readonly VietsubSubtitleStore _subtitleStore;

    public VietsubProjectStore(VietsubAppPaths paths, VietsubSubtitleStore subtitleStore)
    {
        _paths = paths;
        _subtitleStore = subtitleStore;
    }

    public async Task<VietsubProjectManifest> CreateAsync(
        Guid organizationId,
        string ownerUserId,
        string name,
        Guid? projectId = null,
        CancellationToken cancellationToken = default)
    {
        ValidateOwner(organizationId, ownerUserId);
        var normalizedName = NormalizeName(name);
        var nowUtc = DateTime.UtcNow;
        var manifest = new VietsubProjectManifest
        {
            ProjectId = projectId ?? Guid.NewGuid(),
            OrganizationId = organizationId,
            OwnerUserId = ownerUserId.Trim(),
            Name = normalizedName,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            LastOpenedAtUtc = nowUtc,
            LastCleanShutdown = true
        };

        var projectDirectory = _paths.GetProjectDirectory(manifest.ProjectId);
        if (Directory.Exists(projectDirectory))
        {
            throw new InvalidOperationException("Mã dự án Vietsub đã tồn tại trên máy.");
        }

        _paths.CreateProjectDirectories(manifest.ProjectId);
        await _subtitleStore.InitializeAsync(manifest.ProjectId, cancellationToken);
        await SaveAsync(manifest, cancellationToken);
        return manifest;
    }

    public async Task<VietsubProjectManifest> OpenAsync(
        Guid projectId,
        Guid organizationId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        ValidateOwner(organizationId, ownerUserId);
        var manifest = await LoadAsync(projectId, cancellationToken)
            ?? throw new FileNotFoundException("Không tìm thấy hoặc không thể phục hồi dự án Vietsub.");
        EnsureAccess(manifest, organizationId, ownerUserId);
        manifest.RecoveryRequired = manifest.RecoveryRequired || !manifest.LastCleanShutdown;
        return manifest;
    }

    public async Task<VietsubProjectManifest> RenameAsync(
        Guid projectId,
        Guid organizationId,
        string ownerUserId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var projectLock = GetProjectLock(projectId);
        await projectLock.WaitAsync(cancellationToken);
        try
        {
            var manifest = await LoadBestManifestCoreAsync(projectId, cancellationToken)
                ?? throw new FileNotFoundException("Không tìm thấy dự án Vietsub.");
            EnsureAccess(manifest, organizationId, ownerUserId);
            manifest.Name = NormalizeName(name);
            await SaveCoreAsync(manifest, cancellationToken);
            return manifest;
        }
        finally
        {
            projectLock.Release();
        }
    }

    public async Task<IReadOnlyList<VietsubProjectSummary>> ListAsync(
        Guid organizationId,
        string ownerUserId,
        CancellationToken cancellationToken = default)
    {
        ValidateOwner(organizationId, ownerUserId);
        var results = new List<VietsubProjectSummary>();
        foreach (var directory in Directory.EnumerateDirectories(_paths.ProjectsDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out var projectId))
            {
                continue;
            }

            var manifest = await LoadAsync(projectId, cancellationToken);
            if (manifest is null
                || manifest.OrganizationId != organizationId
                || !string.Equals(manifest.OwnerUserId, ownerUserId, StringComparison.Ordinal))
            {
                continue;
            }

            results.Add(ToSummary(manifest));
        }

        return results
            .OrderByDescending(project => project.UpdatedAtUtc)
            .ThenBy(project => project.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task SaveAsync(
        VietsubProjectManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ValidateManifest(manifest);
        var projectLock = GetProjectLock(manifest.ProjectId);
        await projectLock.WaitAsync(cancellationToken);
        try
        {
            await SaveCoreAsync(manifest, cancellationToken);
        }
        finally
        {
            projectLock.Release();
        }
    }

    public FileStream AcquireExclusiveLock(Guid projectId)
    {
        var lockPath = _paths.GetProjectPath(projectId, "workspace.lock");
        try
        {
            return new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                "Dự án Vietsub đang được mở ở một phiên khác.",
                exception);
        }
    }

    internal static VietsubProjectSummary ToSummary(VietsubProjectManifest manifest) =>
        new(
            manifest.ProjectId,
            manifest.Name,
            manifest.Status,
            manifest.SourceLanguageCode,
            manifest.TargetLanguageCode,
            manifest.UpdatedAtUtc,
            manifest.RecoveryRequired,
            manifest.ServerSynchronized,
            manifest.ServerSyncErrorCode);

    private async Task<VietsubProjectManifest?> LoadAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var projectLock = GetProjectLock(projectId);
        await projectLock.WaitAsync(cancellationToken);
        try
        {
            return await LoadBestManifestCoreAsync(projectId, cancellationToken);
        }
        finally
        {
            projectLock.Release();
        }
    }

    private async Task<VietsubProjectManifest?> LoadBestManifestCoreAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var manifestPath = _paths.GetProjectPath(projectId, ManifestFileName);
        var candidates = new[] { manifestPath, manifestPath + ".tmp", manifestPath + ".bak" }
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        foreach (var candidate in candidates)
        {
            try
            {
                VietsubProjectManifest? manifest;
                await using (var stream = new FileStream(
                    candidate,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    64 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    manifest = await JsonSerializer.DeserializeAsync<VietsubProjectManifest>(
                        stream,
                        JsonOptions,
                        cancellationToken);
                }
                if (manifest is null || manifest.ProjectId != projectId)
                {
                    continue;
                }

                ValidateManifest(manifest);
                var recoveredFromAlternateManifest = !string.Equals(
                    candidate,
                    manifestPath,
                    StringComparison.OrdinalIgnoreCase);
                manifest.RecoveryRequired = !manifest.LastCleanShutdown || recoveredFromAlternateManifest;
                if (recoveredFromAlternateManifest)
                {
                    await SaveCoreAsync(manifest, cancellationToken);
                }
                return manifest;
            }
            catch (Exception exception) when (
                exception is JsonException
                    or IOException
                    or UnauthorizedAccessException
                    or InvalidDataException)
            {
                // Try the next atomic-save candidate.
            }
        }

        return null;
    }

    private async Task SaveCoreAsync(
        VietsubProjectManifest manifest,
        CancellationToken cancellationToken)
    {
        ValidateManifest(manifest);
        _paths.CreateProjectDirectories(manifest.ProjectId);
        manifest.UpdatedAtUtc = DateTime.UtcNow;
        var manifestPath = _paths.GetProjectPath(manifest.ProjectId, ManifestFileName);
        var temporaryPath = manifestPath + ".tmp";
        var backupPath = manifestPath + ".bak";
        var completeTemporaryManifest = false;
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            completeTemporaryManifest = true;
            await PublishManifestAsync(temporaryPath, manifestPath, backupPath, cancellationToken);
            completeTemporaryManifest = false;
        }
        finally
        {
            if (!completeTemporaryManifest && File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static async Task PublishManifestAsync(
        string temporaryPath,
        string manifestPath,
        string backupPath,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        File.Replace(temporaryPath, manifestPath, backupPath, ignoreMetadataErrors: true);
                    }
                    catch (Exception exception) when (exception is IOException or PlatformNotSupportedException)
                    {
                        File.Copy(manifestPath, backupPath, overwrite: true);
                        File.Move(temporaryPath, manifestPath, overwrite: true);
                    }
                }
                else
                {
                    File.Move(temporaryPath, manifestPath);
                }
                return;
            }
            catch (Exception exception) when (
                (exception is IOException or UnauthorizedAccessException)
                && attempt < PublishRetryDelays.Length)
            {
                await Task.Delay(PublishRetryDelays[attempt], cancellationToken);
            }
        }
    }

    private SemaphoreSlim GetProjectLock(Guid projectId) =>
        _projectLocks.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));

    private static void EnsureAccess(
        VietsubProjectManifest manifest,
        Guid organizationId,
        string ownerUserId)
    {
        if (manifest.OrganizationId != organizationId
            || !string.Equals(manifest.OwnerUserId, ownerUserId, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Bạn không có quyền mở dự án Vietsub này.");
        }
    }

    private static void ValidateOwner(Guid organizationId, string ownerUserId)
    {
        if (organizationId == Guid.Empty || string.IsNullOrWhiteSpace(ownerUserId))
        {
            throw new ArgumentException("Tổ chức hoặc tài khoản Vietsub không hợp lệ.");
        }
    }

    private static string NormalizeName(string name)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length is < 1 or > 120 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Tên dự án Vietsub phải có từ 1 đến 120 ký tự hợp lệ.", nameof(name));
        }
        return normalized;
    }

    private static void ValidateManifest(VietsubProjectManifest manifest)
    {
        if (manifest.SchemaVersion != VietsubProjectManifest.CurrentSchemaVersion
            || manifest.ProjectId == Guid.Empty
            || manifest.OrganizationId == Guid.Empty
            || string.IsNullOrWhiteSpace(manifest.OwnerUserId)
            || !string.Equals(manifest.TargetLanguageCode, "vi", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Manifest dự án Vietsub không hợp lệ hoặc chưa được hỗ trợ.");
        }

        _ = NormalizeName(manifest.Name);
        manifest.SourceLanguageCode = string.IsNullOrWhiteSpace(manifest.SourceLanguageCode)
            ? "auto"
            : manifest.SourceLanguageCode.Trim().ToLowerInvariant();
        manifest.TargetLanguageCode = "vi";
    }
}
