using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Generation;
using TOOL_SERVER.Models;
using TOOL_SERVER.Providers;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_TESTS.Generation;

public sealed class LipSyncRuntimeSettingsServiceTests
{
    [Fact]
    public async Task Provider_FallsBackToDeploymentConfigurationWhenDatabaseOverrideIsMissing()
    {
        await using var dbContext = CreateContext();
        var provider = new LipSyncRuntimeSettingsProvider(
            dbContext,
            Options.Create(EnabledOptions("https://configured.example.com/")));

        var snapshot = await provider.GetAsync(CancellationToken.None);

        Assert.Equal("Configuration", snapshot.Source);
        Assert.Equal("https://configured.example.com/", snapshot.Options.PublicBaseUrl);
        Assert.True(LipSyncOptions.IsValid(snapshot.Options));
    }

    [Fact]
    public async Task Provider_UsesDatabaseOverrideAndFailsClosedWhenItIsInvalid()
    {
        await using var dbContext = CreateContext();
        dbContext.AppSettings.Add(Setting(JsonSerializer.Serialize("https://dynamic.example.com/")));
        await dbContext.SaveChangesAsync();
        var provider = new LipSyncRuntimeSettingsProvider(
            dbContext,
            Options.Create(EnabledOptions("https://configured.example.com/")));

        var valid = await provider.GetAsync(CancellationToken.None);
        Assert.Equal("Database", valid.Source);
        Assert.Equal("https://dynamic.example.com/", valid.Options.PublicBaseUrl);

        var setting = await dbContext.AppSettings.SingleAsync();
        setting.ValueJson = JsonSerializer.Serialize("http://unsafe.example.com/");
        await dbContext.SaveChangesAsync();

        var invalid = await provider.GetAsync(CancellationToken.None);
        Assert.Equal("InvalidDatabaseOverride", invalid.Source);
        Assert.Null(invalid.Options.PublicBaseUrl);
        Assert.False(LipSyncOptions.IsValid(invalid.Options));
    }

