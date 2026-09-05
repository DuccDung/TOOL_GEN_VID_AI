using System.Text.Json;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Generation;

public sealed class SceneFirstFrameContractTests
{
    [Fact]
    public void SubmitVideoRequest_KeepsLegacyReferenceAndAppendsFirstFrame()
    {
        var referenceId = Guid.NewGuid();
        var frameId = Guid.NewGuid();
        var request = new SubmitVideoRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "video-key",
            ReferenceImage: new VideoReferenceImageInput(referenceId, "image/png", "legacy", new string('a', 64)),
            ScenePlanVersion: 1,
            ScenePromptVersion: 2,
            FirstFrame: new SceneFirstFrameInput(frameId, "image/png", "veo-frame", new string('b', 64)));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal(referenceId, json.RootElement.GetProperty("referenceImage").GetProperty("characterReferenceId").GetGuid());
        Assert.Equal(frameId, json.RootElement.GetProperty("firstFrame").GetProperty("sceneFirstFrameId").GetGuid());
    }

    [Fact]
    public void SceneFirstFrameStatuses_AreStable()
    {
        Assert.Equal("PendingReview", SceneFirstFrameStatuses.PendingReview);
        Assert.Equal("Approved", SceneFirstFrameStatuses.Approved);
        Assert.Equal("Rejected", SceneFirstFrameStatuses.Rejected);
        Assert.Equal("Superseded", SceneFirstFrameStatuses.Superseded);
        Assert.Equal("Invalidated", SceneFirstFrameStatuses.Invalidated);
    }

    [Fact]
    public void ProjectSceneFirstFrameList_DoesNotRequireASyntheticSceneId()
    {
        var projectId = Guid.NewGuid();
        var response = new ProjectSceneFirstFrameListResponse(projectId, []);

        Assert.Equal(projectId, response.ProjectId);
        Assert.Empty(response.Frames);
    }
}
