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

public sealed class CharacterImageGenerationServiceTests
{
    [Fact]
    public async Task GenerateImage_IdempotentReplayDoesNotCallOrChargeTwice()
    {
        await using var dbContext = CreateContext();
        var (project, character) = SeedProject(dbContext);
        var imageClient = new StubImageClient();
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, imageClient, budget, new StubCostEstimator(0.25m));
        var request = new GenerateCharacterReferenceImageRequest(
            project.ProjectId,
            character.CharacterId,
            "character-image-idempotent",
            project.OrganizationId);

        var first = await service.GenerateCharacterReferenceImageAsync(request, "user-1", Guid.NewGuid(), CancellationToken.None);
        var replay = await service.GenerateCharacterReferenceImageAsync(request, "user-1", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(first, replay);
        Assert.Equal(1, imageClient.CallCount);
        Assert.Equal(1, budget.ReserveCount);
        Assert.Equal(1, budget.SettleCount);
        Assert.Equal(0, budget.ReleaseCount);
        Assert.Contains("InputToken", budget.LastRateSnapshotJson);
        Assert.Contains("OutputToken", budget.LastRateSnapshotJson);
        Assert.Single(dbContext.ProviderRequests);
        Assert.Single(dbContext.GeneratedImageOutputs);
        var requestLog = await dbContext.ProviderRequests.SingleAsync();
        Assert.Equal(character.CharacterId, requestLog.CharacterId);
        Assert.DoesNotContain(character.Name, requestLog.RequestJson, StringComparison.Ordinal);
        Assert.DoesNotContain("b64_json", requestLog.ResponseJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("organization_budget_exceeded", 0.25)]
    [InlineData("pricing_not_configured", 0)]
    public async Task GenerateImage_BudgetOrPricingFailureStopsBeforeOutbound(
        string errorCode,
        decimal estimatedCost)
    {
        await using var dbContext = CreateContext();
        var (project, character) = SeedProject(dbContext);
        var imageClient = new StubImageClient();
        var budget = new StubBudgetService(errorCode);
        var service = CreateService(dbContext, project, imageClient, budget, new StubCostEstimator(estimatedCost));
        var request = new GenerateCharacterReferenceImageRequest(
            project.ProjectId,
            character.CharacterId,
            $"character-image-{errorCode}",
            project.OrganizationId);

        var exception = await Assert.ThrowsAsync<AccountApiException>(
            () => service.GenerateCharacterReferenceImageAsync(request, "user-1", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(errorCode, exception.Code);
        Assert.Equal(0, imageClient.CallCount);
        Assert.Empty(dbContext.ProviderRequests);
    }

    [Theory]
    [InlineData("organization_generation_denied")]
    [InlineData("project_not_found")]
    public async Task GenerateImage_AccessFailureStopsBeforeResolverAndOutbound(string errorCode)
    {
        await using var dbContext = CreateContext();
        var (project, character) = SeedProject(dbContext);
        var imageClient = new StubImageClient();
        var budget = new StubBudgetService();
        var resolver = new StubProviderResolver();
        var service = CreateService(
            dbContext,
            project,
            imageClient,
            budget,
            new StubCostEstimator(0.25m),
            resolver,
            new StubAccessService(new AccountApiException(403, errorCode, "denied")));

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.GenerateCharacterReferenceImageAsync(
                new GenerateCharacterReferenceImageRequest(
                    project.ProjectId,
                    character.CharacterId,
                    $"character-image-{errorCode}",
                    project.OrganizationId),
                "user-1",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal(errorCode, exception.Code);
        Assert.Equal(0, resolver.ResolveCount);
        Assert.Equal(0, imageClient.CallCount);
        Assert.Equal(0, budget.ReserveCount);
    }

    [Fact]
    public async Task GenerateImage_ProviderFailureReleasesReservation()
    {
        await using var dbContext = CreateContext();
        var (project, character) = SeedProject(dbContext);
        var imageClient = new StubImageClient(new ProviderHttpException(
            ProviderCodes.OpenAi,
            "openai_http_500",
            "provider failed"));
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, imageClient, budget, new StubCostEstimator(0.25m));

        await Assert.ThrowsAsync<AccountApiException>(() =>
            service.GenerateCharacterReferenceImageAsync(
                new GenerateCharacterReferenceImageRequest(
                    project.ProjectId,
                    character.CharacterId,
                    "character-image-provider-failure",
                    project.OrganizationId),
                "user-1",
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal(1, imageClient.CallCount);
        Assert.Equal(1, budget.ReleaseCount);
        Assert.Equal("Failed", (await dbContext.ProviderRequests.SingleAsync()).Status);
    }

    private static GenerationService CreateService(
        VideoFactoryDbContext dbContext,
        Project project,
        StubImageClient imageClient,
        StubBudgetService budget,
        StubCostEstimator costEstimator,
        StubProviderResolver? resolver = null,
        StubAccessService? accessService = null) =>
        new(
            dbContext,
            resolver ?? new StubProviderResolver(),
            new UnusedContentClient(),
            imageClient,
            new UnusedSpeechClient(),
            new UnusedKlingClient(),
            accessService ?? new StubAccessService(new GenerationAccessContext(
                project.OrganizationId!.Value,
                "Test organization",
                "Member",
                project)),
            budget,
            costEstimator,
            NullLogger<GenerationService>.Instance,
            TimeProvider.System,
            Options.Create(new OpenAiImageOptions()),
            Options.Create(new OpenAiSpeechOptions()));

    private static VideoFactoryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"character-image-{Guid.NewGuid():N}")
            .Options);

    private static (Project Project, Character Character) SeedProject(VideoFactoryDbContext dbContext)
    {
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
            Status = "Draft",
            CurrencyCode = "USD",
            WorkspaceRelativePath = "test",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8]
        };
        var character = new Character
        {
            CharacterId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            CharacterKey = "hero",
            Version = 1,
            Name = "Sensitive Character Name",
            Role = "Presenter",
            VisualIdentity = "Vietnamese presenter",
            ProfileJson = "{\"gender\":\"female\",\"age\":30,\"face\":\"oval\",\"hair\":\"black\",\"immutableTraits\":[\"oval face\"]}",
            WardrobeJson = "{\"clothing\":\"blue shirt\",\"accessories\":\"glasses\"}",
            ForbiddenChangesJson = "[\"no logo\"]",
            Status = "Draft",
            CreatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8],
            Project = project
        };
        dbContext.AddRange(project, character);
        dbContext.SaveChanges();
        return (project, character);
    }

