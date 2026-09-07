using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
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
        Assert.Contains("title", exception.Errors!["fields"]);
        Assert.Contains("title|language_invalid", exception.Errors["reasons"]);
        Assert.Equal("true", Assert.Single(exception.Errors["canRepair"]));
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
        Assert.NotNull(request.ResponseJson);
        Assert.NotNull(request.ErrorDetailsJson);
        var failedResponse = JsonSerializer.Deserialize<GeneratedContentResponse>(
            request.ResponseJson!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(request.ProviderRequestId, failedResponse!.ProviderRequestId);
        Assert.Contains("language_invalid", request.ErrorDetailsJson!, StringComparison.Ordinal);

        var replay = await Assert.ThrowsAsync<AccountApiException>(() => service.GenerateContentAsync(
            new GenerateContentRequest(project.ProjectId, "content-language-test", project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, replay.StatusCode);
        Assert.Contains("title", replay.Errors!["fields"]);
        Assert.Equal(1, contentClient.GenerateCallCount);
        Assert.Equal(1, budget.SettleCount);
    }

    [Fact]
    public async Task InvalidFalVietnamesePlan_ReturnsValidationErrorWithExactFields()
    {
        await using var dbContext = CreateContext();
        var project = SeedProject(dbContext, ProviderCodes.Fal);
        var contentClient = new StubContentClient(CreateEnglishPlan());
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, contentClient, budget);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => service.GenerateContentAsync(
            new GenerateContentRequest(project.ProjectId, "content-language-fal-test", project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, exception.StatusCode);
        Assert.Equal("fal_content_language_invalid", exception.Code);
        Assert.Contains("title", exception.Errors!["fields"]);
        Assert.Contains("scenes[0].visual_prompt", exception.Errors["fields"]);
        Assert.Equal("vi-VN", contentClient.LanguageCode);
        Assert.Equal(1, budget.SettleCount);
        Assert.Equal(0, budget.ReleaseCount);
    }

    [Fact]
    public async Task LatestFailure_IsRestorableAndSuccessfulRepairConsumesItsSingleAttempt()
    {
        await using var dbContext = CreateContext();
        var project = SeedProject(dbContext, ProviderCodes.Fal);
        var contentClient = new StubContentClient(CreateEnglishPlan(), CreateVietnamesePlan());
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, contentClient, budget);

        var firstFailure = await Assert.ThrowsAsync<AccountApiException>(() => service.GenerateContentAsync(
            new GenerateContentRequest(project.ProjectId, "repair-source", project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));
        var failedRequestId = Guid.Parse(Assert.Single(firstFailure.Errors!["providerRequestId"]));

        var restored = await service.GetLatestContentLanguageFailureAsync(
            project.ProjectId,
            project.OrganizationId,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.NotNull(restored);
        Assert.Equal(failedRequestId, restored.FailedProviderRequestId);
        Assert.True(restored.CanRepair);
        Assert.Contains(restored.Violations, x => x.Field == "title");

        var quote = await service.GetContentRepairQuoteAsync(
            new ContentRepairQuoteRequest(project.ProjectId, failedRequestId, project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.Equal(failedRequestId, quote.FailedProviderRequestId);
        Assert.Equal(1m, quote.EstimatedCost);
        Assert.Equal(0, contentClient.RepairCallCount);
        Assert.Equal(1, budget.ReserveCount);

        var trackedSource = await dbContext.ProviderRequests.SingleAsync(x => x.ProviderRequestId == failedRequestId);
        dbContext.Entry(trackedSource).State = EntityState.Detached;
        var repaired = await service.RepairContentAsync(
            new RepairContentRequest(project.ProjectId, failedRequestId, "repair-once", project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.Equal("Một thói quen tốt", repaired.Plan.Title);
        Assert.Equal(1, contentClient.RepairCallCount);
        Assert.Equal(2, budget.SettleCount);
        Assert.Equal(2, budget.ReserveCount);

        var replay = await service.RepairContentAsync(
            new RepairContentRequest(project.ProjectId, failedRequestId, "repair-once", project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.Equal(repaired.ProviderRequestId, replay.ProviderRequestId);
        Assert.Equal(1, contentClient.RepairCallCount);
        Assert.Equal(2, budget.ReserveCount);
        Assert.Equal(2, budget.SettleCount);

        var requests = await dbContext.ProviderRequests.AsNoTracking().OrderBy(x => x.CreatedAtUtc).ToArrayAsync();
        Assert.Equal(2, requests.Length);
        var source = Assert.Single(requests, x => x.ProviderRequestId == failedRequestId);
        var repair = Assert.Single(requests, x => x.RequestKind == "TextRepair");
        Assert.Equal(failedRequestId, repair.ParentProviderRequestId);
        Assert.Contains("\"canRepair\":false", source.ErrorDetailsJson!, StringComparison.OrdinalIgnoreCase);

        var secondAttempt = await Assert.ThrowsAsync<AccountApiException>(() => service.RepairContentAsync(
            new RepairContentRequest(project.ProjectId, failedRequestId, "repair-twice", project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));
        Assert.Contains(secondAttempt.Code, new[] { "content_repair_unavailable", "content_repair_already_attempted" });
        Assert.Equal(1, contentClient.RepairCallCount);

        Assert.Null(await service.GetLatestContentLanguageFailureAsync(
            project.ProjectId,
            project.OrganizationId,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));
    }

    [Fact]
    public async Task InvalidRepair_IsSettledPersistedAndReplayedWithoutAThirdProviderCall()
    {
        await using var dbContext = CreateContext();
        var project = SeedProject(dbContext, ProviderCodes.Fal);
        var contentClient = new StubContentClient(CreateEnglishPlan());
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, contentClient, budget);

        var firstFailure = await Assert.ThrowsAsync<AccountApiException>(() => service.GenerateContentAsync(
            new GenerateContentRequest(project.ProjectId, "repair-invalid-source", project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));
        var sourceRequestId = Guid.Parse(Assert.Single(firstFailure.Errors!["providerRequestId"]));
        var repairRequest = new RepairContentRequest(
            project.ProjectId,
            sourceRequestId,
            "repair-invalid-once",
            project.OrganizationId);

        var repairFailure = await Assert.ThrowsAsync<AccountApiException>(() => service.RepairContentAsync(
            repairRequest,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, repairFailure.StatusCode);
        Assert.Equal("fal_content_language_invalid", repairFailure.Code);
        Assert.Equal("false", Assert.Single(repairFailure.Errors!["canRepair"]));
        Assert.Equal(1, contentClient.RepairCallCount);
        Assert.Equal(2, budget.ReserveCount);
        Assert.Equal(2, budget.SettleCount);
        Assert.Equal(0, budget.ReleaseCount);

        var repairRequestId = Guid.Parse(Assert.Single(repairFailure.Errors["providerRequestId"]));
        var storedRepair = await dbContext.ProviderRequests.AsNoTracking()
            .SingleAsync(x => x.ProviderRequestId == repairRequestId);
        Assert.Equal("TextRepair", storedRepair.RequestKind);
        Assert.Equal(sourceRequestId, storedRepair.ParentProviderRequestId);
        Assert.Equal("Failed", storedRepair.Status);
        Assert.NotNull(storedRepair.ResponseJson);
        Assert.Contains("\"canRepair\":false", storedRepair.ErrorDetailsJson!, StringComparison.OrdinalIgnoreCase);

        var replay = await Assert.ThrowsAsync<AccountApiException>(() => service.RepairContentAsync(
            repairRequest,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, replay.StatusCode);
        Assert.Equal(repairRequestId.ToString("D"), Assert.Single(replay.Errors!["providerRequestId"]));
        Assert.Equal(1, contentClient.RepairCallCount);
        Assert.Equal(2, budget.ReserveCount);
        Assert.Equal(2, budget.SettleCount);

        var secondAttempt = await Assert.ThrowsAsync<AccountApiException>(() => service.RepairContentAsync(
            repairRequest with { IdempotencyKey = "repair-invalid-twice" },
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));
        Assert.Contains(secondAttempt.Code, new[] { "content_repair_unavailable", "content_repair_already_attempted" });
        Assert.Equal(1, contentClient.RepairCallCount);

        var restored = await service.GetLatestContentLanguageFailureAsync(
            project.ProjectId,
            project.OrganizationId,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.NotNull(restored);
        Assert.Equal(repairRequestId, restored.FailedProviderRequestId);
        Assert.False(restored.CanRepair);
    }

    [Theory]
    [InlineData("credential")]
    [InlineData("pricing")]
    [InlineData("budget")]
    public async Task RepairPrerequisiteFailure_StopsBeforeOpenAiOutbound(string failureStage)
    {
        await using var dbContext = CreateContext();
        var project = SeedProject(dbContext, ProviderCodes.Fal);
        var contentClient = new StubContentClient(CreateEnglishPlan(), CreateVietnamesePlan());
        var budget = new StubBudgetService();
        var resolver = new StubProviderResolver();
        var estimator = new StubCostEstimator();
        var service = CreateService(dbContext, project, contentClient, budget, resolver, null, estimator);

        var sourceFailure = await Assert.ThrowsAsync<AccountApiException>(() => service.GenerateContentAsync(
            new GenerateContentRequest(project.ProjectId, $"repair-{failureStage}-source", project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));
        var sourceRequestId = Guid.Parse(Assert.Single(sourceFailure.Errors!["providerRequestId"]));

        switch (failureStage)
        {
            case "credential":
                resolver.FailOnResolveCall = 2;
                break;
            case "pricing":
                estimator.UnconfiguredOnQuoteCall = 2;
                break;
            case "budget":
                budget.FailOnReserveCall = 2;
                break;
        }

        await Assert.ThrowsAnyAsync<Exception>(() => service.RepairContentAsync(
            new RepairContentRequest(
                project.ProjectId,
                sourceRequestId,
                $"repair-{failureStage}-attempt",
                project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal(0, contentClient.RepairCallCount);
        Assert.Equal(1, await dbContext.ProviderRequests.CountAsync());
        Assert.Equal(1, budget.SettleCount);
    }

    [Fact]
    public async Task RepairProviderFailureBeforeUsage_ReleasesTheReservation()
    {
        await using var dbContext = CreateContext();
        var project = SeedProject(dbContext, ProviderCodes.Fal);
        var contentClient = new StubContentClient(
            CreateEnglishPlan(),
            repairException: new ProviderHttpException(
                ProviderCodes.OpenAi,
                "openai_repair_unavailable",
                "OpenAI repair is unavailable."));
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, contentClient, budget);

        var sourceFailure = await Assert.ThrowsAsync<AccountApiException>(() => service.GenerateContentAsync(
            new GenerateContentRequest(project.ProjectId, "repair-provider-source", project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));
        var sourceRequestId = Guid.Parse(Assert.Single(sourceFailure.Errors!["providerRequestId"]));

        var repairFailure = await Assert.ThrowsAsync<AccountApiException>(() => service.RepairContentAsync(
            new RepairContentRequest(project.ProjectId, sourceRequestId, "repair-provider-attempt", project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal("openai_repair_unavailable", repairFailure.Code);
        Assert.Equal(1, contentClient.RepairCallCount);
        Assert.Equal(2, budget.ReserveCount);
        Assert.Equal(1, budget.SettleCount);
        Assert.Equal(1, budget.ReleaseCount);
        var storedRepair = await dbContext.ProviderRequests.AsNoTracking()
            .SingleAsync(x => x.RequestKind == "TextRepair");
        Assert.Equal("Failed", storedRepair.Status);
        Assert.Null(storedRepair.ResponseJson);
    }

    [Fact]
    public async Task RepairThatChangesSceneContract_IsRejectedAndSettlesConsumedUsage()
    {
        await using var dbContext = CreateContext();
        var project = SeedProject(dbContext, ProviderCodes.Fal);
        var repairedPlan = CreateVietnamesePlan();
        repairedPlan = repairedPlan with
        {
            Scenes = [repairedPlan.Scenes[0] with { DurationSeconds = 4 }]
        };
        var contentClient = new StubContentClient(CreateEnglishPlan(), repairedPlan);
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, contentClient, budget);

        var sourceFailure = await Assert.ThrowsAsync<AccountApiException>(() => service.GenerateContentAsync(
            new GenerateContentRequest(project.ProjectId, "repair-invariant-source", project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));
        var sourceRequestId = Guid.Parse(Assert.Single(sourceFailure.Errors!["providerRequestId"]));

        var repairFailure = await Assert.ThrowsAsync<AccountApiException>(() => service.RepairContentAsync(
            new RepairContentRequest(project.ProjectId, sourceRequestId, "repair-invariant-attempt", project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, repairFailure.StatusCode);
        Assert.Equal("openai_content_repair_invariant_invalid", repairFailure.Code);
        Assert.Contains("scenes[0].structure", repairFailure.Errors!["fields"]);
        Assert.Equal(1, contentClient.RepairCallCount);
        Assert.Equal(2, budget.SettleCount);
        Assert.Equal(0, budget.ReleaseCount);
    }

    [Fact]
    public async Task RepairWithAnotherRequestOwner_IsRejectedBeforeQuoteBudgetOrOutbound()
    {
        await using var dbContext = CreateContext();
        var project = SeedProject(dbContext, ProviderCodes.Fal);
        var contentClient = new StubContentClient(CreateEnglishPlan(), CreateVietnamesePlan());
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, contentClient, budget);

        var sourceFailure = await Assert.ThrowsAsync<AccountApiException>(() => service.GenerateContentAsync(
            new GenerateContentRequest(project.ProjectId, "repair-owner-source", project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));
        var sourceRequestId = Guid.Parse(Assert.Single(sourceFailure.Errors!["providerRequestId"]));

        var repairFailure = await Assert.ThrowsAsync<AccountApiException>(() => service.RepairContentAsync(
            new RepairContentRequest(project.ProjectId, sourceRequestId, "repair-owner-attempt", project.OrganizationId),
            "user-2",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status404NotFound, repairFailure.StatusCode);
        Assert.Equal("content_repair_source_not_found", repairFailure.Code);
        Assert.Equal(0, contentClient.RepairCallCount);
        Assert.Equal(1, budget.ReserveCount);
    }

    [Fact]
    public async Task LegacyLanguageFailureWithoutDetails_RemainsReadableButCannotBeRepaired()
    {
        await using var dbContext = CreateContext();
        var project = SeedProject(dbContext, ProviderCodes.Fal);
        var contentClient = new StubContentClient(CreateEnglishPlan());
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, contentClient, budget);

        await Assert.ThrowsAsync<AccountApiException>(() => service.GenerateContentAsync(
            new GenerateContentRequest(project.ProjectId, "legacy-language-source", project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));
        var stored = await dbContext.ProviderRequests.SingleAsync();
        stored.ErrorDetailsJson = null;
        await dbContext.SaveChangesAsync();

        var restored = await service.GetLatestContentLanguageFailureAsync(
            project.ProjectId,
            project.OrganizationId,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.NotNull(restored);
        Assert.Equal(stored.ProviderRequestId, restored.FailedProviderRequestId);
        Assert.Empty(restored.Violations);
        Assert.False(restored.CanRepair);
        var quoteFailure = await Assert.ThrowsAsync<AccountApiException>(() => service.GetContentRepairQuoteAsync(
            new ContentRepairQuoteRequest(project.ProjectId, stored.ProviderRequestId, project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));
        Assert.Equal("content_repair_unavailable", quoteFailure.Code);
        Assert.Equal(0, contentClient.RepairCallCount);
    }

    [Fact]
    public async Task ViewerAccessDenial_BlocksRepairQuoteAndExecutionBeforeProviderResolution()
    {
        await using var dbContext = CreateContext();
        var project = SeedProject(dbContext, ProviderCodes.Fal);
        var contentClient = new StubContentClient(CreateEnglishPlan(), CreateVietnamesePlan());
        var budget = new StubBudgetService();
        var resolver = new StubProviderResolver();
        var access = new RejectingAccessService(new AccountApiException(
            StatusCodes.Status403Forbidden,
            "organization_generation_denied",
            "Vai trò Viewer không có quyền sử dụng AI."));
        var service = CreateService(dbContext, project, contentClient, budget, resolver, access);
        var sourceRequestId = Guid.NewGuid();

        var quoteFailure = await Assert.ThrowsAsync<AccountApiException>(() => service.GetContentRepairQuoteAsync(
            new ContentRepairQuoteRequest(project.ProjectId, sourceRequestId, project.OrganizationId),
            "viewer-1",
            Guid.NewGuid(),
            CancellationToken.None));
        var repairFailure = await Assert.ThrowsAsync<AccountApiException>(() => service.RepairContentAsync(
            new RepairContentRequest(project.ProjectId, sourceRequestId, "viewer-repair", project.OrganizationId),
            "viewer-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal("organization_generation_denied", quoteFailure.Code);
        Assert.Equal("organization_generation_denied", repairFailure.Code);
        Assert.Equal(0, resolver.ResolveCallCount);
        Assert.Equal(0, budget.ReserveCount);
        Assert.Equal(0, contentClient.RepairCallCount);
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

    [Fact]
    public async Task CanonicalVoiceShortNarration_IsRepairableWithoutAnAutomaticSecondProviderCall()
    {
        await using var dbContext = CreateContext();
        var project = SeedProject(dbContext, ProviderCodes.Kling);
        project.SpeechProductionPolicy = SpeechProductionPolicies.CanonicalVoice;
        project.VoiceSpeakingRate = 1m;
        await dbContext.SaveChangesAsync();
        var shortPlan = CreateVietnameseSpeechPlan("Hít sâu.");
        var repairedPlan = CreateVietnameseSpeechPlan(
            "Đứng thẳng, hít sâu rồi vươn hai tay lên cao chậm.");
        var contentClient = new StubContentClient(shortPlan, repairedPlan);
        var budget = new StubBudgetService();
        var service = CreateService(dbContext, project, contentClient, budget);

        var failure = await Assert.ThrowsAsync<AccountApiException>(() => service.GenerateContentAsync(
            new GenerateContentRequest(project.ProjectId, "content-pacing-source", project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal(ContentPlanErrorCodes.SpeechPacingInvalid, failure.Code);
        Assert.Contains("scenes[0].spoken_text", failure.Errors!["fields"]);
        Assert.Contains(
            "scenes[0].spoken_text|speech_too_short",
            failure.Errors["reasons"]);
        Assert.NotEmpty(failure.Errors["estimatedDurations"]);
        Assert.Equal("true", Assert.Single(failure.Errors["canRepair"]));
        Assert.Equal(1, contentClient.GenerateCallCount);
        Assert.Equal(0, contentClient.RepairCallCount);
        Assert.Equal(1, budget.SettleCount);
        Assert.Equal(0, budget.ReleaseCount);

        var failedRequestId = Guid.Parse(Assert.Single(failure.Errors["providerRequestId"]));
        var restored = await service.GetLatestContentLanguageFailureAsync(
            project.ProjectId,
            project.OrganizationId,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        var restoredViolation = Assert.Single(restored!.Violations);
        Assert.Equal(ContentPlanViolationReasons.SpeechTooShort, restoredViolation.Reason);
        Assert.NotNull(restoredViolation.EstimatedDurationSeconds);

        var quote = await service.GetContentRepairQuoteAsync(
            new ContentRepairQuoteRequest(project.ProjectId, failedRequestId, project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);
        Assert.Equal(0, contentClient.RepairCallCount);
        Assert.Equal(ContentPlanViolationReasons.SpeechTooShort, Assert.Single(quote.Violations).Reason);

        var repaired = await service.RepairContentAsync(
            new RepairContentRequest(
                project.ProjectId,
                failedRequestId,
                "content-pacing-repair",
                project.OrganizationId),
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(repairedPlan.Scenes[0].Narration, repaired.Plan.Scenes[0].Narration);
        Assert.Equal(1, contentClient.RepairCallCount);
        Assert.Equal(2, budget.SettleCount);
    }

    private static GenerationService CreateService(
        VideoFactoryDbContext dbContext,
        Project project,
        IOpenAiContentClient contentClient,
        IAiBudgetService budget,
        IProviderRuntimeResolver? runtimeResolver = null,
        IGenerationAccessService? generationAccess = null,
        IAiCostEstimator? estimator = null) =>
        new(
            dbContext,
            runtimeResolver ?? new StubProviderResolver(),
            contentClient,
            null!,
            null!,
            null!,
            generationAccess ?? new StubAccessService(new GenerationAccessContext(
                project.OrganizationId!.Value,
                "Test organization",
                "Member",
                project)),
            budget,
            estimator ?? new StubCostEstimator(),
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

    private static GeneratedContentPlan CreateVietnameseSpeechPlan(string narration) =>
        new(
            "Một thói quen tốt",
            "Hãy bắt đầu ngay hôm nay",
            "Cách tiếp cận thực tế",
            "Người trưởng thành",
            "Hãy thử một thói quen",
            narration,
            "Ánh sáng tự nhiên",
            "phụ đề, logo, watermark",
            [],
            [
                new GeneratedContentScene(
                    1,
                    "Mở đầu thu hút người xem",
                    narration,
                    "Một cốc nước đặt cạnh cửa sổ sáng trong căn phòng yên tĩnh.",
                    5,
                    [],
                    KlingSpeechModes.NativeVoiceOver,
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

    private sealed class StubContentClient(
        GeneratedContentPlan plan,
        GeneratedContentPlan? repairPlan = null,
        Exception? repairException = null) : IOpenAiContentClient
    {
        public string? LanguageCode { get; private set; }
        public int GenerateCallCount { get; private set; }
        public int RepairCallCount { get; private set; }

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
            GenerateCallCount++;
            LanguageCode = languageCode;
            return Task.FromResult(new OpenAiContentResult(plan, 120, 240, "response-1"));
        }

        public Task<OpenAiContentResult> RepairWithVideoConstraintsAsync(
            ProviderRuntimeConfiguration provider,
            GeneratedContentPlan rejectedPlan,
            IReadOnlyList<ContentLanguageViolation> violations,
            string languageCode,
            string platform,
            string aspectRatio,
            int targetDurationSeconds,
            string safetyIdentifier,
            VideoModelCapabilities videoCapabilities,
            bool enforceKlingLongFormSpeechPolicy,
            decimal speakingRate,
            CancellationToken cancellationToken)
        {
            RepairCallCount++;
            if (repairException is not null)
            {
                return Task.FromException<OpenAiContentResult>(repairException);
            }
            return Task.FromResult(new OpenAiContentResult(
                repairPlan ?? plan,
                100,
                200,
                "repair-response-1"));
        }
    }

    private sealed class StubProviderResolver : IProviderRuntimeResolver
    {
        public int ResolveCallCount { get; private set; }
        public int? FailOnResolveCall { get; set; }

        public Task<ProviderRuntimeConfiguration> ResolveAsync(
            Guid organizationId,
            string providerCode,
            string modality,
            Guid? credentialId,
            CancellationToken cancellationToken)
        {
            ResolveCallCount++;
            if (ResolveCallCount == FailOnResolveCall)
            {
                return Task.FromException<ProviderRuntimeConfiguration>(new AccountApiException(
                    StatusCodes.Status503ServiceUnavailable,
                    "openai_not_configured",
                    "OpenAI credential is unavailable."));
            }
            return Task.FromResult(new ProviderRuntimeConfiguration(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                ProviderCodes.OpenAi,
                "gpt-5.6-luna",
                new Uri("https://api.openai.com/v1/"),
                "Bearer",
                null,
                "test-key"));
        }

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

    private sealed class RejectingAccessService(AccountApiException exception) : IGenerationAccessService
    {
        public Task<GenerationAccessContext> RequireAsync(
            string userId,
            Guid deviceId,
            Guid? requestedOrganizationId,
            Guid? projectId,
            CancellationToken cancellationToken) =>
            Task.FromException<GenerationAccessContext>(exception);
    }

    private sealed class StubBudgetService : IAiBudgetService
    {
        public int ReserveCount { get; private set; }
        public int? FailOnReserveCall { get; set; }
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
            CancellationToken cancellationToken)
        {
            ReserveCount++;
            if (ReserveCount == FailOnReserveCall)
            {
                return Task.FromException<BudgetReservationResult>(new AccountApiException(
                    StatusCodes.Status409Conflict,
                    "organization_budget_exceeded",
                    "Organization budget is unavailable."));
            }
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
        public int QuoteCallCount { get; private set; }
        public int? UnconfiguredOnQuoteCall { get; set; }

        public Task<AiCostQuote> QuoteOpenAiAsync(
            Guid providerModelId,
            int topicCharacters,
            int targetDurationSeconds,
            CancellationToken cancellationToken)
        {
            QuoteCallCount++;
            var estimatedCost = QuoteCallCount == UnconfiguredOnQuoteCall ? 0m : 1m;
            return Task.FromResult(new AiCostQuote(estimatedCost, "USD", "[]", 120, 240));
        }

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
