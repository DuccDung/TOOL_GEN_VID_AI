using System.Security.Cryptography;

namespace TOOL_SHARED.Distribution.StaticModels;

internal static class StaticModelFileSafety
{
    internal const int BufferBytes = 128 * 1024;
    internal const long DiskMarginBytes = 64L * 1024 * 1024;

    internal static bool IsHash(string value) => value is { Length: 64 } && value.All(c =>
        c is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static void AssertSafePath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) throw Invalid();
        for (var current = new DirectoryInfo(full); current is not null; current = current.Parent)
        {
            if ((File.Exists(current.FullName) || Directory.Exists(current.FullName)) &&
                (File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
                throw Invalid();
        }
    }

    internal static void EnsureDirectory(string path)
    {
        AssertSafePath(path);
        Directory.CreateDirectory(path);
        AssertSafePath(path);
    }

    internal static void EnsureDisk(string directory, long remaining)
    {
        AssertSafePath(directory);
        if (remaining < 0 || new DriveInfo(Path.GetPathRoot(Path.GetFullPath(directory))!).AvailableFreeSpace <
            checked(remaining + DiskMarginBytes))
            throw new IOException("Không đủ dung lượng trống trên ổ chứa cache model. Cần giải phóng dung lượng rồi thử lại.");
    }

    internal static async Task<bool> MatchesAsync(string path, long size, string hash, CancellationToken token)
    {
        AssertSafePath(path);
        if (!File.Exists(path)) return false;
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return file.Length == size && Convert.ToHexString(await SHA256.HashDataAsync(file, token))
            .Equals(hash, StringComparison.OrdinalIgnoreCase);
    }

    internal static void DeleteFile(string path)
    {
        AssertSafePath(path);
        if (File.Exists(path)) File.Delete(path);
    }

    // Caller supplies only its own cache/staging directory. Validate the whole tree before any deletion.
    internal static void DeleteOwnedTree(string path)
    {
        AssertSafePath(path);
        if (!Directory.Exists(path)) return;
        var directories = new List<string> { path };
        var files = new List<string>();
        for (var i = 0; i < directories.Count; i++)
        {
            foreach (var item in Directory.EnumerateFileSystemEntries(directories[i]))
            {
                AssertSafePath(item);
                if (Directory.Exists(item)) directories.Add(item); else files.Add(item);
            }
        }
        foreach (var file in files) File.Delete(file);
        for (var i = directories.Count - 1; i >= 0; i--) Directory.Delete(directories[i]);
    }

    internal static string SafeRelativePath(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 220 || value[0] == '/' ||
            value.Contains('\\') || value.Contains(':') || value.Any(char.IsControl)) throw Invalid();
        foreach (var segment in value.Split('/'))
        {
            if (segment is "" or "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.') ||
                segment.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0) throw Invalid();
            var stem = segment.Split('.')[0];
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                    stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '0' and <= '9')) throw Invalid();
        }
        return value;
    }

    internal static string ResolveUnder(string root, string relative)
    {
        SafeRelativePath(relative);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw Invalid();
        AssertSafePath(full);
        return full;
    }

    internal static InvalidDataException Invalid() => new("Đường dẫn hoặc cấu trúc gói model không an toàn.");
}
