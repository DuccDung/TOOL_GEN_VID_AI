using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Domain.Providers;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Models;
using TOOL_SHARED.Contracts.Organizations;

namespace TOOL_TESTS.Generation;

public sealed class ProjectVideoPolicyResolverTests
{
    [Fact]
    public void SeedanceCapabilities_AllowTheConfiguredLongSceneRange()
    {
        var capabilities = VideoModelCapabilities.Parse(
            """{"minDurationSeconds":4,"maxDurationSeconds":30,"framesPerSecond":24,"resolutions":["720p"],"aspectRatios":["16:9"],"nativeAudio":true,"referenceImage":true}""",
            ProviderCodes.BytePlus);

        Assert.Equal(4, capabilities.MinimumDurationSeconds);
        Assert.Equal(30, capabilities.MaximumDurationSeconds);
        Assert.Equal(24, capabilities.FramesPerSecond);
        Assert.Contains("720p", capabilities.Resolutions);
        Assert.True(capabilities.NativeAudio);
    }

    [Fact]
    public async Task ResolveAsync_SnapshotsActivePolicyAndNeverAutoSwitchesExistingProject()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await using var governanceDb = new AiGovernanceDbContext(
            new DbContextOptionsBuilder<AiGovernanceDbContext>()
                .UseInMemoryDatabase($"video-policy-governance-{suffix}")
                .Options);
        await using var providerDb = new ProviderAdminDbContext(
            new DbContextOptionsBuilder<ProviderAdminDbContext>()
                .UseInMemoryDatabase($"video-policy-provider-{suffix}")
                .Options);
        var organizationId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        governanceDb.Organizations.Add(new Organization
        {
            OrganizationId = organizationId,
            Code = "policy-org",
            Name = "Policy Org",
            CreatedByUserId = "owner",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        });
        governanceDb.OrganizationVideoPolicies.Add(new OrganizationVideoPolicy
        {
            OrganizationId = organizationId,
            ProviderId = providerId,
            ProviderModelId = modelId,
            PolicyVersion = 7,
            Resolution = "720p",
            NativeAudio = true,
            IsActive = true,
            UpdatedByUserId = "owner",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        });
        var provider = new AiProvider
        {
            ProviderId = providerId,
            ProviderCode = ProviderCodes.BytePlus,
            DisplayName = "BytePlus ModelArk",
            BaseUrl = "https://ark.ap-southeast.bytepluses.com/api/v3/",
            IsEnabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
        provider.Models.Add(new AiProviderModel
        {
            ProviderModelId = modelId,
            ProviderId = providerId,
            Provider = provider,
            ModelCode = "dreamina-seedance-2-5-260628",
            DisplayName = "Seedance 2.5",
            Modality = "Video",
            IsEnabled = true,
            IsDefault = false,
            CapabilitiesJson = """{"minDurationSeconds":4,"maxDurationSeconds":30,"framesPerSecond":24,"resolutions":["720p"],"aspectRatios":["16:9"],"nativeAudio":true,"referenceImage":true}""",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        });
        providerDb.Providers.Add(provider);
        await governanceDb.SaveChangesAsync();
        await providerDb.SaveChangesAsync();
        var project = NewProject(organizationId);
        var resolver = new ProjectVideoPolicyResolver(governanceDb, providerDb, TimeProvider.System);

        var first = await resolver.ResolveAsync(
            project,
            organizationId,
            OrganizationVideoPolicyScopes.Default,
            CancellationToken.None);

        Assert.Equal(ProviderCodes.BytePlus, first.ProviderCode);
        Assert.Equal("dreamina-seedance-2-5-260628", first.ModelCode);
        Assert.Equal(7, project.VideoPolicyVersion);
        Assert.Equal("720p", project.VideoResolution);
        Assert.True(project.VideoNativeAudio);

        provider.IsEnabled = false;
        provider.Models.Single().IsEnabled = false;
        await providerDb.SaveChangesAsync();

        var preserved = await resolver.ResolveAsync(
            project,
            organizationId,
            OrganizationVideoPolicyScopes.Default,
            CancellationToken.None);

        Assert.Equal(first.ProviderCode, preserved.ProviderCode);
        Assert.Equal(first.ModelCode, preserved.ModelCode);
        Assert.Equal(first.PolicyVersion, preserved.PolicyVersion);
    }

