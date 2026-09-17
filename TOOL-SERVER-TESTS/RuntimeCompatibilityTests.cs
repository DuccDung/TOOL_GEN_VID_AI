using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Data;
using TOOL_SERVER.Models;

namespace TOOL_SERVER_TESTS;

public sealed class RuntimeCompatibilityTests
{
    [Fact]
    public void ServerModelBuildsUnderNet9WithExistingSqlDefaults()
    {
        Assert.Equal(9, Environment.Version.Major);
        Assert.Equal(9, typeof(DbContext).Assembly.GetName().Version?.Major);

        var options = new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseSqlServer("Server=localhost;Database=VideoMakerCompatibilityProbe;Integrated Security=True;TrustServerCertificate=True")
            .Options;
        using var context = new VideoFactoryDbContext(options);

        var release = context.Model.FindEntityType(typeof(AppRelease));
        Assert.NotNull(release);
        Assert.Equal("(sysutcdatetime())", release.FindProperty(nameof(AppRelease.PublishedAtUtc))?.GetDefaultValueSql());
        Assert.Equal(true, release.FindProperty(nameof(AppRelease.IsActive))?.GetDefaultValue());
    }
}
