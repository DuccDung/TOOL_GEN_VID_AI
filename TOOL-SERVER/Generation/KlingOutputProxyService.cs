using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Data;
using TOOL_SERVER.Models;
using TOOL_SERVER.Organizations;

namespace TOOL_SERVER.Generation;

public interface IKlingOutputProxyService
{
    Task CopyToResponseAsync(
        HttpContext httpContext,
        Guid providerRequestId,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken);
}

public interface IVideoOutputStore
{
    Task CacheAsync(
        Guid providerRequestId,
        string outputUrl,
        CancellationToken cancellationToken);

    Task CopyToResponseAsync(
        HttpContext httpContext,
        Guid providerRequestId,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken);

    Task<int> CleanupExpiredAsync(CancellationToken cancellationToken);
}

internal sealed class VideoOutputOptions
{
    public const string SectionName = "Generation:VideoOutputs";
    public string? StorageRoot { get; set; }
    public int RetentionHours { get; set; } = 48;
    public long MaximumFileBytes { get; set; } = 1024L * 1024 * 1024;
    public long MaximumStorageBytes { get; set; } = 20L * 1024 * 1024 * 1024;
    public Dictionary<string, string[]> AllowedHostSuffixes { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [ProviderCodes.Kling] = ["klingai.com", "kwaicdn.com", "kwimgs.com"],
        [ProviderCodes.BytePlus] = ["bytepluses.com", "volces.com"],
        [ProviderCodes.Fal] = ["fal.media", "=storage.googleapis.com"]
    };
}

