using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;
using TOOL_SHARED.Distribution.Updates;

namespace TOOL_SETUP;

internal sealed partial class LauncherInstallerService
{
    private static readonly JsonSerializerOptions StaticJson = new(JsonSerializerDefaults.Web);
    public Func<string, CancellationToken, Task<bool>>? ConfirmResourceWarningAsync { get; set; }
    private sealed record PackageReceipt(string Url, string Sha256, long SizeBytes, string? ETag);
    private sealed record InstallReceipt(int SchemaVersion, string BundleId, string TargetRoot, string Version,
        int BuildNumber, string PackageSha256, string OperationId, DateTime CompletedAtUtc);

    private Task InstallStaticAsync(DesktopReleaseResponse release, string installDirectory,
        bool createDesktopShortcut, bool launchAfterInstall, IProgress<DesktopUpdateProgress>? progress, CancellationToken token) =>
        InstallStaticCoreAsync(release, null, installDirectory, createDesktopShortcut, launchAfterInstall, progress, token);

    private async Task InstallStaticCoreAsync(DesktopReleaseResponse? legacyRelease, DesktopDirectReleaseResponse? directRelease, string installDirectory,
        bool createDesktopShortcut, bool launchAfterInstall, IProgress<DesktopUpdateProgress>? progress, CancellationToken token,
        DesktopStandaloneReleaseResponse? standaloneRelease = null)
    {
        if (standaloneRelease is not null)
        {
            ValidateStandaloneRelease(standaloneRelease, options);
            var approved = await GetStandaloneReleaseAsync(standaloneRelease, token)
                ?? throw new InvalidDataException("Release catalog đã bị thu hồi. Chưa thay ứng dụng hiện tại.");
            ValidateStandaloneSnapshot(standaloneRelease, approved, options);
        }
        else if (directRelease is not null)
        {
            ValidateDirectRelease(directRelease, options);
            var approved = await GetDirectReleaseAsync(directRelease, token)
                ?? throw new InvalidDataException("Release catalog đã bị thu hồi. Chưa thay ứng dụng hiện tại.");
            ValidateDirectSnapshot(directRelease, approved, options);
            await ValidateDirectBootstrapperFileAsync(Environment.ProcessPath ?? throw new InvalidDataException("Không xác định được Setup."), directRelease, options, token);
        }
        else
        {
            ArgumentNullException.ThrowIfNull(legacyRelease);
            ValidateStaticRelease(legacyRelease, options);
            if (options.IsSharedBootstrapper)
            {
            var approved = await GetReleaseAsync(legacyRelease, token)
                ?? throw new InvalidDataException("Release đã bị thu hồi hoặc không còn tương thích. Chưa thay ứng dụng hiện tại.");
            SetupMaintenanceHandoff.ValidateReleaseSnapshot(legacyRelease, approved, options, allowMetadataAugmentation: true);
            legacyRelease = approved;
            await ValidateExecutingSharedBootstrapperAsync(legacyRelease, options, token);
            }
        }
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitOperatingSystem || !Environment.Is64BitProcess)
            throw new InvalidOperationException("Bộ cài ứng dụng và model yêu cầu Windows x64.");
        var target = ValidateStaticTarget(installDirectory);
        var userRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToolGenPostVideo", "system-setup");
        StaticSetupTransaction.EnsureSafeDirectory(userRoot);
        var cache = Path.Combine(userRoot, "static-model-cache");
        var transactionRoot = Path.Combine(userRoot, "installer-transactions", StaticSetupTransaction.TargetKey(target));
        StaticSetupTransaction.EnsureSafeDirectory(transactionRoot);
        // Separate lease survives stage/model phase and prevents a second Setup from recovering active work.
        var setupLockPath = Path.Combine(userRoot, ".static-installer.lock");
        StaticSetupTransaction.AssertSafeFile(setupLockPath);
        using var setupLease = new FileStream(setupLockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var transaction = new StaticSetupTransaction(transactionRoot, target);
        using (AcquireUsageBarrier(userRoot))
        {
            EnsureNotRunning(Path.Combine(target, ExecutableName));
            EnsureNoComponentProcess();
            transaction.Recover();
            ValidateExistingInstallation(target);
        }
        // Downloads and read-only probes do not invalidate the existing application's commit.
        // The persistent transaction starts only when promotion is ready under the usage barrier.
        var previousCommit = transaction.Current is { State: "Committed" } ? transaction.Current : null;
        var operation = Guid.NewGuid().ToString("N");
        progress?.Report(new("preflight", 1, "Đang kiểm tra thư mục, bản phát hành và dung lượng cài đặt."));
        DesktopReleaseResponse release;
        ResolvedDesktopDirectPackage? resolved = null;
        ResolvedDesktopStandalonePackage? standaloneResolved = null;
        if (standaloneRelease is not null)
        {
            await DownloadDirectPackageAsync(standaloneRelease.DownloadUrl, standaloneRelease.ExpectedSha256, standaloneRelease.Revision.ToString("D"),
                transaction.PackagePath, progress, token);
            standaloneResolved = await DesktopStandalonePackageResolver.ResolveAsync(transaction.PackagePath, standaloneRelease, token);
            release = standaloneResolved.Release;
        }
        else if (directRelease is not null)
        {
            await DownloadDirectPackageAsync(directRelease.DownloadUrl, directRelease.ExpectedSha256, directRelease.Revision.ToString("D"),
                transaction.PackagePath, progress, token);
            resolved = await DesktopDirectPackageResolver.ResolveAsync(transaction.PackagePath, directRelease, token);
            release = resolved.Release;
        }
        else
        {
            release = legacyRelease!;
            await DownloadStaticPackageAsync(release, transaction.PackagePath, progress, token);
        }
        StaticSetupTransaction.DeleteOwnedDirectory(transaction.StageRoot);
        StaticSetupTransaction.EnsureSafeDirectory(transaction.StageRoot);
        if (standaloneResolved is not null)
            await DesktopStandalonePackageResolver.ExtractAndMaterializeAsync(transaction.PackagePath, transaction.StageRoot, standaloneResolved, token);
        else if (resolved is not null)
            await DesktopDirectPackageResolver.ExtractAndMaterializeAsync(transaction.PackagePath, transaction.StageRoot, resolved, token);
        else
            await PrepareStaticPackageStageAsync(release, transaction.PackagePath, transaction.StageRoot,
                new Progress<DesktopUpdateProgress>(p => progress?.Report(p with { Percent = 15 + (p.Percent - 60) / 2 })), token);
        var manifest = ValidateStage(release, transaction.StageRoot);
        var managed = manifest.ManagedFiles.Select(StaticSetupTransaction.NormalizeRelative).ToArray();
        if (managed.Distinct(StringComparer.OrdinalIgnoreCase).Count() != managed.Length ||
            !managed.Contains("update-manifest.json", StringComparer.OrdinalIgnoreCase) || !managed.Contains(ExecutableName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Danh mục package có file trùng hoặc thiếu thành phần bắt buộc.");
        await StaticBootstrapRunner.ValidatePolicyAsync(Path.Combine(transaction.StageRoot, ExecutableName), token,
            options.DistributionProtocol, options.IsSharedBootstrapper ? options.Profile : null);
        var prerequisites = await StaticBootstrapRunner.RunAsync(Path.Combine(transaction.StageRoot, ExecutableName), operation,
            ApprovedStaticModelBundle.Current.BundleId, cache, transaction.ResultPath, true, false, progress, token);
        if (!prerequisites.Succeeded && prerequisites.Components.Any(c => c.ComponentId == "webview2" && !c.Ready))
        {
            progress?.Report(new("prerequisite", 26, "Đang cài Microsoft Edge WebView2 đã được xác minh."));
            await ApprovedWebViewInstaller.InstallAsync(transaction.StageRoot, options, token,
                Path.Combine(transaction.Root, "prerequisites"));
            prerequisites = await StaticBootstrapRunner.RunAsync(Path.Combine(transaction.StageRoot, ExecutableName), operation,
                ApprovedStaticModelBundle.Current.BundleId, cache, transaction.ResultPath, true, false, progress, token);
        }
        if (!prerequisites.Succeeded)
            throw new InvalidOperationException(prerequisites.Message ?? "Windows chưa đủ prerequisite cho OCR, media hoặc WebView2. Hãy xử lý rồi thử lại.");
        // Combine all outstanding allocations on the actual volumes before a multi-gigabyte download.
        // Estimates only discount reusable artifacts after verification; the owners recheck before writing.
        var appBytes = managed.Sum(file => new FileInfo(StaticSetupTransaction.SafePath(transaction.StageRoot, file)).Length);
        var previousFiles = ResolvePreviousManagedFiles(target, transaction);
        if (options.IsSharedBootstrapper && previousFiles.Length > 0)
        {
            using var ownedReceipt = SetupMaintenanceHandoff.ReadOwnedJson(Path.Combine(target, "static-setup-receipt.json"), 64 * 1024);
            var installed = ownedReceipt.RootElement.Deserialize<InstallReceipt>(StaticJson)
                ?? throw new InvalidDataException("Thiếu receipt phiên bản đang cài.");
            if (standaloneRelease is not null)
                ValidateStandaloneVersionTransition(standaloneRelease, installed.Version, installed.BuildNumber, installed.PackageSha256);
            else if (directRelease is not null)
                ValidateDirectVersionTransition(directRelease, installed.Version, installed.BuildNumber, installed.PackageSha256);
            else ValidateSharedVersionTransition(release, installed.Version, installed.BuildNumber, installed.PackageSha256);
        }
        var backupBytes = previousFiles.Select(file => StaticSetupTransaction.SafePath(target, file))
            .Where(File.Exists).Sum(file => new FileInfo(file).Length);
        using (var store = new StaticModelArchiveStore())
        {
            var remainingCacheBytes = await store.EstimateAdditionalDiskBytesAsync(cache, token);
            var runtimeBytes = prerequisites.RuntimeDiskRequiredBytes
                ?? throw new InvalidDataException("Bootstrap chưa cung cấp dự toán dung lượng runtime.");
            if (runtimeBytes is < 0 or > 16L * 1024 * 1024 * 1024)
                throw new InvalidDataException("Dự toán dung lượng runtime không hợp lệ.");
            EnsureDiskBudget([(target, checked(appBytes + 128L * 1024 * 1024)),
                (transactionRoot, checked(backupBytes + 128L * 1024 * 1024)),
                (cache, checked(remainingCacheBytes + 64L * 1024 * 1024)),
                (userRoot, checked(runtimeBytes + 64L * 1024 * 1024))]);
            await store.EnsurePayloadAsync(cache, new Progress<StaticModelSetupProgress>(value =>
            {
                var fraction = value.TotalBytes > 0 ? Math.Clamp((double)value.BytesProcessed / value.TotalBytes, 0, 1) : 0;
                progress?.Report(new("models", 30 + (int)(fraction * 35), value.Message));
            }), token);
        }
        if (standaloneRelease is not null)
        {
            var current = await GetStandaloneReleaseAsync(standaloneRelease, token)
                ?? throw new InvalidDataException("Catalog đã thu hồi release. Chưa thay ứng dụng hiện tại.");
            ValidateStandaloneSnapshot(standaloneRelease, current, options);
        }
        else if (directRelease is not null)
        {
            var current = await GetDirectReleaseAsync(directRelease, token)
                ?? throw new InvalidDataException("Catalog đã thu hồi release. Chưa thay ứng dụng hiện tại.");
            ValidateDirectSnapshot(directRelease, current, options);
        }
        else if (options.IsSharedBootstrapper)
        {
            var current = await GetReleaseAsync(release, token)
                ?? throw new InvalidDataException("Release không còn được phép cài. Chưa thay ứng dụng hiện tại.");
            SetupMaintenanceHandoff.ValidateReleaseSnapshot(release, current, options);
        }
        token.ThrowIfCancellationRequested();
        var receiptPath = Path.Combine(target, "static-setup-receipt.json");
        var pendingPublished = false;
        try
        {
            using (AcquireUsageBarrier(userRoot))
            {
                EnsureNotRunning(Path.Combine(target, ExecutableName));
                EnsureNoComponentProcess();
                transaction.Begin(release.Sha256.ToLowerInvariant(), operation, preserveStagedPayload: true, previousCommitted: previousCommit);
                StaticSetupTransaction.AtomicJson(Path.Combine(transaction.StageRoot, "static-setup-receipt.json"), new { schemaVersion = 1, pending = true });
                await Task.Run(() => transaction.Snapshot(managed.Append("static-setup-receipt.json"), previousFiles.Append("static-setup-receipt.json"), token), token);
                StaticSetupTransaction.AtomicJson(Path.Combine(userRoot, "static-model-install.pending.json"),
                    new { schemaVersion = 1, operationId = operation, bundleId = ApprovedStaticModelBundle.Current.BundleId });
                pendingPublished = true;
                await Task.Run(() => transaction.Apply(progress, token), token);
            }
            var result = await StaticBootstrapRunner.RunAsync(Path.Combine(target, ExecutableName), operation,
                ApprovedStaticModelBundle.Current.BundleId, cache, transaction.ResultPath, false, false, progress, token);
            for (var resourceAttempt = 0; !result.Succeeded && result.ErrorCode == "static_model_resource_warning" && resourceAttempt < 3; resourceAttempt++)
            {
                if (string.IsNullOrWhiteSpace(result.ResourceProfileId))
                    throw new InvalidDataException("Bootstrap chưa xác định profile tài nguyên cần xác nhận.");
                var accepted = ConfirmResourceWarningAsync is not null && await ConfirmResourceWarningAsync(
                    result.Message ?? "Tài nguyên RAM/commit dưới mức khuyến nghị. Chỉ tiếp tục khi bạn chấp nhận thực hiện kiểm tra trên máy này.", token);
                if (!accepted) throw new InvalidOperationException("Chưa xác nhận tài nguyên để kiểm tra Qwen. Có thể giải phóng RAM rồi chạy Setup lại; cache vẫn được giữ.");
                result = await StaticBootstrapRunner.RunAsync(Path.Combine(target, ExecutableName), operation,
                    ApprovedStaticModelBundle.Current.BundleId, cache, transaction.ResultPath, false, true, progress, token, result.ResourceProfileId);
            }
            if (!result.Succeeded) throw new InvalidOperationException(result.Message ?? "Chưa kiểm tra đạt bộ model. Nhật ký và cache được giữ để thử lại.");
            if (standaloneRelease is not null)
                await RevalidateStandaloneCommitAsync(standaloneRelease, transaction, operation, token);
            else if (directRelease is not null)
                await RevalidateDirectCommitAsync(directRelease, transaction, operation, token);
            using (AcquireUsageBarrier(userRoot))
            {
                progress?.Report(new("commit", 96, "Các thành phần đã đạt; đang ghi nhận hoàn tất giao dịch cài đặt."));
                var pendingPath = Path.Combine(userRoot, "static-model-install.pending.json");
                ValidatePendingMarker(pendingPath, operation, ApprovedStaticModelBundle.Current.BundleId);
                StaticSetupTransaction.AtomicJson(receiptPath, new InstallReceipt(1, ApprovedStaticModelBundle.Current.BundleId,
                    target, release.Version, release.BuildNumber, release.Sha256, operation, DateTime.UtcNow));
                transaction.Commit();
                // The desktop may enter only after both the model receipt and app transaction commit.
                StaticSetupTransaction.DeleteOwnedFile(pendingPath);
            }
        }
        catch (Exception failure)
        {
            if (transaction.Current is { State: "Committed" } committed && committed.OperationId == operation)
                throw new InvalidOperationException("Ứng dụng và model đã được kiểm tra nhưng chưa mở khóa hoàn tất. Bấm Thử lại để kiểm tra và hoàn tất lượt cài.", failure);
            // No shortcut/launch is produced on a failed probe. Retain caches and the pending gate.
            using (AcquireUsageBarrier(userRoot))
            {
                if (!pendingPublished && previousCommit is not null) transaction.RestoreCommittedBeforeMutation(previousCommit);
                else await Task.Run(transaction.Recover, CancellationToken.None);
            }
            if (failure is OperationCanceledException) throw;
            if (!pendingPublished && previousCommit is not null)
                throw new InvalidOperationException(failure.Message + " Chưa thay bản ứng dụng đang dùng; bấm Thử lại sau khi xử lý nguyên nhân.", failure);
            throw new InvalidOperationException(failure.Message + " Ứng dụng đã được khôi phục; bộ model vẫn bị khóa cho đến khi Setup kiểm tra lại. Bấm Thử lại để tiếp tục.", failure);
        }
        CreateShortcuts(target, createDesktopShortcut);
        progress?.Report(new("complete", 100, "Đã cài và kiểm tra đạt ứng dụng, Qwen, Piper, OCR, media và WebView2."));
        if (launchAfterInstall)
            _ = Process.Start(new ProcessStartInfo(Path.Combine(target, ExecutableName)) { WorkingDirectory = target, UseShellExecute = true });
    }

    internal static void ValidatePendingMarker(string path, string operation, string bundle)
    {
        StaticSetupTransaction.AssertSafeFile(path);
        if (!File.Exists(path) || new FileInfo(path).Length > 4096)
            throw new InvalidDataException("Thiếu marker chờ commit của đúng lượt cài đặt.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (!root.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1 ||
            !root.TryGetProperty("operationId", out var id) || id.GetString() != operation ||
            !root.TryGetProperty("bundleId", out var model) || model.GetString() != bundle)
            throw new InvalidDataException("Marker chờ commit không khớp lượt cài đặt/model.");
    }

    private static void EnsureNoComponentProcess()
    {
        foreach (var name in new[] { "TOOL-LOCAL", "VideoMaker.Vietsub.TranslationWorker", "piper", "python", "pythonw", "uv" })
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                if (name is "TOOL-LOCAL" or "VideoMaker.Vietsub.TranslationWorker" or "piper")
                    throw new InvalidOperationException("VideoMaker hoặc worker model đang chạy. Hãy đóng các phiên VideoMaker và chờ tác vụ model kết thúc rồi thử lại.");
                string? executable;
                try { executable = process.MainModule?.FileName; }
                catch { throw new InvalidOperationException("Chưa xác định được tiến trình Python/uv có đang dùng model. Hãy kết thúc tác vụ liên quan hoặc kiểm tra quyền tiến trình rồi thử lại."); }
                var normalized = executable?.Replace('\\', '/');
                if (normalized is null || normalized.Contains("/ToolGenPostVideo/components/", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains("/components/piper/", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains("/components/voice/piper/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Một tác vụ Piper đang dùng runtime. Hãy chờ tác vụ kết thúc rồi thử lại; Setup không dừng tiến trình của bạn.");
            }
        }
    }

    internal static void ValidateStaticRelease(DesktopReleaseResponse release, InstallerOptions options)
    {
        ValidateApplicationRelease(release, options);
        if (options.IsSharedBootstrapper)
        {
            DesktopSharedBootstrapperPolicy.ValidateRelease(release, new Uri(options.ServerBaseUrl), options.Profile);
            var bootstrapper = release.SharedBootstrapper!;
            if (bootstrapper.Version != options.BootstrapperVersion || bootstrapper.ModelBundleId != ApprovedStaticModelBundle.Current.BundleId)
                throw new InvalidDataException("Setup này không khớp bootstrapper/model policy đã được server duyệt.");
            return;
        }
        if (!string.Equals(release.Version, options.DesktopVersion, StringComparison.Ordinal) || release.BuildNumber != options.DesktopBuildNumber ||
            !string.Equals(release.Platform, "win-x64", StringComparison.Ordinal) || !string.Equals(release.Channel, options.Channel, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(release.Sha256, options.DesktopSha256, StringComparison.OrdinalIgnoreCase) || release.InstallMode != DesktopInstallModes.StaticArchive
            || release.DistributionProtocol != DesktopDistributionProtocol.Current || release.ModelBundleId != ApprovedStaticModelBundle.Current.BundleId)
            throw new InvalidDataException("Release ứng dụng không khớp bản đã ghép với Setup/model. Hãy tải đúng Setup của phiên bản này.");
    }

    internal static Uri ValidateApplicationRelease(DesktopReleaseResponse release, InstallerOptions options)
    {
        if (release is null || release.ProductName != "VideoMaker" || string.IsNullOrWhiteSpace(release.Version) || release.Version.Length > 100
            || release.BuildNumber < 1 || release.Platform != options.Platform || !string.Equals(release.Channel, options.Channel, StringComparison.OrdinalIgnoreCase)
            || release.SizeBytes <= 0 || release.SizeBytes > DesktopArtifactDownloadPolicy.MaximumAppArchiveBytes
            || release.Sha256 is not { Length: 64 } || release.Sha256.Any(c => !Uri.IsHexDigit(c))
            || release.DistributionProtocol < 1 || release.DistributionProtocol > options.DistributionProtocol || release.InstallMode is not ("Legacy" or "StaticArchive")
            || (release.SourceKind == DesktopArtifactSourceKinds.ExternalVps && release.DistributionProtocol != options.DistributionProtocol))
            throw new InvalidDataException("Metadata package ứng dụng không hợp lệ hoặc vượt phạm vi bộ cài hỗ trợ.");
        return DesktopArtifactDownloadPolicy.Validate(new Uri(options.ServerBaseUrl), release.DownloadUrl, release.SourceKind);
    }

    internal static async Task ValidateExecutingSharedBootstrapperAsync(DesktopReleaseResponse release, InstallerOptions options, CancellationToken token)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidDataException("Không xác định được file Setup đang chạy.");
        await ValidateSharedBootstrapperFileAsync(executable, release, options, token);
    }

    internal static async Task ValidateSharedBootstrapperFileAsync(string executable, DesktopReleaseResponse release, InstallerOptions options, CancellationToken token)
    {
        ValidateStaticRelease(release, options);
        var setup = release.SharedBootstrapper ?? throw new InvalidDataException("Thiếu metadata Setup hệ thống.");
        await using var file = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, true);
        if (file.Length != setup.SizeBytes || !Convert.ToHexString(await SHA256.HashDataAsync(file, token)).Equals(setup.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("File Setup đang chạy không khớp checksum bootstrapper đã được duyệt.");
    }

    internal static void ValidateSharedVersionTransition(DesktopReleaseResponse release, string installedVersion, int installedBuild, string installedSha256)
    {
        if (release.Version == installedVersion && release.BuildNumber == installedBuild &&
            release.Sha256.Equals(installedSha256, StringComparison.OrdinalIgnoreCase)) return;
        if (!DesktopReleaseVersionPolicy.IsReleaseNewerVersionFirst(release.Version, release.BuildNumber, installedVersion, installedBuild))
            throw new InvalidDataException("Setup chỉ cập nhật lên bản mới hơn hoặc sửa đúng package đang cài; không thay bytes cùng version/build hoặc hạ phiên bản.");
    }

    internal static string ValidateStaticTarget(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidDataException("Hãy chọn thư mục cài đặt.");
        var target = Path.GetFullPath(directory.Trim()).TrimEnd(Path.DirectorySeparatorChar);
        if (target.Length < 4 || target.Equals(Path.GetPathRoot(target)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            target.Length > 160 || target.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException("Hãy chọn thư mục cài local có đường dẫn ngắn, không phải thư mục gốc của ổ đĩa.");
        StaticSetupTransaction.AssertSafeAncestors(target);
        if (new DriveInfo(Path.GetPathRoot(target)!).DriveType != DriveType.Fixed)
            throw new InvalidDataException("Bộ cài yêu cầu thư mục trên ổ đĩa local cố định.");
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (target.Equals(windows, StringComparison.OrdinalIgnoreCase) || target.StartsWith(windows + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Không cài ứng dụng vào thư mục Windows.");
        StaticSetupTransaction.EnsureSafeDirectory(target);
        var probe = Path.Combine(target, ".setup-write-" + Guid.NewGuid().ToString("N"));
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        File.Delete(probe);
        return target;
    }

    private static FileStream AcquireUsageBarrier(string root)
    {
        var path = Path.Combine(root, ".static-model-usage.lock");
        StaticSetupTransaction.AssertSafeFile(path);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException exception) { throw new InvalidOperationException("VideoMaker hoặc bộ cài model đang dùng runtime. Hãy đóng ứng dụng rồi thử lại.", exception); }
    }

    internal static void ValidateExistingInstallation(string target)
    {
        var executable = Path.Combine(target, ExecutableName);
        var path = Path.Combine(target, "static-setup-receipt.json");
        StaticSetupTransaction.AssertSafeFile(path);
        if (!File.Exists(path))
        {
            if (File.Exists(executable))
                throw new InvalidOperationException("Bản cũ chưa có khóa cài đặt tương thích. Hãy chọn thư mục mới; Setup giữ nguyên ứng dụng và dữ liệu cũ.");
            if (Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).Any())
                throw new InvalidOperationException("Thư mục đã chứa dữ liệu ngoài bộ cài. Hãy chọn một thư mục trống để giữ nguyên dữ liệu đó.");
            return;
        }
        using var document = SetupMaintenanceHandoff.ReadOwnedJson(path, 64 * 1024);
        var receipt = document.RootElement.Deserialize<InstallReceipt>(StaticJson);
        if (receipt is null || receipt.SchemaVersion != 1 || !string.Equals(receipt.TargetRoot, target, StringComparison.OrdinalIgnoreCase)
            || receipt.BundleId != ApprovedStaticModelBundle.Current.BundleId || string.IsNullOrWhiteSpace(receipt.Version)
            || receipt.BuildNumber < 1 || !DesktopMaintenanceProtocol.IsHash(receipt.PackageSha256)
            || !Guid.TryParseExact(receipt.OperationId, "N", out _) || receipt.CompletedAtUtc == default || receipt.CompletedAtUtc > DateTime.UtcNow.AddMinutes(5))
            throw new InvalidOperationException("Bản cũ chưa có khóa cài đặt tương thích. Hãy chọn thư mục mới; Setup giữ nguyên ứng dụng và dữ liệu cũ.");
        // The executable may be missing or corrupt. Ownership comes from the bounded
        // installation receipt, so standalone exact Setup can repair a non-starting app.
    }

    internal static string[] ResolvePreviousManagedFiles(string target, StaticSetupTransaction transaction)
    {
        var receiptPath = StaticSetupTransaction.SafePath(target, "static-setup-receipt.json");
        StaticSetupTransaction.AssertSafeFile(receiptPath);
        // The installed manifest is mutable application data, never an ownership grant.
        // Fresh installation retries are allowed only while no application receipt/files exist.
        if (!File.Exists(receiptPath) && (transaction.Current is null or { State: "Staging" or "RolledBack" }))
        {
            ValidateExistingInstallation(target);
            return [];
        }
        ValidateExistingInstallation(target);
        var journal = transaction.Current;
        if (journal is not { State: "Committed" }) throw new InvalidDataException("Thiếu journal Committed đã xác minh để sửa bản cài.");
        using var receiptDocument = SetupMaintenanceHandoff.ReadOwnedJson(receiptPath, 64 * 1024);
        var receipt = receiptDocument.RootElement.Deserialize<InstallReceipt>(StaticJson)
            ?? throw new InvalidDataException("Thiếu receipt sở hữu bản cài.");
        var metadata = Path.GetDirectoryName(Path.GetDirectoryName(transaction.Root))!;
        SetupMaintenanceHandoff.ValidateOwnership(target, receipt.Version, receipt.BuildNumber, receipt.PackageSha256, metadata);
        if (journal.OperationId != receipt.OperationId
            || !string.Equals(journal.ReleaseSha256, receipt.PackageSha256, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(journal.TargetRoot, target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Journal đã đổi identity so với receipt sở hữu bản cài.");
        var owned = journal.Changes.Where(change => !change.RemoveOnly)
            .Select(change => StaticSetupTransaction.NormalizeRelative(change.RelativePath)).ToArray();
        if (owned.Length == 0 || owned.Distinct(StringComparer.OrdinalIgnoreCase).Count() != owned.Length
            || !owned.Contains(ExecutableName, StringComparer.OrdinalIgnoreCase)
            || !owned.Contains("update-manifest.json", StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Journal không đủ danh mục file sở hữu để sửa manifest.");
        foreach (var relative in owned) StaticSetupTransaction.AssertSafeFile(StaticSetupTransaction.SafePath(target, relative));
        return owned;
    }

    internal static string DescribeInstallationAction(string targetDirectory, DesktopReleaseResponse release, string? metadataRoot = null)
        => DescribeInstallationAction(targetDirectory, release.Version, release.BuildNumber, release.Sha256, metadataRoot);
    internal static string DescribeInstallationAction(string targetDirectory, DesktopDirectReleaseResponse release, string? metadataRoot = null)
        => DescribeInstallationAction(targetDirectory, release.Version, release.DeclaredBuildNumber, release.ExpectedSha256, metadataRoot);
    internal static string DescribeInstallationAction(string targetDirectory, DesktopStandaloneReleaseResponse release, string? metadataRoot = null)
        => DescribeInstallationAction(targetDirectory, release.Version, release.DeclaredBuildNumber, release.ExpectedSha256, metadataRoot);
    private static string DescribeInstallationAction(string targetDirectory, string version, int build, string sha256, string? metadataRoot)
    {
        try
        {
            if (!Path.IsPathFullyQualified(targetDirectory) || targetDirectory.StartsWith("\\\\", StringComparison.Ordinal)
                || targetDirectory.Length > 160 || !Directory.Exists(targetDirectory)) return "Cài đặt";
            var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetDirectory));
            var path = Path.Combine(target, "static-setup-receipt.json");
            if (!File.Exists(path)) return "Cài đặt";
            ValidateExistingInstallation(target);
            using var document = SetupMaintenanceHandoff.ReadOwnedJson(path, 64 * 1024);
            var receipt = document.RootElement.Deserialize<InstallReceipt>(StaticJson)!;
            SetupMaintenanceHandoff.ValidateOwnership(target, receipt.Version, receipt.BuildNumber, receipt.PackageSha256, metadataRoot);
            return receipt.Version == version && receipt.BuildNumber == build
                && string.Equals(receipt.PackageSha256, sha256, StringComparison.OrdinalIgnoreCase) ? "Sửa chữa" : "Cập nhật";
        }
        catch (Exception error) when (error is IOException or ArgumentException or JsonException or InvalidDataException
            or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException or FormatException)
        { return "Cài đặt"; }
    }

    internal static void EnsureDiskBudget(IEnumerable<(string Path, long Bytes)> requests)
    {
        foreach (var group in requests.GroupBy(request => Path.GetPathRoot(Path.GetFullPath(request.Path))!, StringComparer.OrdinalIgnoreCase))
        {
            var needed = checked(group.Sum(request => request.Bytes));
            var drive = new DriveInfo(group.Key);
            if (!drive.IsReady || drive.AvailableFreeSpace < needed)
                throw new IOException($"Ổ {group.Key} cần {needed:N0} byte ({needed / 1024d / 1024d / 1024d:0.00} GiB), hiện trống {drive.AvailableFreeSpace:N0} byte; thiếu {Math.Max(0, needed - drive.AvailableFreeSpace):N0} byte. Cache tải dở vẫn được giữ.");
        }
    }

    internal static void ValidateDesktopArchive(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.Entries.Count > 200_000) throw new InvalidDataException("Package có quá nhiều entry.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/')) name = name.TrimEnd('/');
            var normalized = StaticSetupTransaction.NormalizeRelative(name);
            if (!names.Add(normalized) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Package có entry trùng hoặc liên kết không an toàn.");
            total = checked(total + entry.Length);
            if (total > DesktopArtifactDownloadPolicy.MaximumExpandedBytes) throw new InvalidDataException("Package giải nén quá lớn.");
        }
        EnsureDiskBudget([(Path.GetDirectoryName(path)!, checked(total + 128L * 1024 * 1024))]);
    }

    internal async Task DownloadStaticPackageAsync(DesktopReleaseResponse release, string path,
        IProgress<DesktopUpdateProgress>? progress, CancellationToken token)
    {
        var resource = ValidateApplicationRelease(release, options);
        if (File.Exists(path) && new FileInfo(path).Length == release.SizeBytes &&
            (await HashAsync(path, token)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase)) return;
        StaticSetupTransaction.DeleteOwnedFile(path);
        var part = path + ".part";
        var receiptPath = path + ".receipt.json";
        StaticSetupTransaction.AssertSafeFile(part);
        StaticSetupTransaction.AssertSafeFile(receiptPath);
        PackageReceipt? receipt = null;
        try { if (File.Exists(receiptPath) && new FileInfo(receiptPath).Length < 4096) receipt = JsonSerializer.Deserialize<PackageReceipt>(File.ReadAllText(receiptPath), StaticJson); }
        catch (JsonException) { }
        if (receipt?.Url != resource.AbsoluteUri || receipt.Sha256 != release.Sha256 || receipt.SizeBytes != release.SizeBytes ||
            !EntityTagHeaderValue.TryParse(receipt.ETag, out var receivedTag) || receivedTag.IsWeak)
        { StaticSetupTransaction.DeleteOwnedFile(part); receipt = null; }
        using var http = new HttpClient(artifactHandler ?? new HttpClientHandler
            { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None }, disposeHandler: artifactHandler is null)
            { Timeout = Timeout.InfiniteTimeSpan };
        for (var attempt = 0; attempt < 4; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var offset = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (offset == release.SizeBytes && (await HashAsync(part, token)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            { File.Move(part, path, true); return; }
            if (offset >= release.SizeBytes || receipt?.ETag is null)
            { StaticSetupTransaction.DeleteOwnedFile(part); offset = 0; }
            EnsureDiskBudget([(Path.GetDirectoryName(path)!, checked(release.SizeBytes - offset + 64L * 1024 * 1024))]);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, resource);
                request.Headers.AcceptEncoding.ParseAdd("identity");
                if (offset > 0)
                { request.Headers.Range = new RangeHeaderValue(offset, null); request.Headers.IfRange = new RangeConditionHeaderValue(new EntityTagHeaderValue(receipt!.ETag!)); }
                using var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                headerTimeout.CancelAfter(TimeSpan.FromSeconds(60));
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerTimeout.Token);
                headerTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
                if ((int)response.StatusCode is >= 300 and < 400)
                    throw new InvalidDataException("Nguồn package yêu cầu redirect chưa được duyệt; bộ cài không đổi nguồn tải.");
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                { StaticSetupTransaction.DeleteOwnedFile(part); receipt = null; continue; }
                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable && attempt < 3)
                {
                    var retry = response.Headers.RetryAfter;
                    var wait = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(1 << attempt));
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(wait.TotalSeconds, 1, 60)), token); continue;
                }
                response.EnsureSuccessStatusCode();
                ValidatePackageMime(response.Content.Headers.ContentType?.MediaType);
                if (response.Content.Headers.ContentEncoding.Count != 0) throw new InvalidDataException("Package bị thay đổi content encoding.");
                if (response.StatusCode == HttpStatusCode.OK) offset = 0;
                else if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    var range = response.Content.Headers.ContentRange;
                    if (offset == 0 || range?.Unit != "bytes" || range.From != offset || range.To != release.SizeBytes - 1 || range.Length != release.SizeBytes ||
                        response.Headers.ETag?.IsWeak != false || response.Headers.ETag.ToString() != receipt?.ETag)
                        throw new InvalidDataException("Phản hồi tải tiếp package không khớp.");
                }
                else throw new InvalidDataException("Phản hồi package không hợp lệ.");
                if (response.Content.Headers.ContentLength is { } length && length != release.SizeBytes - offset)
                    throw new InvalidDataException("Dung lượng phản hồi package không đúng.");
                receipt = new(resource.AbsoluteUri, release.Sha256, release.SizeBytes, response.Headers.ETag?.IsWeak == false ? response.Headers.ETag.ToString() : null);
                StaticSetupTransaction.AtomicJson(receiptPath, receipt);
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using (var output = new FileStream(part, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true))
                {
                    var buffer = new byte[128 * 1024];
                    while (true)
                    {
                        using var stall = CancellationTokenSource.CreateLinkedTokenSource(token);
                        stall.CancelAfter(TimeSpan.FromSeconds(60));
                        var read = await input.ReadAsync(buffer, stall.Token);
                        if (read == 0) break;
                        if (offset + read > release.SizeBytes) throw new InvalidDataException("Package vượt dung lượng đã duyệt.");
                        await output.WriteAsync(buffer.AsMemory(0, read), token); offset += read;
                        progress?.Report(new("download", 2 + (int)(offset * 12 / release.SizeBytes), $"Đang tải ứng dụng: {offset / 1024d / 1024d:0.0}/{release.SizeBytes / 1024d / 1024d:0.0} MB"));
                    }
                    await output.FlushAsync(token);
                }
                if (offset != release.SizeBytes || !(await HashAsync(part, token)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                { StaticSetupTransaction.DeleteOwnedFile(part); throw new InvalidDataException("Package tải về sai kích thước hoặc SHA-256."); }
                ValidatePackageSignature(part);
                File.Move(part, path, true); StaticSetupTransaction.DeleteOwnedFile(receiptPath); return;
            }
            catch (Exception exception) when (attempt < 3 && !token.IsCancellationRequested && exception is HttpRequestException or IOException or OperationCanceledException)
            { await Task.Delay(TimeSpan.FromSeconds(1 << attempt), token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new TimeoutException("Kết nối tải ứng dụng không có tiến triển trong thời gian cho phép. Bấm Thử lại để tiếp tục."); }
        }
        throw new HttpRequestException("Không tải được package sau số lần thử cho phép. Mở Setup lại để tiếp tục.");
    }
    private static async Task<string> HashAsync(string path, CancellationToken token)
    { await using var input = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(input, token)).ToLowerInvariant(); }

    internal static void ValidatePackageMime(string? mime)
    {
        if (mime is null || !new[] { "application/zip", "application/x-zip-compressed", "application/octet-stream" }.Contains(mime, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Nguồn tải không trả về MIME ZIP được hỗ trợ.");
    }

    internal static void ValidatePackageSignature(string path)
    {
        using var input = File.OpenRead(path);
        Span<byte> signature = stackalloc byte[4];
        if (input.Read(signature) != 4 || signature[0] != 'P' || signature[1] != 'K' || signature[2] != 3 || signature[3] != 4)
            throw new InvalidDataException("File ứng dụng không có chữ ký định dạng ZIP hợp lệ.");
    }
}