    private sealed class StubAccessService : IGenerationAccessService
    {
        private readonly GenerationAccessContext? _context;
        private readonly AccountApiException? _exception;

        public StubAccessService(GenerationAccessContext context) => _context = context;

        public StubAccessService(AccountApiException exception) => _exception = exception;

        public Task<GenerationAccessContext> RequireAsync(
            string userId,
            Guid deviceId,
            Guid? requestedOrganizationId,
            Guid? projectId,
            CancellationToken cancellationToken) =>
            _exception is not null
                ? Task.FromException<GenerationAccessContext>(_exception)
                : Task.FromResult(_context!);
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
                ProviderCodes.OpenAi,
                "gpt-image-2",
                new Uri("https://api.openai.com/v1/"),
                "Bearer",
                null,
                "test-key"));
        }

        public Task<GenerationProviderStatusResponse> GetStatusAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(new GenerationProviderStatusResponse(true, "gpt-5.6-luna", true, "kling-3.0"));
    }

    private sealed class StubImageClient(Exception? exception = null) : IOpenAiImageClient
    {
        public int CallCount { get; private set; }

        public Task<OpenAiImageResult> GenerateAsync(
            ProviderRuntimeConfiguration provider,
            string prompt,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (exception is not null)
            {
                return Task.FromException<OpenAiImageResult>(exception);
            }
            var bytes = new byte[24];
            var image = new ValidatedGeneratedImage(
                bytes,
                "image/png",
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                1024,
                1024);
            return Task.FromResult(new OpenAiImageResult(image, 100, 200, "provider-request-1"));
        }
    }

    private sealed class StubCostEstimator(decimal imageCost) : IAiCostEstimator
    {
        public Task<AiCostQuote> QuoteOpenAiImageAsync(
            Guid providerModelId,
            int promptCharacters,
            long estimatedInputTokens,
            long estimatedOutputTokens,
            CancellationToken cancellationToken) =>
            Task.FromResult(new AiCostQuote(
                imageCost,
                "USD",
                "[{\"usageType\":\"InputToken\",\"unit\":\"MillionTokens\",\"unitPrice\":1},{\"usageType\":\"OutputToken\",\"unit\":\"MillionTokens\",\"unitPrice\":1}]",
                estimatedInputTokens,
                estimatedOutputTokens));

        public Task<decimal> CalculateOpenAiActualAsync(string rateSnapshotJson, long inputTokens, long outputTokens, CancellationToken cancellationToken) =>
            Task.FromResult(0.0003m);

        public Task<AiCostQuote> QuoteOpenAiAsync(Guid providerModelId, int topicCharacters, int targetDurationSeconds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AiCostQuote> QuoteOpenAiVoiceAsync(Guid providerModelId, int narrationCharacters, decimal estimatedCharactersPerSecond, long estimatedOutputTokensPerSecond, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AiCostQuote> QuoteKlingAsync(Guid providerModelId, int durationSeconds, string resolution, bool nativeAudio, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubBudgetService(string? reserveErrorCode = null) : IAiBudgetService
    {
        public int ReserveCount { get; private set; }

        public int SettleCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public string LastRateSnapshotJson { get; private set; } = string.Empty;

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
            if (reserveErrorCode is not null)
            {
                return Task.FromException<BudgetReservationResult>(new AccountApiException(
                    503,
                    reserveErrorCode,
                    "blocked before outbound"));
            }
            return Task.FromResult(new BudgetReservationResult(Guid.NewGuid(), Guid.NewGuid(), amount, "USD"));
        }

        public Task SettleAsync(Guid reservationId, decimal actualAmount, Guid? organizationProviderCredentialId, object? usage, object? rateSnapshot, CancellationToken cancellationToken)
        {
            SettleCount++;
            LastRateSnapshotJson = JsonSerializer.Serialize(rateSnapshot);
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
        public Task<OpenAiContentResult> GenerateAsync(ProviderRuntimeConfiguration provider, string topic, string languageCode, string platform, string aspectRatio, int targetDurationSeconds, string safetyIdentifier, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedSpeechClient : IOpenAiSpeechClient
    {
        public Task<OpenAiSpeechResult> GenerateAsync(ProviderRuntimeConfiguration provider, string narration, string providerVoiceCode, string instructions, decimal speakingRate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedKlingClient : IKlingVideoClient
    {
        public Task<KlingTaskResult> SubmitAsync(ProviderRuntimeConfiguration provider, string prompt, string aspectRatio, int durationSeconds, string resolution, bool nativeAudio, string externalTaskId, KlingReferenceImageData? referenceImage, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<KlingTaskResult> GetStatusAsync(ProviderRuntimeConfiguration provider, string externalRequestId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
