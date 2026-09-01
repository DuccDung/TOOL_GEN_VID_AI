using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Models;
using TOOL_SERVER.Organizations;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Generation;

public sealed class GenerationServiceKlingLongFormPromptTests
{
    [Fact]
    public async Task LongFormEnglishPrompt_IsBlockedBeforeResolverBudgetAndOutbound()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext, GenerationWorkflowTypes.OpenAiStructuredPlan, "A woman walks through a quiet old town.");
        var fixture = CreateService(dbContext, seeded.Project);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => fixture.Service.SubmitVideoAsync(
            CreateRequest(seeded),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal("kling_prompt_language_invalid", exception.Code);
        Assert.Equal(0, fixture.ProviderResolver.ResolveCount);
        Assert.Equal(0, fixture.Budget.ReserveCount);
        Assert.Equal(0, fixture.VideoClient.SubmitCount);
        Assert.Empty(dbContext.ProviderRequests);
    }

    [Fact]
    public async Task DirectShortVideoVietnamesePrompt_RemainsAllowed()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(dbContext, GenerationWorkflowTypes.DirectShortVideo, "Một cô gái đang đi bộ trên phố cổ.");
        var fixture = CreateService(dbContext, seeded.Project);

        var response = await fixture.Service.SubmitVideoAsync(
            CreateRequest(seeded),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("Submitted", response.Status);
        Assert.Equal(1, fixture.ProviderResolver.ResolveCount);
        Assert.Equal(1, fixture.Budget.ReserveCount);
        Assert.Equal(1, fixture.VideoClient.SubmitCount);
        Assert.Contains("Một cô gái đang đi bộ trên phố cổ.", fixture.VideoClient.LastPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LongFormVietnameseSpeech_UsesVietnameseInstructionAndMetadata()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(
            dbContext,
            GenerationWorkflowTypes.OpenAiStructuredPlan,
            "Người dẫn nhìn vào máy quay trong studio yên tĩnh.",
            narration: "Hãy bắt đầu bằng một hành động nhỏ.");
        var fixture = CreateService(dbContext, seeded.Project);

        await fixture.Service.SubmitVideoAsync(
            CreateRequest(seeded),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Contains("Ngôn ngữ: tiếng Việt", fixture.VideoClient.LastPrompt, StringComparison.Ordinal);
        Assert.Contains("LỜI DẪN NATIVE NGOÀI KHUNG HÌNH", fixture.VideoClient.LastPrompt, StringComparison.Ordinal);
        var request = await dbContext.ProviderRequests.SingleAsync();
        Assert.Contains("\"effectiveGenerationLanguageCode\":\"vi-VN\"", request.RequestJson, StringComparison.Ordinal);
        Assert.Contains(KlingLongFormLanguagePolicy.PolicyVersion, request.RequestJson, StringComparison.Ordinal);
        Assert.Contains(KlingNativeAudioPromptComposer.VietnameseTemplateVersion, request.RequestJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LongFormVoiceOverWithCharacter_IsBlockedBeforeResolverBudgetAndOutbound()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(
            dbContext,
            GenerationWorkflowTypes.OpenAiStructuredPlan,
            "A presenter stands in a bright room.",
            narration: "Start with one small action.");
        var reference = SeedApprovedCharacter(dbContext, seeded);
        var fixture = CreateService(dbContext, seeded.Project);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => fixture.Service.SubmitVideoAsync(
            CreateRequest(seeded, reference),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal("kling_voice_over_character_not_allowed", exception.Code);
        Assert.Equal(0, fixture.ProviderResolver.ResolveCount);
        Assert.Equal(0, fixture.Budget.ReserveCount);
        Assert.Equal(0, fixture.VideoClient.SubmitCount);
        Assert.Empty(dbContext.ProviderRequests);
    }

    [Fact]
    public async Task NativeAudioInvalidOnCameraRetry_UsesSpeechRecoveryProfile()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedProject(
            dbContext,
            GenerationWorkflowTypes.OpenAiStructuredPlan,
            "Người dẫn đang nói với máy quay trong khung trung cảnh, khuôn mặt và miệng hiện rõ.");
        seeded.Scene.Dialogue = "Hãy bắt đầu bằng một hành động nhỏ.";
        seeded.Scene.RequiredCapabilitiesJson =
            "{\"speechMode\":\"OnCameraDialogue\",\"voiceStyle\":\"ấm áp và rõ ràng\",\"ambientAudio\":\"tiếng xe cộ nhẹ\",\"soundEffects\":\"tiếng bước chân nhỏ\"}";
        seeded.Scene.Status = "NativeAudioInvalid";
        var reference = SeedApprovedCharacter(dbContext, seeded);
        var scenePromptId = await dbContext.ScenePrompts
            .Where(x => x.SceneId == seeded.Scene.SceneId)
            .Select(x => x.ScenePromptId)
            .SingleAsync();
        dbContext.VideoGenerations.Add(new VideoGeneration
        {
            VideoGenerationId = Guid.NewGuid(),
            SceneId = seeded.Scene.SceneId,
            ScenePromptId = scenePromptId,
            ProviderRequestId = Guid.NewGuid(),
            AttemptNumber = 1,
            Status = "NativeAudioInvalid",
            RequestedDurationMs = 5000,
            CreatedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8]
        });
        await dbContext.SaveChangesAsync();
        var fixture = CreateService(dbContext, seeded.Project);
        var submitRequest = CreateRequest(seeded, reference);

        await fixture.Service.SubmitVideoAsync(
            submitRequest,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var previousGeneration = await dbContext.VideoGenerations.SingleAsync();
        previousGeneration.Status = "Pending";
        await dbContext.SaveChangesAsync();
        await fixture.Service.SubmitVideoAsync(
            submitRequest,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Contains("PHỤC HỒI LỜI THOẠI", fixture.VideoClient.LastPrompt, StringComparison.Ordinal);
        Assert.Contains("âm nền phòng tự nhiên ở mức tối thiểu", fixture.VideoClient.LastPrompt, StringComparison.Ordinal);
        var request = await dbContext.ProviderRequests.SingleAsync();
        Assert.Contains(KlingNativeAudioPromptComposer.SpeechRecoveryProfile, request.RequestJson, StringComparison.Ordinal);
        Assert.DoesNotContain(seeded.Scene.Dialogue, request.RequestJson, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Budget.ReserveCount);
        Assert.Equal(1, fixture.VideoClient.SubmitCount);
    }

    private static SubmitVideoRequest CreateRequest(
        SeededProject seeded,
        VideoReferenceImageInput? referenceImage = null) =>
        new(
            seeded.Project.ProjectId,
            seeded.Scene.SceneId,
            $"video-language-{Guid.NewGuid():N}",
            seeded.Project.OrganizationId,
            referenceImage,
            ScenePlanVersion: 1,
            ScenePromptVersion: 1);

    private static VideoReferenceImageInput SeedApprovedCharacter(
        VideoFactoryDbContext dbContext,
        SeededProject seeded)
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var character = new Character
        {
            CharacterId = Guid.NewGuid(),
            ProjectId = seeded.Project.ProjectId,
            CharacterKey = "presenter",
            Version = 1,
            Name = "Maya",
            Role = "Người dẫn chương trình",
            ProfileJson = "{\"face\":\"khuôn mặt trái xoan\",\"hair\":\"tóc đen ngắn\"}",
            WardrobeJson = "{\"clothing\":\"áo sơ mi xanh\"}",
            ForbiddenChangesJson = "[]",
            VisualIdentity = "Người dẫn có khuôn mặt trái xoan và mái tóc đen ngắn.",
            Status = "Approved",
            CreatedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8]
        };
        var media = new MediaAsset
        {
            MediaAssetId = Guid.NewGuid(),
            ProjectId = seeded.Project.ProjectId,
            AssetType = "CharacterReference",
            RelativePath = "characters/presenter.png",
            MimeType = "image/png",
            SizeBytes = bytes.Length,
            Sha256 = hash,
            Status = "Ready",
            SourceType = "Generated",
            SourceProviderCode = ProviderCodes.OpenAi,
            CreatedAtUtc = DateTime.UtcNow,
            VerifiedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8]
        };
        var characterReference = new CharacterReference
        {
            CharacterReferenceId = Guid.NewGuid(),
            CharacterId = character.CharacterId,
            MediaAssetId = media.MediaAssetId,
            ReferenceType = "Primary",
            IsPrimary = true,
            ApprovalStatus = "Approved",
            CreatedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8]
        };
        seeded.Scene.CharacterIdsJson = JsonSerializer.Serialize(new[] { character.CharacterId });
        dbContext.AddRange(character, media, characterReference);
        dbContext.SaveChanges();
        return new VideoReferenceImageInput(
            characterReference.CharacterReferenceId,
            media.MimeType,
            Convert.ToBase64String(bytes),
            hash);
    }

    private static Fixture CreateService(VideoFactoryDbContext dbContext, Project project)
    {
        var providerResolver = new StubProviderResolver();
        var budget = new StubBudgetService();
        var videoClient = new StubVideoClient();
        var service = new GenerationService(
            dbContext,
            providerResolver,
            null!,
            null!,
            null!,
            null!,
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
            new StubVideoPolicyResolver(),
            new StubVideoRouter(videoClient),
            new StubVideoOutputStore());
        return new Fixture(service, providerResolver, budget, videoClient);
    }

    private static VideoFactoryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"kling-long-form-prompt-{Guid.NewGuid():N}")
            .Options);

    private static SeededProject SeedProject(
        VideoFactoryDbContext dbContext,
        string structureType,
        string finalPrompt,
        string? narration = null)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            ProjectId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            RemoteUserId = "user-1",
            CreatedByUserId = "user-1",
            Name = "Video workflow language test",
            Topic = finalPrompt,
            LanguageCode = "vi-VN",
            Platform = "YouTube",
            AspectRatio = "16:9",
            TargetDurationSeconds = 5,
            OutputWidth = 1280,
            OutputHeight = 720,
            OutputFrameRate = 25,
            Status = "ScenePlanning",
            CurrentScriptVersion = 1,
            CurrentScenePlanVersion = 1,
            VideoProviderCode = ProviderCodes.Kling,
            VideoModelCode = "kling-3.0",
            VideoPolicyVersion = 1,
            VideoResolution = "720p",
            VideoNativeAudio = true,
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
            StructureType = structureType,
            FullText = finalPrompt,
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
            StoryPurpose = structureType == GenerationWorkflowTypes.OpenAiStructuredPlan
                ? "Cảnh mở đầu"
                : "Opening shot",
            Narration = narration,
            VisualDescription = finalPrompt,
            ContentDurationMs = 5000,
            GenerationDurationMs = 5000,
            TimelineEndMs = 5000,
            EntryStateJson = "{}",
            ExitStateJson = "{}",
            RequiredCapabilitiesJson = narration is null
                ? structureType == GenerationWorkflowTypes.OpenAiStructuredPlan
                    ? "{\"speechMode\":\"None\",\"ambientAudio\":\"âm thanh đường phố tự nhiên\",\"soundEffects\":\"tiếng bước chân nhẹ\"}"
                    : "{\"speechMode\":\"None\",\"ambientAudio\":\"natural street ambience\",\"soundEffects\":\"soft footsteps\"}"
                : "{\"speechMode\":\"NativeVoiceOver\",\"voiceStyle\":\"ấm áp và rõ ràng\",\"ambientAudio\":\"âm nền căn phòng yên tĩnh\",\"soundEffects\":\"tiếng cử động tay nhẹ\"}",
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
            PromptTemplateName = structureType == GenerationWorkflowTypes.DirectShortVideo
                ? "manual-short-video"
                : "openai-content-plan",
            PromptTemplateVersion = "3",
            CanonicalInputJson = "{}",
            FinalPrompt = finalPrompt,
            NegativePrompt = structureType == GenerationWorkflowTypes.OpenAiStructuredPlan
                ? "phụ đề, logo, watermark"
                : "subtitles, logos, watermarks",
            PromptHash = new string('a', 64),
            Status = "Approved",
            CreatedAtUtc = now,
            ApprovedAtUtc = now,
            RowVersion = new byte[8]
        };
        dbContext.AddRange(project, script, scene, prompt);
        dbContext.SaveChanges();
        return new SeededProject(project, scene);
    }

    private sealed record SeededProject(Project Project, Scene Scene);
    private sealed record Fixture(
        GenerationService Service,
        StubProviderResolver ProviderResolver,
        StubBudgetService Budget,
        StubVideoClient VideoClient);

    private sealed class StubVideoPolicyResolver : IProjectVideoPolicyResolver
    {
        public Task<ProjectVideoSnapshot> ResolveAsync(Project project, Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProjectVideoSnapshot(
                ProviderCodes.Kling,
                "Kling",
                "kling-3.0",
                "Kling 3.0",
                1,
                "720p",
                true,
                VideoModelCapabilities.KlingDefault));
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

        public Task<ProviderRuntimeConfiguration> ResolveAsync(Guid organizationId, string providerCode, string modality, Guid? credentialId, CancellationToken cancellationToken)
        {
            ResolveCount++;
            return Task.FromResult(_provider);
        }

        public Task<GenerationProviderStatusResponse> GetStatusAsync(Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubVideoRouter(StubVideoClient client) : IVideoProviderRouter
    {
        public IVideoProviderClient Resolve(string providerCode) => client;
    }

    private sealed class StubVideoClient : IVideoProviderClient
    {
        public string ProviderCode => ProviderCodes.Kling;
        public int SubmitCount { get; private set; }
        public string LastPrompt { get; private set; } = string.Empty;

        public Task<VideoProviderTaskResult> SubmitAsync(
            ProviderRuntimeConfiguration provider,
            string prompt,
            string aspectRatio,
            int durationSeconds,
            string resolution,
            bool nativeAudio,
            string safetyIdentifier,
            VideoProviderReferenceImage? referenceImage,
            CancellationToken cancellationToken)
        {
            SubmitCount++;
            LastPrompt = prompt;
            return Task.FromResult(new VideoProviderTaskResult(
                "task-1",
                "Submitted",
                5,
                null,
                null,
                null,
                null,
                null,
                durationSeconds,
                "{}"));
        }

        public Task<VideoProviderTaskResult> GetStatusAsync(ProviderRuntimeConfiguration provider, string externalRequestId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubAccessService(GenerationAccessContext context) : IGenerationAccessService
    {
        public Task<GenerationAccessContext> RequireAsync(string userId, Guid deviceId, Guid? requestedOrganizationId, Guid? projectId, CancellationToken cancellationToken) => Task.FromResult(context);
    }

    private sealed class StubVideoOutputStore : IVideoOutputStore
    {
        public Task CacheAsync(Guid providerRequestId, string outputUrl, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CopyToResponseAsync(HttpContext httpContext, Guid providerRequestId, string userId, Guid deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class StubBudgetService : IAiBudgetService
    {
        public int ReserveCount { get; private set; }
        public Task<BudgetSnapshot> GetSnapshotAsync(Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BudgetReservationResult> ReserveAsync(Guid organizationId, string userId, Guid projectId, Guid providerRequestId, string operationKey, string providerCode, string modelCode, decimal amount, CancellationToken cancellationToken)
        {
            ReserveCount++;
            return Task.FromResult(new BudgetReservationResult(Guid.NewGuid(), Guid.NewGuid(), amount, "USD"));
        }
        public Task SettleAsync(Guid reservationId, decimal actualAmount, Guid? organizationProviderCredentialId, object? usage, object? rateSnapshot, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubCostEstimator : IAiCostEstimator
    {
        public Task<AiCostQuote> QuoteKlingAsync(Guid providerModelId, int durationSeconds, string resolution, bool nativeAudio, CancellationToken cancellationToken) =>
            Task.FromResult(new AiCostQuote(0.5m, "USD", "[]"));
        public Task<AiCostQuote> QuoteOpenAiAsync(Guid providerModelId, int topicCharacters, int targetDurationSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiCostQuote> QuoteOpenAiImageAsync(Guid providerModelId, int promptCharacters, long estimatedInputTokens, long estimatedOutputTokens, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiCostQuote> QuoteOpenAiVoiceAsync(Guid providerModelId, int narrationCharacters, decimal estimatedCharactersPerSecond, long estimatedOutputTokensPerSecond, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<decimal> CalculateOpenAiActualAsync(string rateSnapshotJson, long inputTokens, long outputTokens, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
