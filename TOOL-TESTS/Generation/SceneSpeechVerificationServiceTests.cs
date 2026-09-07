using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
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

public sealed class SceneSpeechVerificationServiceTests
{
    [Fact]
    public async Task VerifySceneSpeechAsync_CompletesAndReplaysWithoutSecondCharge()
    {
        await using var dbContext = CreateContext();
        var (project, scene) = SeedProject(dbContext);
        var wav = CreatePcmWav(16_000, 1, 1);
        var mediaHash = Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant();
        var expectedHash = Hash(GenerationService.NormalizeNarration(scene.Narration));
        var request = new VerifySceneSpeechRequest(
            project.ProjectId,
            scene.SceneId,
            scene.ScenePlanVersion,
            expectedHash,
            mediaHash,
            1_000,
            $"verify:{scene.SceneId:N}:{mediaHash}",
            SourceMediaAssetId: dbContext.MediaAssets.Single().MediaAssetId);
        var transcription = new StubTranscriptionClient("Xin chào Việt Nam");
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, transcription, budget, 0.01m);

        var first = await service.VerifySceneSpeechAsync(
            request, wav, "user-1", Guid.NewGuid(), CancellationToken.None);
        var replay = await service.VerifySceneSpeechAsync(
            request, wav, "user-1", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(SpeechVerificationStatuses.Passed, first.Status);
        Assert.Equal(first.SpeechVerificationReportId, replay.SpeechVerificationReportId);
        Assert.Equal(1, transcription.CallCount);
        Assert.Equal(1, budget.ReserveCount);
        Assert.Equal(1, budget.SettleCount);
        var report = await dbContext.SpeechVerificationReports.SingleAsync();
        Assert.Equal("Xin chào Việt Nam", report.Transcript);
        var providerRequest = await dbContext.ProviderRequests.SingleAsync();
        Assert.DoesNotContain("Xin chào Việt Nam", providerRequest.RequestJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Xin chào Việt Nam", providerRequest.ResponseJson, StringComparison.Ordinal);
        Assert.Equal("SpeechReviewRequired", (await dbContext.Scenes.SingleAsync()).SpeechStatus);
    }

    [Fact]
    public async Task ApproveSpeechVerificationReviewAsync_RequiresAuditedOverrideAndReplaysIdempotently()
    {
        await using var dbContext = CreateContext();
        var (project, scene) = SeedProject(dbContext);
        var wav = CreatePcmWav(16_000, 1, 1);
        var mediaHash = Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant();
        var service = CreateService(
            dbContext,
            project,
            new StubTranscriptionClient("noi dung khac"),
            new StubBudgetService(),
            0.01m);

        var verification = await service.VerifySceneSpeechAsync(
            CreateRequest(dbContext, project, scene, mediaHash),
            wav,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(SpeechVerificationStatuses.NeedsReview, verification.Status);
        Assert.False(verification.ReviewApproved);
        Assert.False(string.IsNullOrWhiteSpace(verification.RowVersion));

        const string reason = "Da nghe doi chieu va chap nhan sai lech phat am.";
        var request = new ApproveSpeechVerificationReviewRequest(
            project.ProjectId,
            scene.SceneId,
            verification.SpeechVerificationReportId,
            reason,
            verification.RowVersion!);
        var approved = await service.ApproveSpeechVerificationReviewAsync(
            request,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var replay = await service.ApproveSpeechVerificationReviewAsync(
            request,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(approved.ReviewApproved);
        Assert.Equal(reason, approved.ReviewReason);
        Assert.NotNull(approved.ReviewedAtUtc);
        Assert.Equal(approved.SpeechVerificationReportId, replay.SpeechVerificationReportId);
        var report = await dbContext.SpeechVerificationReports.SingleAsync();
        Assert.Equal("user-1", report.ReviewedByUserId);
        Assert.Equal(reason, report.ReviewReason);
    }

    [Fact]
    public async Task ApproveSpeechVerificationReviewAsync_RejectsInvalidReasonAndStaleRowVersion()
    {
        await using var dbContext = CreateContext();
        var (project, scene) = SeedProject(dbContext);
        var wav = CreatePcmWav(16_000, 1, 1);
        var mediaHash = Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant();
        var service = CreateService(
            dbContext,
            project,
            new StubTranscriptionClient("noi dung khac"),
            new StubBudgetService(),
            0.01m);
        var verification = await service.VerifySceneSpeechAsync(
            CreateRequest(dbContext, project, scene, mediaHash),
            wav,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        var invalidReason = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.ApproveSpeechVerificationReviewAsync(
                new ApproveSpeechVerificationReviewRequest(
                    project.ProjectId,
                    scene.SceneId,
                    verification.SpeechVerificationReportId,
                    "qua ngan",
                    verification.RowVersion!),
                "user-1",
                Guid.NewGuid(),
                CancellationToken.None));
        Assert.Equal(SpeechSynchronizationErrorCodes.SpeechVerificationReviewInvalid, invalidReason.Code);

        var staleVersion = Convert.ToBase64String(Enumerable.Repeat((byte)1, 8).ToArray());
        var stale = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.ApproveSpeechVerificationReviewAsync(
                new ApproveSpeechVerificationReviewRequest(
                    project.ProjectId,
                    scene.SceneId,
                    verification.SpeechVerificationReportId,
                    "Chap nhan sai lech sau khi nghe doi chieu.",
                    staleVersion),
                "user-1",
                Guid.NewGuid(),
                CancellationToken.None));
        Assert.Equal("speech_verification_changed", stale.Code);
        Assert.False((await dbContext.SpeechVerificationReports.SingleAsync()).ReviewApproved);
    }

    [Fact]
    public async Task VerifySceneSpeechAsync_MissingPricingStopsBeforeReservationAndOutbound()
    {
        await using var dbContext = CreateContext();
        var (project, scene) = SeedProject(dbContext);
        var wav = CreatePcmWav(16_000, 1, 1);
        var mediaHash = Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant();
        var transcription = new StubTranscriptionClient("Xin chào Việt Nam");
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, transcription, budget, 0m);

        var exception = await Assert.ThrowsAsync<TOOL_SERVER.Authentication.AccountApiException>(() =>
            service.VerifySceneSpeechAsync(
                new VerifySceneSpeechRequest(
                    project.ProjectId,
                    scene.SceneId,
                    scene.ScenePlanVersion,
                    Hash(GenerationService.NormalizeNarration(scene.Narration)),
                    mediaHash,
                    1_000,
                    $"verify:{scene.SceneId:N}:{mediaHash}",
                    SourceMediaAssetId: dbContext.MediaAssets.Single().MediaAssetId),
                wav,
                "user-1",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("pricing_not_configured", exception.Code);
        Assert.Equal(0, budget.ReserveCount);
        Assert.Equal(0, transcription.CallCount);
    }

    [Fact]
    public async Task VerifySceneSpeechAsync_CanonicalVoiceStopsBeforePricingAndOutbound()
    {
        await using var dbContext = CreateContext();
        var (project, scene) = SeedProject(dbContext);
        var wav = CreatePcmWav(16_000, 1, 1);
        var mediaHash = Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant();
        var expectedHash = Hash(GenerationService.NormalizeNarration(scene.Narration));
        var (sourceAsset, _) = ConfigureCanonicalVoice(
            dbContext,
            project,
            scene,
            mediaHash,
            expectedHash);
        await dbContext.SaveChangesAsync();
        var transcription = new StubTranscriptionClient("Xin chào Việt Nam");
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, transcription, budget, 0.01m);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => service.VerifySceneSpeechAsync(
            new VerifySceneSpeechRequest(
                project.ProjectId,
                scene.SceneId,
                scene.ScenePlanVersion,
                expectedHash,
                mediaHash,
                1_000,
                $"verify:{scene.SceneId:N}:{mediaHash}",
                SourceMediaAssetId: sourceAsset.MediaAssetId),
            wav,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal(SpeechSynchronizationErrorCodes.SpeechVerificationNotRequired, exception.Code);
        Assert.Equal(0, transcription.CallCount);
        Assert.Equal(0, budget.ReserveCount);
        Assert.Empty(dbContext.SpeechVerificationReports);
    }

    [Fact]
    public async Task VerifySceneSpeechAsync_BudgetFailureStopsBeforeOutbound()
    {
        await using var dbContext = CreateContext();
        var (project, scene) = SeedProject(dbContext);
        var wav = CreatePcmWav(16_000, 1, 1);
        var mediaHash = Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant();
        var transcription = new StubTranscriptionClient("Xin chào Việt Nam");
        var budget = new StubBudgetService("organization_budget_exceeded");
        var service = CreateService(dbContext, project, transcription, budget, 0.01m);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.VerifySceneSpeechAsync(
                CreateRequest(dbContext, project, scene, mediaHash),
                wav,
                "user-1",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("organization_budget_exceeded", exception.Code);
        Assert.Equal(1, budget.ReserveCount);
        Assert.Equal(0, transcription.CallCount);
        Assert.Empty(await dbContext.ProviderRequests.ToListAsync());
    }

    [Fact]
    public async Task VerifySceneSpeechAsync_ProviderFailureMarksFailedAndReleasesReservation()
    {
        await using var dbContext = CreateContext();
        var (project, scene) = SeedProject(dbContext);
        var wav = CreatePcmWav(16_000, 1, 1);
        var mediaHash = Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant();
        var transcription = new StubTranscriptionClient(
            "",
            new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_transcription_rate_limited",
                "OpenAI rate limited the transcription request."));
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, transcription, budget, 0.01m);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.VerifySceneSpeechAsync(
                CreateRequest(dbContext, project, scene, mediaHash),
                wav,
                "user-1",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("openai_transcription_rate_limited", exception.Code);
        Assert.Equal(1, transcription.CallCount);
        Assert.Equal(1, budget.ReleaseCount);
        Assert.Equal(0, budget.SettleCount);
        Assert.Equal("Failed", (await dbContext.ProviderRequests.SingleAsync()).Status);
        Assert.Equal(SpeechVerificationStatuses.Failed, (await dbContext.SpeechVerificationReports.SingleAsync()).Status);
        Assert.Equal("SpeechInvalid", (await dbContext.Scenes.SingleAsync()).SpeechStatus);
    }

    [Fact]
    public async Task VerifySceneSpeechAsync_InvalidWaveStopsBeforePricingAndOutbound()
    {
        await using var dbContext = CreateContext();
        var (project, scene) = SeedProject(dbContext);
        var invalidWav = new byte[] { 1, 2, 3, 4 };
        var mediaHash = Convert.ToHexString(SHA256.HashData(invalidWav)).ToLowerInvariant();
        var resolver = new StubProviderResolver();
        var transcription = new StubTranscriptionClient("Xin chào Việt Nam");
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, transcription, budget, 0.01m, resolver);

        var exception = await Assert.ThrowsAsync<ProviderHttpException>(() =>
            service.VerifySceneSpeechAsync(
                CreateRequest(dbContext, project, scene, mediaHash),
                invalidWav,
                "user-1",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("voice_audio_invalid", exception.Code);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(0, budget.ReserveCount);
        Assert.Equal(0, transcription.CallCount);
    }

    [Fact]
    public async Task VerifySceneSpeechAsync_AccessDeniedStopsBeforeProviderResolution()
    {
        await using var dbContext = CreateContext();
        var (project, scene) = SeedProject(dbContext);
        var wav = CreatePcmWav(16_000, 1, 1);
        var mediaHash = Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant();
        var resolver = new StubProviderResolver();
        var transcription = new StubTranscriptionClient("Xin chào Việt Nam");
        var service = CreateService(
            dbContext,
            project,
            transcription,
            new StubBudgetService(),
            0.01m,
            resolver,
            new StubAccessService(new AccountApiException(
                403,
                "organization_generation_denied",
                "Viewer cannot generate AI output.")));

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.VerifySceneSpeechAsync(
                CreateRequest(dbContext, project, scene, mediaHash),
                wav,
                "viewer-1",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("organization_generation_denied", exception.Code);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(0, transcription.CallCount);
    }

    private static GenerationService CreateService(
        VideoFactoryDbContext dbContext,
        Project project,
        StubTranscriptionClient transcription,
        StubBudgetService budget,
        decimal cost,
        StubProviderResolver? resolver = null,
        IGenerationAccessService? generationAccessService = null) =>
        new(
            dbContext,
            resolver ?? new StubProviderResolver(),
            new UnusedContentClient(),
            new UnusedImageClient(),
            new UnusedSpeechClient(),
            new UnusedKlingClient(),
            generationAccessService ?? new StubAccessService(new GenerationAccessContext(
                project.OrganizationId!.Value,
                "Test organization",
                "Member",
                project)),
            budget,
            new StubCostEstimator(cost),
            NullLogger<GenerationService>.Instance,
            TimeProvider.System,
            Options.Create(new OpenAiImageOptions()),
            Options.Create(new OpenAiSpeechOptions()),
            openAiTranscriptionClient: transcription,
            transcriptionOptions: Options.Create(new OpenAiTranscriptionOptions()),
            speechSynchronizationOptions: Options.Create(new SpeechSynchronizationOptions
            {
                CanonicalVoiceEnabled = true,
                SpeechVerificationEnabled = true
            }));

    private static VerifySceneSpeechRequest CreateRequest(
        VideoFactoryDbContext dbContext,
        Project project,
        Scene scene,
        string mediaHash) =>
        new(
            project.ProjectId,
            scene.SceneId,
            scene.ScenePlanVersion,
            Hash(GenerationService.NormalizeNarration(scene.Narration)),
            mediaHash,
            1_000,
            $"verify:{scene.SceneId:N}:{mediaHash}",
            SourceMediaAssetId: dbContext.MediaAssets.Single().MediaAssetId);

    private static VideoFactoryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"speech-verification-{Guid.NewGuid():N}")
            .Options);

    private static (Project Project, Scene Scene) SeedProject(VideoFactoryDbContext dbContext)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            ProjectId = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), RemoteUserId = "user-1",
            CreatedByUserId = "user-1", Name = "Speech verification", Topic = "Speech verification",
            LanguageCode = "vi-VN", Platform = "YouTube", AspectRatio = "16:9", Status = "GeneratingScenes",
            CurrencyCode = "USD", WorkspaceRelativePath = "test", CreatedAtUtc = now, UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var script = new Script
        {
            ScriptId = Guid.NewGuid(), ProjectId = project.ProjectId, Version = 1, StructureType = "Narrative",
            FullText = "Xin chào Việt Nam", StoryBeatsJson = "[]", Status = "Approved", CreatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var style = new StyleProfile
        {
            StyleProfileId = Guid.NewGuid(), ProjectId = project.ProjectId, Version = 1, Name = "Default",
            VisualStyleJson = "{}", Status = "Approved", CreatedAtUtc = now, RowVersion = new byte[8]
        };
        var scene = new Scene
        {
            SceneId = Guid.NewGuid(), ProjectId = project.ProjectId, ScriptId = script.ScriptId,
            StyleProfileId = style.StyleProfileId, ScenePlanVersion = 1, SequenceNumber = 1,
            StoryPurpose = "Hook", Narration = "Xin chào Việt Nam", VisualDescription = "Presenter",
            GenerationDurationMs = 5_000, ContentDurationMs = 5_000, EntryStateJson = "{}", ExitStateJson = "{}",
            Status = "AudioReviewRequired", SpeechStatus = "SpeechVerificationRequired",
            CreatedAtUtc = now, UpdatedAtUtc = now, RowVersion = new byte[8]
        };
        var sourceAsset = new MediaAsset
        {
            MediaAssetId = Guid.NewGuid(), ProjectId = project.ProjectId, SceneId = scene.SceneId,
            AssetType = "SceneVideo", DisplayName = "Speech source", RelativePath = "scene.mp4",
            MimeType = "video/mp4", SizeBytes = 1, Sha256 = new string('a', 64), Status = "Ready",
            SourceType = "Generated", CreatedAtUtc = now, RowVersion = new byte[8]
        };
        dbContext.AddRange(project, script, style, scene, sourceAsset);
        dbContext.SaveChanges();
        return (project, scene);
    }

    private static (MediaAsset SourceAsset, Guid VoiceGenerationId) ConfigureCanonicalVoice(
        VideoFactoryDbContext dbContext,
        Project project,
        Scene scene,
        string mediaHash,
        string expectedHash)
    {
        var sourceAsset = dbContext.MediaAssets.Single();
        sourceAsset.AssetType = "SceneVoice";
        sourceAsset.MimeType = "audio/wav";
        sourceAsset.Sha256 = mediaHash;
        sourceAsset.DurationMs = 1_000;
        project.SpeechProductionPolicy = SpeechProductionPolicies.CanonicalVoice;
        var voiceRequestId = Guid.NewGuid();
        var voiceGenerationId = Guid.NewGuid();
        dbContext.ProviderRequests.Add(new ProviderRequest
        {
            ProviderRequestId = voiceRequestId,
            OrganizationId = project.OrganizationId,
            RequestedByUserId = project.RemoteUserId,
            ProjectId = project.ProjectId,
            SceneId = scene.SceneId,
            RequestKind = "Voice",
            ProviderCode = ProviderCodes.OpenAi,
            ModelCode = "gpt-4o-mini-tts",
            IdempotencyKey = $"voice:{scene.SceneId:N}",
            Status = "Completed",
            RequestJson = "{}",
            CurrencyCode = "USD",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8]
        });
        dbContext.VoiceGenerations.Add(new VoiceGeneration
        {
            VoiceGenerationId = voiceGenerationId,
            ProjectId = project.ProjectId,
            ScriptId = scene.ScriptId,
            SceneId = scene.SceneId,
            ScenePlanVersion = scene.ScenePlanVersion,
            ProviderRequestId = voiceRequestId,
            Version = 1,
            VoiceCode = "female-sweet",
            NarrationHash = expectedHash,
            LanguageCode = "vi-VN",
            SpeakingRate = 1m,
            VerificationStatus = SpeechVerificationStatuses.NotRequested,
            Status = "Completed",
            DurationMs = 1_000,
            OutputMediaAssetId = sourceAsset.MediaAssetId,
            CreatedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8]
        });
        return (sourceAsset, voiceGenerationId);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static byte[] CreatePcmWav(int sampleRate, short channels, int durationSeconds)
    {
        const short bitsPerSample = 16;
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        var dataLength = byteRate * durationSeconds;
        var bytes = new byte[44 + dataLength];
        "RIFF"u8.CopyTo(bytes.AsSpan(0, 4));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), bytes.Length - 8);
        "WAVE"u8.CopyTo(bytes.AsSpan(8, 4));
        "fmt "u8.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22, 2), channels);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28, 4), byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32, 2), checked((short)(channels * bitsPerSample / 8)));
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34, 2), bitsPerSample);
        "data"u8.CopyTo(bytes.AsSpan(36, 4));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40, 4), dataLength);
        return bytes;
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

        public Task<ProviderRuntimeConfiguration> ResolveAsync(Guid organizationId, string providerCode, string modality, Guid? credentialId, CancellationToken cancellationToken) =>
            Resolve();

        private Task<ProviderRuntimeConfiguration> Resolve()
        {
            ResolveCount++;
            return Task.FromResult(new ProviderRuntimeConfiguration(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ProviderCodes.OpenAi, "whisper-1",
                new Uri("https://api.openai.com/v1/"), "Bearer", null, "test-key"));
        }
        public Task<GenerationProviderStatusResponse> GetStatusAsync(Guid organizationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubTranscriptionClient(
        string transcript,
        Exception? exception = null,
        bool includeWordTimings = true) : IOpenAiTranscriptionClient
    {
        public int CallCount { get; private set; }
        public Task<OpenAiTranscriptionResult> TranscribeAsync(ProviderRuntimeConfiguration provider, byte[] wavBytes, string languageCode, CancellationToken cancellationToken)
        {
            CallCount++;
            if (exception is not null)
            {
                return Task.FromException<OpenAiTranscriptionResult>(exception);
            }
            return Task.FromResult(new OpenAiTranscriptionResult(
                transcript,
                includeWordTimings
                    ? [new TranscribedWord("Xin", 100, 250), new TranscribedWord("chào", 260, 500)]
                    : [],
                "transcription-request-1"));
        }
    }

    private sealed class StubCostEstimator(decimal cost) : IAiCostEstimator
    {
        public Task<AiCostQuote> QuoteTranscriptionAsync(Guid providerModelId, long durationMs, CancellationToken cancellationToken) =>
            Task.FromResult(new AiCostQuote(cost, "USD", "[{\"usageType\":\"AudioSecond\",\"unit\":\"Second\",\"unitPrice\":0.01}]", 1, 0));
        public Task<AiCostQuote> QuoteOpenAiAsync(Guid providerModelId, int topicCharacters, int targetDurationSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiCostQuote> QuoteOpenAiImageAsync(Guid providerModelId, int promptCharacters, long estimatedInputTokens, long estimatedOutputTokens, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiCostQuote> QuoteOpenAiVoiceAsync(Guid providerModelId, int narrationCharacters, decimal estimatedCharactersPerSecond, long estimatedOutputTokensPerSecond, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiCostQuote> QuoteKlingAsync(Guid providerModelId, int durationSeconds, string resolution, bool nativeAudio, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<decimal> CalculateOpenAiActualAsync(string rateSnapshotJson, long inputTokens, long outputTokens, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubBudgetService(string? reserveErrorCode = null) : IAiBudgetService
    {
        public int ReserveCount { get; private set; }
        public int SettleCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public Task<BudgetSnapshot> GetSnapshotAsync(Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
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
    private sealed class UnusedSpeechClient : IOpenAiSpeechClient
    {
        public Task<OpenAiSpeechResult> GenerateAsync(ProviderRuntimeConfiguration provider, string narration, string providerVoiceCode, string instructions, decimal speakingRate, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class UnusedKlingClient : IKlingVideoClient
    {
        public Task<KlingTaskResult> SubmitAsync(ProviderRuntimeConfiguration provider, string prompt, string aspectRatio, int durationSeconds, string resolution, bool nativeAudio, string externalTaskId, KlingReferenceImageData? referenceImage, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<KlingTaskResult> GetStatusAsync(ProviderRuntimeConfiguration provider, string externalRequestId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
