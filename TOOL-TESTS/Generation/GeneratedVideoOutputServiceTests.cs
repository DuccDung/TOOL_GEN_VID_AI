using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Models;
using TOOL_SERVER.Organizations;

namespace TOOL_TESTS.Generation;

public sealed class GeneratedVideoOutputServiceTests
{
    [Fact]
    public async Task WriteAndPromote_ClosesTemporaryFileBeforeMove()
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), $"videomaker-output-promote-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        try
        {
            var temporaryPath = Path.Combine(storageRoot, "output.mp4.tmp");
            var destinationPath = Path.Combine(storageRoot, "output.mp4");
            var payload = new byte[] { 0, 0, 0, 24, 102, 116, 121, 112 };
            await using var source = new MemoryStream(payload);

            var result = await KlingOutputProxyService.WriteAndPromoteAsync(
                source,
                temporaryPath,
                destinationPath,
                payload.Length,
                payload.Length,
                CancellationToken.None);

            Assert.False(File.Exists(temporaryPath));
            Assert.True(File.Exists(destinationPath));
            Assert.Equal(payload.Length, result.Total);
            Assert.Equal(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant(),
                result.Sha256);
            Assert.Equal(payload, await File.ReadAllBytesAsync(destinationPath));
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAndPromote_RemovesTemporaryFileWhenTransferExceedsLimit()
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), $"videomaker-output-limit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        try
        {
            var temporaryPath = Path.Combine(storageRoot, "output.mp4.tmp");
            var destinationPath = Path.Combine(storageRoot, "output.mp4");
            await using var source = new MemoryStream(new byte[] { 0, 1, 2, 3 });

            await Assert.ThrowsAsync<AccountApiException>(() =>
                KlingOutputProxyService.WriteAndPromoteAsync(
                    source,
                    temporaryPath,
                    destinationPath,
                    transferLimit: 2,
                    maximumFileBytes: 4,
                    CancellationToken.None));

            Assert.False(File.Exists(temporaryPath));
            Assert.False(File.Exists(destinationPath));
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CopyToResponse_ReturnsOnlyServerCachedBytesAndMarksDownload()
    {
        var fixture = await CreateFixtureAsync();
        await using (fixture)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.Body = new MemoryStream();

            await fixture.Service.CopyToResponseAsync(
                httpContext,
                fixture.Request.ProviderRequestId,
                "user-1",
                Guid.NewGuid(),
                CancellationToken.None);

            Assert.Equal("video/mp4", httpContext.Response.ContentType);
            Assert.Equal(fixture.Payload.Length, httpContext.Response.ContentLength);
            Assert.Equal($"\"{fixture.Output.Sha256}\"", httpContext.Response.Headers.ETag.ToString());
            Assert.Equal(fixture.Payload, ((MemoryStream)httpContext.Response.Body).ToArray());
            Assert.NotNull(fixture.Output.DownloadedAtUtc);
        }
    }

    [Fact]
    public async Task CopyToResponse_HidesAnotherUsersCompletedVideo()
    {
        var fixture = await CreateFixtureAsync();
        await using (fixture)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.Body = new MemoryStream();

            var exception = await Assert.ThrowsAsync<AccountApiException>(() =>
                fixture.Service.CopyToResponseAsync(
                    httpContext,
                    fixture.Request.ProviderRequestId,
                    "user-2",
                    Guid.NewGuid(),
                    CancellationToken.None));

            Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
            Assert.Equal("generation_not_found", exception.Code);
            Assert.Equal(0, httpContext.Response.Body.Length);
        }
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), $"videomaker-output-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        var dbContext = new VideoFactoryDbContext(
            new DbContextOptionsBuilder<VideoFactoryDbContext>()
                .UseInMemoryDatabase($"generated-video-output-{Guid.NewGuid():N}")
                .Options);
        var now = DateTime.UtcNow;
        var organizationId = Guid.NewGuid();
        var project = new Project
        {
            ProjectId = Guid.NewGuid(),
            OrganizationId = organizationId,
            RemoteUserId = "user-1",
            Name = "Cached output test",
            Topic = "Test",
            LanguageCode = "vi-VN",
            Platform = "YouTube",
            AspectRatio = "16:9",
            Status = "GeneratingScenes",
            CurrencyCode = "USD",
            WorkspaceRelativePath = "test",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
        var request = new ProviderRequest
        {
            ProviderRequestId = Guid.NewGuid(),
            OrganizationId = organizationId,
            RequestedByUserId = "user-1",
            ProjectId = project.ProjectId,
            RequestKind = "Video",
            ProviderCode = ProviderCodes.BytePlus,
            ModelCode = "dreamina-seedance-2-5-260628",
            ExternalRequestId = "task-1",
            IdempotencyKey = "output-test",
            Status = "Completed",
            RequestJson = "{}",
            CurrencyCode = "USD",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CompletedAtUtc = now,
            RowVersion = new byte[8]
        };
        var payload = new byte[] { 0, 0, 0, 24, 102, 116, 121, 112 };
        var storageKey = $"{request.ProviderRequestId:N}.mp4";
        await File.WriteAllBytesAsync(Path.Combine(storageRoot, storageKey), payload);
        var output = new GeneratedVideoOutput
        {
            ProviderRequestId = request.ProviderRequestId,
            StorageKey = storageKey,
            MimeType = "video/mp4",
            Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant(),
            SizeBytes = payload.Length,
            Status = "Ready",
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(1),
            RowVersion = new byte[8]
        };
        dbContext.AddRange(project, request, output);
        await dbContext.SaveChangesAsync();
        var access = new StubAccessService(new GenerationAccessContext(
            organizationId,
            "Test organization",
            "Member",
            project));
        var service = new KlingOutputProxyService(
            dbContext,
            access,
            new UnusedHttpClientFactory(),
            Options.Create(new VideoOutputOptions { StorageRoot = storageRoot }),
            TimeProvider.System);
        return new Fixture(storageRoot, dbContext, service, request, output, payload);
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

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException();
    }

    private sealed class Fixture(
        string storageRoot,
        VideoFactoryDbContext dbContext,
        KlingOutputProxyService service,
        ProviderRequest request,
        GeneratedVideoOutput output,
        byte[] payload) : IAsyncDisposable
    {
        public KlingOutputProxyService Service { get; } = service;
        public ProviderRequest Request { get; } = request;
        public GeneratedVideoOutput Output { get; } = output;
        public byte[] Payload { get; } = payload;

        public async ValueTask DisposeAsync()
        {
            await dbContext.DisposeAsync();
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }
}
