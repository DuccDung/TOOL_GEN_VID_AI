using Microsoft.EntityFrameworkCore;

namespace TOOL_LOCAL.Data;

public sealed class VideoFactoryDbContextFactory(string connectionString) : IDbContextFactory<VideoFactoryDbContext>
{
    private readonly DbContextOptions<VideoFactoryDbContext> _options =
        new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseSqlServer(connectionString)
            .EnableDetailedErrors()
            .Options;

    public VideoFactoryDbContext CreateDbContext() => new(_options);

    public Task<VideoFactoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }
}
