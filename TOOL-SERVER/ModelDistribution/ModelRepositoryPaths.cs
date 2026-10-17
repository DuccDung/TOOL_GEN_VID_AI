namespace TOOL_SERVER.ModelDistribution;

internal static class ModelRepositoryPaths
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    internal static bool IsSafeSegment(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 180 && value is not "." and not ".." &&
        value[^1] != '.' && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.') &&
        !IsReservedWindowsName(value);

    internal static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(character => char.IsAsciiHexDigit(character));

    internal static bool IsWithin(string path, string root) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, PathComparison);

    internal static string Resolve(string root, string relativePath)
    {
        // Reject noncanonical paths rather than normalizing traversal, alternate streams, or URLs.
        var segments = relativePath.Split('/');
        if (segments.Length == 0 || segments.Any(segment => !IsSafeSegment(segment)))
        {
            throw new InvalidDataException("Đường dẫn kho model không hợp lệ.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        if (!IsWithin(fullPath, root))
        {
            throw new InvalidDataException("Đường dẫn kho model không hợp lệ.");
        }

        RejectReparsePoints(fullPath);
        return fullPath;
    }

    internal static void RejectReparsePoints(string fullPath)
    {
        // Check the root and its ancestors too: a junction above StorageRoot is still unsafe.
        var root = Path.GetPathRoot(fullPath) ?? throw new InvalidDataException("Kho model không hợp lệ.");
        var current = root;
        CheckAttributes(current);
        foreach (var segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            CheckAttributes(current);
        }
    }

    private static void CheckAttributes(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Kho model không được chứa liên kết hoặc reparse point.");
        }
    }

    private static bool IsReservedWindowsName(string value)
    {
        var stem = value.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               (stem.Length == 4 && stem[3] is >= '1' and <= '9' &&
                (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)));
    }
}
