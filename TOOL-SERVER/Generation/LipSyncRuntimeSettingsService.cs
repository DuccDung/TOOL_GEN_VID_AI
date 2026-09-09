using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Models;
using TOOL_SERVER.Providers;

namespace TOOL_SERVER.Generation;

public sealed record LipSyncRuntimeSettingsResponse(
    bool LipSyncEnabled,
    string? PublicBaseUrl,
    string Source,
    bool PublicUrlValid,
    DateTime? UpdatedAtUtc,
    string? RowVersion);

public sealed record UpdateLipSyncRuntimeSettingsRequest(
    string PublicBaseUrl,
    string? RowVersion);

internal sealed record LipSyncRuntimeSettingsSnapshot(
    LipSyncOptions Options,
    string Source,
    DateTime? UpdatedAtUtc,
    byte[]? RowVersion);

internal interface ILipSyncRuntimeSettingsProvider
{
    Task<LipSyncRuntimeSettingsSnapshot> GetAsync(CancellationToken cancellationToken);
}

public interface ILipSyncRuntimeSettingsAdminService
{
    Task<LipSyncRuntimeSettingsResponse> GetAsync(CancellationToken cancellationToken);
    Task<LipSyncRuntimeSettingsResponse> UpdateAsync(
        UpdateLipSyncRuntimeSettingsRequest request,
        AdminRequestContext context,
        CancellationToken cancellationToken);
}

internal sealed class LipSyncRuntimeSettingsProvider(
    VideoFactoryDbContext dbContext,
    IOptions<LipSyncOptions> configuredOptions) : ILipSyncRuntimeSettingsProvider
{
    internal const string PublicBaseUrlSettingKey = "Generation:LipSync:PublicBaseUrl";
    private readonly LipSyncOptions _configuredOptions = configuredOptions.Value;

    public async Task<LipSyncRuntimeSettingsSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        var setting = await dbContext.AppSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.SettingKey == PublicBaseUrlSettingKey, cancellationToken);
        if (setting is null)
        {
            return new LipSyncRuntimeSettingsSnapshot(
                CopyWithPublicBaseUrl(_configuredOptions, _configuredOptions.PublicBaseUrl),
                string.IsNullOrWhiteSpace(_configuredOptions.PublicBaseUrl) ? "Missing" : "Configuration",
                null,
                null);
        }

        var publicBaseUrl = DeserializePublicBaseUrl(setting.ValueJson);
        return new LipSyncRuntimeSettingsSnapshot(
            CopyWithPublicBaseUrl(_configuredOptions, publicBaseUrl),
            publicBaseUrl is null ? "InvalidDatabaseOverride" : "Database",
            setting.UpdatedAtUtc,
            setting.RowVersion?.ToArray());
    }

    internal static string? DeserializePublicBaseUrl(string? valueJson)
    {
        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return null;
        }

        try
        {
            var value = JsonSerializer.Deserialize<string>(valueJson);
            return LipSyncOptions.TryNormalizePublicBaseUrl(value, out var normalized)
                ? normalized
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static LipSyncOptions CopyWithPublicBaseUrl(LipSyncOptions source, string? publicBaseUrl) =>
        new()
        {
            Enabled = source.Enabled,
            ProviderCode = source.ProviderCode,
            ModelCode = source.ModelCode,
            PublicBaseUrl = publicBaseUrl,
            InputStorageRoot = source.InputStorageRoot,
            InputRetentionHours = source.InputRetentionHours,
            MaximumVideoBytes = source.MaximumVideoBytes,
            MaximumAudioBytes = source.MaximumAudioBytes,
            MaximumDurationSeconds = source.MaximumDurationSeconds
        };
}

