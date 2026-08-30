using Microsoft.AspNetCore.Mvc;
using TOOL_SERVER.Controllers;
using TOOL_SERVER.Domain.Updates;
using TOOL_SERVER.Updates;
using TOOL_SHARED.Contracts.Updates;

namespace TOOL_TESTS.Updates;

public sealed class DesktopUpdatesControllerTests
{
    [Fact]
    public async Task Repair_ReturnsOnlyTheExactVisibleVersionAndBuild()
    {
        var expected = CreatePackage("1.4.2", 17);
        var service = new FakeDesktopReleaseService(
            [CreatePackage("1.4.3", 18), expected]);
        var controller = new DesktopUpdatesController(service);

        var action = await controller.Repair("1.4.2", 17, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var response = Assert.IsType<DesktopReleaseResponse>(ok.Value);
        Assert.Equal(expected.Release.AppReleaseId, response.ReleaseId);
        Assert.Equal(17, response.BuildNumber);
    }

    [Fact]
    public async Task Repair_DoesNotFallBackToAnotherVersion()
    {
        var service = new FakeDesktopReleaseService([CreatePackage("1.4.3", 18)]);
        var controller = new DesktopUpdatesController(service);

        var action = await controller.Repair("1.4.2", 17, cancellationToken: CancellationToken.None);

        Assert.IsType<NotFoundResult>(action.Result);
    }

    private static DesktopReleasePackage CreatePackage(string version, int buildNumber)
    {
        var release = new AppRelease
        {
            AppReleaseId = Guid.NewGuid(),
            Version = version,
            BuildNumber = buildNumber,
            Channel = DesktopReleaseChannels.Stable,
            Platform = DesktopReleasePlatforms.WindowsX64,
            IsActive = true,
            PublishedAtUtc = DateTime.UtcNow
        };
        var artifact = new AppReleaseArtifact
        {
            AppReleaseArtifactId = Guid.NewGuid(),
            AppReleaseId = release.AppReleaseId,
            Kind = DesktopArtifactKinds.DesktopPackage,
            FileName = $"VideoMaker-{version}-{buildNumber}.zip",
            RelativePath = $"releases/{release.AppReleaseId:D}/package.zip",
            SizeBytes = 1234,
            Sha256 = new string('a', 64),
            Release = release
        };
        release.Artifacts.Add(artifact);
        return new DesktopReleasePackage(release, artifact);
    }

    private sealed class FakeDesktopReleaseService(IReadOnlyList<DesktopReleasePackage> packages)
        : IDesktopReleaseService
    {
        public Task<IReadOnlyList<DesktopReleasePackage>> GetVisiblePackagesAsync(
            string platform,
            string channel,
            CancellationToken cancellationToken) => Task.FromResult(packages);

        public Task<DesktopReleasePackage?> GetLatestPackageAsync(string platform, string channel, CancellationToken cancellationToken) =>
            Task.FromResult(packages.FirstOrDefault());

        public Task<DesktopReleasePackage?> GetVisiblePackageAsync(Guid releaseId, CancellationToken cancellationToken) =>
            Task.FromResult(packages.FirstOrDefault(package => package.Release.AppReleaseId == releaseId));

        public Task<DesktopReleasePackage?> GetLatestArtifactAsync(string platform, string channel, string kind, CancellationToken cancellationToken) =>
            Task.FromResult(packages.FirstOrDefault());

        public Task<IReadOnlyList<AdminDesktopReleaseResponse>> GetAdminReleasesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AdminDesktopReleaseResponse> CreateAsync(AdminDesktopReleaseRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AdminDesktopReleaseResponse> UpdateAsync(Guid releaseId, AdminDesktopReleaseRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AdminDesktopArtifactResponse> SaveArtifactAsync(Guid releaseId, string kind, string fileName, Stream stream, long length, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid releaseId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