    [Fact]
    public async Task ResolveAsync_DoesNotSnapshotDisabledProviderForNewProject()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await using var governanceDb = new AiGovernanceDbContext(
            new DbContextOptionsBuilder<AiGovernanceDbContext>()
                .UseInMemoryDatabase($"disabled-video-policy-governance-{suffix}")
                .Options);
        await using var providerDb = new ProviderAdminDbContext(
            new DbContextOptionsBuilder<ProviderAdminDbContext>()
                .UseInMemoryDatabase($"disabled-video-policy-provider-{suffix}")
                .Options);
        var organizationId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        governanceDb.Organizations.Add(new Organization
        {
            OrganizationId = organizationId,
            Code = "disabled-policy-org",
            Name = "Disabled Policy Org",
            CreatedByUserId = "owner",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        });
        governanceDb.OrganizationVideoPolicies.Add(new OrganizationVideoPolicy
        {
            OrganizationId = organizationId,
            ProviderId = providerId,
            ProviderModelId = modelId,
            PolicyVersion = 1,
            Resolution = "720p",
            NativeAudio = true,
            IsActive = true,
            UpdatedByUserId = "owner",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        });
        providerDb.Providers.Add(new AiProvider
        {
            ProviderId = providerId,
            ProviderCode = ProviderCodes.BytePlus,
            DisplayName = "BytePlus ModelArk",
            BaseUrl = "https://ark.ap-southeast.bytepluses.com/api/v3/",
            IsEnabled = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8],
            Models =
            [
                new AiProviderModel
                {
                    ProviderModelId = modelId,
                    ProviderId = providerId,
                    ModelCode = "dreamina-seedance-2-5-260628",
                    DisplayName = "Seedance 2.5",
                    Modality = "Video",
                    IsEnabled = false,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    RowVersion = new byte[8]
                }
            ]
        });
        await governanceDb.SaveChangesAsync();
        await providerDb.SaveChangesAsync();
        var resolver = new ProjectVideoPolicyResolver(governanceDb, providerDb, TimeProvider.System);

        var exception = await Assert.ThrowsAsync<AccountApiException>(
            () => resolver.ResolveAsync(
                NewProject(organizationId),
                organizationId,
                OrganizationVideoPolicyScopes.Default,
                CancellationToken.None));

        Assert.Equal("video_model_not_enabled", exception.Code);
    }

    [Fact]
    public async Task ResolveAsync_UsesRequestedScopeAndPreservesExistingSnapshot()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await using var governanceDb = new AiGovernanceDbContext(
            new DbContextOptionsBuilder<AiGovernanceDbContext>()
                .UseInMemoryDatabase($"scoped-video-policy-governance-{suffix}")
                .Options);
        await using var providerDb = new ProviderAdminDbContext(
            new DbContextOptionsBuilder<ProviderAdminDbContext>()
                .UseInMemoryDatabase($"scoped-video-policy-provider-{suffix}")
                .Options);
        var organizationId = Guid.NewGuid();
        var providerId = Guid.NewGuid();
        var defaultModelId = Guid.NewGuid();
        var longFormModelId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        governanceDb.OrganizationVideoPolicies.AddRange(
            new OrganizationVideoPolicy
            {
                OrganizationId = organizationId,
                PolicyScope = OrganizationVideoPolicyScopes.Default,
                ProviderId = providerId,
                ProviderModelId = defaultModelId,
                PolicyVersion = 2,
                Resolution = "720p",
                NativeAudio = true,
                IsActive = true,
                UpdatedByUserId = "owner",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = new byte[8]
            },
            new OrganizationVideoPolicy
            {
                OrganizationId = organizationId,
                PolicyScope = OrganizationVideoPolicyScopes.LongForm,
                ProviderId = providerId,
                ProviderModelId = longFormModelId,
                PolicyVersion = 5,
                Resolution = "720p",
                NativeAudio = true,
                IsActive = true,
                UpdatedByUserId = "owner",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                RowVersion = new byte[8]
            });
        var provider = new AiProvider
        {
            ProviderId = providerId,
            ProviderCode = ProviderCodes.Kling,
            DisplayName = "Kling AI",
            BaseUrl = "https://api-singapore.klingai.com/v1/",
            IsEnabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            RowVersion = new byte[8]
        };
        provider.Models.Add(NewVideoModel(defaultModelId, providerId, provider, "kling-default", now));
        provider.Models.Add(NewVideoModel(longFormModelId, providerId, provider, "kling-long-form", now));
        providerDb.Providers.Add(provider);
        await governanceDb.SaveChangesAsync();
        await providerDb.SaveChangesAsync();
        var resolver = new ProjectVideoPolicyResolver(governanceDb, providerDb, TimeProvider.System);
        var shortProject = NewProject(organizationId);
        var longProject = NewProject(organizationId);

        var shortSnapshot = await resolver.ResolveAsync(
            shortProject,
            organizationId,
            OrganizationVideoPolicyScopes.Default,
            CancellationToken.None);
        var longSnapshot = await resolver.ResolveAsync(
            longProject,
            organizationId,
            OrganizationVideoPolicyScopes.LongForm,
            CancellationToken.None);

        Assert.Equal("kling-default", shortSnapshot.ModelCode);
        Assert.Equal(2, shortSnapshot.PolicyVersion);
        Assert.Equal("kling-long-form", longSnapshot.ModelCode);
        Assert.Equal(5, longSnapshot.PolicyVersion);

        var preserved = await resolver.ResolveAsync(
            shortProject,
            organizationId,
            OrganizationVideoPolicyScopes.LongForm,
            CancellationToken.None);
        Assert.Equal("kling-default", preserved.ModelCode);
        Assert.Equal(2, preserved.PolicyVersion);
    }

    private static AiProviderModel NewVideoModel(
        Guid modelId,
        Guid providerId,
        AiProvider provider,
        string code,
        DateTime now) => new()
    {
        ProviderModelId = modelId,
        ProviderId = providerId,
        Provider = provider,
        ModelCode = code,
        DisplayName = code,
        Modality = "Video",
        IsEnabled = true,
        CapabilitiesJson = """{"minDurationSeconds":3,"maxDurationSeconds":15,"resolutions":["720p"],"aspectRatios":["16:9"],"nativeAudio":true,"referenceImage":true}""",
        CreatedAtUtc = now,
        UpdatedAtUtc = now,
        RowVersion = new byte[8]
    };

    private static Project NewProject(Guid organizationId) => new()
    {
        ProjectId = Guid.NewGuid(),
        OrganizationId = organizationId,
        Name = "Project",
        Topic = "Topic",
        LanguageCode = "vi-VN",
        Platform = "YouTube",
        AspectRatio = "16:9",
        Status = "Draft",
        CurrencyCode = "USD",
        WorkspaceRelativePath = "project",
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
        RowVersion = new byte[8]
    };
}
