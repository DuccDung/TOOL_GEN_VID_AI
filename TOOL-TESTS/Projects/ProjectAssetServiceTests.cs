using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Models;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.Projects;
using TOOL_SHARED.Contracts.Projects;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Projects;

public sealed class ProjectAssetServiceTests
{
    [Fact]
    public async Task Library_LocksImmutableVersionAndAssignsOnlyInsideCurrentProject()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext);
        var service = CreateService(dbContext, seeded.Project, "Member");

        var created = await service.CreateAsync(
            seeded.Project.ProjectId,
            new CreateProjectAssetRequest(
                ProjectAssetTypes.Background,
                "Căn bếp nhà Minh",
                "Tủ gỗ nâu, tường trắng, cửa sổ luôn nằm bên trái."),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var locked = await service.LockAsync(
            seeded.Project.ProjectId,
            created.ProjectAssetId,
            new ChangeProjectAssetLockRequest(created.ConcurrencyToken),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var assignment = await service.UpdateSceneAssignmentsAsync(
            seeded.Project.ProjectId,
            seeded.Scene.SceneId,
            new UpdateSceneAssetAssignmentsRequest([locked.ProjectAssetId]),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var library = await service.GetLibraryAsync(
            seeded.Project.ProjectId,
            seeded.Project.OrganizationId,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(ProjectAssetStatuses.Locked, locked.Status);
        Assert.Equal(1, locked.CurrentVersion);
        var version = await dbContext.ProjectAssetVersions.SingleAsync();
        Assert.Equal(locked.ProjectAssetId, version.ProjectAssetId);
        Assert.Equal("Căn bếp nhà Minh", version.Name);
        Assert.Equal("Tủ gỗ nâu, tường trắng, cửa sổ luôn nằm bên trái.", version.CanonicalDescription);
        Assert.False(assignment.HasUnlockedAssets);
        Assert.Equal([locked.ProjectAssetId], assignment.ProjectAssetIds);
        Assert.True(library.CanEdit);
        Assert.Equal([seeded.Scene.SceneId], Assert.Single(library.Assets).SceneIds);
    }

    [Fact]
    public async Task LockedAsset_MustBeUnlockedBeforeEditingAndCreatesNewVersionWhenRelocked()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext);
        var service = CreateService(dbContext, seeded.Project, "Member");
        var created = await service.CreateAsync(
            seeded.Project.ProjectId,
            new CreateProjectAssetRequest(ProjectAssetTypes.Item, "Cốc đỏ", "Cốc gốm đỏ, quai đen."),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var lockedV1 = await service.LockAsync(
            seeded.Project.ProjectId,
            created.ProjectAssetId,
            new ChangeProjectAssetLockRequest(created.ConcurrencyToken),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => service.UpdateAsync(
            seeded.Project.ProjectId,
            created.ProjectAssetId,
            new UpdateProjectAssetRequest(ProjectAssetTypes.Item, "Cốc đỏ", "Mô tả khác", lockedV1.ConcurrencyToken),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));
        Assert.Equal("project_asset_locked", exception.Code);

        var draft = await service.UnlockAsync(
            seeded.Project.ProjectId,
            created.ProjectAssetId,
            new ChangeProjectAssetLockRequest(lockedV1.ConcurrencyToken),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var updated = await service.UpdateAsync(
            seeded.Project.ProjectId,
            created.ProjectAssetId,
            new UpdateProjectAssetRequest(ProjectAssetTypes.Item, "Cốc đỏ", "Cốc gốm đỏ sẫm, quai đen mờ.", draft.ConcurrencyToken),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var lockedV2 = await service.LockAsync(
            seeded.Project.ProjectId,
            created.ProjectAssetId,
            new ChangeProjectAssetLockRequest(updated.ConcurrencyToken),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(2, lockedV2.CurrentVersion);
        Assert.Equal(2, await dbContext.ProjectAssetVersions.CountAsync());
        Assert.Equal(
            ["Cốc gốm đỏ, quai đen.", "Cốc gốm đỏ sẫm, quai đen mờ."],
            await dbContext.ProjectAssetVersions.OrderBy(x => x.Version).Select(x => x.CanonicalDescription).ToArrayAsync());
    }

    [Fact]
    public async Task Viewer_CanReadButCannotMutateLibrary()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext);
        var service = CreateService(dbContext, seeded.Project, "Viewer");

        var library = await service.GetLibraryAsync(
            seeded.Project.ProjectId,
            seeded.Project.OrganizationId,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var exception = await Assert.ThrowsAsync<AccountApiException>(() => service.CreateAsync(
            seeded.Project.ProjectId,
            new CreateProjectAssetRequest(ProjectAssetTypes.Prop, "Máy ảnh", "Máy ảnh phim màu đen."),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.False(library.CanEdit);
        Assert.Equal("project_asset_edit_denied", exception.Code);
        Assert.Empty(dbContext.ProjectAssets);
    }

    [Fact]
    public async Task KlingLongFormAsset_RejectsEnglishButDirectShortRemainsAllowed()
    {
        await using var longFormDb = CreateContext();
        var longForm = SeedProject(longFormDb, "OpenAiStructuredPlan", klingSnapshot: true);
        var longFormService = CreateService(longFormDb, longForm.Project, "Member");

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => longFormService.CreateAsync(
            longForm.Project.ProjectId,
            new CreateProjectAssetRequest(
                ProjectAssetTypes.Background,
                "Minh's kitchen",
                "Brown cabinets and a window that always stays on the left."),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal("kling_prompt_language_invalid", exception.Code);
        Assert.Empty(longFormDb.ProjectAssets);

        await using var shortDb = CreateContext();
        var shortVideo = SeedProject(shortDb, "DirectShortVideo", klingSnapshot: true);
        var shortService = CreateService(shortDb, shortVideo.Project, "Member");
        var created = await shortService.CreateAsync(
            shortVideo.Project.ProjectId,
            new CreateProjectAssetRequest(
                ProjectAssetTypes.Background,
                "Minh's kitchen",
                "Brown cabinets and a window that always stays on the left."),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("Minh's kitchen", created.Name);
    }

    [Fact]
    public async Task SceneAssignment_RequiresExactlyOneBackgroundWhenAssetsAreUsed()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext);
        var service = CreateService(dbContext, seeded.Project, "Member");
        var prop = await service.CreateAsync(
            seeded.Project.ProjectId,
            new CreateProjectAssetRequest(ProjectAssetTypes.Prop, "Camera", "A compact black cinema camera."),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => service.UpdateSceneAssignmentsAsync(
            seeded.Project.ProjectId,
            seeded.Scene.SceneId,
            new UpdateSceneAssetAssignmentsRequest([prop.ProjectAssetId]),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal("scene_asset_background_invalid", exception.Code);
        Assert.Empty(dbContext.SceneAssetAssignments);
    }

    [Fact]
    public async Task KlingSceneAssignment_RejectsAssetsThatOverflowPromptBeforeProviderCall()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext, "OpenAiStructuredPlan", klingSnapshot: true);
        dbContext.ScenePrompts.Add(new ScenePrompt
        {
            ScenePromptId = Guid.NewGuid(),
            SceneId = seeded.Scene.SceneId,
            Version = 1,
            PromptTemplateName = "test",
            PromptTemplateVersion = "1",
            CanonicalInputJson = "{}",
            FinalPrompt = "Một người phụ nữ đi qua căn phòng trong một cú máy điện ảnh liên tục.",
            PromptHash = "test",
            Status = "Ready",
            CreatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8]
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, seeded.Project, "Member");
        var longDescription = string.Join(' ', Enumerable.Repeat("bề mặt hình ảnh ổn định và chi tiết", 42));
        var background = await service.CreateAsync(
            seeded.Project.ProjectId,
            new CreateProjectAssetRequest(ProjectAssetTypes.Background, "Phòng ăn cân đối", longDescription),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var prop = await service.CreateAsync(
            seeded.Project.ProjectId,
            new CreateProjectAssetRequest(ProjectAssetTypes.Prop, "Xe phục vụ nhiều chi tiết", longDescription),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => service.UpdateSceneAssignmentsAsync(
            seeded.Project.ProjectId,
            seeded.Scene.SceneId,
            new UpdateSceneAssetAssignmentsRequest([background.ProjectAssetId, prop.ProjectAssetId]),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal("kling_prompt_too_long", exception.Code);
        Assert.Empty(dbContext.SceneAssetAssignments);
    }

    [Fact]
    public async Task ApproveAiAssets_LocksAllAssignedAiDraftsInOneSave()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext);
        var now = DateTime.UtcNow;
        var background = NewAiAsset(seeded.Project.ProjectId, ProjectAssetTypes.Background, "room", "Balanced room", now);
        var prop = NewAiAsset(seeded.Project.ProjectId, ProjectAssetTypes.Prop, "bottle", "Clear bottle", now);
        dbContext.ProjectAssets.AddRange(background, prop);
        dbContext.SceneAssetAssignments.AddRange(
            NewAssignment(seeded.Scene.SceneId, background.ProjectAssetId, now),
            NewAssignment(seeded.Scene.SceneId, prop.ProjectAssetId, now));
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, seeded.Project, "Member");
        var library = await service.GetLibraryAsync(
            seeded.Project.ProjectId,
            seeded.Project.OrganizationId,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        var result = await service.ApproveAiAssetsAsync(
            seeded.Project.ProjectId,
            new ApproveAiProjectAssetsRequest(library.Assets.Select(x =>
                new ApproveProjectAssetInput(x.ProjectAssetId, x.ConcurrencyToken)).ToArray()),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(2, result.LockedAssets);
        Assert.Equal(1, result.ReadyScenes);
        Assert.All(await dbContext.ProjectAssets.ToListAsync(), asset => Assert.Equal(ProjectAssetStatuses.Locked, asset.Status));
        Assert.Equal(2, await dbContext.ProjectAssetVersions.CountAsync());
    }

    [Fact]
    public async Task ConfirmSceneAssets_LocksAssignedDraftsButLeavesUnassignedAssetUntouched()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext);
        var now = DateTime.UtcNow;
        var background = NewAiAsset(seeded.Project.ProjectId, ProjectAssetTypes.Background, "room", "Balanced room", now);
        var prop = NewAiAsset(seeded.Project.ProjectId, ProjectAssetTypes.Prop, "bottle", "Clear bottle", now);
        prop.SourceKind = ProjectAssetSourceKinds.Manual;
        var unassigned = NewAiAsset(seeded.Project.ProjectId, ProjectAssetTypes.Item, "phone", "Face-down phone", now);
        dbContext.ProjectAssets.AddRange(background, prop, unassigned);
        dbContext.SceneAssetAssignments.AddRange(
            NewAssignment(seeded.Scene.SceneId, background.ProjectAssetId, now),
            NewAssignment(seeded.Scene.SceneId, prop.ProjectAssetId, now));
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, seeded.Project, "Member");
        var library = await service.GetLibraryAsync(
            seeded.Project.ProjectId,
            seeded.Project.OrganizationId,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var assignedIds = library.SceneAssignments.Single().ProjectAssetIds.ToHashSet();
        var inputs = library.Assets
            .Where(x => assignedIds.Contains(x.ProjectAssetId))
            .Select(x => new ApproveProjectAssetInput(x.ProjectAssetId, x.ConcurrencyToken))
            .ToArray();

        var result = await service.ConfirmSceneAssetsAsync(
            seeded.Project.ProjectId,
            seeded.Scene.SceneId,
            new ConfirmSceneProjectAssetsRequest(inputs),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(2, result.LockedAssets);
        Assert.False(result.Assignment.HasUnlockedAssets);
        Assert.All(
            await dbContext.ProjectAssets.Where(x => assignedIds.Contains(x.ProjectAssetId)).ToListAsync(),
            asset => Assert.Equal(ProjectAssetStatuses.Locked, asset.Status));
        Assert.Equal(ProjectAssetStatuses.Draft, (await dbContext.ProjectAssets.FindAsync(unassigned.ProjectAssetId))!.Status);
        Assert.Equal(2, await dbContext.ProjectAssetVersions.CountAsync());
        Assert.Empty(dbContext.ProviderRequests);
    }

    [Fact]
    public async Task ConfirmSceneAssets_InvalidSelectionDoesNotLockAnyAsset()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext);
        var now = DateTime.UtcNow;
        var prop = NewAiAsset(seeded.Project.ProjectId, ProjectAssetTypes.Prop, "bottle", "Clear bottle", now);
        dbContext.ProjectAssets.Add(prop);
        dbContext.SceneAssetAssignments.Add(NewAssignment(seeded.Scene.SceneId, prop.ProjectAssetId, now));
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, seeded.Project, "Member");
        var library = await service.GetLibraryAsync(
            seeded.Project.ProjectId,
            seeded.Project.OrganizationId,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var asset = Assert.Single(library.Assets);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => service.ConfirmSceneAssetsAsync(
            seeded.Project.ProjectId,
            seeded.Scene.SceneId,
            new ConfirmSceneProjectAssetsRequest([
                new ApproveProjectAssetInput(asset.ProjectAssetId, asset.ConcurrencyToken)
            ]),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal("scene_asset_background_invalid", exception.Code);
        Assert.Equal(ProjectAssetStatuses.Draft, (await dbContext.ProjectAssets.FindAsync(prop.ProjectAssetId))!.Status);
        Assert.Empty(dbContext.ProjectAssetVersions);
    }

    [Fact]
    public async Task Materialize_CreatesAiDraftAndSceneAssignmentIdempotently()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext);
        var providerRequestId = Guid.NewGuid();
        var generatedScene = new GeneratedContentScene(
            1,
            "Hook",
            "Hello",
            "Presenter in the same bright room.",
            5,
            [],
            AssetKeys: ["bright-room"]);
        var response = new GeneratedContentResponse(
            providerRequestId,
            "openai",
            "test-model",
            10,
            20,
            new GeneratedContentPlan(
                "Title",
                "Hook",
                "Angle",
                "Audience",
                "CTA",
                "Script",
                "Natural",
                "watermark",
                [],
                [generatedScene],
                [new GeneratedProjectAsset(
                    "bright-room",
                    ProjectAssetTypes.Background,
                    "Phòng sáng",
                    "Phòng sáng tự nhiên với cửa sổ lớn cố định bên trái.",
                    [1])]));
        dbContext.ProviderRequests.Add(new ProviderRequest
        {
            ProviderRequestId = providerRequestId,
            OrganizationId = seeded.Project.OrganizationId,
            ProjectId = seeded.Project.ProjectId,
            RequestKind = "Text",
            ProviderCode = "openai",
            ModelCode = "test-model",
            IdempotencyKey = $"content:{seeded.Project.ProjectId:N}:v1",
            Status = "Completed",
            RequestJson = "{}",
            ResponseJson = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            CurrencyCode = "USD",
            RowVersion = new byte[8]
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, seeded.Project, "Member");
        var request = new MaterializeProjectAssetPlanRequest(providerRequestId, 1);

        var first = await service.MaterializeAsync(
            seeded.Project.ProjectId,
            request,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var second = await service.MaterializeAsync(
            seeded.Project.ProjectId,
            request,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        var asset = await dbContext.ProjectAssets.SingleAsync();
        Assert.Equal(ProjectAssetSourceKinds.AiGenerated, asset.SourceKind);
        Assert.Equal(ProjectAssetStatuses.Draft, asset.Status);
        Assert.Equal("bright-room", asset.AssetKey);
        Assert.Equal(1, first.CreatedAssets);
        Assert.Equal(0, second.CreatedAssets);
        Assert.Equal(1, second.PreservedAssets);
        Assert.Equal(1, await dbContext.SceneAssetAssignments.CountAsync());
    }

    private static ProjectAssetService CreateService(
        VideoFactoryDbContext dbContext,
        Project project,
        string role) =>
        new(
            dbContext,
            new StubAccessService(new GenerationAccessContext(
                project.OrganizationId!.Value,
                "Test organization",
                role,
                project)),
            TimeProvider.System);

    private static ProjectAsset NewAiAsset(
        Guid projectId,
        string assetType,
        string assetKey,
        string name,
        DateTime now) => new()
    {
        ProjectAssetId = Guid.NewGuid(),
        ProjectId = projectId,
        AssetKey = assetKey,
        AssetType = assetType,
        Name = name,
        CanonicalDescription = $"A visually consistent {name.ToLowerInvariant()}.",
        Status = ProjectAssetStatuses.Draft,
        SourceKind = ProjectAssetSourceKinds.AiGenerated,
        SourcePlanVersion = 1,
        CurrentVersion = 0,
        CreatedAtUtc = now,
        CreatedByUserId = "user-1",
        UpdatedAtUtc = now,
        UpdatedByUserId = "user-1",
        RowVersion = new byte[8]
    };

    private static SceneAssetAssignment NewAssignment(Guid sceneId, Guid assetId, DateTime now) => new()
    {
        SceneId = sceneId,
        ProjectAssetId = assetId,
        AssignedByUserId = "user-1",
        AssignedAtUtc = now
    };

    private static VideoFactoryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"project-asset-library-{Guid.NewGuid():N}")
            .Options);

    private static SeededProject SeedProject(
        VideoFactoryDbContext dbContext,
        string? structureType = null,
        bool klingSnapshot = false)
    {
        var now = DateTime.UtcNow;
        var scriptId = Guid.NewGuid();
        var project = new Project
        {
            ProjectId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            RemoteUserId = "user-1",
            CreatedByUserId = "user-1",
            Name = "Asset library test",
            Topic = "Test",
            LanguageCode = "vi-VN",
            Platform = "YouTube",
            AspectRatio = "16:9",
            TargetDurationSeconds = 5,
            OutputWidth = 1280,
            OutputHeight = 720,
            OutputFrameRate = 25,
            Status = "ScenePlanning",
            CurrentScriptVersion = structureType is null ? null : 1,
            CurrentScenePlanVersion = 1,
            VideoProviderCode = klingSnapshot ? ProviderCodes.Kling : null,
            VideoModelCode = klingSnapshot ? "kling-3.0" : null,
            VideoPolicyVersion = klingSnapshot ? 1 : null,
            VideoResolution = klingSnapshot ? "720p" : null,
            VideoNativeAudio = klingSnapshot,
            CurrencyCode = "USD",
            WorkspaceRelativePath = "test",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var scene = new Scene
        {
            SceneId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            ScriptId = scriptId,
            StyleProfileId = Guid.NewGuid(),
            ScenePlanVersion = 1,
            SequenceNumber = 1,
            StoryPurpose = structureType == "OpenAiStructuredPlan" ? "Cảnh mở đầu" : "Hook",
            VisualDescription = structureType == "OpenAiStructuredPlan" ? "Cảnh thử nghiệm trong căn phòng sáng." : "Test scene",
            ContentDurationMs = 5000,
            GenerationDurationMs = 5000,
            TimelineEndMs = 5000,
            EntryStateJson = "{}",
            ExitStateJson = "{}",
            Status = "PromptReady",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
        dbContext.AddRange(project, scene);
        if (structureType is not null)
        {
            dbContext.Scripts.Add(new Script
            {
                ScriptId = scriptId,
                ProjectId = project.ProjectId,
                Version = 1,
                StructureType = structureType,
                FullText = structureType == "OpenAiStructuredPlan" ? "Kịch bản thử nghiệm bằng tiếng Việt." : "Test script",
                StoryBeatsJson = "[]",
                Status = "Approved",
                CreatedAtUtc = now,
                RowVersion = new byte[8]
            });
        }
        dbContext.SaveChanges();
        return new SeededProject(project, scene);
    }

    private sealed record SeededProject(Project Project, Scene Scene);

    private sealed class StubAccessService(GenerationAccessContext context) : IGenerationAccessService
    {
        public Task<GenerationAccessContext> RequireAsync(
            string userId,
            Guid deviceId,
            Guid? requestedOrganizationId,
            Guid? projectId,
            CancellationToken cancellationToken) => Task.FromResult(context);
    }
}
