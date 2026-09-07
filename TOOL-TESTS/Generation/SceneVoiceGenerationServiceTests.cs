using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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

public sealed class SceneVoiceGenerationServiceTests
{
    [Fact]
    public async Task GenerateSceneVoiceAsync_CompletesAndReplayDoesNotChargeOrCallProviderTwice()
    {
        await using var dbContext = CreateContext();
        var (project, scene) = SeedProject(dbContext);
        var speech = new StubSpeechClient();
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, speech, budget, new StubCostEstimator(0.02m));
        var request = CreateRequest(project, scene);

        var first = await service.GenerateSceneVoiceAsync(request, "user-1", Guid.NewGuid(), CancellationToken.None);
        var replay = await service.GenerateSceneVoiceAsync(request, "user-1", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(first.ProviderRequestId, replay.ProviderRequestId);
        Assert.Equal("Completed", first.Status);
        Assert.Equal("audio/wav", first.MimeType);
        Assert.Equal(1, speech.CallCount);
        Assert.Equal(1, budget.ReserveCount);
        Assert.Equal(1, budget.SettleCount);
        Assert.Equal(0, budget.ReleaseCount);
        Assert.Single(await dbContext.GeneratedVoiceOutputs.ToListAsync());
        var generation = Assert.Single(await dbContext.VoiceGenerations.ToListAsync());
        Assert.Equal("Completed", generation.Status);
        Assert.Equal(scene.SceneId, generation.SceneId);
        Assert.Equal(
            SceneSpeechStatuses.SpeechReviewRequired,
            (await dbContext.Scenes.SingleAsync()).SpeechStatus);
        var requestLog = Assert.Single(await dbContext.ProviderRequests.ToListAsync());
        Assert.DoesNotContain(scene.Narration!, requestLog.RequestJson, StringComparison.Ordinal);
        Assert.Contains("InputToken", requestLog.RateSnapshotJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateSceneVoiceAsync_MissingPricingStopsBeforeReservationAndOutbound()
    {
        await using var dbContext = CreateContext();
        var (project, scene) = SeedProject(dbContext);
        var speech = new StubSpeechClient();
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, speech, budget, new StubCostEstimator(0));

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.GenerateSceneVoiceAsync(CreateRequest(project, scene), "user-1", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("pricing_not_configured", exception.Code);
        Assert.Equal(0, budget.ReserveCount);
        Assert.Equal(0, speech.CallCount);
        Assert.Empty(await dbContext.ProviderRequests.ToListAsync());
    }

    [Fact]
    public async Task GenerateSceneVoiceAsync_BudgetFailureStopsBeforeOutbound()
    {
        await using var dbContext = CreateContext();
        var (project, scene) = SeedProject(dbContext);
        var speech = new StubSpeechClient();
        var budget = new StubBudgetService("organization_budget_exceeded");
        var service = CreateService(dbContext, project, speech, budget, new StubCostEstimator(0.02m));

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.GenerateSceneVoiceAsync(CreateRequest(project, scene), "user-1", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("organization_budget_exceeded", exception.Code);
        Assert.Equal(1, budget.ReserveCount);
        Assert.Equal(0, speech.CallCount);
        Assert.Empty(await dbContext.ProviderRequests.ToListAsync());
    }

    [Fact]
    public async Task GenerateSceneVoiceAsync_ProviderFailureMarksFailedAndReleasesReservation()
    {
        await using var dbContext = CreateContext();
        var (project, scene) = SeedProject(dbContext);
        var speech = new StubSpeechClient(new ProviderHttpException(
            ProviderCodes.OpenAi,
            "openai_voice_rate_limited",
            "OpenAI rate limited the request."));
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, speech, budget, new StubCostEstimator(0.02m));

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.GenerateSceneVoiceAsync(CreateRequest(project, scene), "user-1", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("openai_voice_rate_limited", exception.Code);
        Assert.Equal(1, speech.CallCount);
        Assert.Equal(1, budget.ReleaseCount);
        Assert.Equal(0, budget.SettleCount);
        Assert.Equal("Failed", (await dbContext.ProviderRequests.SingleAsync()).Status);
        Assert.Equal("Failed", (await dbContext.VoiceGenerations.SingleAsync()).Status);
    }

    [Fact]
    public async Task GenerateSceneVoiceAsync_StaleNarrationHashStopsBeforeProviderResolution()
    {
        await using var dbContext = CreateContext();
        var (project, scene) = SeedProject(dbContext);
        var resolver = new StubProviderResolver();
        var speech = new StubSpeechClient();
        var service = CreateService(
            dbContext,
            project,
            speech,
            new StubBudgetService(),
            new StubCostEstimator(0.02m),
            resolver);
        var request = CreateRequest(project, scene) with { ExpectedNarrationHash = new string('0', 64) };

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.GenerateSceneVoiceAsync(request, "user-1", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(SpeechSynchronizationErrorCodes.SceneSpeechChanged, exception.Code);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(0, speech.CallCount);
    }

    [Fact]
    public async Task GenerateSceneVoiceAsync_AccessDeniedStopsBeforePricingAndOutbound()
    {
        await using var dbContext = CreateContext();
        var (project, scene) = SeedProject(dbContext);
        var resolver = new StubProviderResolver();
        var speech = new StubSpeechClient();
        var service = CreateService(
            dbContext,
            project,
            speech,
            new StubBudgetService(),
            new StubCostEstimator(0.02m),
            resolver,
            new StubAccessService(new AccountApiException(
                403,
                "organization_generation_denied",
                "Viewer cannot generate AI output.")));

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.GenerateSceneVoiceAsync(CreateRequest(project, scene), "viewer-1", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("organization_generation_denied", exception.Code);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(0, speech.CallCount);
    }

    [Fact]
    public async Task VoiceProfileLifecycle_DraftRequiresPreviewThenApprovesAndSupersedesPreviousVersion()
    {
        await using var dbContext = CreateContext();
        var (project, _) = SeedProject(dbContext);
        var previousVersion = await dbContext.VoiceProfileVersions.SingleAsync();
        var speech = new StubSpeechClient();
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, speech, budget, new StubCostEstimator(0.02m));

        var draft = await service.CreateVoiceProfileDraftAsync(
            new CreateVoiceProfileDraftRequest(
                project.ProjectId,
                VoiceProfileScopes.ProjectNarrator,
                "male-warm",
                1.05m),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(2, draft.Version);
        Assert.Equal(VoiceProfileVersionStatuses.Draft, draft.Status);
        var approvalWithoutPreview = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.ApproveVoiceProfileVersionAsync(
                new ApproveVoiceProfileVersionRequest(project.ProjectId, draft.VoiceProfileVersionId, draft.SnapshotHash),
                "user-1",
                Guid.NewGuid(),
                CancellationToken.None));
        Assert.Equal(SpeechSynchronizationErrorCodes.VoicePreviewRequired, approvalWithoutPreview.Code);

        var previewRequest = new GenerateVoiceProfilePreviewRequest(
            project.ProjectId,
            draft.VoiceProfileVersionId,
            draft.SnapshotHash,
            $"voice-preview:{draft.VoiceProfileVersionId:N}");
        var preview = await service.GenerateVoiceProfilePreviewAsync(
            previewRequest,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var replay = await service.GenerateVoiceProfilePreviewAsync(
            previewRequest,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(preview.ProviderRequestId, replay.ProviderRequestId);
        Assert.Equal("audio/wav", preview.MimeType);
        Assert.Equal(1, speech.CallCount);
        Assert.Equal(1, budget.ReserveCount);
        Assert.Equal(1, budget.SettleCount);

        var approvalWithoutPlaybackConfirmation = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.ApproveVoiceProfileVersionAsync(
                new ApproveVoiceProfileVersionRequest(
                    project.ProjectId,
                    draft.VoiceProfileVersionId,
                    draft.SnapshotHash,
                    PlaybackConfirmed: false),
                "user-1",
                Guid.NewGuid(),
                CancellationToken.None));
        Assert.Equal(SpeechSynchronizationErrorCodes.VoicePreviewRequired, approvalWithoutPlaybackConfirmation.Code);

        var approved = await service.ApproveVoiceProfileVersionAsync(
            new ApproveVoiceProfileVersionRequest(
                project.ProjectId,
                draft.VoiceProfileVersionId,
                draft.SnapshotHash,
                PlaybackConfirmed: true),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(VoiceProfileVersionStatuses.Approved, approved.Status);
        Assert.Equal(draft.VoiceProfileVersionId, project.ApprovedNarratorVoiceProfileVersionId);
        Assert.Equal(
            VoiceProfileVersionStatuses.Superseded,
            (await dbContext.VoiceProfileVersions.SingleAsync(x => x.VoiceProfileVersionId == previousVersion.VoiceProfileVersionId)).Status);

        var superseded = await service.SupersedeVoiceProfileVersionAsync(
            new SupersedeVoiceProfileVersionRequest(project.ProjectId, draft.VoiceProfileVersionId, draft.SnapshotHash),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(VoiceProfileVersionStatuses.Superseded, superseded.Status);
        Assert.Null(project.ApprovedNarratorVoiceProfileVersionId);
    }

    [Fact]
    public async Task VoiceProfilePreviewQuote_DatabaseDecimalScaleDoesNotInvalidateSnapshot()
    {
        await using var dbContext = CreateContext();
        var (project, _) = SeedProject(dbContext);
        var service = CreateService(
            dbContext,
            project,
            new StubSpeechClient(),
            new StubBudgetService(),
            new StubCostEstimator(0.02m));

        var draft = await service.CreateVoiceProfileDraftAsync(
            new CreateVoiceProfileDraftRequest(
                project.ProjectId,
                VoiceProfileScopes.ProjectNarrator,
                "male-warm",
                1m),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var storedVersion = await dbContext.VoiceProfileVersions.SingleAsync(
            x => x.VoiceProfileVersionId == draft.VoiceProfileVersionId);

        // SQL Server materializes decimal(6,3) as 1.000 even when the request
        // that produced the immutable snapshot contained the numeric value 1.
        storedVersion.SpeakingRate = decimal.Parse("1.000", CultureInfo.InvariantCulture);

        var quote = await service.GetVoiceProfilePreviewQuoteAsync(
            new VoiceProfilePreviewQuoteRequest(
                project.ProjectId,
                draft.VoiceProfileVersionId,
                draft.SnapshotHash),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(draft.VoiceProfileVersionId, quote.VoiceProfileVersionId);
        Assert.Equal(0.02m, quote.EstimatedCost);
    }

    private static GenerationService CreateService(
        VideoFactoryDbContext dbContext,
        Project project,
        StubSpeechClient speechClient,
        StubBudgetService budgetService,
        StubCostEstimator costEstimator,
        StubProviderResolver? resolver = null,
        IGenerationAccessService? accessService = null) =>
        new(
            dbContext,
            resolver ?? new StubProviderResolver(),
            new UnusedContentClient(),
            new UnusedImageClient(),
            speechClient,
            new UnusedKlingClient(),
            accessService ?? new StubAccessService(new GenerationAccessContext(
                project.OrganizationId!.Value,
                "Test organization",
                "Member",
                project)),
            budgetService,
            costEstimator,
            NullLogger<GenerationService>.Instance,
            TimeProvider.System,
            Options.Create(new OpenAiImageOptions()),
            Options.Create(new OpenAiSpeechOptions()),
            speechSynchronizationOptions: Options.Create(new SpeechSynchronizationOptions
            {
                CanonicalVoiceEnabled = true,
                SpeechVerificationEnabled = true
            }));

    private static GenerateSceneVoiceRequest CreateRequest(Project project, Scene scene)
    {
        var narration = GenerationService.NormalizeNarration(scene.Narration);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(narration))).ToLowerInvariant();
        return new GenerateSceneVoiceRequest(
            project.ProjectId,
            scene.SceneId,
            scene.ScenePlanVersion,
            hash,
            $"voice:{scene.SceneId:N}:{hash}");
    }

    private static VideoFactoryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"scene-voice-{Guid.NewGuid():N}")
            .Options);

    private static (Project Project, Scene Scene) SeedProject(VideoFactoryDbContext dbContext)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            ProjectId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            RemoteUserId = "user-1",
            CreatedByUserId = "user-1",
            Name = "Voice test",
            Topic = "Voice test",
            LanguageCode = "vi-VN",
            VoiceCode = "female-sweet",
            VoiceSpeakingRate = 1m,
            SpeechProductionPolicy = SpeechProductionPolicies.CanonicalVoice,
            Platform = "YouTube",
            AspectRatio = "16:9",
            Status = "GeneratingScenes",
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
            StructureType = "Narrative",
            FullText = "Xin chào các bạn.",
            StoryBeatsJson = "[]",
            Status = "Approved",
            CreatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var style = new StyleProfile
        {
            StyleProfileId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            Version = 1,
            Name = "Default",
            VisualStyleJson = "{}",
            Status = "Approved",
            CreatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var scene = new Scene
        {
            SceneId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            ScriptId = script.ScriptId,
            StyleProfileId = style.StyleProfileId,
            ScenePlanVersion = 1,
            SequenceNumber = 1,
            StoryPurpose = "Hook",
            Narration = "Xin chào các bạn.",
            VisualDescription = "Presenter",
            GenerationDurationMs = 5_000,
            ContentDurationMs = 5_000,
            EntryStateJson = "{}",
            ExitStateJson = "{}",
            Status = "Approved",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
        const string providerVoiceCode = "shimmer";
        const string voiceInstructions = "Speak natural Vietnamese clearly, warmly, and at an educational narration pace.";
        var voiceProfile = new VoiceProfile
        {
            VoiceProfileId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            Scope = VoiceProfileScopes.ProjectNarrator,
            CreatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var snapshotJson = JsonSerializer.Serialize(new
        {
            Scope = VoiceProfileScopes.ProjectNarrator,
            CharacterId = (Guid?)null,
            ProviderCode = ProviderCodes.OpenAi,
            ModelCode = "gpt-4o-mini-tts",
            VoiceCode = project.VoiceCode,
            ProviderVoiceCode = providerVoiceCode,
            LanguageCode = project.LanguageCode,
            SpeakingRate = project.VoiceSpeakingRate,
            Instructions = voiceInstructions
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var voiceVersion = new VoiceProfileVersion
        {
            VoiceProfileVersionId = Guid.NewGuid(),
            VoiceProfileId = voiceProfile.VoiceProfileId,
            Version = 1,
            ProviderCode = ProviderCodes.OpenAi,
            ModelCode = "gpt-4o-mini-tts",
            VoiceCode = project.VoiceCode!,
            ProviderVoiceCode = providerVoiceCode,
            LanguageCode = project.LanguageCode,
            SpeakingRate = project.VoiceSpeakingRate ?? 1m,
            VoiceInstructions = voiceInstructions,
            SnapshotHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson))).ToLowerInvariant(),
            Status = VoiceProfileVersionStatuses.Approved,
            PreviewProviderRequestId = Guid.NewGuid(),
            PreviewSha256 = new string('f', 64),
            PreviewDurationMs = 1_500,
            PreviewExpiresAtUtc = now.AddHours(1),
            CreatedAtUtc = now,
            ApprovedAtUtc = now,
            RowVersion = new byte[8],
            VoiceProfile = voiceProfile
        };
        voiceProfile.Versions.Add(voiceVersion);
        project.ApprovedNarratorVoiceProfileVersionId = voiceVersion.VoiceProfileVersionId;
        dbContext.AddRange(project, script, style, scene, voiceProfile, voiceVersion);
        dbContext.SaveChanges();
        return (project, scene);
    }

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

    private sealed class StubProviderResolver : IProviderRuntimeResolver
    {
        public int ResolveCount { get; private set; }

        public Task<ProviderRuntimeConfiguration> ResolveAsync(Guid organizationId, string providerCode, string modality, Guid? credentialId, CancellationToken cancellationToken)
        {
            ResolveCount++;
            return Task.FromResult(new ProviderRuntimeConfiguration(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ProviderCodes.OpenAi, "gpt-4o-mini-tts",
                new Uri("https://api.openai.com/v1/"), "Bearer", null, "test-key"));
        }

        public Task<GenerationProviderStatusResponse> GetStatusAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(new GenerationProviderStatusResponse(
                true, "gpt-5.6-luna", true, "kling-3.0",
                OpenAiVoiceReady: true, OpenAiVoiceModel: "gpt-4o-mini-tts"));
    }

    private sealed class StubSpeechClient(Exception? exception = null) : IOpenAiSpeechClient
    {
        public int CallCount { get; private set; }

        public Task<OpenAiSpeechResult> GenerateAsync(ProviderRuntimeConfiguration provider, string narration, string providerVoiceCode, string instructions, decimal speakingRate, CancellationToken cancellationToken)
        {
            CallCount++;
            if (exception is not null)
            {
                return Task.FromException<OpenAiSpeechResult>(exception);
            }
            var bytes = Encoding.ASCII.GetBytes("validated-wave-payload");
            return Task.FromResult(new OpenAiSpeechResult(
                new ValidatedGeneratedVoice(
                    bytes,
                    "audio/wav",
                    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                    1_500,
                    24_000,
                    1),
                "speech-request-1"));
        }
    }

    private sealed class StubCostEstimator(decimal cost) : IAiCostEstimator
    {
        private const string Snapshot = "[{\"usageType\":\"InputToken\",\"unit\":\"MillionTokens\",\"unitPrice\":1},{\"usageType\":\"OutputToken\",\"unit\":\"MillionTokens\",\"unitPrice\":2}]";

        public Task<AiCostQuote> QuoteOpenAiVoiceAsync(Guid providerModelId, int narrationCharacters, decimal estimatedCharactersPerSecond, long estimatedOutputTokensPerSecond, CancellationToken cancellationToken) =>
            Task.FromResult(new AiCostQuote(cost, "USD", Snapshot, 10, 100));

        public Task<AiCostQuote> QuoteOpenAiAsync(Guid providerModelId, int topicCharacters, int targetDurationSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiCostQuote> QuoteOpenAiImageAsync(Guid providerModelId, int promptCharacters, long estimatedInputTokens, long estimatedOutputTokens, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiCostQuote> QuoteKlingAsync(Guid providerModelId, int durationSeconds, string resolution, bool nativeAudio, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<decimal> CalculateOpenAiActualAsync(string rateSnapshotJson, long inputTokens, long outputTokens, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubBudgetService(string? reserveErrorCode = null) : IAiBudgetService
    {
        public int ReserveCount { get; private set; }
        public int SettleCount { get; private set; }
        public int ReleaseCount { get; private set; }

        public Task<BudgetSnapshot> GetSnapshotAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(new BudgetSnapshot(Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddMonths(1), 10, 0, 0, 10, "USD"));

        public Task<BudgetReservationResult> ReserveAsync(Guid organizationId, string userId, Guid projectId, Guid providerRequestId, string operationKey, string providerCode, string modelCode, decimal amount, CancellationToken cancellationToken)
        {
            ReserveCount++;
            return reserveErrorCode is null
                ? Task.FromResult(new BudgetReservationResult(Guid.NewGuid(), Guid.NewGuid(), amount, "USD"))
                : Task.FromException<BudgetReservationResult>(new AccountApiException(503, reserveErrorCode, "blocked before outbound"));
        }

        public Task SettleAsync(Guid reservationId, decimal actualAmount, Guid? organizationProviderCredentialId, object? usage, object? rateSnapshot, CancellationToken cancellationToken)
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

    private sealed class UnusedContentClient : IOpenAiContentClient
    {
        public Task<OpenAiContentResult> GenerateAsync(ProviderRuntimeConfiguration provider, string topic, string languageCode, string platform, string aspectRatio, int targetDurationSeconds, string safetyIdentifier, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedImageClient : IOpenAiImageClient
    {
        public Task<OpenAiImageResult> GenerateAsync(ProviderRuntimeConfiguration provider, string prompt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedKlingClient : IKlingVideoClient
    {
        public Task<KlingTaskResult> SubmitAsync(ProviderRuntimeConfiguration provider, string prompt, string aspectRatio, int durationSeconds, string resolution, bool nativeAudio, string externalTaskId, KlingReferenceImageData? referenceImage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<KlingTaskResult> GetStatusAsync(ProviderRuntimeConfiguration provider, string externalRequestId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
