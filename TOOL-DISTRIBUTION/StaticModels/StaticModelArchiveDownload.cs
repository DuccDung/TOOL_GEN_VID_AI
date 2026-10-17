using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using TOOL_SHARED.Contracts.ModelDistribution;

namespace TOOL_SHARED.Distribution.StaticModels;

/// <summary>Only the release descriptor selects a URL. No request takes a URL from UI or configuration.</summary>
internal sealed class StaticModelArchiveDownload(HttpClient http, StaticModelBundleDescriptor descriptor,
    TimeSpan? inactivityTimeout = null, Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
    Action<string, long>? checkDisk = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delayAsync ?? Task.Delay;
    private readonly Action<string, long> _checkDisk = checkDisk ?? StaticModelFileSafety.EnsureDisk;
    private readonly TimeSpan _timeout = inactivityTimeout ?? TimeSpan.FromSeconds(60);
    private sealed record Receipt(int SchemaVersion, string Url, long SizeBytes, string Sha256, string? ETag);

    internal async Task<long> EstimateRemainingDownloadBytesAsync(string destination, CancellationToken token)
    {
        StaticModelFileSafety.AssertSafePath(destination);
        if (await MatchesAsync(destination, token)) return 0;
        var part = destination + ".part";
        var receiptPath = part + ".receipt.json";
        StaticModelFileSafety.AssertSafePath(part);
        var receipt = ReadReceipt(receiptPath);
        if (receipt is null || receipt.SchemaVersion != 1 || receipt.Url != descriptor.ArchiveUrl.AbsoluteUri ||
            receipt.SizeBytes != descriptor.ArchiveSizeBytes || receipt.Sha256 != descriptor.ArchiveSha256 ||
            !File.Exists(part)) return descriptor.ArchiveSizeBytes;
        var size = new FileInfo(part).Length;
        if (size == descriptor.ArchiveSizeBytes && await MatchesAsync(part, token)) return 0;
        return size > 0 && size < descriptor.ArchiveSizeBytes && IsStrongETag(receipt.ETag)
            ? checked(descriptor.ArchiveSizeBytes - size) : descriptor.ArchiveSizeBytes;
    }

    internal async Task<string> EnsureAsync(string destination, IProgress<StaticModelSetupProgress>? progress,
        CancellationToken token)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        StaticModelFileSafety.EnsureDirectory(directory);
        StaticModelFileSafety.AssertSafePath(destination);
        var part = destination + ".part";
        var receiptPath = part + ".receipt.json";
        StaticModelFileSafety.AssertSafePath(part);
        StaticModelFileSafety.AssertSafePath(receiptPath);
        progress?.Report(new(StaticModelSetupStages.Verifying, 0, descriptor.ArchiveSizeBytes, "Kiểm tra cache model."));
        if (await MatchesAsync(destination, token)) return destination;
        StaticModelFileSafety.DeleteFile(destination);
        var receipt = ReadReceipt(receiptPath);
        if (receipt is null || receipt.SchemaVersion != 1 || receipt.Url != descriptor.ArchiveUrl.AbsoluteUri ||
            receipt.SizeBytes != descriptor.ArchiveSizeBytes || receipt.Sha256 != descriptor.ArchiveSha256)
        {
            Reset(part, receiptPath);
            receipt = null;
        }
        if (File.Exists(part) && new FileInfo(part).Length == descriptor.ArchiveSizeBytes)
        {
            if (await MatchesAsync(part, token))
            {
                File.Move(part, destination);
                StaticModelFileSafety.DeleteFile(receiptPath);
                return destination;
            }
            Reset(part, receiptPath);
            receipt = null;
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            token.ThrowIfCancellationRequested();
            StaticModelFileSafety.AssertSafePath(part);
            var offset = File.Exists(part) ? new FileInfo(part).Length : 0;
            var resume = offset > 0 && offset < descriptor.ArchiveSizeBytes && IsStrongETag(receipt?.ETag);
            if (offset > 0 && !resume) { Reset(part, receiptPath); offset = 0; receipt = null; }
            _checkDisk(directory, checked(descriptor.ArchiveSizeBytes - offset));
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, descriptor.ArchiveUrl);
                request.Headers.AcceptEncoding.ParseAdd("identity");
                if (resume)
                {
                    request.Headers.Range = new RangeHeaderValue(offset, null);
                    request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(receipt!.ETag!));
                }
                using var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                headersTimeout.CancelAfter(_timeout);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token);
                headersTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
                EnsureExactResource(response);
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    Reset(part, receiptPath);
                    receipt = null;
                    continue;
                }
                if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    if (attempt == 3) throw new HttpRequestException("Kho model tạm thời không khả dụng.");
                    var retryAfter = response.Headers.RetryAfter;
                    response.Dispose();
                    await WaitAsync(attempt, retryAfter, token);
                    continue;
                }
                if ((int)response.StatusCode is >= 300 and < 400)
                    throw new InvalidDataException("Kho model chuyển hướng ngoài file đã được phê duyệt.");
                if (!response.IsSuccessStatusCode)
                    throw new InvalidDataException($"Kho model từ chối tải file (HTTP {(int)response.StatusCode}).");
                if (response.Content.Headers.ContentEncoding.Any(e => !e.Equals("identity", StringComparison.OrdinalIgnoreCase)) ||
                    response.Content.Headers.ContentType?.MediaType is not ("application/zip" or "application/octet-stream"))
                    throw new InvalidDataException("Kho model trả loại nội dung không hợp lệ.");
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    var range = response.Content.Headers.ContentRange;
                    if (!resume || range is null || range.Unit != "bytes" || range.From != offset ||
                        range.To != descriptor.ArchiveSizeBytes - 1 || range.Length != descriptor.ArchiveSizeBytes ||
                        response.Headers.ETag?.IsWeak != false || response.Headers.ETag.ToString() != receipt!.ETag)
                        throw new InvalidDataException("Phản hồi tải tiếp model không khớp phần đã tải.");
                }
                else if (response.StatusCode == HttpStatusCode.OK)
                {
                    // A server can ignore Range or change ETag. Restart, never append a full response.
                    offset = 0;
                    // The old prefix is reclaimed by FileMode.Create below, so do not charge it twice.
                    var reclaimedPrefix = File.Exists(part) ? Math.Min(new FileInfo(part).Length, descriptor.ArchiveSizeBytes) : 0;
                    _checkDisk(directory, checked(descriptor.ArchiveSizeBytes - reclaimedPrefix));
                }
                else throw new InvalidDataException("Phản hồi tải model không hợp lệ.");
                if (response.Content.Headers.ContentLength is { } size && size != descriptor.ArchiveSizeBytes - offset)
                    throw new InvalidDataException("Dung lượng phản hồi model không đúng.");

                receipt = new(1, descriptor.ArchiveUrl.AbsoluteUri, descriptor.ArchiveSizeBytes, descriptor.ArchiveSha256,
                    response.Headers.ETag?.IsWeak == false ? response.Headers.ETag.ToString() : null);
                await WriteReceiptAsync(receiptPath, receipt, token);
                StaticModelFileSafety.AssertSafePath(part);
                await using (var output = new FileStream(part, offset == 0 ? FileMode.Create : FileMode.Open,
                    FileAccess.Write, FileShare.None, StaticModelFileSafety.BufferBytes,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    output.Position = offset;
                    await using var input = await response.Content.ReadAsStreamAsync(token);
                    var buffer = new byte[StaticModelFileSafety.BufferBytes];
                    var written = offset;
                    while (true)
                    {
                        var read = await ReadAsync(input, buffer, token);
                        if (read == 0) break;
                        if (read > descriptor.ArchiveSizeBytes - written)
                            throw new InvalidDataException("File model vượt dung lượng đã được duyệt.");
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                        written += read;
                        progress?.Report(new(StaticModelSetupStages.Downloading, written, descriptor.ArchiveSizeBytes,
                            "Đang tải bộ Qwen và Piper từ VPS."));
                    }
                    await output.FlushAsync(token);
                    if (written != descriptor.ArchiveSizeBytes) throw new IOException("Lượt tải model bị gián đoạn.");
                }
                progress?.Report(new(StaticModelSetupStages.Verifying, 0, descriptor.ArchiveSizeBytes,
                    "Kiểm tra SHA-256 toàn bộ ZIP model."));
                if (!await MatchesAsync(part, token)) throw new InvalidDataException("SHA-256 ZIP model không khớp bản phát hành.");
                StaticModelFileSafety.AssertSafePath(destination);
                File.Move(part, destination);
                StaticModelFileSafety.DeleteFile(receiptPath);
                return destination;
            }
            catch (InvalidDataException) { Reset(part, receiptPath); throw; }
            catch (HttpRequestException) when (attempt < 3) { await WaitAsync(attempt, null, token); }
            catch (IOException) when (attempt < 3) { await WaitAsync(attempt, null, token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested && attempt < 3)
            { await WaitAsync(attempt, null, token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new HttpRequestException("Kho model không phản hồi trong thời gian cho phép."); }
        }
        throw new HttpRequestException("Chưa thể hoàn tất tải model. Chạy lại Setup để tiếp tục.");
    }

    private void EnsureExactResource(HttpResponseMessage response)
    {
        if (response.RequestMessage?.RequestUri is { } actual &&
            !string.Equals(actual.AbsoluteUri, descriptor.ArchiveUrl.AbsoluteUri, StringComparison.Ordinal))
            throw new InvalidDataException("Kho model chuyển hướng ngoài file đã được phê duyệt.");
    }

    private Task<bool> MatchesAsync(string path, CancellationToken token) => StaticModelFileSafety.MatchesAsync(
        path, descriptor.ArchiveSizeBytes, descriptor.ArchiveSha256, token);

    private async Task<int> ReadAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(_timeout);
        try { return await stream.ReadAsync(buffer, timeout.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new IOException("Kho model ngừng truyền dữ liệu quá thời gian cho phép."); }
    }

    private Task WaitAsync(int attempt, RetryConditionHeaderValue? retryAfter, CancellationToken token)
    {
        var delay = TimeSpan.FromMilliseconds(500 * (attempt + 1));
        var requested = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.Zero);
        if (requested > delay) delay = requested;
        if (delay > TimeSpan.FromSeconds(60)) delay = TimeSpan.FromSeconds(60);
        return _delay(delay, token);
    }

    private static bool IsStrongETag(string? value) => value is not null &&
        EntityTagHeaderValue.TryParse(value, out var tag) && !tag.IsWeak && tag.Tag != "*";

    private static Receipt? ReadReceipt(string path)
    {
        StaticModelFileSafety.AssertSafePath(path);
        if (!File.Exists(path) || new FileInfo(path).Length > 16 * 1024) return null;
        try { return JsonSerializer.Deserialize<Receipt>(File.ReadAllBytes(path), Json); }
        catch (JsonException) { return null; }
    }

    private static async Task WriteReceiptAsync(string path, Receipt receipt, CancellationToken token)
    {
        StaticModelFileSafety.AssertSafePath(path);
        StaticModelFileSafety.AssertSafePath(path + ".tmp");
        await File.WriteAllBytesAsync(path + ".tmp", JsonSerializer.SerializeToUtf8Bytes(receipt, Json), token);
        File.Move(path + ".tmp", path, true);
    }

    private static void Reset(string part, string receipt)
    {
        StaticModelFileSafety.DeleteFile(part);
        StaticModelFileSafety.DeleteFile(receipt);
    }
}
