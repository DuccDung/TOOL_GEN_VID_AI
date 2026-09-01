using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Models;
using TOOL_SERVER.Organizations;
using TOOL_SHARED.Contracts.Generation;
using TOOL_SHARED.Contracts.Projects;

namespace TOOL_TESTS.Generation;

public sealed class GenerationServiceKlingNativeAudioTests
{
    [Fact]
    public async Task SubmitKling_LongFormEnglishContentIsBlockedBeforeResolverBudgetAndOutbound()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext);
        seeded.Script.StructureType = GenerationWorkflowTypes.OpenAiStructuredPlan;
        await dbContext.SaveChangesAsync();
        var fixture = CreateService(dbContext, seeded.Project);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            fixture.Service.SubmitKlingVideoAsync(
                CreateRequest(seeded, scenePlanVersion: 1, scenePromptVersion: 1),
                "user-1",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("kling_prompt_language_invalid", exception.Code);
        Assert.Equal(0, fixture.Resolver.ResolveCount);
        Assert.Equal(0, fixture.Budget.ReserveCount);
        Assert.Equal(0, fixture.Kling.CallCount);
        Assert.Empty(dbContext.ProviderRequests);
    }

    [Fact]
    public async Task SubmitKling_RejectsStaleScenePlanBeforeResolverBudgetAndOutbound()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext);
        seeded.Project.CurrentScenePlanVersion = 2;
        await dbContext.SaveChangesAsync();
        var fixture = CreateService(dbContext, seeded.Project);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            fixture.Service.SubmitKlingVideoAsync(
                CreateRequest(seeded, scenePlanVersion: 1, scenePromptVersion: 1),
                "user-1",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("scene_plan_changed", exception.Code);
        Assert.Equal(0, fixture.Resolver.ResolveCount);
        Assert.Equal(0, fixture.Budget.ReserveCount);
        Assert.Equal(0, fixture.Kling.CallCount);
        Assert.Empty(dbContext.ProviderRequests);
    }

    [Fact]
    public async Task SubmitKling_IdempotentReplayUsesSnapshotAndDoesNotChargeOrSubmitTwice()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext);
        var fixture = CreateService(dbContext, seeded.Project);
        var request = CreateRequest(seeded, scenePlanVersion: 1, scenePromptVersion: 1);

        var first = await fixture.Service.SubmitKlingVideoAsync(
            request,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var replay = await fixture.Service.SubmitKlingVideoAsync(
            request,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(first.ProviderRequestId, replay.ProviderRequestId);
        Assert.Equal(1, fixture.Kling.CallCount);
        Assert.Equal(1, fixture.Budget.ReserveCount);
        var requestLog = await dbContext.ProviderRequests.SingleAsync();
        using var snapshot = JsonDocument.Parse(requestLog.RequestJson);
        var root = snapshot.RootElement;
        Assert.Equal(seeded.Project.OrganizationId, root.GetProperty("organizationId").GetGuid());
        Assert.Equal("user-1", root.GetProperty("userId").GetString());
        Assert.Equal(seeded.Prompt.ScenePromptId, root.GetProperty("scenePromptId").GetGuid());
        Assert.Equal(1, root.GetProperty("scenePlanVersion").GetInt32());
        Assert.Equal(1, root.GetProperty("scenePromptVersion").GetInt32());
        Assert.Equal("kling-3.0", root.GetProperty("modelCode").GetString());
        Assert.DoesNotContain(seeded.Prompt.FinalPrompt, requestLog.RequestJson, StringComparison.Ordinal);
        Assert.DoesNotContain(seeded.Scene.Narration!, requestLog.RequestJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitKling_SameKeyAfterPromptVersionChangesReturnsIdempotencyConflict()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext);
        var fixture = CreateService(dbContext, seeded.Project);
        var request = CreateRequest(seeded, scenePlanVersion: 1, scenePromptVersion: 1);
        await fixture.Service.SubmitKlingVideoAsync(
            request,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        seeded.Prompt.Status = "Superseded";
        dbContext.ScenePrompts.Add(new ScenePrompt
        {
            ScenePromptId = Guid.NewGuid(),
            SceneId = seeded.Scene.SceneId,
            Version = 2,
            PromptTemplateName = "kling-native-audio",
            PromptTemplateVersion = "2",
            CanonicalInputJson = "{}",
            FinalPrompt = "A visibly different approved scene prompt.",
            PromptHash = new string('b', 64),
            Status = "Approved",
            CreatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8]
        });
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            fixture.Service.SubmitKlingVideoAsync(
                request with { ScenePromptVersion = 2 },
                "user-1",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("idempotency_key_conflict", exception.Code);
        Assert.Equal(1, fixture.Kling.CallCount);
        Assert.Equal(1, fixture.Budget.ReserveCount);
    }

    [Fact]
    public async Task SubmitKling_BudgetFailureStopsBeforeOutbound()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext);
        var fixture = CreateService(dbContext, seeded.Project, "organization_budget_exceeded");

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            fixture.Service.SubmitKlingVideoAsync(
                CreateRequest(seeded, scenePlanVersion: 1, scenePromptVersion: 1),
                "user-1",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("organization_budget_exceeded", exception.Code);
        Assert.Equal(0, fixture.Kling.CallCount);
        Assert.Empty(dbContext.ProviderRequests);
    }

    [Fact]
    public async Task SubmitKling_AppliesOnlyLockedSceneAssetsAndPersistsVersionSnapshot()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext);
        var asset = SeedProjectAsset(dbContext, seeded, ProjectAssetStatuses.Locked);
        var fixture = CreateService(dbContext, seeded.Project);

        await fixture.Service.SubmitKlingVideoAsync(
            CreateRequest(seeded, scenePlanVersion: 1, scenePromptVersion: 1),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        var effectivePrompt = Assert.IsType<string>(fixture.Kling.LastPrompt);
        Assert.Contains("BACKGROUND CONTINUITY LOCK", effectivePrompt);
        Assert.Contains("Căn bếp nhà Minh", effectivePrompt);
        Assert.Contains("cửa sổ luôn nằm bên trái", effectivePrompt);
        var snapshot = await dbContext.ProviderRequestAssetVersions.SingleAsync();
        Assert.Equal(asset.Version.ProjectAssetVersionId, snapshot.ProjectAssetVersionId);
        var requestLog = await dbContext.ProviderRequests.SingleAsync();
        Assert.Contains(asset.Asset.ProjectAssetId.ToString(), requestLog.RequestJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(asset.Version.CanonicalDescription, requestLog.RequestJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitKling_AssignedDraftAssetStopsBeforeResolverBudgetAndOutbound()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext);
        SeedProjectAsset(dbContext, seeded, ProjectAssetStatuses.Draft);
        var fixture = CreateService(dbContext, seeded.Project);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            fixture.Service.SubmitKlingVideoAsync(
                CreateRequest(seeded, scenePlanVersion: 1, scenePromptVersion: 1),
                "user-1",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("scene_asset_not_locked", exception.Code);
        Assert.Equal(0, fixture.Resolver.ResolveCount);
        Assert.Equal(0, fixture.Budget.ReserveCount);
        Assert.Equal(0, fixture.Kling.CallCount);
        Assert.Empty(dbContext.ProviderRequests);
    }

    [Fact]
    public async Task SubmitKling_ImmediateCompletionCachesOutputWithoutPersistingSignedUrl()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext);
        var fixture = CreateService(dbContext, seeded.Project, completeImmediately: true);

        var response = await fixture.Service.SubmitKlingVideoAsync(
            CreateRequest(seeded, scenePlanVersion: 1, scenePromptVersion: 1),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("Completed", response.Status);
        Assert.Equal($"/api/generation/kling/videos/{response.ProviderRequestId:D}/content", response.OutputUrl);
        Assert.Equal(1, fixture.OutputStore.CacheCount);
        Assert.Contains("media.kwaicdn.com", fixture.OutputStore.CachedUrl, StringComparison.OrdinalIgnoreCase);
        var requestLog = await dbContext.ProviderRequests.SingleAsync();
        Assert.DoesNotContain("media.kwaicdn.com", requestLog.ResponseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signature", requestLog.ResponseJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitKling_CacheFailureLeavesTaskForWorkerAndDoesNotReleaseReservation()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext);
        var fixture = CreateService(
            dbContext,
            seeded.Project,
            completeImmediately: true,
            outputCacheFailure: true);

        await Assert.ThrowsAsync<AccountApiException>(() => fixture.Service.SubmitKlingVideoAsync(
            CreateRequest(seeded, scenePlanVersion: 1, scenePromptVersion: 1),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        var requestLog = await dbContext.ProviderRequests.SingleAsync();
        Assert.Equal("Processing", requestLog.Status);
        Assert.Equal("provider_output_download_failed", requestLog.ErrorCode);
        Assert.NotNull(requestLog.NextPollAtUtc);
        Assert.Null(requestLog.ResponseJson);
        Assert.Equal(0, fixture.Budget.ReleaseCount);
    }

    private static SubmitKlingVideoRequest CreateRequest(
        SeededProject seeded,
        int scenePlanVersion,
        int scenePromptVersion) =>
        new(
            seeded.Project.ProjectId,
            seeded.Scene.SceneId,
            "desktop prompt is not trusted",
            5,
            "16:9",
            "720p",
            true,
            "kling-native-audio-idempotency",
            seeded.Project.OrganizationId,
            ScenePlanVersion: scenePlanVersion,
            ScenePromptVersion: scenePromptVersion);

    private static ServiceFixture CreateService(
        VideoFactoryDbContext dbContext,
        Project project,
        string? budgetFailureCode = null,
        bool completeImmediately = false,
        bool outputCacheFailure = false)
    {
        var resolver = new StubProviderResolver();
        var kling = new StubKlingClient(completeImmediately);
        var budget = new StubBudgetService(budgetFailureCode);
        var outputStore = new StubVideoOutputStore(outputCacheFailure);
        var service = new GenerationService(
            dbContext,
            resolver,
            new UnusedContentClient(),
            new UnusedImageClient(),
            new UnusedSpeechClient(),
            kling,
            new StubAccessService(new GenerationAccessContext(
                project.OrganizationId!.Value,
                "Test organization",
                "Member",
                project)),
            budget,
            new StubCostEstimator(),
            NullLogger<GenerationService>.Instance,
            TimeProvider.System,
            Options.Create(new OpenAiImageOptions()),
            Options.Create(new OpenAiSpeechOptions()),
            null,
            null,
            outputStore);
        return new ServiceFixture(service, resolver, kling, budget, outputStore);
    }

    private static VideoFactoryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"kling-native-audio-{Guid.NewGuid():N}")
            .Options);

    private static SeededProject SeedProject(VideoFactoryDbContext dbContext)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            ProjectId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            RemoteUserId = "user-1",
            CreatedByUserId = "user-1",
            Name = "Kling native audio test",
            Topic = "Test",
            LanguageCode = "vi-VN",
            Platform = "YouTube",
            AspectRatio = "16:9",
            TargetDurationSeconds = 5,
            OutputWidth = 1280,
            OutputHeight = 720,
            OutputFrameRate = 25,
            Status = "GeneratingScenes",
            CurrentScenePlanVersion = 1,
            CurrencyCode = "USD",
            WorkspaceRelativePath = "test",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var script = new Script
        {
            ScriptId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            Version = 1,
            StructureType = GenerationWorkflowTypes.DirectShortVideo,
            FullText = "Test",
            StoryBeatsJson = "[]",
            Status = "Approved",
            CreatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var scene = new Scene
        {
            SceneId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            ScriptId = script.ScriptId,
            StyleProfileId = Guid.NewGuid(),
            ScenePlanVersion = 1,
            SequenceNumber = 1,
            StoryPurpose = "Hook",
            Narration = "Xin chào bạn.",
            VisualDescription = "Presenter in a quiet studio.",
            ContentDurationMs = 5000,
            GenerationDurationMs = 5000,
            TimelineEndMs = 5000,
            EntryStateJson = "{}",
            ExitStateJson = "{}",
            RequiredCapabilitiesJson = "{\"speechMode\":\"NativeVoiceOver\",\"voiceStyle\":\"clear\"}",
            Status = "PromptReady",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var prompt = new ScenePrompt
        {
            ScenePromptId = Guid.NewGuid(),
            SceneId = scene.SceneId,
            Version = 1,
            PromptTemplateName = "kling-native-audio",
            PromptTemplateVersion = "2",
            CanonicalInputJson = "{}",
            FinalPrompt = "Presenter looks toward camera in a quiet studio.",
            NegativePrompt = "watermark",
            PromptHash = new string('a', 64),
            Status = "Approved",
            CreatedAtUtc = now,
            RowVersion = new byte[8]
        };
        dbContext.AddRange(project, script, scene, prompt);
        dbContext.SaveChanges();
        return new SeededProject(project, script, scene, prompt);
    }

    private static SeededAsset SeedProjectAsset(
        VideoFactoryDbContext dbContext,
        SeededProject seeded,
        string status)
    {
        var now = DateTime.UtcNow;
        var asset = new ProjectAsset
        {
            ProjectAssetId = Guid.NewGuid(),
            ProjectId = seeded.Project.ProjectId,
            AssetType = ProjectAssetTypes.Background,
            Name = "Căn bếp nhà Minh",
            CanonicalDescription = "Tủ gỗ nâu, tường trắng, cửa sổ luôn nằm bên trái.",
            Status = status,
            CurrentVersion = status == ProjectAssetStatuses.Locked ? 1 : 0,
            LockedAtUtc = status == ProjectAssetStatuses.Locked ? now : null,
            CreatedAtUtc = now,
            CreatedByUserId = "user-1",
            UpdatedAtUtc = now,
            UpdatedByUserId = "user-1",
            RowVersion = new byte[8]
        };
        var version = new ProjectAssetVersion
        {
            ProjectAssetVersionId = Guid.NewGuid(),
            ProjectAssetId = asset.ProjectAssetId,
            Version = 1,
            AssetType = asset.AssetType,
            Name = asset.Name,
            CanonicalDescription = asset.CanonicalDescription,
            LockedAtUtc = now,
            LockedByUserId = "user-1"
        };
        var assignment = new SceneAssetAssignment
        {
            SceneId = seeded.Scene.SceneId,
            ProjectAssetId = asset.ProjectAssetId,
            AssignedByUserId = "user-1",
            AssignedAtUtc = now
        };
        dbContext.Add(asset);
        if (status == ProjectAssetStatuses.Locked)
        {
            dbContext.Add(version);
        }
        dbContext.Add(assignment);
        dbContext.SaveChanges();
        return new SeededAsset(asset, version);
    }

    private sealed record SeededProject(Project Project, Script Script, Scene Scene, ScenePrompt Prompt);
    private sealed record SeededAsset(ProjectAsset Asset, ProjectAssetVersion Version);
    private sealed record ServiceFixture(
        GenerationService Service,
        StubProviderResolver Resolver,
        StubKlingClient Kling,
        StubBudgetService Budget,
        StubVideoOutputStore OutputStore);

    private sealed class StubAccessService(GenerationAccessContext context) : IGenerationAccessService
    {
        public Task<GenerationAccessContext> RequireAsync(
            string userId,
            Guid deviceId,
            Guid? requestedOrganizationId,
            Guid? projectId,
            CancellationToken cancellationToken) => Task.FromResult(context);
    }

    private sealed class StubProviderResolver : IProviderRuntimeResolver
    {
        private readonly ProviderRuntimeConfiguration _provider = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ProviderCodes.Kling,
            "kling-3.0",
            new Uri("https://api-singapore.klingai.com/"),
            "Bearer",
            null,
            "test-key");

        public int ResolveCount { get; private set; }

        public Task<ProviderRuntimeConfiguration> ResolveAsync(
            Guid organizationId,
            string providerCode,
            string modality,
            Guid? credentialId,
            CancellationToken cancellationToken)
        {
            ResolveCount++;
            return Task.FromResult(_provider);
        }

        public Task<GenerationProviderStatusResponse> GetStatusAsync(
            Guid organizationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new GenerationProviderStatusResponse(true, "gpt-5.6-luna", true, "kling-3.0"));
    }

    private sealed class StubKlingClient(bool completeImmediately) : IKlingVideoClient
    {
        public int CallCount { get; private set; }
        public string? LastPrompt { get; private set; }

        public Task<KlingTaskResult> SubmitAsync(
            ProviderRuntimeConfiguration provider,
            string prompt,
            string aspectRatio,
            int durationSeconds,
            string resolution,
            bool nativeAudio,
            string externalTaskId,
            KlingReferenceImageData? referenceImage,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastPrompt = prompt;
            var outputUrl = completeImmediately
                ? "https://media.kwaicdn.com/video.mp4?signature=secret"
                : null;
            return Task.FromResult(new KlingTaskResult(
                "task-1",
                completeImmediately ? "Completed" : "Submitted",
                completeImmediately ? 100 : 5,
                outputUrl,
                null,
                null,
                completeImmediately ? 0.5m : null,
                JsonSerializer.Serialize(new { taskId = "task-1", status = completeImmediately ? "Completed" : "Submitted", outputUrl })));
        }

        public Task<KlingTaskResult> GetStatusAsync(
            ProviderRuntimeConfiguration provider,
            string externalRequestId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubCostEstimator : IAiCostEstimator
    {
        public Task<AiCostQuote> QuoteKlingAsync(
            Guid providerModelId,
            int durationSeconds,
            string resolution,
            bool nativeAudio,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiCostQuote(
                0.5m,
                "USD",
                "{\"usageType\":\"VideoSecond\",\"resolution\":\"720p\",\"nativeAudio\":true}",
                0,
                0));

        public Task<AiCostQuote> QuoteOpenAiAsync(Guid providerModelId, int topicCharacters, int targetDurationSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiCostQuote> QuoteOpenAiImageAsync(Guid providerModelId, int promptCharacters, long estimatedInputTokens, long estimatedOutputTokens, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiCostQuote> QuoteOpenAiVoiceAsync(Guid providerModelId, int narrationCharacters, decimal estimatedCharactersPerSecond, long estimatedOutputTokensPerSecond, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<decimal> CalculateOpenAiActualAsync(string rateSnapshotJson, long inputTokens, long outputTokens, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubBudgetService(string? failureCode) : IAiBudgetService
    {
        public int ReserveCount { get; private set; }
        public int ReleaseCount { get; private set; }

        public Task<BudgetSnapshot> GetSnapshotAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(new BudgetSnapshot(Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddMonths(1), 10, 0, 0, 10, "USD"));

        public Task<BudgetReservationResult> ReserveAsync(
            Guid organizationId,
            string userId,
            Guid projectId,
            Guid providerRequestId,
            string operationKey,
            string providerCode,
            string modelCode,
            decimal amount,
            CancellationToken cancellationToken)
        {
            ReserveCount++;
            return failureCode is null
                ? Task.FromResult(new BudgetReservationResult(Guid.NewGuid(), Guid.NewGuid(), amount, "USD"))
                : Task.FromException<BudgetReservationResult>(new AccountApiException(409, failureCode, "blocked"));
        }

        public Task SettleAsync(Guid reservationId, decimal actualAmount, Guid? organizationProviderCredentialId, object? usage, object? rateSnapshot, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken)
        {
            ReleaseCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubVideoOutputStore(bool failCache) : IVideoOutputStore
    {
        public int CacheCount { get; private set; }
        public string? CachedUrl { get; private set; }

        public Task CacheAsync(Guid providerRequestId, string outputUrl, CancellationToken cancellationToken)
        {
            CacheCount++;
            CachedUrl = outputUrl;
            return failCache
                ? Task.FromException(new IOException("test cache failure"))
                : Task.CompletedTask;
        }

        public Task CopyToResponseAsync(
            HttpContext httpContext,
            Guid providerRequestId,
            string userId,
            Guid deviceId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    private sealed class UnusedContentClient : IOpenAiContentClient
    {
        public Task<OpenAiContentResult> GenerateAsync(ProviderRuntimeConfiguration provider, string topic, string languageCode, string platform, string aspectRatio, int targetDurationSeconds, string safetyIdentifier, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedImageClient : IOpenAiImageClient
    {
        public Task<OpenAiImageResult> GenerateAsync(ProviderRuntimeConfiguration provider, string prompt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedSpeechClient : IOpenAiSpeechClient
    {
        public Task<OpenAiSpeechResult> GenerateAsync(ProviderRuntimeConfiguration provider, string narration, string providerVoiceCode, string instructions, decimal speakingRate, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
