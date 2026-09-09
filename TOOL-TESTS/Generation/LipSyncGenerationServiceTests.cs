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

namespace TOOL_TESTS.Generation;

public sealed class LipSyncGenerationServiceTests
{
    [Fact]
    public async Task CreateInputSession_IsIdempotentAndSnapshotsApprovedLineage()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedApprovedInputs(dbContext);
        var fixture = CreateService(dbContext, seeded);
        var request = CreateSessionRequest(seeded);

        var first = await fixture.Service.CreateInputSessionAsync(
            request,
            seeded.UserId,
            Guid.NewGuid(),
            CancellationToken.None);
        var second = await fixture.Service.CreateInputSessionAsync(
            request,
            seeded.UserId,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(first.LipSyncInputSessionId, second.LipSyncInputSessionId);
        Assert.Equal(LipSyncStatuses.Uploading, first.Status);
        Assert.Single(dbContext.LipSyncInputSessions);
        Assert.Equal(2, fixture.ProviderResolver.ResolveCount);
        Assert.Equal(0, fixture.Budget.ReserveCount);
        Assert.Equal(ProviderCodes.Fal, seeded.Project.LipSyncProviderCode);
        Assert.Equal(FalLipSyncPolicy.EndpointId, seeded.Project.LipSyncModelCode);
        Assert.Equal(FalLipSyncPolicy.PolicyVersion, seeded.Project.LipSyncPolicyVersion);
        Assert.NotNull(seeded.Project.LipSyncSnapshotAtUtc);
    }

    [Fact]
    public async Task Submit_SameIdempotencyKey_ReusesTaskWithoutSecondReservationOrProviderCall()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedApprovedInputs(dbContext);
        var fixture = CreateService(
            dbContext,
            seeded,
            providerResult: ProviderResult(LipSyncStatuses.Submitted));
        var session = await CreateReadySessionAsync(fixture, seeded);
        var request = new SubmitLipSyncRequest(
            seeded.Project.ProjectId,
            seeded.Scene.SceneId,
            session.LipSyncInputSessionId,
            "lip-sync-idempotent",
            seeded.OrganizationId);

