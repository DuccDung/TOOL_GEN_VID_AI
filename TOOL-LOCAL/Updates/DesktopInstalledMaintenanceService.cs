using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.ModelDistribution;
using TOOL_LOCAL.SystemSetup;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_LOCAL.Updates;

/// <summary>Hands installation ownership to the exact Setup before the desktop exits.
/// It never installs files, repairs markers or attaches Setup to the desktop's job.</summary>
internal sealed partial class DesktopInstalledMaintenanceService(Uri apiOrigin)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task StartAsync(DesktopReleaseResponse release, string action,
        IProgress<DesktopUpdateProgress>? progress, CancellationToken token)
    {
        DesktopMaintenanceProtocol.ValidateRelease(release);
        DesktopArtifactDownloadPolicy.Validate(apiOrigin, release.DownloadUrl, release.SourceKind);
        DesktopSetupFileMetadata setup;
        Uri setupUrl;
        if (release.DistributionProtocol == DesktopDistributionProtocol.Shared)
        {
            DesktopSharedBootstrapperPolicy.ValidateRelease(release, apiOrigin, DesktopBuildInfo.Profile);
            var shared = release.SharedBootstrapper!;
            setup = new(shared.FileName, shared.SizeBytes, shared.Sha256);
            setupUrl = DesktopArtifactDownloadPolicy.Validate(apiOrigin, shared.DownloadUrl, DesktopArtifactSourceKinds.Internal);
        }
        else
        {
            var exact = release.SetupArtifact!;
            setup = new(exact.FileName, exact.SizeBytes, exact.Sha256);
            setupUrl = DesktopArtifactDownloadPolicy.Validate(apiOrigin, exact.DownloadUrl, DesktopArtifactSourceKinds.Internal);
        }
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        var installed = ReadOwnership(target, SystemSetupPaths.MetadataRoot);
        if (installed.Version != DesktopBuildInfo.Version || installed.BuildNumber != DesktopBuildInfo.BuildNumber ||
            installed.BundleId != release.ModelBundleId)
            throw new InvalidDataException("Bản cài chưa có ownership tương thích. Hãy chạy đúng Setup để sửa hoặc cài vào thư mục mới.");

        var operation = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ToolGenPostVideo", "Updates", "maintenance", operation);
        AssertSafePath(directory);
        Directory.CreateDirectory(directory);
        var executable = Path.Combine(directory, "VideoMaker.Setup.exe");
        Process? process = null;
        var handedOff = false;
        try
        {
            progress?.Report(new("download", 2, "Đang tải đúng bộ cài để cập nhật hoặc sửa ứng dụng..."));
            await DownloadSetupAsync(setupUrl, setup, executable, progress, token);
            var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var pipeName = "VideoMaker.Setup." + Environment.ProcessId + "." + operation;
            using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var current = Process.GetCurrentProcess();
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User?.Value ?? throw new InvalidOperationException("Cannot identify installation user.");
            var request = new DesktopMaintenanceRequest(1, operation, nonce, action, release, target,
                installed.Version, installed.BuildNumber, installed.PackageSha256,
                current.Id, current.StartTime.ToUniversalTime().Ticks, user, true, DateTime.UtcNow);
            DesktopMaintenanceProtocol.ValidateRequest(request, DateTime.UtcNow);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = directory };
            foreach (var argument in new[] { "--maintenance-pipe", pipeName, "--maintenance-nonce", nonce }) start.ArgumentList.Add(argument);
            process = Process.Start(start) ?? throw new InvalidOperationException("Không thể mở bộ cài cập nhật.");
            var started = process.StartTime.ToUniversalTime().Ticks;
            var connected = pipe.WaitForConnectionAsync(timeout.Token);
            var exited = process.WaitForExitAsync(timeout.Token);
            if (await Task.WhenAny(connected, exited) != connected)
                throw new InvalidOperationException("Bộ cài chưa nhận được yêu cầu. Ứng dụng vẫn được giữ mở.");
            await connected;
            if (DesktopMaintenanceProtocol.GetClientProcessId(pipe) != process.Id)
                throw new InvalidDataException("Maintenance peer does not own the Setup process.");
            await DesktopMaintenanceProtocol.WriteAsync(pipe, request, timeout.Token);
            var ack = await DesktopMaintenanceProtocol.ReadAsync<DesktopMaintenanceAck>(pipe, timeout.Token);
            DesktopMaintenanceProtocol.ValidateAck(request, ack, process.Id, started, DateTime.UtcNow);
            if (process.HasExited) throw new InvalidOperationException("Bộ cài đã đóng trước khi nhận chuyển giao.");
            timeout.Cancel();
            handedOff = true;
            progress?.Report(new("ready", 100, "Bộ cài đã nhận yêu cầu. Ứng dụng sẽ đóng để hoàn tất cập nhật hoặc sửa chữa."));
        }
        finally
        {
            if (!handedOff && process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            process?.Dispose();
            if (!handedOff && Directory.Exists(directory))
            {
                AssertSafePath(directory);
                if (Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
                    .Any(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0))
                    throw new IOException("Unsafe maintenance cleanup path.");
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    internal static StaticApplicationReceipt ReadOwnership(string target, string metadataRoot)
    {
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        var receipt = ReadBounded<StaticApplicationReceipt>(Path.Combine(target, "static-setup-receipt.json"), 64 * 1024);
        if (receipt.SchemaVersion != 1 || !PathEquals(receipt.TargetRoot, target) || receipt.BuildNumber <= 0 ||
            string.IsNullOrWhiteSpace(receipt.Version) || receipt.BundleId != ApprovedStaticModelBundle.Current.BundleId ||
            !DesktopMaintenanceProtocol.IsHash(receipt.PackageSha256) || !DesktopMaintenanceProtocol.IsHex(receipt.OperationId, 32) ||
            receipt.CompletedAtUtc == default || receipt.CompletedAtUtc > DateTime.UtcNow.AddMinutes(5))
            throw new InvalidDataException("Installed application ownership is invalid.");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target.TrimEnd('\\').ToUpperInvariant()))).ToLowerInvariant();
        var journal = ReadBounded<StaticApplicationJournal>(Path.Combine(metadataRoot, "installer-transactions", key, "transaction.json"), 8 * 1024 * 1024);
        if (journal.SchemaVersion != 1 || journal.State != "Committed" || journal.OperationId != receipt.OperationId ||
            !PathEquals(journal.TargetRoot, target) || !journal.ReleaseSha256.Equals(receipt.PackageSha256, StringComparison.OrdinalIgnoreCase) ||
            journal.Changes.ValueKind != JsonValueKind.Array || journal.Changes.GetArrayLength() > 200_000)
            throw new InvalidDataException("Installed application transaction is not committed.");
        return receipt;
    }

    private static T ReadBounded<T>(string path, int maximumBytes)
    {
        AssertSafePath(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 || stream.Length > maximumBytes) throw new InvalidDataException("Invalid installation metadata size.");
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
        RejectDuplicates(document.RootElement);
        return document.RootElement.Deserialize<T>(Json) ?? throw new InvalidDataException("Invalid installation metadata.");
    }

    private static async Task DownloadSetupAsync(Uri url, DesktopSetupFileMetadata setup, string destination,
        IProgress<DesktopUpdateProgress>? progress, CancellationToken token)
    {
        var drive = new DriveInfo(Path.GetPathRoot(destination)!);
        if (!drive.IsReady || drive.AvailableFreeSpace < checked(setup.SizeBytes + 64L * 1024 * 1024))
            throw new IOException("Không đủ dung lượng tải bộ cài cập nhật.");
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None };
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        headerTimeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token);
        if (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentEncoding.Count != 0 ||
            response.Content.Headers.ContentLength is { } length && length != setup.SizeBytes)
            throw new InvalidDataException("Setup download response is invalid.");
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not ("application/octet-stream" or "application/vnd.microsoft.portable-executable" or "application/x-msdownload"))
            throw new InvalidDataException("Setup download MIME is invalid.");
        var part = destination + ".part";
        await using (var input = await response.Content.ReadAsStreamAsync(token))
        await using (var output = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true))
        using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var buffer = new byte[128 * 1024];
            long written = 0;
            while (true)
            {
                using var inactivity = CancellationTokenSource.CreateLinkedTokenSource(token);
                inactivity.CancelAfter(TimeSpan.FromSeconds(60));
                var read = await input.ReadAsync(buffer, inactivity.Token);
                if (read == 0) break;
                if (written + read > setup.SizeBytes) throw new InvalidDataException("Setup exceeds approved length.");
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), token);
                written += read;
                progress?.Report(new("download", 2 + (int)(written * 90 / setup.SizeBytes), "Đang tải bộ cài đúng phiên bản..."));
            }
            if (written != setup.SizeBytes || !Convert.ToHexString(hash.GetHashAndReset()).Equals(setup.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Setup checksum does not match the release.");
        }
        using (var file = File.OpenRead(part))
            if (file.ReadByte() != 'M' || file.ReadByte() != 'Z') throw new InvalidDataException("Setup is not a Windows executable.");
        File.Move(part, destination);
    }

    private static bool PathEquals(string left, string right) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(left))
        .Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    private static void AssertSafePath(string path)
    {
        for (var item = new DirectoryInfo(Path.GetFullPath(path)); item is not null; item = item.Parent)
            if ((File.Exists(item.FullName) || Directory.Exists(item.FullName)) &&
                (File.GetAttributes(item.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Installation metadata cannot use reparse points.");
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate installation metadata property.");
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }
}