internal sealed class LipSyncRuntimeSettingsAdminService(
    VideoFactoryDbContext dbContext,
    ILipSyncRuntimeSettingsProvider runtimeSettingsProvider,
    IOptions<LipSyncOptions> configuredOptions,
    TimeProvider timeProvider) : ILipSyncRuntimeSettingsAdminService
{
    private const string Description = "Public HTTPS root URL used by Fal to download signed lip-sync inputs.";
    private readonly LipSyncOptions _configuredOptions = configuredOptions.Value;

    public async Task<LipSyncRuntimeSettingsResponse> GetAsync(CancellationToken cancellationToken) =>
        ToResponse(await runtimeSettingsProvider.GetAsync(cancellationToken));

    public async Task<LipSyncRuntimeSettingsResponse> UpdateAsync(
        UpdateLipSyncRuntimeSettingsRequest request,
        AdminRequestContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!LipSyncOptions.TryNormalizePublicBaseUrl(request.PublicBaseUrl, out var normalizedPublicBaseUrl))
        {
            throw new ArgumentException(
                "PublicBaseUrl phải là HTTPS URL gốc dùng cổng 443, có domain công khai và không chứa path, query, fragment hoặc thông tin đăng nhập.");
        }

        var setting = await dbContext.AppSettings.SingleOrDefaultAsync(
            x => x.SettingKey == LipSyncRuntimeSettingsProvider.PublicBaseUrlSettingKey,
            cancellationToken);
        var previousPublicBaseUrl = setting is null
            ? _configuredOptions.PublicBaseUrl
            : LipSyncRuntimeSettingsProvider.DeserializePublicBaseUrl(setting.ValueJson);
        if (setting is null)
        {
            if (!string.IsNullOrWhiteSpace(request.RowVersion))
            {
                throw Conflict();
            }

            setting = new AppSetting
            {
                AppSettingId = Guid.NewGuid(),
                SettingKey = LipSyncRuntimeSettingsProvider.PublicBaseUrlSettingKey,
                Description = Description,
                RowVersion = new byte[8]
            };
            dbContext.AppSettings.Add(setting);
        }
        else
        {
            RequireCurrentRowVersion(setting.RowVersion, request.RowVersion);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        setting.ValueJson = JsonSerializer.Serialize(normalizedPublicBaseUrl);
        setting.Description = Description;
        setting.UpdatedAtUtc = now;
        dbContext.AccountAuditLogs.Add(new AccountAuditLog
        {
            UserId = context.UserId,
            EventType = "LipSyncPublicBaseUrlUpdated",
            Succeeded = true,
            IpAddress = context.IpAddress,
            UserAgent = context.UserAgent,
            CorrelationId = context.CorrelationId,
            DetailsJson = JsonSerializer.Serialize(new
            {
                SettingKey = LipSyncRuntimeSettingsProvider.PublicBaseUrlSettingKey,
                PreviousPublicBaseUrl = previousPublicBaseUrl,
                PublicBaseUrl = normalizedPublicBaseUrl
            }),
            OccurredAtUtc = now
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict();
        }
        catch (DbUpdateException) when (string.IsNullOrWhiteSpace(request.RowVersion))
        {
            throw Conflict();
        }

        return new LipSyncRuntimeSettingsResponse(
            _configuredOptions.Enabled,
            normalizedPublicBaseUrl,
            "Database",
            true,
            setting.UpdatedAtUtc,
            EncodeRowVersion(setting.RowVersion));
    }

    private LipSyncRuntimeSettingsResponse ToResponse(LipSyncRuntimeSettingsSnapshot snapshot)
    {
        var valid = LipSyncOptions.TryNormalizePublicBaseUrl(snapshot.Options.PublicBaseUrl, out var normalized);
        return new LipSyncRuntimeSettingsResponse(
            snapshot.Options.Enabled,
            valid ? normalized : null,
            snapshot.Source,
            valid,
            snapshot.UpdatedAtUtc,
            EncodeRowVersion(snapshot.RowVersion));
    }

    private static void RequireCurrentRowVersion(byte[] current, string? value)
    {
        byte[] supplied;
        try
        {
            supplied = Convert.FromBase64String(value ?? string.Empty);
        }
        catch (FormatException)
        {
            throw Conflict();
        }

        if (supplied.Length == 0 ||
            current.Length != supplied.Length ||
            !CryptographicOperations.FixedTimeEquals(current, supplied))
        {
            throw Conflict();
        }
    }

    private static string? EncodeRowVersion(byte[]? value) =>
        value is { Length: > 0 } ? Convert.ToBase64String(value) : null;

    private static AccountApiException Conflict() =>
        new(
            StatusCodes.Status409Conflict,
            "runtime_setting_conflict",
            "Cấu hình đã được quản trị viên khác cập nhật. Hãy tải lại trước khi lưu.");
}
