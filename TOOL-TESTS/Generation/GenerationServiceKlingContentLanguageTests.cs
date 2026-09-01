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

public sealed class GenerationServiceKlingContentLanguageTests
{
    [Fact]
    public async Task InvalidKlingVietnamesePlan_IsFailedButSettlesConsumedOpenAiUsage()
    {
        await using var dbContext = CreateContext();
        var project = SeedProject(dbContext, ProviderCodes.Kling);
        var contentClient = new StubContentClient(CreateEnglishPlan());
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, contentClient, budget);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => service.GenerateContentAsync(
            new GenerateContentRequest(project.ProjectId, "content-language-test", project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, exception.StatusCode);
        Assert.Equal("kling_content_language_invalid", exception.Code);
        Assert.Equal("vi-VN", contentClient.LanguageCode);
        Assert.Equal(1, budget.SettleCount);
        Assert.Equal(0, budget.ReleaseCount);
        Assert.Equal(0.4m, budget.SettledAmount);
        var request = await dbContext.ProviderRequests.SingleAsync();
        Assert.Equal("Failed", request.Status);
        Assert.Equal("kling_content_language_invalid", request.ErrorCode);
        Assert.Equal(120, request.InputTokens);
        Assert.Equal(240, request.OutputTokens);
        Assert.Equal(0.4m, request.ActualCost);
        Assert.Null(request.ResponseJson);
    }

    [Fact]
    public async Task NonKlingLongForm_KeepsTheProjectLanguageAndAcceptsVietnamesePlan()
    {
        await using var dbContext = CreateContext();
        var project = SeedProject(dbContext, ProviderCodes.BytePlus);
        var contentClient = new StubContentClient(CreateVietnamesePlan());
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, contentClient, budget);

