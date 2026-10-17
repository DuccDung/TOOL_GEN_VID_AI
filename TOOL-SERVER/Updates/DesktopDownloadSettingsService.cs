using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Configuration;
using TOOL_SERVER.Data;
using TOOL_SERVER.Models;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_SERVER.Updates;

/// <summary>Stores the website link without downloading a file or changing bootstrapper state.</summary>
public sealed class DesktopDownloadSettingsService(
    VideoFactoryDbContext db,
    IOptions<DesktopReleaseOptions> settings,
    TimeProvider clock)
{
    public const string SettingKey = "Desktop.SetupDownload";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<DesktopDownloadSettingsResponse> GetAsync(CancellationToken token = default)
    {
        var row = await db.ServerSettings.AsNoTracking().SingleOrDefaultAsync(x => x.SettingKey == SettingKey, token);
        return Map(row);
    }

    public async Task<string?> GetPublicDownloadUrlAsync(CancellationToken token = default) =>
        (await GetAsync(token)).SetupDownloadUrl;

    public async Task<DesktopDownloadSettingsResponse> SaveAsync(
        string actor, DesktopDownloadSettingsRequest request, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 450)
            throw new AccountApiException(403, "admin_required", "Cần quyền quản trị hệ thống.");
        if (request is null) throw InvalidUrl();
        var url = ValidateUrl(request.SetupDownloadUrl);
        var row = await db.ServerSettings.SingleOrDefaultAsync(x => x.SettingKey == SettingKey, token);
        if (row is null)
        {
            if (request.Revision is not null) throw Changed();
            row = new ServerSetting
            {
                ServerSettingId = Guid.NewGuid(), SettingKey = SettingKey,
                Description = "Public Windows Setup download link."
            };
            db.ServerSettings.Add(row);
        }
        else
        {
            byte[] revision;
            try { revision = Convert.FromBase64String(request.Revision ?? string.Empty); }
            catch (FormatException) { throw Changed(); }
            if (revision.Length != 8 || !revision.AsSpan().SequenceEqual(row.RowVersion)) throw Changed();
            db.Entry(row).Property(x => x.RowVersion).OriginalValue = revision;
        }
        row.ValueJson = JsonSerializer.Serialize(new { setupDownloadUrl = url }, Json);
        row.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { throw Changed(); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        { throw Changed(); }
        return Map(row);
    }

    private DesktopDownloadSettingsResponse Map(ServerSetting? row) => row is null
        ? new(null, null, null)
        : new(ReadStoredUrl(row.ValueJson), Convert.ToBase64String(row.RowVersion), DateTime.SpecifyKind(row.UpdatedAtUtc, DateTimeKind.Utc));

    private string? ReadStoredUrl(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 16384) return null;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                document.RootElement.EnumerateObject().Count(x => x.Name.Equals("setupDownloadUrl", StringComparison.OrdinalIgnoreCase)) != 1 ||
                !document.RootElement.TryGetProperty("setupDownloadUrl", out var value) || value.ValueKind != JsonValueKind.String)
                return null;
            return ValidateUrl(value.GetString());
        }
        catch (JsonException) { return null; }
        catch (AccountApiException) { return null; }
    }

    private string ValidateUrl(string? value)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrEmpty(candidate) || candidate.Length > 2000 ||
            candidate.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '\\' or '?' or '#') ||
            Regex.IsMatch(candidate, @"%(?![0-9a-fA-F]{2})|%2f|%5c", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out var resource) || resource.Scheme != Uri.UriSchemeHttps ||
            resource.UserInfo.Length != 0 || resource.Query.Length != 0 || resource.Fragment.Length != 0)
            throw InvalidUrl();
        var decoded = Uri.UnescapeDataString(candidate);
        if (decoded.Any(c => char.IsControl(c) || c is '\\' or '%' or '?' or '#') ||
            decoded.Split('/').Any(segment => segment is "." or "..") ||
            !Uri.UnescapeDataString(resource.AbsolutePath).EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            resource.AbsoluteUri.Length > 2000)
            throw InvalidUrl();
        try
        {
            if (!(settings.Value.AllowedExternalPrefixes ?? []).Any(prefix => DesktopArtifactDownloadPolicy.MatchesPrefix(resource, prefix)))
                throw InvalidUrl();
        }
        catch (InvalidDataException) { throw InvalidUrl(); }
        return resource.AbsoluteUri;
    }

    private static AccountApiException InvalidUrl() => new(400, "desktop_setup_download_url_invalid",
        "Nhập link HTTPS tới file Setup.exe trong nguồn tải VPS đã cấu hình.");
    private static AccountApiException Changed() => new(409, "desktop_setup_download_settings_changed",
        "Link tải đã được thay đổi. Tải lại thông tin rồi lưu lại.");
}