internal sealed class KlingOutputProxyService(
    VideoFactoryDbContext dbContext,
    IGenerationAccessService accessService,
    IHttpClientFactory httpClientFactory,
    IOptions<VideoOutputOptions> options,
    TimeProvider timeProvider) : IKlingOutputProxyService, IVideoOutputStore
{
    private readonly string _storageRoot = ResolveStorageRoot(options.Value.StorageRoot);
    private readonly TimeSpan _retention = TimeSpan.FromHours(Math.Clamp(options.Value.RetentionHours, 1, 24 * 30));
    private readonly long _maximumFileBytes = Math.Clamp(options.Value.MaximumFileBytes, 1L * 1024 * 1024, 10L * 1024 * 1024 * 1024);
    private readonly long _maximumStorageBytes = Math.Clamp(options.Value.MaximumStorageBytes, 1L * 1024 * 1024, 1024L * 1024 * 1024 * 1024);
    private readonly IReadOnlyDictionary<string, string[]> _allowedHostSuffixes = NormalizeAllowedHostSuffixes(
        options.Value.AllowedHostSuffixes);

    public async Task CopyToResponseAsync(
        HttpContext httpContext,
        Guid providerRequestId,
        string userId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var requestLog = await dbContext.ProviderRequests
            .SingleOrDefaultAsync(
                x => x.ProviderRequestId == providerRequestId &&
                     x.RequestKind == "Video" &&
                     x.Status == "Completed",
                cancellationToken)
            ?? throw NotFound();
        var access = await accessService.RequireAsync(
            userId,
            deviceId,
            requestLog.OrganizationId,
            requestLog.ProjectId,
            cancellationToken);
        if (requestLog.OrganizationId != access.OrganizationId || requestLog.RequestedByUserId != userId)
        {
            throw NotFound();
        }

        var cached = await dbContext.GeneratedVideoOutputs
            .SingleOrDefaultAsync(
                x => x.ProviderRequestId == providerRequestId &&
                     x.Status == "Ready" &&
                     x.DeletedAtUtc == null,
                cancellationToken);
        if (cached is not null)
        {
            await CopyCachedAsync(httpContext, cached, cancellationToken);
            cached.DownloadedAtUtc ??= UtcNow();
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        // Completed Kling requests created before migration 4.0.4 do not have a
        // cached output. Keep the old secure proxy path for those rows only.
        if (requestLog.ProviderCode != ProviderCodes.Kling)
        {
            throw new AccountApiException(
                StatusCodes.Status502BadGateway,
                "provider_output_download_failed",
                "Video provider đã hoàn tất nhưng output chưa được server lưu an toàn.");
        }

        var outputUrl = GenerationService.ExtractOutputUrl(requestLog.ResponseJson);
        if (!Uri.TryCreate(outputUrl, UriKind.Absolute, out var current))
        {
            throw new AccountApiException(StatusCodes.Status502BadGateway, "kling_output_missing", "Kling không trả về tệp video hợp lệ.");
        }

        using var response = await SendValidatedAsync(current, requestLog.ProviderCode, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AccountApiException(StatusCodes.Status502BadGateway, "kling_download_failed", "Không thể tải video từ Kling.");
        }
        if (response.Content.Headers.ContentLength is { } contentLength && contentLength > _maximumFileBytes)
        {
            throw new AccountApiException(StatusCodes.Status413PayloadTooLarge, "kling_output_too_large", "Video Kling vượt quá giới hạn 1 GB.");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null &&
            !mediaType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) &&
            !mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            throw new AccountApiException(
                StatusCodes.Status502BadGateway,
                "kling_output_invalid_content_type",
                "Kling trả về nội dung không phải video.");
        }

        httpContext.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "video/mp4";
        httpContext.Response.ContentLength = response.Content.Headers.ContentLength;
        httpContext.Response.Headers.CacheControl = "private, no-store";
        httpContext.Response.Headers.XContentTypeOptions = "nosniff";
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }
            total += read;
            if (total > _maximumFileBytes)
            {
                throw new AccountApiException(StatusCodes.Status413PayloadTooLarge, "kling_output_too_large", "Video Kling vượt quá giới hạn 1 GB.");
            }
            await httpContext.Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    public async Task CacheAsync(
        Guid providerRequestId,
        string outputUrl,
        CancellationToken cancellationToken)
    {
        if (await dbContext.GeneratedVideoOutputs
            .AsNoTracking()
            .AnyAsync(
                x => x.ProviderRequestId == providerRequestId &&
                     x.Status == "Ready" &&
                     x.DeletedAtUtc == null,
                cancellationToken))
        {
            return;
        }
        var providerCode = await dbContext.ProviderRequests
            .AsNoTracking()
            .Where(x => x.ProviderRequestId == providerRequestId && x.RequestKind == "Video")
            .Select(x => x.ProviderCode)
            .SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(providerCode))
        {
            throw NotFound();
        }
        var usedStorageBytes = await dbContext.GeneratedVideoOutputs
            .AsNoTracking()
            .Where(x => x.Status == "Ready" && x.DeletedAtUtc == null)
            .SumAsync(x => (long?)x.SizeBytes, cancellationToken) ?? 0;
        var availableStorageBytes = _maximumStorageBytes - usedStorageBytes;
        if (availableStorageBytes <= 0)
        {
            throw OutputCacheFull();
        }
        var transferLimit = Math.Min(_maximumFileBytes, availableStorageBytes);
        if (!Uri.TryCreate(outputUrl, UriKind.Absolute, out var uri))
        {
            throw new AccountApiException(
                StatusCodes.Status502BadGateway,
                "provider_output_missing",
                "Provider không trả về URL video hợp lệ.");
        }
        Directory.CreateDirectory(_storageRoot);
        var storageKey = $"{providerRequestId:N}.mp4";
        var destinationPath = ResolveStoragePath(storageKey);
        var temporaryPath = ResolveStoragePath($"{storageKey}.{Guid.NewGuid():N}.tmp");
        using var response = await SendValidatedAsync(uri, providerCode, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new AccountApiException(
                StatusCodes.Status502BadGateway,
                "provider_output_download_failed",
                "Không thể tải video từ provider.");
        }
        if (response.Content.Headers.ContentLength is { } contentLength && contentLength > transferLimit)
        {
            throw contentLength > _maximumFileBytes ? OutputTooLarge() : OutputCacheFull();
        }
        var mimeType = response.Content.Headers.ContentType?.MediaType ?? "video/mp4";
        if (!mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) &&
            !mimeType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            throw new AccountApiException(
                StatusCodes.Status502BadGateway,
                "provider_output_invalid_content_type",
                "Provider trả về nội dung không phải video.");
        }
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        var (total, sha256) = await WriteAndPromoteAsync(
            source,
            temporaryPath,
            destinationPath,
            transferLimit,
            _maximumFileBytes,
            cancellationToken);

        var now = UtcNow();
        var existing = await dbContext.GeneratedVideoOutputs
            .SingleOrDefaultAsync(x => x.ProviderRequestId == providerRequestId, cancellationToken);
        if (existing is null)
        {
            existing = new GeneratedVideoOutput
            {
                ProviderRequestId = providerRequestId,
                RowVersion = new byte[8]
            };
            dbContext.GeneratedVideoOutputs.Add(existing);
        }
        existing.StorageKey = storageKey;
        existing.MimeType = mimeType;
        existing.Sha256 = sha256;
        existing.SizeBytes = total;
        existing.Status = "Ready";
        existing.CreatedAtUtc = now;
        existing.ExpiresAtUtc = now.Add(_retention);
        existing.DeletedAtUtc = null;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }
            throw;
        }
    }

    internal static async Task<(long Total, string Sha256)> WriteAndPromoteAsync(
        Stream source,
        string temporaryPath,
        string destinationPath,
        long transferLimit,
        long maximumFileBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
                System.Security.Cryptography.HashAlgorithmName.SHA256);
            long total = 0;
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
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
                    if (total > transferLimit)
                    {
                        throw total > maximumFileBytes ? OutputTooLarge() : OutputCacheFull();
                    }
                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (total <= 0)
                {
                    throw new AccountApiException(
                        StatusCodes.Status502BadGateway,
                        "provider_output_empty",
                        "Provider trả về tệp video rỗng.");
                }
                await destination.FlushAsync(cancellationToken);
            }

            var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            File.Move(temporaryPath, destinationPath, true);
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

    public async Task<int> CleanupExpiredAsync(CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var expired = await dbContext.GeneratedVideoOutputs
            .Where(x => x.Status == "Ready" && x.ExpiresAtUtc <= now)
            .OrderBy(x => x.ExpiresAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
        foreach (var output in expired)
        {
            var path = ResolveStoragePath(output.StorageKey);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            output.Status = "Deleted";
            output.DeletedAtUtc = now;
        }
        if (expired.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return expired.Count;
    }

    private async Task CopyCachedAsync(
        HttpContext httpContext,
        GeneratedVideoOutput output,
        CancellationToken cancellationToken)
    {
        var path = ResolveStoragePath(output.StorageKey);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != output.SizeBytes || output.SizeBytes <= 0 || output.SizeBytes > _maximumFileBytes)
        {
            throw new AccountApiException(
                StatusCodes.Status502BadGateway,
                "provider_output_cache_invalid",
                "Video đã lưu trên server không còn nguyên vẹn.");
        }
        httpContext.Response.ContentType = output.MimeType;
        httpContext.Response.ContentLength = output.SizeBytes;
        httpContext.Response.Headers.CacheControl = "private, no-store";
        httpContext.Response.Headers.XContentTypeOptions = "nosniff";
        httpContext.Response.Headers.ETag = $"\"{output.Sha256}\"";
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await stream.CopyToAsync(httpContext.Response.Body, 128 * 1024, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendValidatedAsync(
        Uri initialUri,
        string providerCode,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("ProviderMediaDownload");
        var current = initialUri;
        for (var redirect = 0; redirect <= 3; redirect++)
        {
            await ValidatePublicHttpsUriAsync(current, providerCode, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException)
            {
                throw UnsafeUrl();
            }
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                if (redirect == 3)
                {
                    response.Dispose();
                    throw new AccountApiException(StatusCodes.Status502BadGateway, "kling_redirect_limit", "Kling trả về quá nhiều chuyển hướng.");
                }
                current = location.IsAbsoluteUri ? location : new Uri(current, location);
                response.Dispose();
                continue;
            }
            return response;
        }
        throw new AccountApiException(StatusCodes.Status502BadGateway, "kling_download_failed", "Không thể tải video từ Kling.");
    }

    private async Task ValidatePublicHttpsUriAsync(
        Uri uri,
        string providerCode,
        CancellationToken cancellationToken)
    {
        if (uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !IsAllowedProviderOutputHost(providerCode, uri.Host, _allowedHostSuffixes))
        {
            throw UnsafeUrl();
        }
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
        }
        catch (SocketException)
        {
            throw UnsafeUrl();
        }
        if (addresses.Length == 0 || addresses.Any(IsPrivateAddress))
        {
            throw UnsafeUrl();
        }
    }

    internal static async ValueTask<Stream> ConnectPublicHostAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        }
        catch (SocketException exception)
        {
            throw new HttpRequestException("Provider output host could not be resolved.", exception);
        }
        if (addresses.Length == 0 || addresses.Any(IsPrivateAddress))
        {
            throw new HttpRequestException("Provider output host resolved to a blocked address.");
        }

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                if (exception is OperationCanceledException)
                {
                    throw;
                }
                lastError = exception;
            }
        }
        throw new HttpRequestException("Provider output host could not be reached.", lastError);
    }

    internal static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var ipv6 = address.GetAddressBytes();
            return address.IsIPv6LinkLocal ||
                   address.IsIPv6SiteLocal ||
                   ipv6[0] is 0xfc or 0xfd or 0xff ||
                   (ipv6[0] == 0x20 && ipv6[1] == 0x01 && ipv6[2] == 0x0d && ipv6[3] == 0xb8) ||
                   (ipv6[0] == 0x01 && ipv6.Skip(1).Take(7).All(x => x == 0));
        }
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               bytes[0] == 127 ||
               bytes[0] == 0 ||
               (bytes[0] == 169 && bytes[1] == 254) ||
               (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168) ||
               (bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2) ||
               (bytes[0] == 198 && bytes[1] is 18 or 19) ||
               (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) ||
               (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) ||
               (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
               bytes[0] >= 224;
    }

    internal static bool IsAllowedProviderOutputHost(
        string providerCode,
        string host,
        IReadOnlyDictionary<string, string[]> allowedHostSuffixes)
    {
        if (string.IsNullOrWhiteSpace(providerCode) ||
            string.IsNullOrWhiteSpace(host) ||
            IPAddress.TryParse(host, out _) ||
            !allowedHostSuffixes.TryGetValue(providerCode, out var suffixes))
        {
            return false;
        }
        var normalizedHost = host.Trim().TrimEnd('.');
        return suffixes.Any(suffix =>
        {
            var exactMatch = suffix.TrimStart().StartsWith('=');
            var normalizedSuffix = suffix.Trim().TrimStart('=').Trim('.');
            return normalizedSuffix.Length > 0 &&
                   (normalizedHost.Equals(normalizedSuffix, StringComparison.OrdinalIgnoreCase) ||
                    !exactMatch &&
                    normalizedHost.EndsWith($".{normalizedSuffix}", StringComparison.OrdinalIgnoreCase));
        });
    }

    private static AccountApiException UnsafeUrl() =>
        new(StatusCodes.Status502BadGateway, "unsafe_provider_output_url", "URL video của provider không an toàn.");

    private static AccountApiException NotFound() =>
        new(StatusCodes.Status404NotFound, "generation_not_found", "Không tìm thấy video đã tạo.");

    private static AccountApiException OutputTooLarge() =>
        new(StatusCodes.Status413PayloadTooLarge, "provider_output_too_large", "Video provider vượt quá giới hạn dung lượng đã cấu hình.");

    private static AccountApiException OutputCacheFull() =>
        new(
            StatusCodes.Status507InsufficientStorage,
            "provider_output_cache_full",
            "Vùng lưu video tạm trên server đã đầy. Hãy chạy cleanup hoặc tăng dung lượng đã cấu hình.");

    private static string ResolveStorageRoot(string? configured)
    {
        var candidate = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "data", "video-outputs")
            : configured.Trim();
        return Path.GetFullPath(candidate);
    }

    private static IReadOnlyDictionary<string, string[]> NormalizeAllowedHostSuffixes(
        IReadOnlyDictionary<string, string[]> configured)
    {
        var normalized = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in configured)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                continue;
            }
            normalized[entry.Key.Trim()] = entry.Value?
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value =>
                {
                    var trimmed = value.Trim();
                    return trimmed.StartsWith('=')
                        ? $"={trimmed.TrimStart('=').Trim('.')}"
                        : trimmed.Trim('.');
                })
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
        }
        return normalized;
    }

    private string ResolveStoragePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) ||
            storageKey.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            storageKey.Contains(Path.DirectorySeparatorChar) ||
            storageKey.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidDataException("Storage key video không hợp lệ.");
        }
        var path = Path.GetFullPath(Path.Combine(_storageRoot, storageKey));
        var prefix = _storageRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _storageRoot
            : _storageRoot + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Storage key video nằm ngoài vùng lưu trữ.");
        }
        return path;
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
