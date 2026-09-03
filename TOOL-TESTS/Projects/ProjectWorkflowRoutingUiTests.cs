namespace TOOL_TESTS.Projects;

public sealed class ProjectWorkflowRoutingUiTests
{
    [Fact]
    public void SelectingProject_RoutesByPersistedWorkflowInsteadOfSessionOnlyState()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");
        var types = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "types.ts");

        var resolverStart = app.IndexOf("function resolveSelectedProjectPage", StringComparison.Ordinal);
        var resolverEnd = app.IndexOf("const pageHeaders", resolverStart, StringComparison.Ordinal);
        Assert.True(resolverStart >= 0 && resolverEnd > resolverStart);
        var resolver = app[resolverStart..resolverEnd];

        Assert.Contains("workflowStructureType === 'DirectShortVideo'", resolver);
        Assert.Contains("? 'shortVideo' : 'longVideo'", resolver);

        var selectProjectStart = app.IndexOf("const selectProject =", StringComparison.Ordinal);
        var selectProjectEnd = app.IndexOf("const createProject =", selectProjectStart, StringComparison.Ordinal);
        Assert.True(selectProjectStart >= 0 && selectProjectEnd > selectProjectStart);
        var selectProject = app[selectProjectStart..selectProjectEnd];

        Assert.Contains("selectedProjectRequestRef.current = postToHost('project.select', { projectId });", selectProject);
        Assert.DoesNotContain("setPage('longVideo')", selectProject, StringComparison.Ordinal);

        Assert.Contains("isSelectedProjectResponse(selectedProjectRequestRef.current, message.requestId)", app);
        Assert.Contains("setPage(resolveSelectedProjectPage(nextDashboard.selectedProject?.workflowStructureType));", app);
        Assert.Contains("project={dashboard.selectedProject?.workflowStructureType === 'DirectShortVideo'", app);
        Assert.DoesNotContain("shortVideoProjectId", app, StringComparison.Ordinal);
        Assert.Contains("requestId?: string | null;", types);
    }

    [Fact]
    public void BackgroundRefreshWithoutRequestId_DoesNotTriggerProjectNavigation()
    {
        var app = ReadRepositoryFile("TOOL-LOCAL", "Web", "src", "App.tsx");

        var matcherStart = app.IndexOf("function isSelectedProjectResponse", StringComparison.Ordinal);
        var matcherEnd = app.IndexOf("const pageHeaders", matcherStart, StringComparison.Ordinal);
        Assert.True(matcherStart >= 0 && matcherEnd > matcherStart);
        var matcher = app[matcherStart..matcherEnd];

        Assert.Contains("pendingRequestId !== null", matcher);
        Assert.Contains("pendingRequestId === responseRequestId", matcher);
    }

    private static string ReadRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Cannot locate repository file: {Path.Combine(relativeParts)}");
    }
}