        var response = await service.GenerateContentAsync(
            new GenerateContentRequest(project.ProjectId, "content-language-byteplus", project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal("vi-VN", contentClient.LanguageCode);
        Assert.Equal("vi-VN", response.EffectiveGenerationLanguageCode);
        Assert.Null(response.GenerationLanguagePolicyVersion);
        Assert.Equal("Completed", (await dbContext.ProviderRequests.SingleAsync()).Status);
    }

    private static GenerationService CreateService(
        VideoFactoryDbContext dbContext,
        Project project,
        IOpenAiContentClient contentClient,
        IAiBudgetService budget) =>
        new(
            dbContext,
            new StubProviderResolver(),
            contentClient,
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
            Options.Create(new OpenAiSpeechOptions()));

    private static VideoFactoryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"kling-content-language-{Guid.NewGuid():N}")
            .Options);

    private static Project SeedProject(VideoFactoryDbContext dbContext, string videoProviderCode)
    {
        var project = new Project
        {
            ProjectId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            RemoteUserId = "user-1",
            CreatedByUserId = "user-1",
            Name = "Language policy test",
            Topic = "Một thói quen lành mạnh",
            LanguageCode = "vi-VN",
            Platform = "YouTube",
            AspectRatio = "16:9",
            TargetDurationSeconds = 5,
            OutputWidth = 1280,
            OutputHeight = 720,
            OutputFrameRate = 25,
            Status = "Draft",
            VideoProviderCode = videoProviderCode,
            VideoModelCode = videoProviderCode == ProviderCodes.Kling ? "kling-3.0" : "seedance-2.0",
            VideoPolicyVersion = 1,
            VideoResolution = "720p",
            VideoNativeAudio = true,
            CurrencyCode = "USD",
            WorkspaceRelativePath = "test",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8]
        };
        dbContext.Projects.Add(project);
        dbContext.SaveChanges();
        return project;
    }

    private static GeneratedContentPlan CreateEnglishPlan() =>
        new(
            "A Better Habit",
            "Start today",
            "Practical",
            "Adults",
            "Try one habit",
            "Start with one small action.",
            "Natural daylight",
            "subtitles, watermark",
            [],
            [
                new GeneratedContentScene(
                    1,
                    "Opening hook",
                    string.Empty,
                    "A glass of water rests beside a bright window.",
                    5,
                    [],
                    KlingSpeechModes.None,
                    null,
                    "natural and calm",
                    "quiet room tone",
                    "subtle glass movement",
                    ["bright-room"])
            ],
            [
                new GeneratedProjectAsset(
                    "bright-room",
                    "Background",
                    "Bright room",
                    "A clean room with one large window on the left.",
                    [1])
            ]);

    private static GeneratedContentPlan CreateVietnamesePlan() =>
        new(
            "Một thói quen tốt",
            "Hãy bắt đầu ngay hôm nay",
            "Cách tiếp cận thực tế",
            "Người trưởng thành",
            "Hãy thử một thói quen",
            "Bắt đầu bằng một hành động nhỏ.",
            "Ánh sáng tự nhiên",
            "phụ đề, logo, watermark",
            [],
            [
                new GeneratedContentScene(
                    1,
                    "Mở đầu thu hút người xem",
                    string.Empty,
                    "Một cốc nước đặt cạnh cửa sổ sáng trong căn phòng yên tĩnh.",
                    5,
                    [],
                    KlingSpeechModes.None,
                    null,
                    "tự nhiên và bình tĩnh",
                    "âm nền căn phòng yên tĩnh",
                    "tiếng cốc di chuyển nhẹ",
                    ["bright-room"])
            ],
            [
                new GeneratedProjectAsset(
                    "bright-room",
                    "Background",
                    "Căn phòng sáng",
                    "Căn phòng sạch sẽ với một cửa sổ lớn ở bên trái.",
                    [1])
            ]);

    private sealed class StubContentClient(GeneratedContentPlan plan) : IOpenAiContentClient
    {
        public string? LanguageCode { get; private set; }

        public Task<OpenAiContentResult> GenerateAsync(
            ProviderRuntimeConfiguration provider,
            string topic,
            string languageCode,
            string platform,
            string aspectRatio,
            int targetDurationSeconds,
            string safetyIdentifier,
            CancellationToken cancellationToken)
        {
            LanguageCode = languageCode;
            return Task.FromResult(new OpenAiContentResult(plan, 120, 240, "response-1"));
        }
    }

    private sealed class StubProviderResolver : IProviderRuntimeResolver
    {
        public Task<ProviderRuntimeConfiguration> ResolveAsync(
            Guid organizationId,
            string providerCode,
            string modality,
            Guid? credentialId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderRuntimeConfiguration(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ProviderCodes.OpenAi,
                "gpt-5.6-luna",
                new Uri("https://api.openai.com/v1/"),
                "Bearer",
                null,
                "test-key"));

        public Task<GenerationProviderStatusResponse> GetStatusAsync(
            Guid organizationId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubAccessService(GenerationAccessContext context) : IGenerationAccessService
    {
        public Task<GenerationAccessContext> RequireAsync(
            string userId,
            Guid deviceId,
            Guid? requestedOrganizationId,
            Guid? projectId,
            CancellationToken cancellationToken) => Task.FromResult(context);
    }

    private sealed class StubBudgetService : IAiBudgetService
    {
        public int SettleCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public decimal SettledAmount { get; private set; }

        public Task<BudgetSnapshot> GetSnapshotAsync(Guid organizationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BudgetReservationResult> ReserveAsync(
            Guid organizationId,
            string userId,
            Guid projectId,
            Guid providerRequestId,
            string operationKey,
            string providerCode,
            string modelCode,
            decimal amount,
            CancellationToken cancellationToken) =>
            Task.FromResult(new BudgetReservationResult(Guid.NewGuid(), Guid.NewGuid(), amount, "USD"));

        public Task SettleAsync(
            Guid reservationId,
            decimal actualAmount,
            Guid? organizationProviderCredentialId,
            object? usage,
            object? rateSnapshot,
            CancellationToken cancellationToken)
        {
            SettleCount++;
            SettledAmount = actualAmount;
            return Task.CompletedTask;
        }

        public Task ReleaseAsync(Guid reservationId, CancellationToken cancellationToken)
        {
            ReleaseCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubCostEstimator : IAiCostEstimator
    {
        public Task<AiCostQuote> QuoteOpenAiAsync(
            Guid providerModelId,
            int topicCharacters,
            int targetDurationSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiCostQuote(1m, "USD", "[]", 120, 240));

        public Task<decimal> CalculateOpenAiActualAsync(
            string rateSnapshotJson,
            long inputTokens,
            long outputTokens,
            CancellationToken cancellationToken) => Task.FromResult(0.4m);

        public Task<AiCostQuote> QuoteOpenAiImageAsync(Guid providerModelId, int promptCharacters, long estimatedInputTokens, long estimatedOutputTokens, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiCostQuote> QuoteOpenAiVoiceAsync(Guid providerModelId, int narrationCharacters, decimal estimatedCharactersPerSecond, long estimatedOutputTokensPerSecond, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiCostQuote> QuoteKlingAsync(Guid providerModelId, int durationSeconds, string resolution, bool nativeAudio, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