    [Fact]
    public async Task AdminUpdate_PersistsNormalizedUrlAndAuditEntry()
    {
        var now = new DateTimeOffset(2026, 9, 8, 4, 5, 6, TimeSpan.Zero);
        await using var dbContext = CreateContext();
        var options = Options.Create(EnabledOptions(null));
        var provider = new LipSyncRuntimeSettingsProvider(dbContext, options);
        var service = new LipSyncRuntimeSettingsAdminService(
            dbContext,
            provider,
            options,
            new FixedTimeProvider(now));

        var response = await service.UpdateAsync(
            new UpdateLipSyncRuntimeSettingsRequest(" https://video.example.com ", null),
            new AdminRequestContext("admin-1", "127.0.0.1", "test", "correlation-1"),
            CancellationToken.None);

        Assert.True(response.LipSyncEnabled);
        Assert.True(response.PublicUrlValid);
        Assert.Equal("Database", response.Source);
        Assert.Equal("https://video.example.com/", response.PublicBaseUrl);
        Assert.NotNull(response.RowVersion);
        var setting = await dbContext.AppSettings.SingleAsync();
        Assert.Equal(LipSyncRuntimeSettingsProvider.PublicBaseUrlSettingKey, setting.SettingKey);
        Assert.Equal("https://video.example.com/", JsonSerializer.Deserialize<string>(setting.ValueJson!));
        Assert.Equal(now.UtcDateTime, setting.UpdatedAtUtc);
        var audit = await dbContext.AccountAuditLogs.SingleAsync();
        Assert.Equal("LipSyncPublicBaseUrlUpdated", audit.EventType);
        Assert.Equal("admin-1", audit.UserId);
        Assert.True(audit.Succeeded);
        Assert.Contains("https://video.example.com/", audit.DetailsJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://video.example.com/")]
    [InlineData("https://localhost/")]
    [InlineData("https://video.example.com/base/")]
    [InlineData("https://video.example.com/?token=secret")]
    [InlineData("https://user:pass@video.example.com/")]
    public async Task AdminUpdate_RejectsUnsafeOrAmbiguousUrls(string publicBaseUrl)
    {
        await using var dbContext = CreateContext();
        var options = Options.Create(EnabledOptions(null));
        var service = new LipSyncRuntimeSettingsAdminService(
            dbContext,
            new LipSyncRuntimeSettingsProvider(dbContext, options),
            options,
            TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateAsync(
            new UpdateLipSyncRuntimeSettingsRequest(publicBaseUrl, null),
            new AdminRequestContext("admin-1", null, null, "test"),
            CancellationToken.None));
        Assert.Empty(dbContext.AppSettings);
        Assert.Empty(dbContext.AccountAuditLogs);
    }

    [Fact]
    public async Task AdminUpdate_RejectsStaleRowVersion()
    {
        await using var dbContext = CreateContext();
        var setting = Setting(JsonSerializer.Serialize("https://old.example.com/"));
        setting.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
        dbContext.AppSettings.Add(setting);
        await dbContext.SaveChangesAsync();
        var options = Options.Create(EnabledOptions(null));
        var service = new LipSyncRuntimeSettingsAdminService(
            dbContext,
            new LipSyncRuntimeSettingsProvider(dbContext, options),
            options,
            TimeProvider.System);

        var error = await Assert.ThrowsAsync<AccountApiException>(() => service.UpdateAsync(
            new UpdateLipSyncRuntimeSettingsRequest(
                "https://new.example.com/",
                Convert.ToBase64String([8, 7, 6, 5, 4, 3, 2, 1])),
            new AdminRequestContext("admin-1", null, null, "test"),
            CancellationToken.None));

        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
        Assert.Equal("runtime_setting_conflict", error.Code);
        Assert.Equal(JsonSerializer.Serialize("https://old.example.com/"), setting.ValueJson);
        Assert.Empty(dbContext.AccountAuditLogs);
    }

    [Fact]
    public async Task InputStore_BuildsNewSignedUrlsFromLatestDatabaseSetting()
    {
        await using var dbContext = CreateContext();
        var options = Options.Create(EnabledOptions("https://configured.example.com/"));
        var provider = new LipSyncRuntimeSettingsProvider(dbContext, options);
        var store = new LipSyncInputStore(
            dbContext,
            null!,
            new EphemeralDataProtectionProvider(),
            options,
            TimeProvider.System,
            provider);

        var configuredUrl = await store.CreateProviderContentUrlAsync(
            Guid.NewGuid(),
            LipSyncInputKinds.Video,
            DateTime.UtcNow.AddHours(1),
            CancellationToken.None);
        Assert.StartsWith("https://configured.example.com/api/generation/lip-sync/inputs/", configuredUrl, StringComparison.Ordinal);

        dbContext.AppSettings.Add(Setting(JsonSerializer.Serialize("https://dynamic.example.com/")));
        await dbContext.SaveChangesAsync();

        var dynamicUrl = await store.CreateProviderContentUrlAsync(
            Guid.NewGuid(),
            LipSyncInputKinds.Audio,
            DateTime.UtcNow.AddHours(1),
            CancellationToken.None);
        Assert.StartsWith("https://dynamic.example.com/api/generation/lip-sync/inputs/", dynamicUrl, StringComparison.Ordinal);
    }

    private static AppSetting Setting(string valueJson) =>
        new()
        {
            AppSettingId = Guid.NewGuid(),
            SettingKey = LipSyncRuntimeSettingsProvider.PublicBaseUrlSettingKey,
            ValueJson = valueJson,
            UpdatedAtUtc = DateTime.UtcNow,
            RowVersion = new byte[8]
        };

    private static LipSyncOptions EnabledOptions(string? publicBaseUrl) =>
        new()
        {
            Enabled = true,
            PublicBaseUrl = publicBaseUrl
        };

    private static VideoFactoryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<VideoFactoryDbContext>()
            .UseInMemoryDatabase($"lip-sync-runtime-settings-{Guid.NewGuid():N}")
            .Options);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
