using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Domain.Updates;

namespace TOOL_SERVER.Updates;

public interface IDesktopReleaseStorage
{
    Task<StoredDesktopArtifact> SaveAsync(
        Guid releaseId,
        string kind,
        string fileName,
        Stream source,
        long length,
        CancellationToken cancellationToken);

    string ResolvePath(string relativePath);

    void DeleteFile(string? relativePath);

    void DeleteRelease(Guid releaseId);
}

public sealed class DesktopReleaseStorage : IDesktopReleaseStorage
{
    private readonly string _root;
    private readonly long _maximumArtifactBytes;

    public DesktopReleaseStorage(
        IOptions<DesktopReleaseOptions> options,
        IWebHostEnvironment environment)
    {
        var configuredRoot = options.Value.StorageRoot.Trim();
        _root = Path.GetFullPath(Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(environment.ContentRootPath, configuredRoot));
        _maximumArtifactBytes = options.Value.MaximumArtifactBytes;
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredDesktopArtifact> SaveAsync(
        Guid releaseId,
        string kind,
        string fileName,
        Stream source,
        long length,
        CancellationToken cancellationToken)
    {
        var normalizedKind = NormalizeKind(kind);
        var safeName = Path.GetFileName(fileName?.Trim());
        if (string.IsNullOrWhiteSpace(safeName) || safeName.Length > 260)
        {
            throw new ArgumentException("Tên artifact không hợp lệ.");
        }

        var expectedExtension = normalizedKind == DesktopArtifactKinds.DesktopPackage ? ".zip" : ".exe";
        if (!string.Equals(Path.GetExtension(safeName), expectedExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Artifact {normalizedKind} phải là file {expectedExtension}.");
        }

        if (length <= 0 || length > _maximumArtifactBytes)
        {
            throw new ArgumentException("Kích thước artifact không hợp lệ.");
        }

        var relativeDirectory = Path.Combine(releaseId.ToString("N"), normalizedKind);
        var targetDirectory = ResolvePath(relativeDirectory);
        Directory.CreateDirectory(targetDirectory);
        var relativePath = Path.Combine(relativeDirectory, $"{Guid.NewGuid():N}-{safeName}");
        var targetPath = ResolvePath(relativePath);
        var temporaryPath = targetPath + ".upload";

        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                long written = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    written += read;
                    if (written > _maximumArtifactBytes)
                    {
                        throw new ArgumentException("Artifact vượt quá giới hạn cho phép.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                if (written <= 0 || (length > 0 && written != length))
                {
                    throw new InvalidDataException("Artifact tải lên không đầy đủ.");
                }
            }

            if (normalizedKind == DesktopArtifactKinds.DesktopPackage)
            {
                ValidateDesktopPackage(temporaryPath);
            }
            else
            {
                ValidatePortableExecutable(temporaryPath);
            }

            File.Move(temporaryPath, targetPath);
            var info = new FileInfo(targetPath);
            return new StoredDesktopArtifact(
                safeName,
                relativePath,
                info.Length,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    public string ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Đường dẫn artifact không hợp lệ.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Đường dẫn artifact nằm ngoài vùng lưu trữ.");
        }

        return fullPath;
    }

    public void DeleteFile(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        TryDelete(ResolvePath(relativePath));
    }

    public void DeleteRelease(Guid releaseId)
    {
        var directory = ResolvePath(releaseId.ToString("N"));
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string NormalizeKind(string kind)
    {
        var match = DesktopArtifactKinds.All.FirstOrDefault(value =>
            string.Equals(value, kind?.Trim(), StringComparison.OrdinalIgnoreCase));
        return match ?? throw new ArgumentException("Loại artifact không được hỗ trợ.");
    }

    private void ValidateDesktopPackage(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            if (archive.Entries.Count > 200_000 || archive.Entries.Sum(entry => entry.Length) > _maximumArtifactBytes)
            {
                throw new ArgumentException("Package có quá nhiều file hoặc kích thước giải nén vượt giới hạn.");
            }

            var fileNames = archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name))
                .Select(entry => ValidateAndNormalizeEntry(entry.FullName))
                .ToArray();
            if (!fileNames.Any(name => name.EndsWith("/TOOL-LOCAL.exe", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(name, "TOOL-LOCAL.exe", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Package không chứa TOOL-LOCAL.exe.");
            }

            if (!fileNames.Any(name => name.EndsWith("/update-manifest.json", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(name, "update-manifest.json", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Package không chứa update-manifest.json.");
            }
        }
        catch (InvalidDataException exception)
        {
            throw new ArgumentException("Desktop package không phải ZIP hợp lệ.", exception);
        }
    }

    private static string ValidateAndNormalizeEntry(string path)
    {
        var normalized = path.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (normalized.StartsWith('/') || segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
        {
            throw new ArgumentException("Package chứa đường dẫn không an toàn.");
        }

        return string.Join('/', segments);
    }

    private static void ValidatePortableExecutable(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
        {
            throw new ArgumentException("Setup artifact không phải Windows executable hợp lệ.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
