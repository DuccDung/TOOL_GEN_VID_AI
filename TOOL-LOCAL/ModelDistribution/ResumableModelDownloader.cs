using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using TOOL_LOCAL.SystemSetup;
using TOOL_SHARED.Contracts.ModelDistribution;

namespace TOOL_LOCAL.ModelDistribution;

internal sealed record ModelDownloadProgress(string Stage, long BytesProcessed, long TotalBytes);

/// <summary>Transfers only descriptors selected by the signed-catalog client. Never accepts paths from WebView.</summary>
internal sealed class ResumableModelDownloader(HttpClient http, Uri origin, string channel, TimeSpan? inactivityTimeout = null,
    TimeProvider? timeProvider = null, Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
{
    private const int BufferBytes = 128 * 1024;
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(60);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync = delayAsync ?? ((delay, token) => Task.Delay(delay, token));
    private sealed record Receipt(string Sha256, long SizeBytes, string? ETag, string? Scope = null);

    public async Task<string> DownloadAsync(ModelDistributionArtifact artifact, string destination,
        IProgress<ModelDownloadProgress>? progress, CancellationToken token)
    {
        if (artifact.SizeBytes <= 0 || artifact.SizeBytes > 16L * 1024 * 1024 * 1024 ||
            artifact.Sha256.Length != 64 || artifact.Sha256.Any(c => !Uri.IsHexDigit(c)) ||
            artifact.FileName != Path.GetFileName(artifact.FileName) || artifact.FileName.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new InvalidDataException("Thông tin file model không hợp lệ.");
        var directory = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        SystemSetupPaths.EnsureSafeDirectory(directory);
        AssertSafeFile(destination);
        var part = destination + ".part";
        var receiptPath = part + ".receipt.json";
        AssertSafeFile(part);
        AssertSafeFile(receiptPath);
        AssertSafeFile(destination + ".download.lock");
        using var lease = new FileStream(destination + ".download.lock", FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        if (await MatchesAsync(destination, artifact, token)) return destination;
        var receipt = ReadReceipt(receiptPath);
        var scope = origin.AbsoluteUri + "|" + channel + "|win-x64|" + artifact.ArtifactId;
        if (receipt is null || receipt.Sha256 != artifact.Sha256 || receipt.SizeBytes != artifact.SizeBytes || receipt.Scope != scope)
        {
            DeleteOwnedFile(part);
            DeleteOwnedFile(receiptPath);
            receipt = null;
        }
        if (File.Exists(part) && new FileInfo(part).Length == artifact.SizeBytes)
        {
            if (await MatchesAsync(part, artifact, token))
            {
                File.Move(part, destination, true);
                DeleteOwnedFile(receiptPath);
                return destination;
            }
            DeleteOwnedFile(part);
            DeleteOwnedFile(receiptPath);
            receipt = null;
        }
        var resource = new Uri(origin, $"api/model-distribution/{Uri.EscapeDataString(channel)}/artifacts/{artifact.Sha256}/{Uri.EscapeDataString(artifact.FileName)}");
        for (var attempt = 0; attempt < 4; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var offset = File.Exists(part) ? new FileInfo(part).Length : 0;
            var canResume = offset > 0 && offset < artifact.SizeBytes && StrongETag(receipt?.ETag);
            if (offset > 0 && !canResume)
            {
                DeleteOwnedFile(part);
                offset = 0;
            }
            EnsureDisk(directory, checked(artifact.SizeBytes - offset + artifact.MinimumFreeDiskBytes));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, resource);
                request.Headers.AcceptEncoding.ParseAdd("identity");
                if (canResume)
                {
                    request.Headers.Range = new RangeHeaderValue(offset, null);
                    request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(receipt!.ETag!));
                }
                using var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                headersTimeout.CancelAfter(inactivityTimeout ?? TimeSpan.FromSeconds(60));
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token);
                EnsureOrigin(response, resource);
                if (response.StatusCode is HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    DeleteOwnedFile(part);
                    DeleteOwnedFile(receiptPath);
                    receipt = null;
                    continue;
                }
                if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (attempt == 3) throw new HttpRequestException("Kho model tạm thời không khả dụng.");
                    var retryAfter = response.Headers.RetryAfter;
                    // Release the error response/connection before waiting. A busy server can
                    // advertise a recovery window longer than the local fallback backoff.
                    response.Dispose();
                    await DelayBeforeRetryAsync(attempt, retryAfter, token);
                    continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentEncoding.Any(encoding => !encoding.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Kho model trả định dạng truyền không hợp lệ.");
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType is not null && mediaType is not ("application/octet-stream" or "application/zip" or "application/json"))
                    throw new InvalidDataException("Kho model trả loại nội dung không hợp lệ.");
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    var range = response.Content.Headers.ContentRange;
                    if (!canResume || range is null || range.Unit != "bytes" || range.From != offset ||
                        range.To != artifact.SizeBytes - 1 || range.Length != artifact.SizeBytes ||
                        response.Headers.ETag?.IsWeak != false || response.Headers.ETag.ToString() != receipt!.ETag)
                        throw new InvalidDataException("Phản hồi tải tiếp model không khớp file đã chọn.");
                }
                else if (response.StatusCode == HttpStatusCode.OK)
                {
                    offset = 0;
                }
                else throw new InvalidDataException("Phản hồi tải model không hợp lệ.");
                if (response.Content.Headers.ContentLength is { } length && length != artifact.SizeBytes - offset)
                    throw new InvalidDataException("Dung lượng phản hồi model không đúng.");
                receipt = new Receipt(artifact.Sha256, artifact.SizeBytes,
                    response.Headers.ETag?.IsWeak == false ? response.Headers.ETag.ToString() : null, scope);
                await WriteReceiptAsync(receiptPath, receipt, token);
                await using (var output = new FileStream(part, offset == 0 ? FileMode.Create : FileMode.Open,
                    FileAccess.Write, FileShare.None, BufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    output.Position = offset;
                    await using var input = await response.Content.ReadAsStreamAsync(token);
                    var buffer = new byte[BufferBytes];
                    var written = offset;
                    while (true)
                    {
                        var read = await ReadWithTimeoutAsync(input, buffer, token);
                        if (read == 0) break;
                        if (read > artifact.SizeBytes - written)
                            throw new InvalidDataException("File model vượt dung lượng đã được duyệt.");
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                        written += read;
                        progress?.Report(new("DOWNLOADING", written, artifact.SizeBytes));
                    }
                    await output.FlushAsync(token);
                    if (written != artifact.SizeBytes) throw new IOException("Lượt tải model bị gián đoạn.");
                }
                progress?.Report(new("VERIFYING", artifact.SizeBytes, artifact.SizeBytes));
                if (!await MatchesAsync(part, artifact, token))
                    throw new InvalidDataException("SHA-256 của model tải về không khớp.");
                AssertSafeFile(destination);
                File.Move(part, destination, true);
                DeleteOwnedFile(receiptPath);
                return destination;
            }
            catch (InvalidDataException)
            {
                DeleteOwnedFile(part);
                DeleteOwnedFile(receiptPath);
                throw;
            }
            catch (HttpRequestException) when (attempt < 3)
            {
                await DelayBeforeRetryAsync(attempt, null, token);
            }
            catch (IOException) when (attempt < 3)
            {
                await DelayBeforeRetryAsync(attempt, null, token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && attempt < 3)
            {
                await DelayBeforeRetryAsync(attempt, null, token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new HttpRequestException("Kho model không phản hồi trong thời gian cho phép.");
            }
        }
        throw new HttpRequestException("Không thể hoàn tất tải model. Có thể thử lại để tiếp tục.");
    }

    private Task DelayBeforeRetryAsync(int attempt, RetryConditionHeaderValue? retryAfter, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var delay = TimeSpan.FromMilliseconds(300 * (attempt + 1));
        var requested = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - _timeProvider.GetUtcNow() : TimeSpan.Zero);
        if (requested > delay) delay = requested;
        if (delay > MaximumRetryDelay) delay = MaximumRetryDelay;
        // Four requests allow at most three waits (180 seconds in total). Header/body
        // inactivity deadlines stay unchanged, and the caller can cancel every wait.
        return _delayAsync(delay, token);
    }

    private async Task<int> ReadWithTimeoutAsync(Stream input, byte[] buffer, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(inactivityTimeout ?? TimeSpan.FromSeconds(60));
        try { return await input.ReadAsync(buffer, timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException("Kho model ngừng truyền dữ liệu quá thời gian cho phép.");
        }
    }

    internal static async Task<bool> MatchesAsync(string path, ModelDistributionArtifact artifact, CancellationToken token)
    {
        AssertSafeFile(path);
        if (!File.Exists(path)) return false;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return stream.Length == artifact.SizeBytes && Convert.ToHexString(await SHA256.HashDataAsync(stream, token))
            .Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StrongETag(string? value) => value is not null &&
        EntityTagHeaderValue.TryParse(value, out var tag) && !tag.IsWeak;

    private static Receipt? ReadReceipt(string path)
    {
        if (!File.Exists(path)) return null;
        AssertSafeFile(path);
        if (new FileInfo(path).Length > 16 * 1024) return null;
        try { return JsonSerializer.Deserialize<Receipt>(File.ReadAllBytes(path), Json); }
        catch (JsonException) { return null; }
    }

    private static async Task WriteReceiptAsync(string path, Receipt receipt, CancellationToken token)
    {
        AssertSafeFile(path);
        AssertSafeFile(path + ".tmp");
        await File.WriteAllBytesAsync(path + ".tmp", JsonSerializer.SerializeToUtf8Bytes(receipt, Json), token);
        File.Move(path + ".tmp", path, true);
    }

    internal static void AssertSafeFile(string path)
    {
        var full = Path.GetFullPath(path);
        for (var parent = new FileInfo(full).Directory; parent is not null; parent = parent.Parent)
            if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Thư mục component không được là liên kết.");
        if ((File.Exists(full) || Directory.Exists(full)) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("File component không được là liên kết.");
    }

    private static void DeleteOwnedFile(string path)
    {
        AssertSafeFile(path);
        if (File.Exists(path)) File.Delete(path);
    }

    private static void EnsureDisk(string directory, long remaining)
    {
        if (new DriveInfo(Path.GetPathRoot(directory)!).AvailableFreeSpace < remaining + 64L * 1024 * 1024)
            throw new IOException("Không đủ dung lượng trống để tải model.");
    }

    private static void EnsureOrigin(HttpResponseMessage response, Uri expected)
    {
        if (response.RequestMessage?.RequestUri is { } actual &&
            (actual.Scheme != expected.Scheme || actual.IdnHost != expected.IdnHost || actual.Port != expected.Port ||
             actual.AbsolutePath != expected.AbsolutePath))
            throw new InvalidDataException("Kho model chuyển hướng ngoài đường tải đã được duyệt.");
    }
}
