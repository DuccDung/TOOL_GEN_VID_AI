using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Models;
using TOOL_SERVER.Organizations;

namespace TOOL_TESTS.Generation;

public sealed class GeneratedImageContentServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsBinaryAndMarksFirstDownloadForRequestOwner()
    {
        await using var dbContext = CreateContext();
        var seeded = Seed(dbContext, DateTime.UtcNow.AddHours(1));
        var service = new GeneratedImageContentService(
            dbContext,
            new StubAccessService(new GenerationAccessContext(
                seeded.Project.OrganizationId!.Value,
                "Test organization",
                "Member",
                seeded.Project)),
            TimeProvider.System);

        var result = await service.GetAsync(
            seeded.Request.ProviderRequestId,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(seeded.Output.Payload, result.Payload);
        Assert.Equal("image/png", result.MimeType);
        Assert.NotNull((await dbContext.GeneratedImageOutputs.SingleAsync()).DownloadedAtUtc);
    }

    [Fact]
    public async Task GetAsync_HidesPayloadWhenOrganizationContextDoesNotMatch()
    {
        await using var dbContext = CreateContext();
        var seeded = Seed(dbContext, DateTime.UtcNow.AddHours(1));
        var foreignProject = new Project
        {
            ProjectId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            RemoteUserId = "user-1",
            CreatedByUserId = "user-1",
            Name = "Foreign",
            Topic = "Foreign",
            LanguageCode = "vi-VN",
            Platform = "YouTube",
            AspectRatio = "16:9",
            Status = "Draft",
            CurrencyCode = "USD",
            WorkspaceRelativePath = "foreign",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8]
        };
        var service = new GeneratedImageContentService(
            dbContext,
            new StubAccessService(new GenerationAccessContext(
                foreignProject.OrganizationId!.Value,
                "Foreign organization",
                "Member",
                foreignProject)),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => service.GetAsync(
            seeded.Request.ProviderRequestId,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal(404, exception.StatusCode);
        Assert.Equal("generated_image_not_found", exception.Code);
        Assert.Null((await dbContext.GeneratedImageOutputs.SingleAsync()).DownloadedAtUtc);
    }

    [Fact]
    public async Task GetAsync_RejectsExpiredOutput()
    {
        await using var dbContext = CreateContext();
        var seeded = Seed(dbContext, DateTime.UtcNow.AddMinutes(-1));
        var service = new GeneratedImageContentService(
            dbContext,
            new StubAccessService(new GenerationAccessContext(
                seeded.Project.OrganizationId!.Value,
                "Test organization",
                "Member",
                seeded.Project)),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => service.GetAsync(
            seeded.Request.ProviderRequestId,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None));

        Assert.Equal(410, exception.StatusCode);
        Assert.Equal("generated_image_expired", exception.Code);
    }

    [Fact]
    public async Task GetAsync_DoesNotServeCharacterImageFromSceneFirstFrameScope()
    {
        await using var dbContext = CreateContext();
        var seeded = Seed(dbContext, DateTime.UtcNow.AddHours(1));
        var service = new GeneratedImageContentService(
            dbContext,
            new StubAccessService(new GenerationAccessContext(
                seeded.Project.OrganizationId!.Value,
                "Test organization",
                "Member",
                seeded.Project)),
            TimeProvider.System);

        var exception = await Assert.ThrowsAsync<AccountApiException>(() => service.GetAsync(
            seeded.Request.ProviderRequestId,
            "user-1",
            Guid.NewGuid(),
            CancellationToken.None,
            GeneratedImageContentKind.SceneFirstFrame));

        Assert.Equal(404, exception.StatusCode);
        Assert.Equal("generated_image_not_found", exception.Code);
    }

    private static VideoFactoryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"generated-image-content-{Guid.NewGuid():N}")
            .Options);

    private static SeededOutput Seed(VideoFactoryDbContext dbContext, DateTime expiresAtUtc)
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
            Status = "Draft",
            CurrencyCode = "USD",
            WorkspaceRelativePath = "test",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var character = new Character
        {
            CharacterId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            CharacterKey = "hero",
            Version = 1,
            Name = "Hero",
            ProfileJson = "{}",
            Status = "Draft",
            CreatedAtUtc = now,
            RowVersion = new byte[8],
            Project = project
        };
        var request = new ProviderRequest
        {
            ProviderRequestId = Guid.NewGuid(),
            OrganizationId = project.OrganizationId,
            RequestedByUserId = "user-1",
            ProjectId = project.ProjectId,
            CharacterId = character.CharacterId,
            RequestKind = "Image",
            ProviderCode = ProviderCodes.OpenAi,
            ModelCode = "gpt-image-2",
            IdempotencyKey = "image-content-test",
            RequestHash = new string('a', 64),
            Status = "Completed",
            RequestJson = "{}",
            CurrencyCode = "USD",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8],
            Project = project,
            Character = character
        };
        var output = new GeneratedImageOutput
        {
            ProviderRequestId = request.ProviderRequestId,
            Payload = [1, 2, 3, 4],
            MimeType = "image/png",
            Sha256 = new string('b', 64),
            SizeBytes = 4,
            Width = 1024,
            Height = 1024,
            CreatedAtUtc = now.AddMinutes(-2),
            ExpiresAtUtc = expiresAtUtc,
            ProviderRequest = request
        };
        dbContext.AddRange(project, character, request, output);
        dbContext.SaveChanges();
        return new SeededOutput(project, request, output);
    }

    private sealed record SeededOutput(Project Project, ProviderRequest Request, GeneratedImageOutput Output);

    private sealed class StubAccessService(GenerationAccessContext context) : IGenerationAccessService
    {
        public Task<GenerationAccessContext> RequireAsync(
            string userId,
            Guid deviceId,
            Guid? requestedOrganizationId,
            Guid? projectId,
            CancellationToken cancellationToken) => Task.FromResult(context);
    }
}