        var first = await fixture.Service.SubmitAsync(
            request,
            seeded.UserId,
            Guid.NewGuid(),
            CancellationToken.None);
        var second = await fixture.Service.SubmitAsync(
            request,
            seeded.UserId,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(first.ProviderRequestId, second.ProviderRequestId);
        Assert.Equal(first.LipSyncGenerationId, second.LipSyncGenerationId);
        Assert.Equal(1, fixture.Budget.ReserveCount);
        Assert.Equal(1, fixture.ProviderClient.SubmitCount);
        Assert.Single(dbContext.LipSyncGenerations);
        Assert.Single(dbContext.ProviderRequests.Where(x => x.RequestKind == "LipSync"));
        var persisted = await dbContext.ProviderRequests.SingleAsync(x => x.RequestKind == "LipSync");
        Assert.DoesNotContain("signed-video-token", persisted.RequestJson, StringComparison.Ordinal);
        Assert.DoesNotContain("signed-audio-token", persisted.RequestJson, StringComparison.Ordinal);
        Assert.DoesNotContain("https://inputs.example.com", persisted.RequestJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompletedTask_MaterializeAndApprove_PinsExactOutputAndSettlesOnce()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedApprovedInputs(dbContext);
        var fixture = CreateService(
            dbContext,
            seeded,
            providerResult: ProviderResult(
                LipSyncStatuses.Completed,
                outputUrl: "https://v3.fal.media/files/output.mp4"));
        var session = await CreateReadySessionAsync(fixture, seeded);

        var completed = await fixture.Service.SubmitAsync(
            new SubmitLipSyncRequest(
                seeded.Project.ProjectId,
                seeded.Scene.SceneId,
                session.LipSyncInputSessionId,
                "lip-sync-completed",
                seeded.OrganizationId),
            seeded.UserId,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(LipSyncStatuses.Completed, completed.Status);
        Assert.Equal($"/api/generation/lip-sync/{completed.ProviderRequestId:D}/content", completed.OutputUrl);
        Assert.Equal(1, fixture.OutputStore.CacheCount);
        Assert.Equal(1, fixture.Budget.SettleCount);
        Assert.Equal(0, fixture.Budget.ReleaseCount);
        var providerRequest = await dbContext.ProviderRequests.SingleAsync(x => x.ProviderRequestId == completed.ProviderRequestId);
        Assert.Equal(providerRequest.EstimatedCost, providerRequest.ActualCost);
        Assert.DoesNotContain("v3.fal.media", providerRequest.ResponseJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var outputAsset = new MediaAsset
        {
            MediaAssetId = Guid.NewGuid(),
            ProjectId = seeded.Project.ProjectId,
            SceneId = seeded.Scene.SceneId,
            AssetType = "SceneVideoLipSynced",
            RelativePath = "scenes/001/lip-sync-final.mp4",
            MimeType = "video/mp4",
            SizeBytes = 2048,
            Sha256 = Hash('e'),
            DurationMs = seeded.DurationMs,
            Status = "Ready",
            SourceType = "Generated",
            SourceProviderCode = ProviderCodes.Fal,
            SourceProviderRequestId = completed.ProviderRequestId,
            CreatedAtUtc = DateTime.UtcNow,
            VerifiedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8]
        };
        dbContext.MediaAssets.Add(outputAsset);
        await dbContext.SaveChangesAsync();

        var reviewRequired = await fixture.Service.MaterializeAsync(
            new MaterializeLipSyncOutputRequest(
                seeded.Project.ProjectId,
                seeded.Scene.SceneId,
                completed.LipSyncGenerationId,
                outputAsset.MediaAssetId,
                outputAsset.Sha256,
                seeded.DurationMs,
                seeded.OrganizationId),
            seeded.UserId,
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.Equal(LipSyncStatuses.ReviewRequired, reviewRequired.Status);
        Assert.Equal("AudioReviewRequired", seeded.Scene.Status);

        var approved = await fixture.Service.ApproveAsync(
            new ReviewLipSyncOutputRequest(
                seeded.Project.ProjectId,
                seeded.Scene.SceneId,
                completed.LipSyncGenerationId,
                reviewRequired.RowVersion!,
                true,
                seeded.OrganizationId),
            seeded.UserId,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(LipSyncStatuses.Approved, approved.Status);
        Assert.Equal(completed.LipSyncGenerationId, seeded.Scene.ApprovedLipSyncGenerationId);
        Assert.Equal(outputAsset.MediaAssetId, seeded.Scene.ApprovedRenderMediaAssetId);
        Assert.Equal(SceneSpeechStatuses.SpeechApproved, seeded.Scene.SpeechStatus);
        Assert.Equal("Approved", seeded.Scene.Status);
    }

    [Fact]
    public async Task Submit_WhenApprovedSourceChanges_BlocksBeforeReservationAndProviderCall()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedApprovedInputs(dbContext);
        var fixture = CreateService(dbContext, seeded);
        var session = await CreateReadySessionAsync(fixture, seeded);
        seeded.VideoAsset.Sha256 = Hash('f');
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => fixture.Service.SubmitAsync(
            new SubmitLipSyncRequest(
                seeded.Project.ProjectId,
                seeded.Scene.SceneId,
                session.LipSyncInputSessionId,
                "lip-sync-stale",
                seeded.OrganizationId),
            seeded.UserId,
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal(LipSyncErrorCodes.InputChanged, exception.Code);
        Assert.Equal(0, fixture.Budget.ReserveCount);
        Assert.Equal(0, fixture.ProviderClient.SubmitCount);
        Assert.Empty(dbContext.ProviderRequests.Where(x => x.RequestKind == "LipSync"));
    }

    [Fact]
    public async Task GetStatus_DifferentUser_IsHiddenAsNotFound()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedApprovedInputs(dbContext);
        var fixture = CreateService(
            dbContext,
            seeded,
            providerResult: ProviderResult(LipSyncStatuses.Submitted));
        var session = await CreateReadySessionAsync(fixture, seeded);
        var submitted = await fixture.Service.SubmitAsync(
            new SubmitLipSyncRequest(
                seeded.Project.ProjectId,
                seeded.Scene.SceneId,
                session.LipSyncInputSessionId,
                "lip-sync-owner",
                seeded.OrganizationId),
            seeded.UserId,
            Guid.NewGuid(),
            CancellationToken.None);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => fixture.Service.GetStatusAsync(
            submitted.ProviderRequestId,
            "other-user",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.Equal("lip_sync_not_found", exception.Code);
    }

    private static async Task<LipSyncInputSessionResponse> CreateReadySessionAsync(
        Fixture fixture,
        SeededInputs seeded)
    {
        var session = await fixture.Service.CreateInputSessionAsync(
            CreateSessionRequest(seeded),
            seeded.UserId,
            Guid.NewGuid(),
            CancellationToken.None);
        var entity = await fixture.DbContext.LipSyncInputSessions.SingleAsync(
            x => x.LipSyncInputSessionId == session.LipSyncInputSessionId);
        entity.VideoStorageKey = "inputs/video.mp4";
        entity.AudioStorageKey = "inputs/audio.wav";
        entity.VideoSizeBytes = 1024;
        entity.AudioSizeBytes = 512;
        entity.Status = LipSyncStatuses.Ready;
        await fixture.DbContext.SaveChangesAsync();
        return session;
    }

    private static CreateLipSyncInputSessionRequest CreateSessionRequest(SeededInputs seeded) =>
        new(
            seeded.Project.ProjectId,
            seeded.Scene.SceneId,
            1,
            seeded.VideoGeneration.VideoGenerationId,
            seeded.VoiceGeneration.VoiceGenerationId,
            seeded.VideoAsset.Sha256,
            seeded.VoiceAsset.Sha256,
            Hash('c'),
            Hash('d'),
            seeded.DurationMs,
            seeded.OrganizationId);

    private static Fixture CreateService(
        VideoFactoryDbContext dbContext,
        SeededInputs seeded,
        LipSyncProviderTaskResult? providerResult = null)
    {
        var providerResolver = new StubProviderResolver();
        var budget = new StubBudgetService();
        var providerClient = new StubLipSyncProviderClient(providerResult ?? ProviderResult(LipSyncStatuses.Submitted));
        var outputStore = new StubVideoOutputStore();
        var service = new LipSyncGenerationService(
            dbContext,
            new StubAccessService(seeded),
            providerResolver,
            new StubCostEstimator(),
            budget,
            new StubInputStore(),
            new StubLipSyncProviderRouter(providerClient),
            outputStore,
            Options.Create(new LipSyncOptions
            {
                Enabled = true,
                PublicBaseUrl = "https://inputs.example.com/"
            }),
            TimeProvider.System,
            NullLogger<LipSyncGenerationService>.Instance);
        return new Fixture(service, dbContext, providerResolver, budget, providerClient, outputStore);
    }

    private static LipSyncProviderTaskResult ProviderResult(string status, string? outputUrl = null) =>
        new(
            "fal-request-1",
            status,
            status == LipSyncStatuses.Completed ? 100m : 5m,
            outputUrl,
            null,
            null,
            status == LipSyncStatuses.Completed ? 4 : null,
            "{\"status\":\"safe\"}");

    private static VideoFactoryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"lip-sync-service-{Guid.NewGuid():N}")
            .Options);

    private static SeededInputs SeedApprovedInputs(VideoFactoryDbContext dbContext)
    {
        const string userId = "user-1";
        const long durationMs = 4_000;
        var now = DateTime.UtcNow;
        var organizationId = Guid.NewGuid();
        var project = new Project
        {
            ProjectId = Guid.NewGuid(),
            OrganizationId = organizationId,
            CreatedByUserId = userId,
            RemoteUserId = userId,
            Name = "Lip-sync service test",
            Topic = "Người dẫn nói trực diện",
            LanguageCode = "vi-VN",
            SpeechProductionPolicy = SpeechProductionPolicies.CanonicalVoice,
            VideoProviderCode = ProviderCodes.Kling,
            VideoModelCode = "kling-3.0",
            VideoPolicyVersion = 1,
            VideoResolution = "720p",
            VideoNativeAudio = true,
            Platform = "YouTube",
            AspectRatio = "16:9",
            TargetDurationSeconds = 4,
            OutputWidth = 1280,
            OutputHeight = 720,
            OutputFrameRate = 25,
            Status = "ScenePlanning",
            CurrentScriptVersion = 1,
            CurrentScenePlanVersion = 1,
            CurrencyCode = "USD",
            WorkspaceRelativePath = "lip-sync-test",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var script = new Script
        {
            ScriptId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            Version = 1,
            StructureType = GenerationWorkflowTypes.OpenAiStructuredPlan,
            FullText = "Xin chào mọi người.",
            StoryBeatsJson = "[]",
            Status = "Approved",
            CreatedAtUtc = now,
            ApprovedAtUtc = now,
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
            StoryPurpose = "Mở đầu",
            Dialogue = "Xin chào mọi người.",
            VisualDescription = "Người dẫn nhìn vào máy quay.",
            ContentDurationMs = durationMs,
            GenerationDurationMs = durationMs,
            TimelineEndMs = durationMs,
            EntryStateJson = "{}",
            ExitStateJson = "{}",
            RequiredCapabilitiesJson = "{\"speechMode\":\"OnCameraDialogue\"}",
            Status = "Generated",
            SpeechStatus = SceneSpeechStatuses.SpeechReadyForLipSync,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var videoRequest = ProviderRequestFor(project, scene, userId, "Video", ProviderCodes.Kling, "kling-3.0", now);
        var voiceRequest = ProviderRequestFor(project, scene, userId, "Voice", ProviderCodes.OpenAi, "gpt-4o-mini-tts", now);
        var videoAsset = MediaAssetFor(project, scene, "SceneVideo", "video/mp4", Hash('a'), durationMs, videoRequest.ProviderRequestId, now);
        var voiceAsset = MediaAssetFor(project, scene, "SceneVoice", "audio/wav", Hash('b'), durationMs, voiceRequest.ProviderRequestId, now);
        var videoGeneration = new VideoGeneration
        {
            VideoGenerationId = Guid.NewGuid(),
            SceneId = scene.SceneId,
            ScenePromptId = Guid.NewGuid(),
            ProviderRequestId = videoRequest.ProviderRequestId,
            AttemptNumber = 1,
            Status = "Approved",
            RequestedDurationMs = durationMs,
            ActualDurationMs = durationMs,
            OutputMediaAssetId = videoAsset.MediaAssetId,
            CreatedAtUtc = now,
            CompletedAtUtc = now,
            RowVersion = new byte[8]
        };
        var voiceGeneration = new VoiceGeneration
        {
            VoiceGenerationId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            ScriptId = script.ScriptId,
            SceneId = scene.SceneId,
            ScenePlanVersion = 1,
            ProviderRequestId = voiceRequest.ProviderRequestId,
            Version = 1,
            VoiceCode = "coral",
            ProviderVoiceCode = "coral",
            NarrationHash = Hash('9'),
            VoiceSnapshotHash = Hash('8'),
            VerificationStatus = SpeechVerificationStatuses.NotRequested,
            LanguageCode = "vi-VN",
            SpeakingRate = 1m,
            Status = "Approved",
            DurationMs = durationMs,
            OutputMediaAssetId = voiceAsset.MediaAssetId,
            CreatedAtUtc = now,
            CompletedAtUtc = now,
            ApprovedAtUtc = now,
            RowVersion = new byte[8]
        };
        scene.ApprovedGenerationId = videoGeneration.VideoGenerationId;
        scene.ApprovedVoiceGenerationId = voiceGeneration.VoiceGenerationId;
        dbContext.AddRange(
            project,
            script,
            scene,
            videoRequest,
            voiceRequest,
            videoAsset,
            voiceAsset,
            videoGeneration,
            voiceGeneration);
        dbContext.SaveChanges();
        return new SeededInputs(
            organizationId,
            userId,
            durationMs,
            project,
            scene,
            videoGeneration,
            voiceGeneration,
            videoAsset,
            voiceAsset);
    }

    private static ProviderRequest ProviderRequestFor(
        Project project,
        Scene scene,
        string userId,
        string kind,
        string providerCode,
        string modelCode,
        DateTime now) => new()
        {
            ProviderRequestId = Guid.NewGuid(),
            OrganizationId = project.OrganizationId,
            RequestedByUserId = userId,
            ProjectId = project.ProjectId,
            SceneId = scene.SceneId,
            RequestKind = kind,
            ProviderCode = providerCode,
            ModelCode = modelCode,
            IdempotencyKey = $"{kind.ToLowerInvariant()}-{Guid.NewGuid():N}",
            Status = "Completed",
            RequestJson = "{}",
            CurrencyCode = "USD",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CompletedAtUtc = now,
            RowVersion = new byte[8]
        };

    private static MediaAsset MediaAssetFor(
        Project project,
        Scene scene,
        string assetType,
        string mimeType,
        string sha256,
        long durationMs,
        Guid sourceProviderRequestId,
        DateTime now) => new()
        {
            MediaAssetId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            SceneId = scene.SceneId,
            AssetType = assetType,
            RelativePath = $"scenes/001/{assetType.ToLowerInvariant()}",
            MimeType = mimeType,
            SizeBytes = 1024,
            Sha256 = sha256,
            DurationMs = durationMs,
            Status = "Ready",
            SourceType = "Generated",
            SourceProviderRequestId = sourceProviderRequestId,
            CreatedAtUtc = now,
            VerifiedAtUtc = now,
            RowVersion = new byte[8]
        };

    private static string Hash(char value) => new(value, 64);

    private sealed record SeededInputs(
        Guid OrganizationId,
        string UserId,
        long DurationMs,
        Project Project,
        Scene Scene,
        VideoGeneration VideoGeneration,
        VoiceGeneration VoiceGeneration,
        MediaAsset VideoAsset,
        MediaAsset VoiceAsset);

    private sealed record Fixture(
        LipSyncGenerationService Service,
        VideoFactoryDbContext DbContext,
        StubProviderResolver ProviderResolver,
        StubBudgetService Budget,
        StubLipSyncProviderClient ProviderClient,
        StubVideoOutputStore OutputStore);

    private sealed class StubAccessService(SeededInputs seeded) : IGenerationAccessService
    {
        public Task<GenerationAccessContext> RequireAsync(
            string userId,
            Guid deviceId,
            Guid? requestedOrganizationId,
            Guid? projectId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new GenerationAccessContext(
                seeded.OrganizationId,
                "Test organization",
                "Member",
                seeded.Project));
    }

    private sealed class StubProviderResolver : IProviderRuntimeResolver
    {
        public int ResolveCount { get; private set; }

        public Task<ProviderRuntimeConfiguration> ResolveAsync(
            Guid organizationId,
            string providerCode,
            string modality,
            Guid? credentialId,
            CancellationToken cancellationToken)
        {
            ResolveCount++;
            return Task.FromResult(new ProviderRuntimeConfiguration(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ProviderCodes.Fal,
                FalLipSyncPolicy.EndpointId,
                new Uri("https://queue.fal.run/"),
                "Key",
                null,
                "fake-key"));
        }

        public Task<GenerationProviderStatusResponse> GetStatusAsync(
            Guid organizationId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubCostEstimator : IAiCostEstimator
    {
        public Task<AiCostQuote> QuoteLipSyncAsync(Guid providerModelId, long durationMs, CancellationToken cancellationToken) =>
            Task.FromResult(new AiCostQuote(0.20m, "USD", "[{\"usageType\":\"VideoSecond\"}]"));
        public Task<AiCostQuote> QuoteOpenAiAsync(Guid providerModelId, int topicCharacters, int targetDurationSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiCostQuote> QuoteOpenAiImageAsync(Guid providerModelId, int promptCharacters, long estimatedInputTokens, long estimatedOutputTokens, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiCostQuote> QuoteOpenAiVoiceAsync(Guid providerModelId, int narrationCharacters, decimal estimatedCharactersPerSecond, long estimatedOutputTokensPerSecond, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiCostQuote> QuoteKlingAsync(Guid providerModelId, int durationSeconds, string resolution, bool nativeAudio, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<decimal> CalculateOpenAiActualAsync(string rateSnapshotJson, long inputTokens, long outputTokens, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubBudgetService : IAiBudgetService
    {
        public int ReserveCount { get; private set; }
        public int SettleCount { get; private set; }
        public int ReleaseCount { get; private set; }

        public Task<BudgetSnapshot> GetSnapshotAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(new BudgetSnapshot(Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddDays(1), 10m, 0m, 0m, 10m, "USD"));

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
            return Task.FromResult(new BudgetReservationResult(Guid.NewGuid(), Guid.NewGuid(), amount, "USD"));
        }

        public Task SettleAsync(
            Guid reservationId,
            decimal actualAmount,
            Guid? organizationProviderCredentialId,
            object? usage,
            object? rateSnapshot,
            CancellationToken cancellationToken)
        {
            SettleCount++;
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken)
        {
            ReleaseCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubInputStore : ILipSyncInputStore
    {
        public Task<string> CreateProviderContentUrlAsync(
            Guid sessionId,
            string inputKind,
            DateTime expiresAtUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult(inputKind == LipSyncInputKinds.Video
                ? "https://inputs.example.com/video?signed-video-token"
                : "https://inputs.example.com/audio?signed-audio-token");

        public Task<LipSyncInputUploadResponse> UploadAsync(Guid sessionId, string inputKind, Stream source, long? contentLength, string? contentType, string userId, Guid deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CopyProviderInputAsync(HttpContext httpContext, Guid sessionId, string inputKind, string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class StubLipSyncProviderRouter(StubLipSyncProviderClient client) : ILipSyncProviderRouter
    {
        public ILipSyncProviderClient Resolve(string providerCode) => client;
    }

    private sealed class StubLipSyncProviderClient(LipSyncProviderTaskResult result) : ILipSyncProviderClient
    {
        public string ProviderCode => ProviderCodes.Fal;
        public int SubmitCount { get; private set; }

        public Task<LipSyncProviderTaskResult> SubmitAsync(
            ProviderRuntimeConfiguration provider,
            string videoUrl,
            string audioUrl,
            CancellationToken cancellationToken)
        {
            SubmitCount++;
            Assert.Contains("signed-video-token", videoUrl, StringComparison.Ordinal);
            Assert.Contains("signed-audio-token", audioUrl, StringComparison.Ordinal);
            return Task.FromResult(result);
        }

        public Task<LipSyncProviderTaskResult> GetStatusAsync(ProviderRuntimeConfiguration provider, string externalRequestId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubVideoOutputStore : IVideoOutputStore
    {
        public int CacheCount { get; private set; }

        public Task CacheAsync(Guid providerRequestId, string outputUrl, CancellationToken cancellationToken)
        {
            CacheCount++;
            return Task.CompletedTask;
        }

        public Task CopyToResponseAsync(HttpContext httpContext, Guid providerRequestId, string userId, Guid deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }
}
