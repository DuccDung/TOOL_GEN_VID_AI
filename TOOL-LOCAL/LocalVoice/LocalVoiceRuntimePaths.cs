namespace TOOL_LOCAL.LocalVoice;

internal static class LocalVoiceRuntimePaths
{
    public static string ResolveDirectory(string value)
    {
        var expanded = Environment.ExpandEnvironmentVariables(value);
        if (!Path.IsPathFullyQualified(expanded) || expanded.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Thư mục runtime/temp giọng local phải là đường dẫn đầy đủ trên ổ đĩa local.");
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
        if (string.Equals(path, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path)!), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Hãy chọn thư mục riêng cho runtime/temp giọng local.");
        RequireNoReparsePoints(path);
        return path;
    }

    public static void RequireNoReparsePoints(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Runtime/temp giọng local không được chứa liên kết thư mục.");
    }

    public static bool IsWithin(string path, string parent) =>
        string.Equals(path, parent, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
