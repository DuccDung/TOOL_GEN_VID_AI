using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Data;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Models;
using TOOL_SERVER.Organizations;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Generation;

public sealed class LipSyncPollingProcessorTests
{
    [Fact]
    public async Task ProcessingResult_AdvancesStateAndClaimPreventsImmediateDuplicatePoll()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedActiveTask(dbContext);
        var fixture = CreateProcessor(
            dbContext,
            ProviderResult(LipSyncStatuses.Processing),
            maximumAttempts: 10);

        await fixture.Processor.ProcessDueAsync(CancellationToken.None);
        await fixture.Processor.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(1, fixture.ProviderClient.StatusCount);
        Assert.Equal(LipSyncStatuses.Processing, seeded.Request.Status);
        Assert.Equal(LipSyncStatuses.Processing, seeded.Generation.Status);
        Assert.Equal(1, seeded.Request.PollCount);
        Assert.NotNull(seeded.Request.NextPollAtUtc);
        Assert.Equal("WaitingProvider", seeded.Scene.Status);
        Assert.Equal(0, fixture.Budget.SettleCount);
        Assert.Equal(0, fixture.Budget.ReleaseCount);
    }

    [Fact]
    public async Task CompletedResult_CachesSettlesAndNeverProcessesTerminalTaskTwice()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedActiveTask(dbContext);
        var fixture = CreateProcessor(
            dbContext,
            ProviderResult(
                LipSyncStatuses.Completed,
                "https://v3.fal.media/files/lip-sync.mp4"),
            maximumAttempts: 10);

        await fixture.Processor.ProcessDueAsync(CancellationToken.None);
        await fixture.Processor.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(1, fixture.ProviderClient.StatusCount);
        Assert.Equal(1, fixture.OutputStore.CacheCount);
        Assert.Equal(1, fixture.Budget.SettleCount);
        Assert.Equal(0, fixture.Budget.ReleaseCount);
        Assert.Equal(LipSyncStatuses.Completed, seeded.Request.Status);
        Assert.Equal(LipSyncStatuses.Completed, seeded.Generation.Status);
        Assert.Equal(seeded.Request.EstimatedCost, seeded.Request.ActualCost);
        Assert.Equal(seeded.Request.EstimatedCost, seeded.Project.ActualCost);
        Assert.Equal("Generated", seeded.Scene.Status);
        Assert.Null(seeded.Request.NextPollAtUtc);
    }

    [Fact]
    public async Task CompletedProviderWithTerminalCacheFailure_SettlesCostButMarksOutputUnavailable()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedActiveTask(dbContext);
        var fixture = CreateProcessor(
            dbContext,
            ProviderResult(
                LipSyncStatuses.Completed,
                "https://v3.fal.media/files/lip-sync.mp4"),
            maximumAttempts: 1,
            failCache: true);

        await fixture.Processor.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(1, fixture.ProviderClient.StatusCount);
        Assert.Equal(1, fixture.OutputStore.CacheCount);
        Assert.Equal(1, fixture.Budget.SettleCount);
        Assert.Equal(0, fixture.Budget.ReleaseCount);
        Assert.Equal(LipSyncStatuses.Failed, seeded.Request.Status);
        Assert.Equal(LipSyncStatuses.Failed, seeded.Generation.Status);
        Assert.Equal("provider_output_download_failed", seeded.Request.ErrorCode);
        Assert.Equal(seeded.Request.EstimatedCost, seeded.Request.ActualCost);
        Assert.Equal(seeded.Request.EstimatedCost, seeded.Project.ActualCost);
        Assert.Equal("PromptReady", seeded.Scene.Status);
        Assert.Contains("outputUnavailable", seeded.Request.UsageJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaleSubmittingAfterRestart_BecomesUnknownWithoutResubmitOrReservationRelease()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedActiveTask(dbContext);
        seeded.Request.Status = LipSyncStatuses.Submitting;
        seeded.Request.ExternalRequestId = null;
        seeded.Request.SubmittedAtUtc = DateTime.UtcNow.AddMinutes(-11);
        seeded.Request.NextPollAtUtc = null;
        seeded.Generation.Status = LipSyncStatuses.Submitting;
        await dbContext.SaveChangesAsync();
        var fixture = CreateProcessor(
            dbContext,
            ProviderResult(LipSyncStatuses.Submitted),
            maximumAttempts: 10);

        await fixture.Processor.ProcessDueAsync(CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var request = await dbContext.ProviderRequests.SingleAsync(x => x.ProviderRequestId == seeded.Request.ProviderRequestId);
        var generation = await dbContext.LipSyncGenerations.SingleAsync(x => x.LipSyncGenerationId == seeded.Generation.LipSyncGenerationId);
        var scene = await dbContext.Scenes.SingleAsync(x => x.SceneId == seeded.Scene.SceneId);
        Assert.Equal(0, fixture.ProviderClient.StatusCount);
        Assert.Equal(LipSyncStatuses.Unknown, request.Status);
        Assert.Equal(LipSyncStatuses.Unknown, generation.Status);
        Assert.Equal("provider_submission_unknown", request.ErrorCode);
        Assert.Equal("WaitingProvider", scene.Status);
        Assert.Equal(0, fixture.Budget.SettleCount);
        Assert.Equal(0, fixture.Budget.ReleaseCount);
    }

    [Fact]
    public async Task TransientPollingFailure_KeepsTaskRetryableWithoutSettlementOrRelease()
    {
        await using var dbContext = CreateContext();
        var seeded = SeedActiveTask(dbContext);
        var fixture = CreateProcessor(
            dbContext,
            ProviderResult(LipSyncStatuses.Processing),
            maximumAttempts: 10,
            providerException: new HttpRequestException("temporary network failure"));

        await fixture.Processor.ProcessDueAsync(CancellationToken.None);

        Assert.Equal(1, fixture.ProviderClient.StatusCount);
        Assert.Equal(LipSyncStatuses.Submitted, seeded.Request.Status);
        Assert.Equal(LipSyncStatuses.Submitted, seeded.Generation.Status);
        Assert.Equal("provider_status_check_failed", seeded.Request.ErrorCode);
        Assert.NotNull(seeded.Request.NextPollAtUtc);
        Assert.Equal(0, fixture.Budget.SettleCount);
        Assert.Equal(0, fixture.Budget.ReleaseCount);
    }

    private static Fixture CreateProcessor(
        VideoFactoryDbContext dbContext,
        LipSyncProviderTaskResult result,
        int maximumAttempts,
        bool failCache = false,
        Exception? providerException = null)
    {
        var providerClient = new StubLipSyncProviderClient(result, providerException);
        var outputStore = new StubVideoOutputStore(failCache);
        var budget = new StubBudgetService();
        var processor = new LipSyncPollingProcessor(
            dbContext,
            new StubProviderResolver(),
            new StubLipSyncProviderRouter(providerClient),
            outputStore,
            budget,
            new FixedTimeProvider(DateTimeOffset.UtcNow),
            Options.Create(new VideoPollingOptions
            {
                MaximumAttempts = maximumAttempts,
                MaximumAgeHours = 72,
                ClaimLeaseMinutes = 5
            }),
            NullLogger<LipSyncPollingProcessor>.Instance);
        return new Fixture(processor, providerClient, outputStore, budget);
    }

    private static LipSyncProviderTaskResult ProviderResult(string status, string? outputUrl = null) =>
        new(
            "fal-request-1",
            status,
            status == LipSyncStatuses.Completed ? 100m : 50m,
            outputUrl,
            null,
            null,
            status == LipSyncStatuses.Completed ? 4 : null,
            "{\"status\":\"safe\"}");

    private static ActiveTask SeedActiveTask(VideoFactoryDbContext dbContext)
    {
        var now = DateTime.UtcNow.AddMinutes(-1);
        var project = new Project
        {
            ProjectId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            CreatedByUserId = "user-1",
            RemoteUserId = "user-1",
            Name = "Lip-sync polling test",
            Topic = "Test",
            LanguageCode = "vi-VN",
            SpeechProductionPolicy = SpeechProductionPolicies.CanonicalVoice,
            Platform = "YouTube",
            AspectRatio = "16:9",
            TargetDurationSeconds = 4,
            OutputWidth = 1280,
            OutputHeight = 720,
            OutputFrameRate = 25,
            Status = "ScenePlanning",
            CurrentScenePlanVersion = 1,
            CurrencyCode = "USD",
            WorkspaceRelativePath = "polling-test",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var scene = new Scene
        {
            SceneId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            ScriptId = Guid.NewGuid(),
            StyleProfileId = Guid.NewGuid(),
            ScenePlanVersion = 1,
            SequenceNumber = 1,
            StoryPurpose = "Test",
            Dialogue = "Xin chào.",
            VisualDescription = "Người dẫn nhìn vào máy quay.",
            ContentDurationMs = 4000,
            GenerationDurationMs = 4000,
            TimelineEndMs = 4000,
            EntryStateJson = "{}",
            ExitStateJson = "{}",
            Status = "WaitingProvider",
            SpeechStatus = SceneSpeechStatuses.SpeechReadyForLipSync,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var request = new ProviderRequest
        {
            ProviderRequestId = Guid.NewGuid(),
            OrganizationId = project.OrganizationId,
            RequestedByUserId = "user-1",
            OrganizationProviderCredentialId = Guid.NewGuid(),
            BudgetReservationId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            SceneId = scene.SceneId,
            ProviderId = Guid.NewGuid(),
            ProviderModelId = Guid.NewGuid(),
            RequestKind = "LipSync",
            ProviderCode = ProviderCodes.Fal,
            ModelCode = FalLipSyncPolicy.EndpointId,
            ExternalRequestId = "fal-request-1",
            IdempotencyKey = "lip-sync-polling",
            Status = LipSyncStatuses.Submitted,
            RequestJson = "{}",
            EstimatedCost = 0.20m,
            RateSnapshotJson = "[{\"usageType\":\"VideoSecond\"}]",
            CurrencyCode = "USD",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            SubmittedAtUtc = now,
            NextPollAtUtc = now,
            RowVersion = new byte[8]
        };
        var generation = new LipSyncGeneration
        {
            LipSyncGenerationId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            SceneId = scene.SceneId,
            LipSyncInputSessionId = Guid.NewGuid(),
            ProviderRequestId = request.ProviderRequestId,
            VideoGenerationId = Guid.NewGuid(),
            VoiceGenerationId = Guid.NewGuid(),
            AttemptNumber = 1,
            Status = LipSyncStatuses.Submitted,
            PolicyVersion = FalLipSyncPolicy.PolicyVersion,
            RequestedDurationMs = 4000,
            VideoSha256 = Hash('a'),
            AudioSha256 = Hash('b'),
            PreparedVideoSha256 = Hash('c'),
            PreparedAudioSha256 = Hash('d'),
            CreatedAtUtc = now,
            RowVersion = new byte[8]
        };
        dbContext.AddRange(project, scene, request, generation);
        dbContext.SaveChanges();
        return new ActiveTask(project, scene, request, generation);
    }

    private static VideoFactoryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var context = new SqliteVideoFactoryDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        context.Database.ExecuteSqlRaw("PRAGMA foreign_keys=OFF;");
        return context;
    }

    private static string Hash(char value) => new(value, 64);

    private sealed record ActiveTask(
        Project Project,
        Scene Scene,
        ProviderRequest Request,
        LipSyncGeneration Generation);

    private sealed record Fixture(
        LipSyncPollingProcessor Processor,
        StubLipSyncProviderClient ProviderClient,
        StubVideoOutputStore OutputStore,
        StubBudgetService Budget);

    private sealed class SqliteVideoFactoryDbContext(DbContextOptions<VideoFactoryDbContext> options)
        : VideoFactoryDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // SQLite does not retain SQL Server schemas, so the auth/vf tables
            // named SchemaVersions would otherwise collide during EnsureCreated.
            modelBuilder.Entity<SchemaVersion>().ToTable("AuthSchemaVersions");
            modelBuilder.Entity<Project>().Property(x => x.RowVersion).ValueGeneratedNever();
            modelBuilder.Entity<Scene>().Property(x => x.RowVersion).ValueGeneratedNever();
            modelBuilder.Entity<ProviderRequest>().Property(x => x.RowVersion).ValueGeneratedNever();
            modelBuilder.Entity<LipSyncGeneration>().Property(x => x.RowVersion).ValueGeneratedNever();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubProviderResolver : IProviderRuntimeResolver
    {
        public Task<ProviderRuntimeConfiguration> ResolveAsync(Guid organizationId, string providerCode, string modality, Guid? credentialId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProviderRuntimeConfiguration(
                Guid.NewGuid(),
                Guid.NewGuid(),
                credentialId ?? Guid.NewGuid(),
                ProviderCodes.Fal,
                FalLipSyncPolicy.EndpointId,
                new Uri("https://queue.fal.run/"),
                "Key",
                null,
                "fake-key"));

        public Task<GenerationProviderStatusResponse> GetStatusAsync(Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubLipSyncProviderRouter(StubLipSyncProviderClient client) : ILipSyncProviderRouter
    {
        public ILipSyncProviderClient Resolve(string providerCode) => client;
    }

    private sealed class StubLipSyncProviderClient(
        LipSyncProviderTaskResult result,
        Exception? providerException) : ILipSyncProviderClient
    {
        public string ProviderCode => ProviderCodes.Fal;
        public int StatusCount { get; private set; }

        public Task<LipSyncProviderTaskResult> SubmitAsync(ProviderRuntimeConfiguration provider, string videoUrl, string audioUrl, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LipSyncProviderTaskResult> GetStatusAsync(ProviderRuntimeConfiguration provider, string externalRequestId, CancellationToken cancellationToken)
        {
            StatusCount++;
            return providerException is null
                ? Task.FromResult(result)
                : Task.FromException<LipSyncProviderTaskResult>(providerException);
        }
    }

    private sealed class StubVideoOutputStore(bool failCache) : IVideoOutputStore
    {
        public int CacheCount { get; private set; }

        public Task CacheAsync(Guid providerRequestId, string outputUrl, CancellationToken cancellationToken)
        {
            CacheCount++;
            return failCache
                ? Task.FromException(new IOException("cache unavailable"))
                : Task.CompletedTask;
        }

        public Task CopyToResponseAsync(HttpContext httpContext, Guid providerRequestId, string userId, Guid deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CleanupExpiredAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class StubBudgetService : IAiBudgetService
    {
        public int SettleCount { get; private set; }
        public int ReleaseCount { get; private set; }

        public Task<BudgetSnapshot> GetSnapshotAsync(Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BudgetReservationResult> ReserveAsync(Guid organizationId, string userId, Guid projectId, Guid providerRequestId, string operationKey, string providerCode, string modelCode, decimal amount, CancellationToken cancellationToken) => throw new NotSupportedException();

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
}
