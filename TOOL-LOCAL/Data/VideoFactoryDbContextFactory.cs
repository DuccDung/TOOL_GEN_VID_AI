using Microsoft.EntityFrameworkCore;

namespace TOOL_LOCAL.Data;

public sealed class VideoFactoryDbContextFactory(string connectionString, bool disabled = false) : IDbContextFactory<VideoFactoryDbContext>
{
    private readonly DbContextOptions<VideoFactoryDbContext>? _options = disabled ? null :
        new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseSqlServer(connectionString)
            .EnableDetailedErrors()
            .Options;

    public VideoFactoryDbContext CreateDbContext() => _options is null
        ? throw new TOOL_LOCAL.Authentication.AccountClientException(
            TOOL_SHARED.Contracts.Common.ApplicationFeaturePolicy.LocalOnlyErrorCode,
            TOOL_SHARED.Contracts.Common.ApplicationFeaturePolicy.LocalOnlyMessage, 403)
        : new(_options);

    public Task<VideoFactoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }
}
