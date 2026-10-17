using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_SETUP;

internal sealed record MaintenanceInvocation(string PipeName, string Nonce);

/// <summary>One-use, same-user IPC. The app owns the pipe; Setup owns all installation work.
/// Closing the app after ACK is expected and does not cancel or terminate Setup.</summary>
internal static partial class SetupMaintenanceHandoff
{

    internal static MaintenanceInvocation ParseArguments(string[] args)
    {
        if (args.Length != 4) throw new InvalidDataException("Tham số chuyển giao bộ cài không hợp lệ.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
            if (args[i] is not ("--maintenance-pipe" or "--maintenance-nonce") || !values.TryAdd(args[i], args[i + 1]))
                throw new InvalidDataException("Tham số chuyển giao bộ cài không hợp lệ.");
        var pipe = values.GetValueOrDefault("--maintenance-pipe"); var nonce = values.GetValueOrDefault("--maintenance-nonce");
        if (pipe is null || pipe.Length is < 16 or > 128 || pipe.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))
            || !IsHash(nonce)) throw new InvalidDataException("Mã chuyển giao bộ cài không hợp lệ.");
        return new(pipe, nonce!);
    }

    internal static async Task<PreparedMaintenance> AcceptAsync(MaintenanceInvocation invocation, InstallerOptions options,
        IInstallerSession service, CancellationToken cancellationToken)
    {
        using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(60)); var token = handshakeTimeout.Token;
        using var pipe = new NamedPipeClientStream(".", invocation.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(15000, token);
        var serverPid = DesktopMaintenanceProtocol.GetServerProcessId(pipe);
        var message = await DesktopMaintenanceProtocol.ReadMaintenanceRequestAsync(pipe, token);
        if (message is DesktopStandaloneMaintenanceRequest standalone)
            return await AcceptStandaloneAsync(pipe, serverPid, standalone, invocation, options, service, token);
        if (message is DesktopDirectMaintenanceRequest direct)
            return await AcceptDirectAsync(pipe, serverPid, direct, invocation, options, service, token);
        var request = message as DesktopMaintenanceRequest ?? throw new InvalidDataException("Yêu cầu chuyển giao không hợp lệ.");
        var userSid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidDataException("Không xác định được người dùng Windows.");
        ValidateRequest(request, invocation, options, userSid);
        if (serverPid != request.ParentProcessId) throw new InvalidDataException("Tiến trình sở hữu IPC không khớp yêu cầu.");
        using var parent = Process.GetProcessById(request.ParentProcessId);
        ValidateParent(parent, request, userSid);
        ValidateOwnership(request.TargetRoot, request.CurrentVersion, request.CurrentBuildNumber, request.CurrentPackageSha256);
        var approved = await service.GetReleaseAsync(request.Release, token)
            ?? throw new InvalidDataException("Server chưa có đúng release đã yêu cầu. Ứng dụng hiện tại được giữ nguyên.");
        ValidateReleaseSnapshot(request.Release, approved, options, allowMetadataAugmentation: true);
        var setup = approved.DistributionProtocol == DesktopDistributionProtocol.Shared
            ? new DesktopSetupFileMetadata(approved.SharedBootstrapper!.FileName, approved.SharedBootstrapper.SizeBytes, approved.SharedBootstrapper.Sha256)
            : new DesktopSetupFileMetadata(approved.SetupArtifact!.FileName, approved.SetupArtifact.SizeBytes, approved.SetupArtifact.Sha256);
        var executable = Environment.ProcessPath ?? throw new InvalidDataException("Không xác định được file Setup.");
        await using (var file = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true))
        {
            if (file.Length != setup.SizeBytes || !Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(setup.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("File Setup không khớp artifact đã được phát hành.");
        }
        // Revalidate immediately before ACK; metadata lookup/hash may have taken time.
        ValidateRequest(request, invocation, options, userSid);
        ValidateParent(parent, request, userSid);
        ValidateOwnership(request.TargetRoot, request.CurrentVersion, request.CurrentBuildNumber, request.CurrentPackageSha256);
        using var current = Process.GetCurrentProcess();
        var ack = new DesktopMaintenanceAck(1, request.OperationId, request.Nonce, approved.ReleaseId, approved.Sha256,
            request.TargetRoot, current.Id,
            current.StartTime.ToUniversalTime().Ticks, userSid, DateTime.UtcNow.AddSeconds(45));
        await DesktopMaintenanceProtocol.WriteAsync(pipe, ack, token);
        return new(request, approved);
    }

    internal static void ValidateRequest(DesktopMaintenanceRequest request, MaintenanceInvocation invocation, InstallerOptions options, string userSid)
    {
        if (request is null) throw new InvalidDataException("IPC thiếu yêu cầu chuyển giao.");
        DesktopMaintenanceProtocol.ValidateRequest(request, DateTime.UtcNow);
        if (!options.IsStaticArchive || request is null || request.SchemaVersion != 1
            || !Guid.TryParseExact(request.OperationId, "N", out _) || request.Nonce != invocation.Nonce || !IsHash(request.Nonce)
            || request.Action is not ("update" or "repair") || request.UserSid != userSid || request.ParentProcessId <= 0
            || request.ParentProcessId == Environment.ProcessId || request.ParentStartTimeUtcTicks <= 0
            || string.IsNullOrWhiteSpace(request.CurrentVersion) || request.CurrentVersion.Length > 100 || request.CurrentBuildNumber <= 0
            || !IsHash(request.CurrentPackageSha256) || request.Release is null)
            throw new InvalidDataException("Yêu cầu chuyển giao không đúng identity hoặc người dùng.");
        ValidateExistingTargetPath(request.TargetRoot);
        LauncherInstallerService.ValidateStaticRelease(request.Release, options);
        ValidateSetupArtifact(request.Release, options);
        if (request.Action == "repair" && (request.CurrentVersion != request.Release.Version
            || request.CurrentBuildNumber != request.Release.BuildNumber
            || !request.CurrentPackageSha256.Equals(request.Release.Sha256, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Repair phải dùng đúng package của phiên bản hiện tại.");
        if (request.Action == "update" && request.CurrentVersion == request.Release.Version && request.CurrentBuildNumber == request.Release.BuildNumber)
            throw new InvalidDataException("Update không được thay bytes của cùng phiên bản/build.");
    }

    internal static void ValidateSetupArtifact(DesktopReleaseResponse release, InstallerOptions options)
    {
        if (options.IsSharedBootstrapper)
        {
            LauncherInstallerService.ValidateStaticRelease(release, options);
            return;
        }
        var setup = release.SetupArtifact;
        if (setup is null || setup.ReleaseId != release.ReleaseId || setup.Version != release.Version || setup.BuildNumber != release.BuildNumber
            || setup.Channel != release.Channel || setup.Platform != release.Platform
            || !string.Equals(setup.PackageSha256, release.Sha256, StringComparison.OrdinalIgnoreCase)
            || setup.InstallMode != DesktopInstallModes.StaticArchive || setup.DistributionProtocol != DesktopDistributionProtocol.Current
            || setup.ModelBundleId != ApprovedStaticModelBundle.Current.BundleId
            || setup.SizeBytes <= 0 || setup.SizeBytes > 512L * 1024 * 1024 || !IsHash(setup.Sha256)
            || string.IsNullOrWhiteSpace(setup.FileName) || setup.FileName != Path.GetFileName(setup.FileName)
            || !setup.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Artifact Setup không được ghép đúng release/package/model.");
        DesktopArtifactDownloadPolicy.Validate(new Uri(options.ServerBaseUrl), setup.DownloadUrl, DesktopArtifactSourceKinds.Internal);
    }

    internal static void ValidateReleaseSnapshot(DesktopReleaseResponse requested, DesktopReleaseResponse approved, InstallerOptions options,
        bool allowMetadataAugmentation = false)
    {
        LauncherInstallerService.ValidateStaticRelease(approved, options); ValidateSetupArtifact(approved, options);
        // A protocol-3 desktop built before metadata artifacts omits that nullable field on IPC.
        // Only an exact API refresh may add it; callers cannot replace an existing reference.
        var metadataMatches = requested.PackageMetadata == approved.PackageMetadata ||
            allowMetadataAugmentation && requested.PackageMetadata is null && approved.PackageMetadata is { } added &&
            string.Equals(added.ArchiveSha256, requested.Sha256, StringComparison.OrdinalIgnoreCase);
        var bootstrapperMatches = requested.SharedBootstrapper == approved.SharedBootstrapper ||
            allowMetadataAugmentation && requested.SharedBootstrapper is { SupportedCatalogProtocols: "3" } oldSetup &&
            approved.SharedBootstrapper is { SupportedCatalogProtocols: "3,4" or "3,4,5" } newSetup &&
            oldSetup with { SupportedCatalogProtocols = newSetup.SupportedCatalogProtocols } == newSetup;
        if (requested.ReleaseId != approved.ReleaseId || requested.Version != approved.Version || requested.BuildNumber != approved.BuildNumber
            || requested.Channel != approved.Channel || requested.Platform != approved.Platform || requested.SourceKind != approved.SourceKind
            || requested.DownloadUrl != approved.DownloadUrl || requested.SizeBytes != approved.SizeBytes
            || !string.Equals(requested.Sha256, approved.Sha256, StringComparison.OrdinalIgnoreCase) || requested.InstallMode != approved.InstallMode
            || requested.DistributionProtocol != approved.DistributionProtocol || requested.ModelBundleId != approved.ModelBundleId
            || requested.SetupArtifact != approved.SetupArtifact || !bootstrapperMatches ||
            requested.Profile != approved.Profile || requested.MinimumBootstrapperVersion != approved.MinimumBootstrapperVersion ||
            !metadataMatches)
            throw new InvalidDataException("Snapshot release đã thay đổi. Ứng dụng hiện tại chưa được đóng.");
    }

    internal static void ValidateOwnership(string targetRoot, string version, int buildNumber, string packageSha256, string? metadataRoot = null)
    {
        var target = ValidateExistingTargetPath(targetRoot);
        using var receipt = ReadOwnedJson(Path.Combine(target, "static-setup-receipt.json"), 64 * 1024);
        var value = receipt.RootElement;
        if (value.GetProperty("schemaVersion").GetInt32() != 1 || value.GetProperty("bundleId").GetString() != ApprovedStaticModelBundle.Current.BundleId
            || !PathEquals(value.GetProperty("targetRoot").GetString(), target) || value.GetProperty("version").GetString() != version
            || value.GetProperty("buildNumber").GetInt32() != buildNumber
            || !string.Equals(value.GetProperty("packageSha256").GetString(), packageSha256, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(value.GetProperty("operationId").GetString(), "N", out _))
            throw new InvalidDataException("Receipt sở hữu bản cài không hợp lệ. Hãy dùng Setup độc lập hoặc liên hệ hỗ trợ.");
        var metadata = metadataRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolGenPostVideo", "system-setup");
        var journalPath = Path.Combine(metadata, "installer-transactions", StaticSetupTransaction.TargetKey(target), "transaction.json");
        using var journal = ReadOwnedJson(journalPath, 8 * 1024 * 1024); var current = journal.RootElement;
        if (current.GetProperty("schemaVersion").GetInt32() != 1 || current.GetProperty("state").GetString() != "Committed"
            || !PathEquals(current.GetProperty("targetRoot").GetString(), target)
            || current.GetProperty("operationId").GetString() != value.GetProperty("operationId").GetString()
            || !string.Equals(current.GetProperty("releaseSha256").GetString(), packageSha256, StringComparison.OrdinalIgnoreCase)
            || current.GetProperty("changes").ValueKind != JsonValueKind.Array || current.GetProperty("changes").GetArrayLength() > 200_000)
            throw new InvalidDataException("Bản cài chưa có giao dịch commit khớp; chưa thể chuyển giao tự động.");
        // Intentionally do not require a model READY receipt: this is the path that repairs it.
    }

    internal static JsonDocument ReadOwnedJson(string path, long maximum)
    {
        StaticSetupTransaction.AssertSafeFile(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 || stream.Length > maximum) throw new InvalidDataException("Metadata sở hữu vượt giới hạn.");
        var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
        try { RejectDuplicates(document.RootElement); return document; } catch { document.Dispose(); throw; }
    }

    private static string ValidateExistingTargetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal))
            throw new InvalidDataException("Thư mục bản cài phải là đường dẫn local tuyệt đối.");
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (target.Length is < 4 or > 160 || target.Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(target)!), StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(target) || new DriveInfo(Path.GetPathRoot(target)!).DriveType != DriveType.Fixed)
            throw new InvalidDataException("Thư mục bản cài không nằm trong phạm vi maintenance.");
        StaticSetupTransaction.AssertSafeAncestors(target); return target;
    }

    private static void ValidateParent(Process parent, DesktopMaintenanceRequest request, string userSid)
    {
        if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != request.ParentStartTimeUtcTicks
            || !PathEquals(parent.MainModule?.FileName, Path.Combine(request.TargetRoot, "TOOL-LOCAL.exe")))
            throw new InvalidDataException("Tiến trình desktop không khớp bản cài hoặc đã đổi identity.");
        if (!OpenProcessToken(parent.Handle, 8, out var token)) throw new InvalidDataException("Không kiểm tra được chủ sở hữu desktop.");
        using (token) using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            if (identity.User?.Value != userSid) throw new InvalidDataException("Desktop và Setup phải chạy cùng người dùng Windows.");
    }

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        { var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); foreach (var item in value.EnumerateObject())
            { if (!names.Add(item.Name)) throw new InvalidDataException("Metadata chứa field trùng."); RejectDuplicates(item.Value); } }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool PathEquals(string? left, string right) => left is not null &&
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
}

