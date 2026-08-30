using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Data;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Models;
using TOOL_SERVER.Organizations;

namespace TOOL_TESTS.Generation;

public sealed class KlingOutputProxySecurityTests
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedHosts =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [ProviderCodes.Kling] = ["klingai.com", "kwaicdn.com", "kwimgs.com"],
            [ProviderCodes.BytePlus] = ["bytepluses.com", "volces.com"]
        };

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.16.10.5", true)]
    [InlineData("192.168.1.10", true)]
    [InlineData("169.254.10.10", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("192.0.2.1", true)]
    [InlineData("198.51.100.1", true)]
    [InlineData("203.0.113.1", true)]
    [InlineData("224.0.0.1", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("1.1.1.1", false)]
    public void ProviderOutputProxy_BlocksPrivateAndSpecialIpv4Ranges(string address, bool blocked)
    {
        Assert.Equal(blocked, KlingOutputProxyService.IsPrivateAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("ff02::1", true)]
    [InlineData("2001:db8::1", true)]
    [InlineData("2606:4700:4700::1111", false)]
    public void ProviderOutputProxy_BlocksPrivateIpv6Ranges(string address, bool blocked)
    {
        Assert.Equal(blocked, KlingOutputProxyService.IsPrivateAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("byteplus", "ark-content-generation-v2-ap-southeast-1.tos-ap-southeast-1.volces.com", true)]
    [InlineData("byteplus", "VOLCES.COM.", true)]
    [InlineData("byteplus", "evilvolces.com", false)]
    [InlineData("byteplus", "media.klingai.com", false)]
    [InlineData("kling", "media.kwaicdn.com", true)]
    [InlineData("kling", "klingai.com.evil.example", false)]
    [InlineData("unknown", "media.volces.com", false)]
    [InlineData("byteplus", "127.0.0.1", false)]
    public void ProviderOutputProxy_RequiresHostOwnedByTheSelectedProvider(
        string providerCode,
        string host,
        bool allowed)
    {
        Assert.Equal(
            allowed,
            KlingOutputProxyService.IsAllowedProviderOutputHost(providerCode, host, AllowedHosts));
    }

    [Fact]
    public async Task VideoOutputCleanup_RemovesExpiredFileAndIsIdempotent()
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), $"videomaker-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);
        try
        {
            await using var dbContext = new VideoFactoryDbContext(
                new DbContextOptionsBuilder<VideoFactoryDbContext>()
                    .UseInMemoryDatabase($"video-output-cleanup-{Guid.NewGuid():N}")
                    .Options);
            var providerRequestId = Guid.NewGuid();
            var storageKey = $"{providerRequestId:N}.mp4";
            var path = Path.Combine(storageRoot, storageKey);
            await File.WriteAllBytesAsync(path, [0, 1, 2, 3]);
            dbContext.GeneratedVideoOutputs.Add(new GeneratedVideoOutput
            {
                ProviderRequestId = providerRequestId,
                StorageKey = storageKey,
                MimeType = "video/mp4",
                Sha256 = new string('a', 64),
                SizeBytes = 4,
                Status = "Ready",
                CreatedAtUtc = DateTime.UtcNow.AddHours(-2),
                ExpiresAtUtc = DateTime.UtcNow.AddHours(-1),
                RowVersion = new byte[8]
            });
            await dbContext.SaveChangesAsync();
            var service = new KlingOutputProxyService(
                dbContext,
                new UnusedAccessService(),
                new UnusedHttpClientFactory(),
                Options.Create(new VideoOutputOptions { StorageRoot = storageRoot }),
                TimeProvider.System);

            var removed = await service.CleanupExpiredAsync(CancellationToken.None);
            var removedAgain = await service.CleanupExpiredAsync(CancellationToken.None);

            Assert.Equal(1, removed);
            Assert.Equal(0, removedAgain);
            Assert.False(File.Exists(path));
            var row = await dbContext.GeneratedVideoOutputs.SingleAsync();
            Assert.Equal("Deleted", row.Status);
            Assert.NotNull(row.DeletedAtUtc);
        }
        finally
        {
            if (Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }

    private sealed class UnusedAccessService : IGenerationAccessService
    {
        public Task<GenerationAccessContext> RequireAsync(
            string userId,
            Guid deviceId,
            Guid? requestedOrganizationId,
            Guid? projectId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotSupportedException();
    }
}
