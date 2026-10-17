using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_SETUP;

internal sealed partial class LauncherInstallerService
{
    private sealed record DirectDownloadReceipt(string Url, string ExpectedSha256, string CatalogRevision,
        long? TotalBytes, string? ETag);

    internal async Task<long> DownloadDirectPackageAsync(string downloadUrl, string expectedSha256, string catalogRevision,
        string path, IProgress<DesktopUpdateProgress>? progress, CancellationToken token)
    {
        if (!DesktopMaintenanceProtocol.IsHash(expectedSha256) || expectedSha256.All(c => c == '0') ||
            string.IsNullOrWhiteSpace(catalogRevision) || catalogRevision.Length > 100)
            throw new InvalidDataException("Snapshot tải ứng dụng chưa hợp lệ.");
        var resource = DesktopArtifactDownloadPolicy.Validate(new Uri(options.ServerBaseUrl), downloadUrl, DesktopArtifactSourceKinds.ExternalVps);
        StaticSetupTransaction.AssertSafeFile(path);
        if (File.Exists(path) && new FileInfo(path).Length is > 0 and <= DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes &&
            (await HashAsync(path, token)).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
        { ValidatePackageSignature(path); return new FileInfo(path).Length; }
        StaticSetupTransaction.DeleteOwnedFile(path);
        var part = path + ".part"; var receiptPath = path + ".direct-receipt.json";
        StaticSetupTransaction.AssertSafeFile(part); StaticSetupTransaction.AssertSafeFile(receiptPath);
        DirectDownloadReceipt? receipt = null;
        try
        {
            if (File.Exists(receiptPath) && new FileInfo(receiptPath).Length < 8192)
                receipt = JsonSerializer.Deserialize<DirectDownloadReceipt>(File.ReadAllText(receiptPath), StaticJson);
        }
        catch (JsonException) { }
        if (receipt?.Url != resource.AbsoluteUri || receipt.ExpectedSha256 != expectedSha256 || receipt.CatalogRevision != catalogRevision ||
            receipt.TotalBytes is <= 0 or > DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes ||
            !EntityTagHeaderValue.TryParse(receipt.ETag, out var previousTag) || previousTag.IsWeak)
        { StaticSetupTransaction.DeleteOwnedFile(part); receipt = null; }
        using var http = new HttpClient(artifactHandler ?? new HttpClientHandler
            { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None }, disposeHandler: artifactHandler is null)
            { Timeout = Timeout.InfiniteTimeSpan };
        for (var attempt = 0; attempt < 4; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var offset = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (offset > 0 && offset <= DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes &&
                (await HashAsync(part, token)).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            { ValidatePackageSignature(part); File.Move(part, path, true); StaticSetupTransaction.DeleteOwnedFile(receiptPath); return offset; }
            if (offset > DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes || receipt is null ||
                receipt.TotalBytes is { } priorLength && offset >= priorLength)
            { StaticSetupTransaction.DeleteOwnedFile(part); offset = 0; receipt = null; }
            EnsureDiskBudget([(Path.GetDirectoryName(path)!, checked((receipt?.TotalBytes is { } known ? known - offset : 0) + 64L * 1024 * 1024))]);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, resource);
                request.Headers.AcceptEncoding.ParseAdd("identity");
                if (offset > 0)
                { request.Headers.Range = new RangeHeaderValue(offset, null); request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(receipt!.ETag!)); }
                using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(token); headerTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token);
                headerTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
                if ((int)response.StatusCode is >= 300 and < 400) throw new InvalidDataException("Nguồn ZIP yêu cầu redirect chưa được duyệt.");
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                { StaticSetupTransaction.DeleteOwnedFile(part); receipt = null; continue; }
                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable && attempt < 3)
                {
                    var retry = response.Headers.RetryAfter;
                    var wait = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(1 << attempt));
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(wait.TotalSeconds, 1, 60)), token); continue;
                }
                response.EnsureSuccessStatusCode(); ValidatePackageMime(response.Content.Headers.ContentType?.MediaType);
                if (response.Content.Headers.ContentEncoding.Count != 0) throw new InvalidDataException("ZIP có content encoding không được hỗ trợ.");
                long? total;
                if (response.StatusCode == HttpStatusCode.OK)
                { offset = 0; total = response.Content.Headers.ContentLength; }
                else if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    var range = response.Content.Headers.ContentRange;
                    if (offset == 0 || range?.Unit != "bytes" || range.From != offset || range.Length is not { } completeLength ||
                        completeLength <= offset || completeLength > DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes || range.To != completeLength - 1 ||
                        receipt?.TotalBytes is { } previousLength && previousLength != completeLength ||
                        response.Headers.ETag?.IsWeak != false || response.Headers.ETag.ToString() != receipt?.ETag)
                        throw new InvalidDataException("Phản hồi tải tiếp ZIP không khớp snapshot.");
                    total = completeLength;
                }
                else throw new InvalidDataException("Phản hồi ZIP không hợp lệ.");
                if (total is <= 0 or > DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes ||
                    response.Content.Headers.ContentLength is { } length && total is { } complete && length != complete - offset)
                    throw new InvalidDataException("Dung lượng ZIP vượt giới hạn hoặc không khớp phản hồi.");
                EnsureDiskBudget([(Path.GetDirectoryName(path)!, checked((total is { } expected ? expected - offset : 0) + 64L * 1024 * 1024))]);
                receipt = new(resource.AbsoluteUri, expectedSha256, catalogRevision, total,
                    response.Headers.ETag?.IsWeak == false ? response.Headers.ETag.ToString() : null);
                StaticSetupTransaction.AtomicJson(receiptPath, receipt);
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using (var output = new FileStream(part, offset > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write, FileShare.None, 128 * 1024, true))
                {
                    var buffer = new byte[128 * 1024];
                    while (true)
                    {
                        using var inactivity = CancellationTokenSource.CreateLinkedTokenSource(token); inactivity.CancelAfter(TimeSpan.FromSeconds(60));
                        var read = await input.ReadAsync(buffer, inactivity.Token); if (read == 0) break;
                        if (offset + read > DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes || total is { } cap && offset + read > cap)
                            throw new InvalidDataException("ZIP vượt kích thước cho phép.");
                        if (total is null) EnsureDiskBudget([(Path.GetDirectoryName(path)!, 64L * 1024 * 1024 + read)]);
                        await output.WriteAsync(buffer.AsMemory(0, read), token); offset += read;
                        progress?.Report(new("download", total is { } size ? 2 + (int)(offset * 12 / size) : 8,
                            total is { } all ? $"Đang tải ứng dụng: {offset / 1024d / 1024d:0.0}/{all / 1024d / 1024d:0.0} MB"
                                : $"Đang tải ứng dụng: {offset / 1024d / 1024d:0.0} MB"));
                    }
                    await output.FlushAsync(token); output.Flush(true);
                }
                if (offset <= 0 || total is { } wanted && offset != wanted ||
                    !(await HashAsync(part, token)).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                { StaticSetupTransaction.DeleteOwnedFile(part); throw new InvalidDataException("ZIP tải về sai kích thước hoặc SHA-256 đã công bố trong tên file."); }
                ValidatePackageSignature(part); File.Move(part, path, true); StaticSetupTransaction.DeleteOwnedFile(receiptPath); return offset;
            }
            catch (Exception error) when (attempt < 3 && !token.IsCancellationRequested && error is HttpRequestException or IOException or OperationCanceledException)
            { await Task.Delay(TimeSpan.FromSeconds(1 << attempt), token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new TimeoutException("Kết nối tải ZIP không có tiến triển. Bấm Thử lại để tiếp tục."); }
        }
        throw new HttpRequestException("Chưa tải được ZIP sau số lần thử cho phép. Cache hợp lệ được giữ để thử lại.");
    }
}
