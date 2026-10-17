using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using TOOL_LOCAL.SystemSetup;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_LOCAL.Updates;

internal sealed partial class DesktopInstalledMaintenanceService
{
    public async Task StartStandaloneAsync(DesktopStandaloneReleaseResponse release, string action,
        IProgress<DesktopUpdateProgress>? progress, CancellationToken token)
    {
        DesktopStandaloneCatalogPolicy.ValidateRelease(release, apiOrigin, DesktopBuildInfo.Profile);
        var setup = release.Setup ?? throw new SetupException("desktop_setup_download_unavailable",
            "Server chưa có link Setup có checksum để tự cập nhật. Hãy tải và chạy Setup mới từ trang tải ứng dụng, hoặc liên hệ quản trị viên.");
        var setupUrl = DesktopArtifactDownloadPolicy.Validate(apiOrigin, setup.DownloadUrl, DesktopArtifactSourceKinds.ExternalVps);
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        var installed = ReadOwnership(target, SystemSetupPaths.MetadataRoot);
        if (installed.Version != DesktopBuildInfo.Version || installed.BuildNumber != DesktopBuildInfo.BuildNumber || installed.BundleId != release.ModelBundleId)
            throw new InvalidDataException("Bản cài chưa có ownership tương thích. Hãy chạy Setup mới độc lập để sửa.");
        if (action is not ("update" or "repair") || action == "repair" && (installed.Version != release.Version
            || installed.BuildNumber != release.DeclaredBuildNumber || installed.PackageSha256 != release.ExpectedSha256)
            || action == "update" && DesktopReleaseVersionPolicy.CompareVersions(release.Version, installed.Version) <= 0)
            throw new InvalidDataException("Yêu cầu cập nhật/sửa chữa không khớp bản đang cài.");
        var operation = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolGenPostVideo", "Updates", "maintenance", operation);
        AssertSafePath(directory); Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "VideoMaker.Setup.exe");
        Process? process = null; var handedOff = false;
        try
        {
            progress?.Report(new("download", 2, "Đang tải Setup để kiểm tra và cài phiên bản từ VPS..."));
            await DownloadStandaloneSetupAsync(setupUrl, setup, release, executable, progress, token);
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var pipeName = "VideoMaker.Setup." + Environment.ProcessId + "." + operation;
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var current = Process.GetCurrentProcess(); using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User?.Value ?? throw new InvalidOperationException("Không xác định được người dùng Windows.");
            var request = new DesktopStandaloneMaintenanceRequest(3, operation, nonce, action, release, target,
                installed.Version, installed.BuildNumber, installed.PackageSha256, current.Id, current.StartTime.ToUniversalTime().Ticks,
                user, true, DateTime.UtcNow);
            DesktopMaintenanceProtocol.ValidateRequest(request, DateTime.UtcNow);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = directory };
            foreach (var argument in new[] { "--maintenance-pipe", pipeName, "--maintenance-nonce", nonce }) start.ArgumentList.Add(argument);
            process = Process.Start(start) ?? throw new InvalidOperationException("Không thể mở Setup.");
            var started = process.StartTime.ToUniversalTime().Ticks;
            var connected = pipe.WaitForConnectionAsync(timeout.Token); var exited = process.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(connected, exited) != connected) throw new InvalidOperationException("Setup chưa nhận yêu cầu. Ứng dụng vẫn mở.");
            await connected;
            if (DesktopMaintenanceProtocol.GetClientProcessId(pipe) != process.Id) throw new InvalidDataException("IPC không thuộc tiến trình Setup.");
            await DesktopMaintenanceProtocol.WriteAsync(pipe, request, timeout.Token);
            var ack = await DesktopMaintenanceProtocol.ReadAsync<DesktopMaintenanceAck>(pipe, timeout.Token);
            DesktopMaintenanceProtocol.ValidateAck(request, ack, process.Id, started, DateTime.UtcNow);
            if (process.HasExited) throw new InvalidOperationException("Setup đã đóng trước khi nhận chuyển giao.");
            timeout.Cancel(); handedOff = true;
            progress?.Report(new("ready", 100, "Setup đã nhận chuyển giao. Ứng dụng sẽ đóng để kiểm tra và hoàn tất cài đặt."));
        }
        finally
        {
            if (!handedOff && process is { HasExited: false })
            { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            process?.Dispose();
            if (!handedOff && Directory.Exists(directory))
            {
                AssertSafePath(directory);
                if (Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
                    .Any(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)) throw new IOException("Unsafe maintenance cleanup path.");
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    internal static async Task DownloadStandaloneSetupAsync(Uri url, DesktopStandaloneSetupResponse setup,
        DesktopStandaloneReleaseResponse release, string destination, IProgress<DesktopUpdateProgress>? progress, CancellationToken token,
        HttpMessageHandler? artifactHandler = null)
    {
        DesktopStandaloneCatalogPolicy.ValidateRelease(release);
        if (release.Setup != setup || url.AbsoluteUri != setup.DownloadUrl) throw new InvalidDataException("Setup source changed.");
        AssertSafePath(destination);
        using var http = new HttpClient(artifactHandler ?? new HttpClientHandler
            { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None }, disposeHandler: artifactHandler is null)
            { Timeout = Timeout.InfiniteTimeSpan };
        using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(token); headerTimeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token);
        var total = response.Content.Headers.ContentLength;
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentEncoding.Count != 0
            || total is <= 0 or > DesktopArtifactDownloadPolicy.MaximumSetupArtifactBytes)
            throw new InvalidDataException("Setup download response is invalid.");
        if (response.Content.Headers.ContentType?.MediaType is not ("application/octet-stream" or "application/vnd.microsoft.portable-executable" or "application/x-msdownload"))
            throw new InvalidDataException("Setup download MIME is invalid.");
        var drive = new DriveInfo(Path.GetPathRoot(destination)!);
        if (!drive.IsReady || drive.AvailableFreeSpace < checked((total ?? DesktopArtifactDownloadPolicy.MaximumSetupArtifactBytes) + 64L * 1024 * 1024))
            throw new IOException("Không đủ dung lượng tải Setup.");
        var part = destination + ".part"; AssertSafePath(part);
        await using (var input = await response.Content.ReadAsStreamAsync(token))
        await using (var output = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            long written = 0; var buffer = new byte[128 * 1024];
            while (true)
            {
                using var inactivity = CancellationTokenSource.CreateLinkedTokenSource(token); inactivity.CancelAfter(TimeSpan.FromSeconds(60));
                var read = await input.ReadAsync(buffer, inactivity.Token);
                if (read == 0) break;
                written += read;
                if (written > DesktopArtifactDownloadPolicy.MaximumSetupArtifactBytes || total is { } expected && written > expected)
                    throw new InvalidDataException("Setup exceeds maximum length.");
                hash.AppendData(buffer, 0, read); await output.WriteAsync(buffer.AsMemory(0, read), token);
                progress?.Report(new("download", total is > 0 ? 2 + (int)(written * 90 / total.Value) : 30, "Đang tải và kiểm tra Setup từ VPS..."));
            }
            await output.FlushAsync(token);
            if (written == 0 || total is { } expectedTotal && written != expectedTotal
                || !Convert.ToHexString(hash.GetHashAndReset()).Equals(setup.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Setup checksum does not match its declared filename.");
        }
        ValidateStandaloneSetupBinding(part, setup, release);
        File.Move(part, destination);
    }

    internal static void ValidateStandaloneSetupBinding(string executable, DesktopStandaloneSetupResponse setup, DesktopStandaloneReleaseResponse release)
    {
        const string prefix = "VideoMaker.SharedBootstrapper.v1:";
        AssertSafePath(executable);
        using (var input = File.OpenRead(executable))
            if (input.Length is <= 0 or > DesktopArtifactDownloadPolicy.MaximumSetupArtifactBytes || input.ReadByte() != 'M' || input.ReadByte() != 'Z')
                throw new InvalidDataException("Setup is not a Windows executable.");
        var description = FileVersionInfo.GetVersionInfo(executable).Comments;
        if (description is null || description.Length > 8192 || !description.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException("Setup thiếu metadata API/profile đóng trong binary.");
        try
        {
            using var document = JsonDocument.Parse(Convert.FromBase64String(description[prefix.Length..]), new JsonDocumentOptions { MaxDepth = 8 });
            RejectDuplicates(document.RootElement);
            var binding = document.RootElement.Deserialize<DesktopBootstrapperBindingInfo>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
                { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
            if (binding is null || binding.SchemaVersion != 1 || binding.Version != setup.Version
                || !Uri.TryCreate(binding.ApiBaseUrl, UriKind.Absolute, out var api) || !DesktopSharedBootstrapperPolicy.SameApi(api, new Uri(release.ApiBaseUrl))
                || binding.Profile != release.Profile || binding.Channel != release.Channel || binding.Platform != release.Platform
                || binding.DistributionProtocol != release.ApplicationDistributionProtocol || binding.ModelBundleId != release.ModelBundleId
                || binding.SupportedCatalogProtocols != "3,4,5"
                || DesktopReleaseVersionPolicy.CompareVersions(binding.Version, release.MinimumSetupVersion) < 0)
                throw new InvalidDataException("Setup không khớp API/profile/model/giao thức của ứng dụng.");
        }
        catch (Exception error) when (error is JsonException or FormatException or ArgumentException)
        { throw new InvalidDataException("Metadata Setup không hợp lệ.", error); }
    }
}
