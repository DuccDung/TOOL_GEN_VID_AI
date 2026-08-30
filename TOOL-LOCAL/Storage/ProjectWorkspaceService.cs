namespace TOOL_LOCAL.Storage;

public sealed class ProjectWorkspaceService
{
    private static readonly string[] ProjectFolders =
    [
        "script",
        "characters",
        "storyboard",
        "scenes",
        "voice",
        "subtitles",
        "music",
        "render",
        "final",
        "thumbnail"
    ];

    private readonly string _workspaceRoot;

    public string WorkspaceRoot => _workspaceRoot;

    public ProjectWorkspaceService(string configuredRoot)
    {
        _workspaceRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredRoot));
        Directory.CreateDirectory(_workspaceRoot);
    }

    public string Create(Guid projectId)
    {
        var relativePath = Path.Combine("projects", projectId.ToString("N"));
        var projectRoot = Resolve(relativePath);
        Directory.CreateDirectory(projectRoot);

        foreach (var folder in ProjectFolders)
        {
            Directory.CreateDirectory(Path.Combine(projectRoot, folder));
        }

        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    public string Resolve(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException("Workspace path phải là đường dẫn tương đối.");
        }

        var resolved = Path.GetFullPath(Path.Combine(_workspaceRoot, relativePath));
        var rootWithSeparator = _workspaceRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Workspace path nằm ngoài thư mục được cho phép.");
        }

        return resolved;
    }
}
