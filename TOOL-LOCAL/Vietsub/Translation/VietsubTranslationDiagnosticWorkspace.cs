namespace TOOL_LOCAL.Vietsub.Translation;

// Only the explicit offline command grants a fresh, temporary component root.
// Regular desktop roots and the pinned model validation remain unchanged.
internal static class VietsubTranslationDiagnosticWorkspace
{
    public const string EnvironmentVariable = "VIDEOMAKER_QWEN_DIAGNOSTIC_COMPONENT_ROOT";
    public const string MarkerFileName = ".qwen-offline-diagnostic";
    private const string Marker = "Qwen offline diagnostic workspace v2";
    private const string Prefix = "vm-qwen-offline-";

    public static string Initialize(string workspace)
    {
        var full = ValidateWorkspacePath(workspace);
        if (Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
            throw new ArgumentException("Use a new, empty diagnostic workspace.");
        Directory.CreateDirectory(full);
        using (var file = new StreamWriter(new FileStream(Path.Combine(full, MarkerFileName),
            FileMode.CreateNew, FileAccess.Write, FileShare.None))) file.Write(Marker);
        var root = Path.Combine(full, "components");
        Directory.CreateDirectory(root);
        return root;
    }

    public static bool IsApprovedComponentRoot(string root, string? expectedRoot)
    {
        if (string.IsNullOrWhiteSpace(expectedRoot)) return false;
        try
        {
            var full = Path.GetFullPath(root);
            if (!string.Equals(full, Path.GetFullPath(expectedRoot), StringComparison.OrdinalIgnoreCase)) return false;
            var workspace = Directory.GetParent(full)?.FullName;
            if (workspace is null || !string.Equals(full, Path.Combine(ValidateWorkspacePath(workspace), "components"),
                StringComparison.OrdinalIgnoreCase)) return false;
            RejectReparsePoints(full);
            var marker = Path.Combine(workspace, MarkerFileName);
            return File.Exists(marker) && (File.GetAttributes(marker) & FileAttributes.ReparsePoint) == 0
                && new FileInfo(marker).Length <= 128 && File.ReadAllText(marker) == Marker;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        { return false; }
    }

    private static string ValidateWorkspacePath(string workspace)
    {
        if (!Path.IsPathFullyQualified(workspace)) throw new ArgumentException("An absolute diagnostic workspace is required.");
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace));
        var parent = Path.GetDirectoryName(full);
        var name = Path.GetFileName(full);
        if (!string.Equals(parent, Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),
                StringComparison.OrdinalIgnoreCase)
            || !name.StartsWith(Prefix, StringComparison.Ordinal) || name.Length != Prefix.Length + 32
            || !Guid.TryParseExact(name[Prefix.Length..], "N", out _))
            throw new ArgumentException("Use a fresh vm-qwen-offline-<GUID> directory inside the temporary directory.");
        RejectReparsePoints(full);
        return full;
    }

    private static void RejectReparsePoints(string path)
    {
        for (var item = new DirectoryInfo(path); item is not null; item = item.Parent)
            if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Diagnostic directories cannot be reparse points.");
    }
}
