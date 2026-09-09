using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Organizations;
using TOOL_SHARED.Contracts.Generation;

namespace TOOL_SERVER.Generation;

internal sealed class LipSyncOptions
{
    public const string SectionName = "Generation:LipSync";

    public bool Enabled { get; set; }
    public string ProviderCode { get; set; } = ProviderCodes.Fal;
    public string ModelCode { get; set; } = FalLipSyncPolicy.EndpointId;
    public string? PublicBaseUrl { get; set; }
    public string? InputStorageRoot { get; set; }
    public int InputRetentionHours { get; set; } = 24;
    public long MaximumVideoBytes { get; set; } = 256L * 1024 * 1024;
    public long MaximumAudioBytes { get; set; } = 50L * 1024 * 1024;
    public int MaximumDurationSeconds { get; set; } = 120;

    public static bool IsValid(LipSyncOptions options)
    {
        if (!options.Enabled)
        {
            return true;
        }
        return IsCoreConfigurationValid(options) &&
               TryNormalizePublicBaseUrl(options.PublicBaseUrl, out _);
    }

    public static bool IsStartupConfigurationValid(LipSyncOptions options) =>
        !options.Enabled ||
        IsCoreConfigurationValid(options) &&
        (string.IsNullOrWhiteSpace(options.PublicBaseUrl) ||
         TryNormalizePublicBaseUrl(options.PublicBaseUrl, out _));

    public static bool TryNormalizePublicBaseUrl(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 2048 ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/" ||
            uri.IsLoopback ||
            Uri.CheckHostName(uri.DnsSafeHost) != UriHostNameType.Dns ||
            !uri.DnsSafeHost.Contains('.'))
        {
            return false;
        }

        normalized = uri.AbsoluteUri;
        return true;
    }

    private static bool IsCoreConfigurationValid(LipSyncOptions options) =>
        options.ProviderCode == ProviderCodes.Fal &&
               FalLipSyncPolicy.IsApprovedEndpoint(options.ModelCode) &&
               options.InputRetentionHours is >= 1 and <= 72 &&
               options.MaximumVideoBytes is >= 1024 and <= 1024L * 1024 * 1024 &&
               options.MaximumAudioBytes is >= 1024 and <= 200L * 1024 * 1024 &&
               options.MaximumDurationSeconds is >= 1 and <= 120;
}

public interface ILipSyncInputStore
{
    Task<LipSyncInputUploadResponse> UploadAsync(
        Guid sessionId,
        string inputKind,
        Stream source,
        long? contentLength,
        string? contentType,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken);

    Task<string> CreateProviderContentUrlAsync(
        Guid sessionId,
        string inputKind,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken);

    Task CopyProviderInputAsync(
        HttpContext httpContext,
        Guid sessionId,
        string inputKind,
        string token,
        CancellationToken cancellationToken);

    Task<int> CleanupExpiredAsync(CancellationToken cancellationToken);
}

