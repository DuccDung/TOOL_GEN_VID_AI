using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32.SafeHandles;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Contracts.Updates;
using TOOL_SHARED.Distribution.StaticModels;

namespace TOOL_SETUP;

/// <summary>Runs only the validated package executable, in a process tree owned by this Setup.</summary>
internal static class StaticBootstrapRunner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16 };
    internal static async Task ValidatePolicyAsync(string executable, CancellationToken token,
        int expectedDistributionProtocol = DesktopDistributionProtocol.Current, string? expectedProfile = null)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--check-static-model-policy");
        using var job = OwnedProcessJob.Create();
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Không kiểm tra được policy của ứng dụng.");
        try { job.Assign(process); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        var stdout = ReadBoundedAsync(process.StandardOutput, 16 * 1024);
        var stderr = DrainAsync(process.StandardError, null);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { job.Terminate(); if (token.IsCancellationRequested) throw; throw new TimeoutException("Kiểm tra policy ứng dụng quá thời gian."); }
        job.Terminate();
        await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(10), token);
        using var document = JsonDocument.Parse(await stdout);
        ValidatePolicyReport(document.RootElement, process.ExitCode, expectedDistributionProtocol, expectedProfile);
    }

    internal static void ValidatePolicyReport(JsonElement root, int exitCode,
        int expectedDistributionProtocol = DesktopDistributionProtocol.Current, string? expectedProfile = null)
    {
        if (exitCode != 0 || root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var schema) ||
            schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var schemaVersion) || schemaVersion != 1 ||
            !root.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("modelUseAllowed", out var allowed) || allowed.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("bundleId", out var bundle) || bundle.GetString() != ApprovedStaticModelBundle.Current.BundleId ||
            !root.TryGetProperty("workerVersion", out var worker) || worker.GetString() != ApprovedStaticModelBundle.Current.RequiredWorkerVersion ||
            !root.TryGetProperty("workerProtocolVersion", out var protocol) || protocol.GetInt32() != ApprovedStaticModelBundle.Current.RequiredWorkerProtocolVersion ||
            !root.TryGetProperty("archiveSha256", out var hash) || hash.GetString() != ApprovedStaticModelBundle.Current.ArchiveSha256)
            throw new InvalidDataException("Ứng dụng và Setup không có cùng policy model bắt buộc đã duyệt.");
        if (expectedDistributionProtocol == DesktopDistributionProtocol.Shared &&
            (!root.TryGetProperty("distributionProtocol", out var distribution) || distribution.ValueKind != JsonValueKind.Number || !distribution.TryGetInt32(out var distributionVersion) ||
                distributionVersion != DesktopDistributionProtocol.Shared || string.IsNullOrWhiteSpace(expectedProfile) ||
                !root.TryGetProperty("profile", out var profile) || profile.ValueKind != JsonValueKind.String || profile.GetString() != expectedProfile))
            throw new InvalidDataException("Binary ứng dụng không hỗ trợ đúng giao thức hoặc profile của Setup dùng chung.");
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximum)
    {
        var value = new System.Text.StringBuilder(); var buffer = new char[2048]; var overflow = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory()); if (read == 0) break;
            if (value.Length + read > maximum) overflow = true;
            else if (!overflow) value.Append(buffer, 0, read);
        }
        if (overflow) throw new InvalidDataException("Kết quả policy ứng dụng quá lớn.");
        return value.ToString();
    }
    internal static async Task<StaticModelBootstrapResult> RunAsync(string executable, string operation,
        string bundleId, string cacheRoot, string resultPath, bool prerequisitesOnly, bool acceptResources,
        IProgress<DesktopUpdateProgress>? progress, CancellationToken cancellationToken, string? confirmedResourceProfile = null)
    {
        StaticSetupTransaction.DeleteOwnedFile(resultPath);
        var jobReadyPath = resultPath + ".jobready";
        StaticSetupTransaction.DeleteOwnedFile(jobReadyPath);
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(prerequisitesOnly ? "--check-static-prerequisites" : "--prepare-static-models");
        foreach (var arg in new[] { "--operation", operation, "--result", resultPath, "--cache-root", cacheRoot,
                     "--parent-pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) }) start.ArgumentList.Add(arg);
        if (acceptResources)
        {
            if (string.IsNullOrWhiteSpace(confirmedResourceProfile) || confirmedResourceProfile.Length > 200)
                throw new InvalidDataException("Thiếu định danh tài nguyên đã được xác nhận.");
            start.ArgumentList.Add("--accept-resource-warning");
            start.ArgumentList.Add("--confirmed-resource-profile"); start.ArgumentList.Add(confirmedResourceProfile);
        }
        start.ArgumentList.Add("--parent-job-ready"); start.ArgumentList.Add(jobReadyPath);
        using var job = OwnedProcessJob.Create();
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Không khởi động được bước kiểm tra bộ cài.");
        try { job.Assign(process); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        using var readyMarker = new JobReadyMarker(jobReadyPath, operation);
        var stdout = DrainAsync(process.StandardOutput, progress, prerequisitesOnly ? "prerequisite" : "probe", prerequisitesOnly ? 26 : 85);
        var stderr = DrainAsync(process.StandardError, null);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(prerequisitesOnly ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            job.Terminate();
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(shutdown.Token); } catch (OperationCanceledException) { }
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw new TimeoutException("Bước kiểm tra runtime quá thời gian. Cache và nhật ký được giữ để thử lại.");
        }
        job.Terminate();
        await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        var result = await ReadResultAsync(resultPath, cancellationToken);
        ValidateResult(result, operation, bundleId, process.ExitCode, prerequisitesOnly);
        return result!;
    }

    internal static async Task<StaticModelBootstrapResult?> ReadResultAsync(string path, CancellationToken token)
    {
        const int maximum = 64 * 1024;
        StaticSetupTransaction.AssertSafeFile(path);
        if (!File.Exists(path)) throw new InvalidDataException("Bootstrap không trả về kết quả có cấu trúc hợp lệ.");
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (input.Length is <= 0 or > maximum) throw new InvalidDataException("File kết quả bootstrap rỗng hoặc vượt giới hạn.");
        var bytes = new byte[maximum + 1]; var count = 0;
        while (count < bytes.Length)
        {
            var read = await input.ReadAsync(bytes.AsMemory(count), token); if (read == 0) break; count += read;
        }
        if (count > maximum) throw new InvalidDataException("File kết quả bootstrap vượt giới hạn.");
        try
        {
            using var document = JsonDocument.Parse(bytes.AsMemory(0, count), new JsonDocumentOptions { MaxDepth = 16 });
            RejectDuplicateProperties(document.RootElement);
            return document.RootElement.Deserialize<StaticModelBootstrapResult>(Json);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException)
        { throw new InvalidDataException("File kết quả bootstrap không đúng schema JSON đã duyệt.", error); }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("File kết quả bootstrap có field trùng.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var value in element.EnumerateArray()) RejectDuplicateProperties(value);
    }

    private sealed class JobReadyMarker : IDisposable
    {
        private readonly string _path;
        public JobReadyMarker(string path, string operation)
        {
            _path = path;
            PublishJobReady(path, operation);
        }
        public void Dispose() => StaticSetupTransaction.DeleteOwnedFile(_path);
    }

    internal static void PublishJobReady(string path, string operation)
    {
        if (!Guid.TryParseExact(operation, "N", out _)) throw new InvalidDataException("Định danh handshake không hợp lệ.");
        StaticSetupTransaction.AssertSafeFile(path);
        var temporary = path + "." + operation + ".part";
        StaticSetupTransaction.DeleteOwnedFile(temporary);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(System.Text.Encoding.ASCII.GetBytes(operation)); stream.Flush(true); }
            File.Move(temporary, path);
        }
        finally { StaticSetupTransaction.DeleteOwnedFile(temporary); }
    }

    internal static void ValidateResult(StaticModelBootstrapResult? result, string operation, string bundleId, int exitCode, bool prerequisitesOnly)
    {
        if (result is null || result.SchemaVersion != 1 || result.OperationId != operation || !Guid.TryParseExact(operation, "N", out _) || result.BundleId != bundleId ||
            result.Components is null || result.Components.Count > 16 || result.Components.Any(x => x is null || string.IsNullOrWhiteSpace(x.ComponentId) || x.ComponentId.Length > 64 ||
                x.Fingerprint is not null && !IsHash(x.Fingerprint) || x.Ready && (!IsHash(x.Fingerprint) || x.ErrorCode is not null)) ||
            result.Components.Select(x => x.ComponentId).Distinct(StringComparer.Ordinal).Count() != result.Components.Count ||
            result.Message?.Length > 4096 || result.ErrorCode?.Length > 128 || result.ResourceProfileId?.Length > 200 ||
            result.RuntimeDiskRequiredBytes is < 0 or > 16L * 1024 * 1024 * 1024)
            throw new InvalidDataException("Kết quả bootstrap không khớp lượt cài đặt hoặc bộ model.");
        if (result.Succeeded && exitCode != 0 || !result.Succeeded && exitCode == 0)
            throw new InvalidDataException("Mã thoát bootstrap không khớp kết quả kiểm tra.");
        if (result.Succeeded)
        {
            if (result.ErrorCode is not null) throw new InvalidDataException("Bootstrap báo hoàn tất cùng mã lỗi.");
            var required = prerequisitesOnly ? new[] { "media", "ocr", "webview2" } : new[] { "media", "ocr", "qwen", "piper", "webview2" };
            if (result.Components.Count != required.Length || required.Any(id => !result.Components.Any(component => component.ComponentId == id && component.Ready)))
                throw new InvalidDataException("Bootstrap chưa kiểm tra đủ các thành phần bắt buộc.");
        }
        else if (string.IsNullOrWhiteSpace(result.ErrorCode)) throw new InvalidDataException("Bootstrap lỗi nhưng không có mã nguyên nhân.");
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static async Task DrainAsync(StreamReader reader, IProgress<DesktopUpdateProgress>? progress, string stage = "probe", int percent = 85)
    {
        // Bound each line, drain native diagnostics without storing or exposing model output.
        var buffer = new char[2048];
        var line = new System.Text.StringBuilder();
        var discard = false;
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory());
            if (read == 0) return;
            for (var i = 0; i < read; i++)
            {
                if (buffer[i] == '\n')
                {
                    if (!discard && progress is not null)
                    {
                        try
                        {
                            var value = JsonSerializer.Deserialize<StaticModelSetupProgress>(line.ToString(), Json);
                            if (value is { Message.Length: > 0 and < 512 })
                                progress.Report(new(stage, percent, ComponentMessage(value.Message)));
                        }
                        catch (JsonException) { }
                    }
                    line.Clear(); discard = false;
                }
                else if (!discard)
                {
                    if (line.Length == 16 * 1024) { discard = true; line.Clear(); }
                    else line.Append(buffer[i]);
                }
            }
        }
    }

    private static string ComponentMessage(string message) => message switch
    {
        "webview2" => "Kiểm tra giao diện Microsoft Edge WebView2.",
        "media" => "Kiểm tra xử lý video và âm thanh FFmpeg/FFprobe.",
        "ocr" => "Kiểm tra nhận dạng phụ đề Anh/Trung bằng PaddleOCR.",
        "qwen" => "Kiểm tra dịch Việt bằng Qwen 4B.",
        "piper" => "Kiểm tra giọng Việt bằng Piper offline.",
        _ => message
    };

    internal sealed class OwnedProcessJob : IDisposable
    {
        private readonly SafeFileHandle _handle;
        private OwnedProcessJob(SafeFileHandle handle) => _handle = handle;
        public static OwnedProcessJob Create()
        {
            var handle = CreateJobObject(IntPtr.Zero, null);
            if (handle.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var limits = new ExtendedLimitInformation { BasicLimitInformation = new BasicLimitInformation { LimitFlags = 0x2000 } };
            if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimitInformation>()))
            { handle.Dispose(); throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
            return new OwnedProcessJob(handle);
        }
        public void Assign(Process process)
        { if (!AssignProcessToJobObject(_handle, process.Handle)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
        public void Terminate() => TerminateJobObject(_handle, 1);
        public void Dispose() => _handle.Dispose();
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimitInformation
        { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimitInformation
        { public BasicLimitInformation BasicLimitInformation; public IoCounters IoInfo; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle handle, int infoClass, ref ExtendedLimitInformation info, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    }
}
