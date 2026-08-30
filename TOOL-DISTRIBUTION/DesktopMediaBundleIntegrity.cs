using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace TOOL_SHARED.Distribution;

public sealed record DesktopMediaBundleValidationResult(
    IReadOnlyDictionary<string, string> Sha256ByFileName,
    string ApprovalScope);

public static partial class DesktopMediaBundleIntegrity
{
    public const string BundleRelativeDirectory = "tools/ffmpeg";

    private static readonly string[] RequiredFileNamesValue =
    [
        "ffmpeg.exe",
        "ffprobe.exe",
        "LICENSE.txt",
        "PROVENANCE.md",
        "checksums.sha256"
    ];

    private static readonly string[] ChecksummedFileNamesValue =
    [
        "ffmpeg.exe",
        "ffprobe.exe",
        "LICENSE.txt"
    ];

    public static IReadOnlyList<string> RequiredFileNames { get; } =
        Array.AsReadOnly(RequiredFileNamesValue);

    public static IReadOnlyList<string> RequiredRelativePaths { get; } =
        Array.AsReadOnly(RequiredFileNamesValue
            .Select(fileName => $"{BundleRelativeDirectory}/{fileName}")
            .ToArray());

    public static IReadOnlyList<string> ChecksummedFileNames { get; } =
        Array.AsReadOnly(ChecksummedFileNamesValue);

    public static DesktopMediaBundleValidationResult ValidatePackageRoot(
        string packageRoot,
        IEnumerable<string>? managedFiles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageRoot);
        var root = Path.GetFullPath(packageRoot);

        if (managedFiles is not null)
        {
            var normalizedManagedFiles = managedFiles
                .Select(NormalizeRelativePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var requiredPath in RequiredRelativePaths)
            {
                if (!normalizedManagedFiles.Contains(requiredPath))
                {
                    throw new InvalidDataException(
                        $"Update manifest thiếu media tool bắt buộc: {requiredPath}.");
                }
            }
        }

        return ValidateBundleDirectory(
            Path.Combine(root, "tools", "ffmpeg"),
            requireReleaseApproval: true);
    }

    public static DesktopMediaBundleValidationResult ValidateBundleDirectory(
        string bundleDirectory,
        bool requireReleaseApproval = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bundleDirectory);
        var root = Path.GetFullPath(bundleDirectory);
        foreach (var fileName in RequiredFileNames)
        {
            var path = Path.Combine(root, fileName);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                throw new InvalidDataException(
                    $"FFmpeg bundle thiếu hoặc rỗng media tool bắt buộc: {fileName}.");
            }
        }

        var provenance = File.ReadAllText(Path.Combine(root, "PROVENANCE.md"));
        if (string.IsNullOrWhiteSpace(provenance))
        {
            throw new InvalidDataException("FFmpeg bundle chưa có hồ sơ nguồn PROVENANCE.md hợp lệ.");
        }

        var approvalScope = ReadProvenanceValue(provenance, "Approval scope");
        if (approvalScope is not ("Development" or "Release"))
        {
            throw new InvalidDataException(
                "PROVENANCE.md phải khai báo Approval scope là Development hoặc Release.");
        }
        if (requireReleaseApproval && approvalScope != "Release")
        {
            throw new InvalidDataException(
                "FFmpeg bundle chỉ được duyệt cho Development, chưa được phép đưa vào package phát hành.");
        }

        var expectedHashes = ParseChecksums(Path.Combine(root, "checksums.sha256"));
        var actualHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fileName in ChecksummedFileNames)
        {
            if (!expectedHashes.TryGetValue(fileName, out var expectedHash))
            {
                throw new InvalidDataException(
                    $"FFmpeg bundle chưa khai báo SHA-256 cho {fileName}.");
            }

            using var stream = File.OpenRead(Path.Combine(root, fileName));
            var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"SHA-256 của {fileName} không khớp hồ sơ FFmpeg đã duyệt.");
            }

            actualHashes[fileName] = actualHash;
        }

        return new DesktopMediaBundleValidationResult(
            new ReadOnlyDictionary<string, string>(actualHashes),
            approvalScope);
    }

    private static string ReadProvenanceValue(string content, string name)
    {
        var match = Regex.Match(
            content,
            $"(?im)^\\s*-\\s*{Regex.Escape(name)}:\\s*(?<value>.+?)\\s*$",
            RegexOptions.CultureInvariant);
        if (!match.Success || string.IsNullOrWhiteSpace(match.Groups["value"].Value))
        {
            throw new InvalidDataException($"PROVENANCE.md thiếu trường {name}.");
        }

        return match.Groups["value"].Value.Trim();
    }

    private static Dictionary<string, string> ParseChecksums(string checksumPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(checksumPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var match = ChecksumLineRegex().Match(line);
            if (!match.Success)
            {
                throw new InvalidDataException(
                    "checksums.sha256 phải dùng định dạng '<sha256> *<tên-file>'.");
            }

            var fileName = match.Groups["file"].Value.Trim();
            if (fileName.Contains('/') || fileName.Contains('\\') ||
                !ChecksummedFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"checksums.sha256 chứa tên file không được hỗ trợ: {fileName}.");
            }

            if (!result.TryAdd(fileName, match.Groups["hash"].Value.ToLowerInvariant()))
            {
                throw new InvalidDataException(
                    $"checksums.sha256 khai báo trùng file: {fileName}.");
            }
        }

        return result;
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            throw new InvalidDataException("Manifest chứa đường dẫn không hợp lệ.");
        }

        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
        {
            throw new InvalidDataException("Manifest chứa đường dẫn không an toàn.");
        }

        return string.Join('/', segments);
    }

    [GeneratedRegex("^(?<hash>[0-9a-fA-F]{64})\\s+\\*?(?<file>.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChecksumLineRegex();
}
