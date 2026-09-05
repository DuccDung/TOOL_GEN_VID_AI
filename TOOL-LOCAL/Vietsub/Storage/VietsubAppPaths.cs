namespace TOOL_LOCAL.Vietsub.Storage;

internal sealed class VietsubAppPaths
{
    private static readonly string[] ProjectDirectories =
    [
        "source",
        "audio",
        "subtitles",
        "voice",
        "music",
        "cache",
        "thumbnails",
        "waveforms",
        "output",
        "temp",
        "logs"
    ];

    public VietsubAppPaths(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("Workspace root không được để trống.", nameof(workspaceRoot));
        }

        var expandedRoot = Environment.ExpandEnvironmentVariables(workspaceRoot.Trim());
        if (!Path.IsPathFullyQualified(expandedRoot))
        {
            expandedRoot = Path.Combine(AppContext.BaseDirectory, expandedRoot);
        }

        RootDirectory = Path.GetFullPath(Path.Combine(expandedRoot, "vietsub"));
        ProjectsDirectory = Path.Combine(RootDirectory, "projects");
        Directory.CreateDirectory(ProjectsDirectory);
    }

    public string RootDirectory { get; }

    public string ProjectsDirectory { get; }

    public string GetProjectDirectory(Guid projectId)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Mã dự án Vietsub không hợp lệ.", nameof(projectId));
        }

        return Path.Combine(ProjectsDirectory, projectId.ToString("N"));
    }

    public string GetProjectPath(Guid projectId, params string[] relativeParts)
    {
        var projectDirectory = Path.GetFullPath(GetProjectDirectory(projectId));
        var combined = relativeParts.Aggregate(projectDirectory, Path.Combine);
        var fullPath = Path.GetFullPath(combined);
        var projectPrefix = projectDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? projectDirectory
            : projectDirectory + Path.DirectorySeparatorChar;
        if (!fullPath.Equals(projectDirectory, StringComparison.OrdinalIgnoreCase)
            && !fullPath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Đường dẫn Vietsub nằm ngoài workspace dự án.");
        }

        return fullPath;
    }

    public void CreateProjectDirectories(Guid projectId)
    {
        Directory.CreateDirectory(GetProjectDirectory(projectId));
        foreach (var directory in ProjectDirectories)
        {
            Directory.CreateDirectory(GetProjectPath(projectId, directory));
        }
    }
}
