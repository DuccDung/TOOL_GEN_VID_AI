using TOOL_LOCAL.WebView;

namespace TOOL_TESTS.Projects;

public sealed class DashboardProjectSelectionTests
{
    [Fact]
    public void InitialLoad_DoesNotSelectTheMostRecentProject()
    {
        var recentProjectId = Guid.NewGuid();
        var olderProjectId = Guid.NewGuid();

        var selectedProjectId = DashboardBridge.ResolveSelectedProjectId(
            selectedProjectId: null,
            availableProjectIds: [recentProjectId, olderProjectId],
            selectDefaultProject: false);

        Assert.Null(selectedProjectId);
    }

    [Fact]
    public void Refresh_PreservesAProjectThatTheUserSelected()
    {
        var recentProjectId = Guid.NewGuid();
        var selectedProjectId = Guid.NewGuid();

        var resolvedProjectId = DashboardBridge.ResolveSelectedProjectId(
            selectedProjectId: selectedProjectId,
            availableProjectIds: [recentProjectId, selectedProjectId],
            selectDefaultProject: false);

        Assert.Equal(selectedProjectId, resolvedProjectId);
    }

    [Fact]
    public void NormalFallback_CanStillSelectTheMostRecentProject()
    {
        var recentProjectId = Guid.NewGuid();
        var olderProjectId = Guid.NewGuid();

        var selectedProjectId = DashboardBridge.ResolveSelectedProjectId(
            selectedProjectId: null,
            availableProjectIds: [recentProjectId, olderProjectId],
            selectDefaultProject: true);

        Assert.Equal(recentProjectId, selectedProjectId);
    }
}
