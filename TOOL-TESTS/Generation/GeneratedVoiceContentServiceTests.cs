using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Models;
using TOOL_SERVER.Organizations;

namespace TOOL_TESTS.Generation;

public sealed class GeneratedVoiceContentServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsAuthorizedPayloadAndMarksFirstDownload()
    {
        await using var dbContext = CreateContext();
        var (project, request, payload) = Seed(dbContext, DateTime.UtcNow.AddHours(1));
        var service = new GeneratedVoiceContentService(
            dbContext,
            new StubAccessService(new GenerationAccessContext(
                project.OrganizationId!.Value, "Organization", "Member", project)),
            TimeProvider.System);

        var content = await service.GetAsync(request.ProviderRequestId, "user-1", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(payload, content.Payload);
        Assert.Equal("audio/wav", content.MimeType);
        Assert.NotNull((await dbContext.GeneratedVoiceOutputs.SingleAsync()).DownloadedAtUtc);
    }

    [Fact]
    public async Task GetAsync_CrossUserIsHiddenAsNotFound()
    {
        await using var dbContext = CreateContext();
        var (project, request, _) = Seed(dbContext, DateTime.UtcNow.AddHours(1));
        var accessProject = new Project
        {
            ProjectId = project.ProjectId,
            OrganizationId = project.OrganizationId,
            RemoteUserId = "user-2"
        };
        var service = new GeneratedVoiceContentService(
            dbContext,
            new StubAccessService(new GenerationAccessContext(
                project.OrganizationId!.Value, "Organization", "Member", accessProject)),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.GetAsync(request.ProviderRequestId, "user-2", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(404, exception.StatusCode);
        Assert.Equal("generated_voice_not_found", exception.Code);
    }

    [Fact]
    public async Task GetAsync_ExpiredOutputReturnsGone()
    {
        await using var dbContext = CreateContext();
        var (project, request, _) = Seed(dbContext, DateTime.UtcNow.AddMinutes(-1));
        var service = new GeneratedVoiceContentService(
            dbContext,
            new StubAccessService(new GenerationAccessContext(
                project.OrganizationId!.Value, "Organization", "Member", project)),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
            service.GetAsync(request.ProviderRequestId, "user-1", Guid.NewGuid(), CancellationToken.None));

        Assert.Equal(410, exception.StatusCode);
        Assert.Equal("generated_voice_expired", exception.Code);
    }

    private static VideoFactoryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"generated-voice-content-{Guid.NewGuid():N}")
            .Options);

    private static (Project Project, ProviderRequest Request, byte[] Payload) Seed(
        VideoFactoryDbContext dbContext,
        DateTime expiresAtUtc)
    {
        var now = DateTime.UtcNow;
        var project = new Project
        {
            ProjectId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            RemoteUserId = "user-1",
            Name = "Test",
            Topic = "Test",
            LanguageCode = "vi-VN",
            Platform = "YouTube",
            AspectRatio = "16:9",
            Status = "Approved",
            CurrencyCode = "USD",
            WorkspaceRelativePath = "test",
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
            StoryPurpose = "Hook",
            VisualDescription = "Visual",
            EntryStateJson = "{}",
            ExitStateJson = "{}",
            Status = "Approved",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var request = new ProviderRequest
        {
            ProviderRequestId = Guid.NewGuid(),
            OrganizationId = project.OrganizationId,
            RequestedByUserId = "user-1",
            ProjectId = project.ProjectId,
            SceneId = scene.SceneId,
            RequestKind = "Voice",
            ProviderCode = ProviderCodes.OpenAi,
            ModelCode = "gpt-4o-mini-tts",
            IdempotencyKey = "voice-test",
            Status = "Completed",
            RequestJson = "{}",
            CurrencyCode = "USD",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var payload = new byte[] { 1, 2, 3, 4 };
        var output = new GeneratedVoiceOutput
        {
            ProviderRequestId = request.ProviderRequestId,
            Payload = payload,
            MimeType = "audio/wav",
            Sha256 = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            SizeBytes = payload.Length,
            DurationMs = 1000,
            SampleRate = 24_000,
            Channels = 1,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc,
            RowVersion = new byte[8]
        };
        dbContext.AddRange(project, scene, request, output);
        dbContext.SaveChanges();
        return (project, request, payload);
    }

    private sealed class StubAccessService(GenerationAccessContext context) : IGenerationAccessService
    {
        public Task<GenerationAccessContext> RequireAsync(string userId, Guid deviceId, Guid? requestedOrganizationId, Guid? projectId, CancellationToken cancellationToken) =>
            Task.FromResult(context);
    }
}
