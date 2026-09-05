using System.Security.Cryptography;
using System.Text.Json;
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

public sealed class SceneFirstFrameServiceTests
{
    [Fact]
    public async Task Generate_Broll_IsIdempotentAndDoesNotPersistPromptOrImagePayload()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedBrollScene(dbContext);
        var imageClient = new StubImageClient();
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, seeded.Project, imageClient, budget, 0.25m);
        var request = CreateRequest(seeded, "scene-first-frame-idempotent", 1);

        var first = await service.GenerateAsync(request, "user-1", Guid.NewGuid(), CancellationToken.None);
        var replay = await service.GenerateAsync(request, "user-1", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(first, replay);
        Assert.StartsWith("/api/generation/images/scene-first-frames/", first.ContentUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", first.ContentUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, imageClient.CallCount);
        Assert.Null(imageClient.LastSourceImage);
        Assert.Equal(1, budget.ReserveCount);
        Assert.Equal(1, budget.SettleCount);
        var requestLog = await dbContext.ProviderRequests.SingleAsync();
        Assert.Equal("Completed", requestLog.Status);
        Assert.DoesNotContain(seeded.Scene.VisualDescription, requestLog.RequestJson, StringComparison.Ordinal);
        Assert.DoesNotContain("base64", requestLog.RequestJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(imageClient.ImageBytes), requestLog.ResponseJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Single(dbContext.GeneratedImageOutputs);
    }

    [Fact]
    public async Task Generate_NewAttemptCreatesANewPaidRequest()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedBrollScene(dbContext);
        var imageClient = new StubImageClient();
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, seeded.Project, imageClient, budget, 0.25m);

        await service.GenerateAsync(CreateRequest(seeded, "scene-first-frame-attempt-1", 1), "user-1", Guid.NewGuid(), CancellationToken.None);
        await service.GenerateAsync(CreateRequest(seeded, "scene-first-frame-attempt-2", 2), "user-1", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(2, imageClient.CallCount);
        Assert.Equal(2, budget.ReserveCount);
        Assert.Equal(2, budget.SettleCount);
        Assert.Equal(2, await dbContext.ProviderRequests.CountAsync());
    }

    [Fact]
    public async Task Generate_MissingPricingStopsBeforeBudgetAndOutbound()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedBrollScene(dbContext);
        var imageClient = new StubImageClient();
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, seeded.Project, imageClient, budget, 0m);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.GenerateAsync(CreateRequest(seeded, "scene-first-frame-no-price", 1), "user-1", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("pricing_not_configured", exception.Code);
        Assert.Equal(0, budget.ReserveCount);
        Assert.Equal(0, imageClient.CallCount);
        Assert.Empty(dbContext.ProviderRequests);
    }

    [Fact]
    public async Task Generate_MissingCredentialStopsBeforeBudgetAndOutbound()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedBrollScene(dbContext);
        var imageClient = new StubImageClient();
        var budget = new StubBudgetService();
        var provider = new StubProviderResolver(new AccountApiException(503, "provider_credential_not_configured", "missing"));
        var service = CreateService(dbContext, seeded.Project, imageClient, budget, 0.25m, provider: provider);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.GenerateAsync(CreateRequest(seeded, "scene-first-frame-no-credential", 1), "user-1", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("provider_credential_not_configured", exception.Code);
        Assert.Equal(1, provider.ResolveCount);
        Assert.Equal(0, budget.ReserveCount);
        Assert.Equal(0, imageClient.CallCount);
        Assert.Empty(dbContext.ProviderRequests);
    }

    [Fact]
    public async Task Generate_BudgetFailureStopsBeforeOutbound()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedBrollScene(dbContext);
        var imageClient = new StubImageClient();
        var budget = new StubBudgetService("organization_budget_exceeded");
        var service = CreateService(dbContext, seeded.Project, imageClient, budget, 0.25m);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.GenerateAsync(CreateRequest(seeded, "scene-first-frame-no-budget", 1), "user-1", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("organization_budget_exceeded", exception.Code);
        Assert.Equal(1, budget.ReserveCount);
        Assert.Equal(0, imageClient.CallCount);
        Assert.Empty(dbContext.ProviderRequests);
    }

    [Theory]
    [InlineData("organization_generation_denied", 403)]
    [InlineData("project_not_found", 404)]
    [InlineData("organization_membership_required", 403)]
    public async Task Generate_AccessDeniedStopsBeforePolicyProviderAndOutbound(string errorCode, int statusCode)
    {
        await using var dbContext = CreateContext();
        var seeded = SeedBrollScene(dbContext);
        var imageClient = new StubImageClient();
        var budget = new StubBudgetService();
        var policy = new StubVideoPolicy();
        var provider = new StubProviderResolver();
        var service = CreateService(
            dbContext,
            seeded.Project,
            imageClient,
            budget,
            0.25m,
            new StubAccessService(new AccountApiException(statusCode, errorCode, "denied")),
            policy,
            provider);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.GenerateAsync(CreateRequest(seeded, "scene-first-frame-denied", 1), "viewer", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(errorCode, exception.Code);
        Assert.Equal(0, policy.ResolveCount);
        Assert.Equal(0, provider.ResolveCount);
        Assert.Equal(0, imageClient.CallCount);
        Assert.Equal(0, budget.ReserveCount);
    }

    [Theory]
    [InlineData("1:1", null, "scene_first_frame_aspect_ratio_invalid")]
    [InlineData("16:9", "two", "scene_first_frame_character_limit_exceeded")]
    public async Task Generate_InvalidScenePreflightStopsBeforePaidOutbound(
        string aspectRatio,
        string? characterMode,
        string expectedCode)
    {
        await using var dbContext = CreateContext();
        var seeded = SeedBrollScene(dbContext);
        seeded.Project.AspectRatio = aspectRatio;
        if (characterMode == "two")
        {
            seeded.Scene.CharacterIdsJson = JsonSerializer.Serialize(new[] { Guid.NewGuid(), Guid.NewGuid() });
        }
        await dbContext.SaveChangesAsync();
        var imageClient = new StubImageClient();
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, seeded.Project, imageClient, budget, 0.25m);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.GenerateAsync(CreateRequest(seeded, $"scene-first-frame-{expectedCode}", 1), "user-1", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
        Assert.Equal(0, imageClient.CallCount);
        Assert.Equal(0, budget.ReserveCount);
    }

    [Fact]
    public async Task OnCameraGeneration_RequiresAndUsesTheApprovedPrimaryReference()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedBrollScene(dbContext);
        var characterInput = SeedApprovedCharacter(dbContext, seeded);
        var imageClient = new StubImageClient();
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, seeded.Project, imageClient, budget, 0.25m);
        var request = CreateRequest(seeded, "scene-first-frame-on-camera", 1) with
        {
            CharacterReference = characterInput
        };

        await service.GenerateAsync(request, "user-1", Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(imageClient.LastSourceImage);
        Assert.Equal(characterInput.CharacterReferenceId, (await dbContext.ProviderRequests.SingleAsync()).Character!.CharacterReferences.Single().CharacterReferenceId);
        Assert.Equal(characterInput.MimeType, imageClient.LastSourceImage!.MimeType);
        Assert.Equal(Convert.FromBase64String(characterInput.Base64Data), imageClient.LastSourceImage.Bytes);
    }

    [Fact]
    public async Task Lifecycle_ApprovesCurrentFrameThenInvalidatesItWhenSceneChanges()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedBrollScene(dbContext);
        var imageClient = new StubImageClient();
        var service = CreateService(dbContext, seeded.Project, imageClient, new StubBudgetService(), 0.25m);
        var generated = await service.GenerateAsync(
            CreateRequest(seeded, "scene-first-frame-lifecycle", 1),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var relativePath = $"projects/{seeded.Project.ProjectId:N}/scenes/{seeded.Scene.SceneId:N}/first-frames/frame.png";

        var pending = await service.MaterializeAsync(
            seeded.Project.ProjectId,
            seeded.Scene.SceneId,
            new MaterializeSceneFirstFrameRequest(
                generated.ProviderRequestId,
                relativePath,
                generated.MimeType,
                generated.Sha256,
                generated.SizeBytes,
                generated.Width,
                generated.Height,
                seeded.Project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var approved = await service.ApproveAsync(
            seeded.Project.ProjectId,
            seeded.Scene.SceneId,
            pending.SceneFirstFrameId,
            new ChangeSceneFirstFrameStatusRequest(pending.RowVersion, seeded.Project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var validated = await service.ValidateForVideoAsync(
            seeded.Project.ProjectId,
            seeded.Scene.SceneId,
            seeded.Project.AspectRatio,
            new SceneFirstFrameInput(
                approved.SceneFirstFrameId,
                approved.MimeType,
                Convert.ToBase64String(imageClient.ImageBytes),
                approved.Sha256),
            CancellationToken.None);

        Assert.Equal(SceneFirstFrameStatuses.PendingReview, pending.Status);
        Assert.Equal(SceneFirstFrameStatuses.Approved, approved.Status);
        Assert.Equal(approved.SceneFirstFrameId, validated.SceneFirstFrameId);
        seeded.Scene.VisualDescription = "The approved scene has changed";
        await dbContext.SaveChangesAsync();

        var stale = await Assert.ThrowsAsync<AccountApiException>(() => service.ValidateForVideoAsync(
            seeded.Project.ProjectId,
            seeded.Scene.SceneId,
            seeded.Project.AspectRatio,
            new SceneFirstFrameInput(
                approved.SceneFirstFrameId,
                approved.MimeType,
                Convert.ToBase64String(imageClient.ImageBytes),
                approved.Sha256),
            CancellationToken.None));
        Assert.Equal("scene_first_frame_stale", stale.Code);

        var refreshed = await service.ListAsync(
            seeded.Project.ProjectId,
            seeded.Scene.SceneId,
            seeded.Project.OrganizationId,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        var invalidated = Assert.Single(refreshed.Frames);
        Assert.Equal(SceneFirstFrameStatuses.Invalidated, invalidated.Status);
        Assert.False(invalidated.IsCurrent);
    }

    [Fact]
    public async Task ListProject_ReturnsAllProjectFramesWithoutFramesFromAnotherProject()
    {
        await using var dbContext = CreateContext();
        var firstProject = SeedBrollScene(dbContext);
        var secondProject = SeedBrollScene(dbContext);
        var firstImageClient = new StubImageClient();
        var secondImageClient = new StubImageClient();
        var firstService = CreateService(
            dbContext,
            firstProject.Project,
            firstImageClient,
            new StubBudgetService(),
            0.25m);
        var secondService = CreateService(
            dbContext,
            secondProject.Project,
            secondImageClient,
            new StubBudgetService(),
            0.25m);
        var expected = await GenerateAndMaterializeAsync(
            firstService,
            firstProject,
            firstImageClient,
            "scene-first-frame-project-list-1");
        await GenerateAndMaterializeAsync(
            secondService,
            secondProject,
            secondImageClient,
            "scene-first-frame-project-list-2");

        var response = await firstService.ListProjectAsync(
            firstProject.Project.ProjectId,
            firstProject.Project.OrganizationId,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(firstProject.Project.ProjectId, response.ProjectId);
        var frame = Assert.Single(response.Frames);
        Assert.Equal(expected.SceneFirstFrameId, frame.SceneFirstFrameId);
        Assert.Equal(firstProject.Scene.SceneId, frame.SceneId);
    }

    private static SceneFirstFrameService CreateService(
        VideoFactoryDbContext dbContext,
        Project project,
        StubImageClient imageClient,
        StubBudgetService budget,
        decimal estimatedCost,
        StubAccessService? access = null,
        StubVideoPolicy? policy = null,
        StubProviderResolver? provider = null) =>
        new(
            dbContext,
            access ?? new StubAccessService(new GenerationAccessContext(project.OrganizationId!.Value, "Test", "Member", project)),
            policy ?? new StubVideoPolicy(),
            provider ?? new StubProviderResolver(),
            imageClient,
            new StubCostEstimator(estimatedCost),
            budget,
            Options.Create(new OpenAiImageOptions()),
            NullLogger<SceneFirstFrameService>.Instance,
            TimeProvider.System);

    private static GenerateSceneFirstFrameRequest CreateRequest(SeededScene seeded, string key, int attempt) =>
        new(
            seeded.Project.ProjectId,
            seeded.Scene.SceneId,
            seeded.Scene.ScenePlanVersion,
            seeded.Prompt.Version,
            key,
            seeded.Project.OrganizationId,
            null,
            attempt);

    private static async Task<SceneFirstFrameSummary> GenerateAndMaterializeAsync(
        SceneFirstFrameService service,
        SeededScene seeded,
        StubImageClient imageClient,
        string idempotencyKey)
    {
        var generated = await service.GenerateAsync(
            CreateRequest(seeded, idempotencyKey, 1),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        return await service.MaterializeAsync(
            seeded.Project.ProjectId,
            seeded.Scene.SceneId,
            new MaterializeSceneFirstFrameRequest(
                generated.ProviderRequestId,
                $"projects/{seeded.Project.ProjectId:N}/scenes/{seeded.Scene.SceneId:N}/first-frames/frame.png",
                generated.MimeType,
                generated.Sha256,
                imageClient.ImageBytes.Length,
                generated.Width,
                generated.Height,
                seeded.Project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
    }

    private static VideoFactoryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"scene-first-frame-{Guid.NewGuid():N}")
            .Options);

    private static SeededScene SeedBrollScene(VideoFactoryDbContext dbContext)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            ProjectId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            RemoteUserId = "user-1",
            CreatedByUserId = "user-1",
            Name = "Test",
            Topic = "Test",
            LanguageCode = "vi-VN",
            Platform = "YouTube",
            AspectRatio = "16:9",
            TargetDurationSeconds = 60,
            OutputWidth = 1280,
            OutputHeight = 720,
            OutputFrameRate = 24,
            Status = "Draft",
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
            StructureType = GenerationWorkflowTypes.OpenAiStructuredPlan,
            FullText = "Script",
            StoryBeatsJson = "[]",
            Status = "Approved",
            CreatedAtUtc = now,
            RowVersion = new byte[8],
            Project = project
        };
        var scene = new Scene
        {
            SceneId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            ScriptId = script.ScriptId,
            StyleProfileId = Guid.NewGuid(),
            ScenePlanVersion = 1,
            SequenceNumber = 1,
            StoryPurpose = "Opening",
            VisualDescription = "A private visual description that must not be logged",
            CharacterIdsJson = "[]",
            EntryStateJson = "{}",
            ExitStateJson = "{}",
            Status = "Ready",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8],
            Project = project,
            Script = script
        };
        var prompt = new ScenePrompt
        {
            ScenePromptId = Guid.NewGuid(),
            SceneId = scene.SceneId,
            Version = 1,
            PromptTemplateName = "scene",
            PromptTemplateVersion = "1",
            CanonicalInputJson = "{}",
            FinalPrompt = "video prompt",
            PromptHash = new string('a', 64),
            Status = "Approved",
            CreatedAtUtc = now,
            ApprovedAtUtc = now,
            RowVersion = new byte[8],
            Scene = scene
        };
        scene.ScenePrompts.Add(prompt);
        dbContext.AddRange(project, script, scene, prompt);
        dbContext.SaveChanges();
        return new SeededScene(project, scene, prompt);
    }

    private static SceneFirstFrameCharacterInput SeedApprovedCharacter(VideoFactoryDbContext dbContext, SeededScene seeded)
    {
        var bytes = CreatePngHeader(1024, 1024);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var character = new Character
        {
            CharacterId = Guid.NewGuid(),
            ProjectId = seeded.Project.ProjectId,
            CharacterKey = "presenter",
            Version = 1,
            Name = "Maya",
            Role = "Presenter",
            VisualIdentity = "Oval face and short black hair",
            ProfileJson = "{\"age\":30}",
            WardrobeJson = "{\"clothing\":\"blue shirt\"}",
            ForbiddenChangesJson = "[]",
            Status = "Approved",
            CreatedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8],
            Project = seeded.Project
        };
        var media = new MediaAsset
        {
            MediaAssetId = Guid.NewGuid(),
            ProjectId = seeded.Project.ProjectId,
            AssetType = "CharacterReference",
            DisplayName = "Maya",
            RelativePath = "characters/maya.png",
            MimeType = "image/png",
            SizeBytes = bytes.Length,
            Sha256 = hash,
            Width = 1024,
            Height = 1024,
            Status = "Ready",
            SourceType = "Generated",
            CreatedAtUtc = DateTime.UtcNow,
            VerifiedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8],
            Project = seeded.Project
        };
        var reference = new CharacterReference
        {
            CharacterReferenceId = Guid.NewGuid(),
            CharacterId = character.CharacterId,
            MediaAssetId = media.MediaAssetId,
            ReferenceType = "Primary",
            IsPrimary = true,
            ApprovalStatus = "Approved",
            CreatedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8],
            Character = character,
            MediaAsset = media
        };
        character.CharacterReferences.Add(reference);
        seeded.Scene.CharacterIdsJson = JsonSerializer.Serialize(new[] { character.CharacterId });
        dbContext.AddRange(character, media, reference);
        dbContext.SaveChanges();
        return new SceneFirstFrameCharacterInput(
            reference.CharacterReferenceId,
            media.MimeType,
            Convert.ToBase64String(bytes),
            hash);
    }

    private sealed record SeededScene(Project Project, Scene Scene, ScenePrompt Prompt);

    private sealed class StubAccessService : IGenerationAccessService
    {
        private readonly GenerationAccessContext? _context;
        private readonly AccountApiException? _exception;

        public StubAccessService(GenerationAccessContext context) => _context = context;
        public StubAccessService(AccountApiException exception) => _exception = exception;

        public Task<GenerationAccessContext> RequireAsync(string userId, Guid deviceId, Guid? requestedOrganizationId, Guid? projectId, CancellationToken cancellationToken) =>
            _exception is null
                ? Task.FromResult(_context!)
                : Task.FromException<GenerationAccessContext>(_exception);
    }

    private sealed class StubVideoPolicy : IProjectVideoPolicyResolver
    {
        public int ResolveCount { get; private set; }

        public Task<ProjectVideoSnapshot> ResolveAsync(Project project, Guid organizationId, string policyScope, CancellationToken cancellationToken)
        {
            ResolveCount++;
            return Task.FromResult(new ProjectVideoSnapshot(
                ProviderCodes.Fal,
                "fal",
                FalVeoPolicy.StandardEndpointId,
                "Veo",
                1,
                FalVeoPolicy.Resolution,
                true,
                VideoModelCapabilities.Parse(null, ProviderCodes.Fal)));
        }
    }

    private sealed class StubProviderResolver(AccountApiException? exception = null) : IProviderRuntimeResolver
    {
        public int ResolveCount { get; private set; }

        public Task<ProviderRuntimeConfiguration> ResolveAsync(Guid organizationId, string providerCode, string modality, Guid? credentialId, CancellationToken cancellationToken)
        {
            ResolveCount++;
            if (exception is not null)
            {
                return Task.FromException<ProviderRuntimeConfiguration>(exception);
            }
            return Task.FromResult(new ProviderRuntimeConfiguration(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ProviderCodes.OpenAi,
                "gpt-image-2",
                new Uri("https://api.openai.com/v1/"),
                "Bearer",
                null,
                "test-key"));
        }

        public Task<GenerationProviderStatusResponse> GetStatusAsync(Guid organizationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubImageClient : IOpenAiImageClient
    {
        public byte[] ImageBytes { get; } = CreatePngHeader(1280, 720);
        public int CallCount { get; private set; }
        public OpenAiImageEditInput? LastSourceImage { get; private set; }

        public Task<OpenAiImageResult> GenerateAsync(ProviderRuntimeConfiguration provider, string prompt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OpenAiImageResult> GenerateSceneFirstFrameAsync(ProviderRuntimeConfiguration provider, string prompt, string aspectRatio, OpenAiImageEditInput? sourceImage, CancellationToken cancellationToken)
        {
            CallCount++;
            LastSourceImage = sourceImage;
            var image = new ValidatedGeneratedImage(
                ImageBytes,
                "image/png",
                Convert.ToHexString(SHA256.HashData(ImageBytes)).ToLowerInvariant(),
                1280,
                720);
            return Task.FromResult(new OpenAiImageResult(image, 100, 200, $"openai-{CallCount}"));
        }
    }

    private sealed class StubCostEstimator(decimal estimatedCost) : IAiCostEstimator
    {
        public Task<AiCostQuote> QuoteOpenAiImageAsync(Guid providerModelId, int promptCharacters, long estimatedInputTokens, long estimatedOutputTokens, CancellationToken cancellationToken) =>
            Task.FromResult(new AiCostQuote(estimatedCost, "USD", "[{\"usageType\":\"InputToken\"},{\"usageType\":\"OutputToken\"}]", estimatedInputTokens, estimatedOutputTokens));

        public Task<decimal> CalculateOpenAiActualAsync(string rateSnapshotJson, long inputTokens, long outputTokens, CancellationToken cancellationToken) => Task.FromResult(0.2m);
        public Task<AiCostQuote> QuoteOpenAiAsync(Guid providerModelId, int topicCharacters, int targetDurationSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiCostQuote> QuoteOpenAiVoiceAsync(Guid providerModelId, int narrationCharacters, decimal estimatedCharactersPerSecond, long estimatedOutputTokensPerSecond, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiCostQuote> QuoteKlingAsync(Guid providerModelId, int durationSeconds, string resolution, bool nativeAudio, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubBudgetService(string? reserveErrorCode = null) : IAiBudgetService
    {
        public int ReserveCount { get; private set; }
        public int SettleCount { get; private set; }

        public Task<BudgetSnapshot> GetSnapshotAsync(Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<BudgetReservationResult> ReserveAsync(Guid organizationId, string userId, Guid projectId, Guid providerRequestId, string operationKey, string providerCode, string modelCode, decimal amount, CancellationToken cancellationToken)
        {
            ReserveCount++;
            if (reserveErrorCode is not null)
            {
                return Task.FromException<BudgetReservationResult>(
                    new AccountApiException(409, reserveErrorCode, "blocked"));
            }
            return Task.FromResult(new BudgetReservationResult(Guid.NewGuid(), Guid.NewGuid(), amount, "USD"));
        }

        public Task SettleAsync(Guid reservationId, decimal actualAmount, Guid? organizationProviderCredentialId, object? usage, object? rateSnapshot, CancellationToken cancellationToken)
        {
            SettleCount++;
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static byte[] CreatePngHeader(int width, int height)
    {
        var bytes = new byte[24];
        bytes[0] = 0x89;
        bytes[1] = 0x50;
        bytes[2] = 0x4E;
        bytes[3] = 0x47;
        bytes[4] = 0x0D;
        bytes[5] = 0x0A;
        bytes[6] = 0x1A;
        bytes[7] = 0x0A;
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        return bytes;
    }

    private static void WriteBigEndian(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }
}
