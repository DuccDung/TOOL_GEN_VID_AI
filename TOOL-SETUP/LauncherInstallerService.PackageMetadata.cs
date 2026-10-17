using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_SETUP;

internal sealed partial class LauncherInstallerService
{
    internal async Task PrepareStaticPackageStageAsync(DesktopReleaseResponse release, string packagePath,
        string stageRoot, IProgress<DesktopUpdateProgress>? progress, CancellationToken token)
    {
        await Task.Run(() => ValidateDesktopArchive(packagePath), token);
        if (release.PackageMetadata is not { } reference)
        {
            await ExtractAsync(packagePath, stageRoot, progress, token);
            return;
        }
        ValidateStaticRelease(release, options);
        DesktopPackageMetadataPolicy.ValidateReference(release, new Uri(options.ServerBaseUrl));
        progress?.Report(new("extract", 60, "Đang lấy danh mục ứng dụng đã được server kiểm tra."));
        var bytes = await DownloadPackageMetadataBytesAsync(reference.DownloadUrl, reference.SizeBytes, reference.Sha256, token);
        var document = DesktopPackageMetadataPolicy.DeserializeDocument(bytes);
        DesktopPackageMetadataPolicy.ValidateDocument(document, release);
        await DesktopPackageMetadataPolicy.ExtractAndMaterializeAsync(packagePath, stageRoot, release, document, token);
        progress?.Report(new("extract", 80, "Đã kiểm tra ZIP và chuẩn bị danh mục cài đặt ứng dụng."));
    }

    // The descriptor is delivered by the trusted API; the large application archive stays on VPS.
    // Keep it outside the maintenance IPC frame, which remains limited to 64 KiB.
    internal async Task<byte[]> DownloadPackageMetadataBytesAsync(string downloadUrl, long sizeBytes,
        string sha256, CancellationToken token)
    {
        if (sizeBytes is <= 0 or > DesktopPackageMetadataPolicy.MaximumMetadataBytes || !DesktopMaintenanceProtocol.IsHash(sha256))
            throw new InvalidDataException("Danh mục ứng dụng vượt giới hạn hoặc thiếu checksum hợp lệ.");
        var resource = DesktopArtifactDownloadPolicy.Validate(new Uri(options.ServerBaseUrl), downloadUrl,
            DesktopArtifactSourceKinds.Internal);
        using var request = new HttpRequestMessage(HttpMethod.Get, resource);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.AcceptEncoding.ParseAdd("identity");
        using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        headerTimeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token);
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentEncoding.Count != 0 ||
            response.Content.Headers.ContentType?.MediaType != "application/json" ||
            response.Content.Headers.ContentLength is { } declaredLength && declaredLength != sizeBytes)
            throw new InvalidDataException("Phản hồi danh mục ứng dụng không đúng định dạng hoặc kích thước đã duyệt.");
        await using var input = await response.Content.ReadAsStreamAsync(token);
        using var result = new MemoryStream(checked((int)sizeBytes));
        var buffer = new byte[64 * 1024];
        while (true)
        {
            using var inactivity = CancellationTokenSource.CreateLinkedTokenSource(token);
            inactivity.CancelAfter(TimeSpan.FromSeconds(60));
            var read = await input.ReadAsync(buffer, inactivity.Token);
            if (read == 0) break;
            if (result.Length + read > sizeBytes)
                throw new InvalidDataException("Danh mục ứng dụng vượt kích thước đã duyệt.");
            result.Write(buffer, 0, read);
        }
        var bytes = result.ToArray();
        if (bytes.LongLength != sizeBytes ||
            !Convert.ToHexString(SHA256.HashData(bytes)).Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Danh mục ứng dụng thiếu dữ liệu hoặc sai checksum. Chưa cài ứng dụng.");
        return bytes;
    }
}