internal sealed record PreparedMaintenance(DesktopMaintenanceRequest? Request, DesktopReleaseResponse? Release,
    DesktopDirectMaintenanceRequest? DirectRequest = null, DesktopDirectReleaseResponse? DirectRelease = null,
    DesktopStandaloneMaintenanceRequest? StandaloneRequest = null, DesktopStandaloneReleaseResponse? StandaloneRelease = null)
{
    internal string TargetRoot => StandaloneRequest?.TargetRoot ?? DirectRequest?.TargetRoot ?? Request!.TargetRoot;
    internal bool LaunchAfterInstall => StandaloneRequest?.LaunchAfterInstall ?? DirectRequest?.LaunchAfterInstall ?? Request!.LaunchAfterInstall;
    internal async Task WaitForParentExitAsync(CancellationToken token)
    {
        Process? parent;
        var parentId = StandaloneRequest?.ParentProcessId ?? DirectRequest?.ParentProcessId ?? Request!.ParentProcessId;
        var parentStarted = StandaloneRequest?.ParentStartTimeUtcTicks ?? DirectRequest?.ParentStartTimeUtcTicks ?? Request!.ParentStartTimeUtcTicks;
        try { parent = Process.GetProcessById(parentId); } catch (ArgumentException) { return; }
        using (parent)
        {
            // PID reuse means the acknowledged parent has already exited. Do not touch its successor.
            try { if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != parentStarted) return; }
            catch (InvalidOperationException) when (parent.HasExited) { return; }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(90));
            try { await parent.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new TimeoutException("Desktop chưa đóng sau khi chuyển giao. Chưa thay file; hãy đóng ứng dụng rồi chạy Setup lại."); }
        }
    }
}