internal sealed class LipSyncInputStore(
    VideoFactoryDbContext dbContext,
    IGenerationAccessService accessService,
    IDataProtectionProvider dataProtectionProvider,
    IOptions<LipSyncOptions> options,
    TimeProvider timeProvider,
    ILipSyncRuntimeSettingsProvider? runtimeSettingsProvider = null) : ILipSyncInputStore
{
    private readonly LipSyncOptions _options = options.Value;
    private readonly string _storageRoot = ResolveStorageRoot(options.Value.InputStorageRoot);
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("VideoMaker.LipSyncInput.v1");

    public async Task<LipSyncInputUploadResponse> UploadAsync(
        Guid sessionId,
        string inputKind,
        Stream source,
        long? contentLength,
        string? contentType,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        EnsureEnabled();
        inputKind = NormalizeKind(inputKind);
        var session = await dbContext.LipSyncInputSessions.SingleOrDefaultAsync(
            x => x.LipSyncInputSessionId == sessionId,
            cancellationToken) ?? throw NotFound();
        var access = await accessService.RequireAsync(
            userId,
            deviceId,
            session.OrganizationId,
            session.ProjectId,
            cancellationToken);
        if (access.OrganizationId != session.OrganizationId ||
            !string.Equals(session.RequestedByUserId, userId, StringComparison.Ordinal))
        {
            throw NotFound();
        }
        var now = UtcNow();
        if (session.ExpiresAtUtc <= now)
        {
            DeleteStorage(session.VideoStorageKey);
            DeleteStorage(session.AudioStorageKey);
            DeleteSessionDirectory(session.LipSyncInputSessionId);
            session.VideoStorageKey = null;
            session.AudioStorageKey = null;
            session.VideoSizeBytes = null;
            session.AudioSizeBytes = null;
            session.Status = LipSyncStatuses.Expired;
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new AccountApiException(StatusCodes.Status410Gone, LipSyncErrorCodes.InputExpired, "Phiên upload lip-sync đã hết hạn.");
        }
        if (session.Status is LipSyncStatuses.Submitted or LipSyncStatuses.Expired or LipSyncStatuses.Failed)
        {
            throw new AccountApiException(StatusCodes.Status409Conflict, LipSyncErrorCodes.InputNotReady, "Phiên upload lip-sync không còn nhận dữ liệu.");
        }

        var isVideo = inputKind == LipSyncInputKinds.Video;
        ValidateContentType(contentType, isVideo);
        var limit = isVideo ? _options.MaximumVideoBytes : _options.MaximumAudioBytes;
        if (contentLength is <= 0 || contentLength > limit)
        {
            throw new AccountApiException(StatusCodes.Status413PayloadTooLarge, "lip_sync_input_too_large", "Input lip-sync rỗng hoặc vượt giới hạn dung lượng.");
        }

        Directory.CreateDirectory(_storageRoot);
        var extension = isVideo ? ".mp4" : ".wav";
        var storageKey = $"{sessionId:N}/{inputKind.ToLowerInvariant()}{extension}";
        var destinationPath = ResolveStoragePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var temporaryPath = destinationPath + $".{Guid.NewGuid():N}.tmp";
        var expectedHash = isVideo ? session.PreparedVideoSha256 : session.PreparedAudioSha256;
        var (size, sha256) = await WriteAsync(source, temporaryPath, limit, cancellationToken);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(sha256),
                    Convert.FromHexString(expectedHash)))
            {
                throw new AccountApiException(StatusCodes.Status422UnprocessableEntity, LipSyncErrorCodes.InputChanged, "SHA-256 của input lip-sync không khớp snapshot đã báo giá.");
            }

            if (isVideo ? !HasIsoBaseMediaSignature(temporaryPath) : !HasWaveSignature(temporaryPath))
            {
                throw new AccountApiException(StatusCodes.Status422UnprocessableEntity, "lip_sync_input_signature_invalid", "Lip-sync input does not have a valid MP4 or WAV signature.");
            }
            // A failed retry must not overwrite or delete the last verified input.
            File.Move(temporaryPath, destinationPath, true);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
            throw;
        }

        if (isVideo)
        {
            session.VideoStorageKey = storageKey;
            session.VideoSizeBytes = size;
        }
        else
        {
            session.AudioStorageKey = storageKey;
            session.AudioSizeBytes = size;
        }
        session.Status = session.VideoStorageKey is not null && session.AudioStorageKey is not null
            ? LipSyncStatuses.Ready
            : LipSyncStatuses.Uploading;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new LipSyncInputUploadResponse(
            sessionId,
            inputKind,
            session.Status,
            sha256,
            size,
            session.VideoStorageKey is not null,
            session.AudioStorageKey is not null);
    }

    public async Task<string> CreateProviderContentUrlAsync(
        Guid sessionId,
        string inputKind,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken)
    {
        var effectiveOptions = runtimeSettingsProvider is null
            ? _options
            : (await runtimeSettingsProvider.GetAsync(cancellationToken)).Options;
        if (!effectiveOptions.Enabled)
        {
            throw new AccountApiException(
                StatusCodes.Status409Conflict,
                LipSyncErrorCodes.Disabled,
                "Lip-sync đang tắt theo feature flag của server.");
        }
        if (!LipSyncOptions.IsValid(effectiveOptions))
        {
            throw new AccountApiException(
                StatusCodes.Status503ServiceUnavailable,
                LipSyncErrorCodes.NotConfigured,
                "Public HTTPS input URL cho lip-sync chưa được cấu hình an toàn.");
        }

        inputKind = NormalizeKind(inputKind);
        var protectedValue = _protector.Protect($"{sessionId:D}|{inputKind}|{expiresAtUtc.ToUniversalTime().Ticks}");
        var relative = $"/api/generation/lip-sync/inputs/{sessionId:D}/{inputKind.ToLowerInvariant()}/content?token={Uri.EscapeDataString(protectedValue)}";
        return new Uri(new Uri(effectiveOptions.PublicBaseUrl!, UriKind.Absolute), relative).AbsoluteUri;
    }

    public async Task CopyProviderInputAsync(
        HttpContext httpContext,
        Guid sessionId,
        string inputKind,
        string token,
        CancellationToken cancellationToken)
    {
        inputKind = NormalizeKind(inputKind);
        ValidateToken(sessionId, inputKind, token);
        var session = await dbContext.LipSyncInputSessions.AsNoTracking().SingleOrDefaultAsync(
            x => x.LipSyncInputSessionId == sessionId &&
                 x.ExpiresAtUtc > UtcNow() &&
                 (x.Status == LipSyncStatuses.Ready || x.Status == LipSyncStatuses.Submitted),
            cancellationToken) ?? throw NotFound();
        var storageKey = inputKind == LipSyncInputKinds.Video
            ? session.VideoStorageKey
            : session.AudioStorageKey;
        var expectedSize = inputKind == LipSyncInputKinds.Video
            ? session.VideoSizeBytes
            : session.AudioSizeBytes;
        if (string.IsNullOrWhiteSpace(storageKey) || expectedSize is not > 0)
        {
            throw NotFound();
        }
        var path = ResolveStoragePath(storageKey);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != expectedSize)
        {
            throw NotFound();
        }
        httpContext.Response.ContentType = inputKind == LipSyncInputKinds.Video ? "video/mp4" : "audio/wav";
        httpContext.Response.ContentLength = file.Length;
        httpContext.Response.Headers.CacheControl = "private, no-store";
        httpContext.Response.Headers.XContentTypeOptions = "nosniff";
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await stream.CopyToAsync(httpContext.Response.Body, 128 * 1024, cancellationToken);
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var expired = await dbContext.LipSyncInputSessions
            .Where(x => x.ExpiresAtUtc <= now && x.Status != LipSyncStatuses.Expired)
            .OrderBy(x => x.ExpiresAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var session in expired)
        {
            DeleteStorage(session.VideoStorageKey);
            DeleteStorage(session.AudioStorageKey);
            DeleteSessionDirectory(session.LipSyncInputSessionId);
            session.VideoStorageKey = null;
            session.AudioStorageKey = null;
            session.VideoSizeBytes = null;
            session.AudioSizeBytes = null;
            session.Status = LipSyncStatuses.Expired;
        }
        if (expired.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return expired.Count;
    }

    private void ValidateToken(Guid sessionId, string inputKind, string token)
    {
        try
        {
            var parts = _protector.Unprotect(token).Split('|');
            if (parts.Length != 3 ||
                !Guid.TryParse(parts[0], out var protectedSessionId) ||
                !long.TryParse(parts[2], out var ticks) ||
                protectedSessionId != sessionId ||
                !string.Equals(parts[1], inputKind, StringComparison.Ordinal) ||
                new DateTime(ticks, DateTimeKind.Utc) <= UtcNow())
            {
                throw NotFound();
            }
        }
        catch (Exception exception) when (exception is not AccountApiException)
        {
            throw NotFound();
        }
    }

    private static async Task<(long Size, string Sha256)> WriteAsync(
        Stream source,
        string temporaryPath,
        long limit,
        CancellationToken cancellationToken)
    {
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }
                    total += read;
                    if (total > limit)
                    {
                        throw new AccountApiException(StatusCodes.Status413PayloadTooLarge, "lip_sync_input_too_large", "Input lip-sync vượt giới hạn dung lượng.");
                    }
                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (total <= 0)
                {
                    throw new AccountApiException(StatusCodes.Status422UnprocessableEntity, "lip_sync_input_empty", "Input lip-sync không được rỗng.");
                }
                await destination.FlushAsync(cancellationToken);
            }
            var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            return (total, sha256);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
            throw;
        }
    }

    private void DeleteStorage(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return;
        }
        var path = ResolveStoragePath(storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void DeleteSessionDirectory(Guid sessionId)
    {
        var knownChildPath = ResolveStoragePath($"{sessionId:N}/video.mp4");
        var sessionDirectory = Path.GetDirectoryName(knownChildPath)!;
        if (Directory.Exists(sessionDirectory))
        {
            Directory.Delete(sessionDirectory, recursive: true);
        }
    }

    private void EnsureEnabled()
    {
        if (!_options.Enabled)
        {
            throw new AccountApiException(StatusCodes.Status409Conflict, LipSyncErrorCodes.Disabled, "Lip-sync đang tắt theo feature flag của server.");
        }
    }

    private static void ValidateContentType(string? contentType, bool isVideo)
    {
        var normalized = contentType?.Split(';', 2)[0].Trim().ToLowerInvariant();
        var valid = isVideo
            ? normalized == "video/mp4"
            : normalized is "audio/wav" or "audio/x-wav" or "audio/wave";
        if (!valid)
        {
            throw new AccountApiException(StatusCodes.Status415UnsupportedMediaType, "lip_sync_input_type_invalid", isVideo ? "Input video phải là MP4." : "Input âm thanh phải là WAV.");
        }
    }

    private static string NormalizeKind(string? inputKind) => inputKind?.Trim().ToLowerInvariant() switch
    {
        "video" => LipSyncInputKinds.Video,
        "audio" => LipSyncInputKinds.Audio,
        _ => throw new AccountApiException(StatusCodes.Status404NotFound, "lip_sync_input_not_found", "Không tìm thấy input lip-sync.")
    };

    private static bool HasIsoBaseMediaSignature(string path)
    {
        Span<byte> header = stackalloc byte[12];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return stream.Read(header) == header.Length &&
               header[4] == (byte)'f' && header[5] == (byte)'t' &&
               header[6] == (byte)'y' && header[7] == (byte)'p';
    }

    private static bool HasWaveSignature(string path)
    {
        Span<byte> header = stackalloc byte[12];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return stream.Read(header) == header.Length &&
               header[..4].SequenceEqual("RIFF"u8) &&
               header[8..12].SequenceEqual("WAVE"u8);
    }

    private string ResolveStoragePath(string storageKey)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_storageRoot, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = _storageRoot.EndsWith(Path.DirectorySeparatorChar) ? _storageRoot : _storageRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Lip-sync storage key vượt khỏi storage root.");
        }
        return fullPath;
    }

    private static string ResolveStorageRoot(string? configured) =>
        Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "data", "lip-sync-inputs")
            : configured);

    private static AccountApiException NotFound() =>
        new(StatusCodes.Status404NotFound, "lip_sync_input_not_found", "Không tìm thấy input lip-sync.");

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}

internal sealed class LipSyncInputCleanupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<LipSyncInputCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ILipSyncInputStore>().CleanupExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Lip-sync input cleanup cycle failed.");
            }
            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                return;
            }
        }
    }
}
