using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Data;
using TOOL_SERVER.Generation;
using TOOL_SHARED.Contracts.Publishing;

namespace TOOL_SERVER.Publishing;

internal sealed record PublishingMediaInfo(string Sha256, long SizeBytes);

internal sealed class PublishingMedia(VideoFactoryDbContext video, IOptions<PublishingOptions> options,
    IOptions<VideoOutputOptions> outputOptions, TimeProvider time)
{
    internal const long MaximumBytes = 200L * 1024 * 1024;
    public async Task PreflightAsync(CancellationToken ct)
    {
        if (!IsConfigured(options.Value)) throw PublishingCalendar.Error("publishing_media_not_ready", "Server cần kho media và FFprobe hợp lệ trước khi tạo video.", 409);
        var tool = Path.GetFullPath(options.Value.FfprobePath!); ValidatePath(tool); ValidatePath(options.Value.MediaRoot!);
        await using var file = File.OpenRead(tool);
        if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, ct)), options.Value.FfprobeSha256, StringComparison.OrdinalIgnoreCase))
            throw PublishingCalendar.Error("publishing_probe_invalid", "FFprobe không khớp checksum. Chưa gửi yêu cầu tạo video.", 409);
        Directory.CreateDirectory(options.Value.MediaRoot!);
        if (new DriveInfo(Path.GetPathRoot(options.Value.MediaRoot!)!).AvailableFreeSpace < MaximumBytes + 256L * 1024 * 1024)
            throw PublishingCalendar.Error("publishing_disk_full", "Server thiếu dung lượng để tạo và lưu video.", 409);
    }
    internal static bool IsConfigured(PublishingOptions value) =>
        !string.IsNullOrWhiteSpace(value.MediaRoot) && Path.IsPathFullyQualified(value.MediaRoot) &&
        !string.IsNullOrWhiteSpace(value.FfprobePath) && Path.IsPathFullyQualified(value.FfprobePath) &&
        File.Exists(value.FfprobePath) && value.FfprobeSha256 is { Length: 64 } && value.FfprobeSha256.All(Uri.IsHexDigit);

    internal string PathFor(Guid runId)
    {
        if (!IsConfigured(options.Value)) throw PublishingCalendar.Error("publishing_media_not_ready", "Kho video và FFprobe của server chưa sẵn sàng.", 409);
        var root = Path.GetFullPath(options.Value.MediaRoot!); ValidatePath(root); Directory.CreateDirectory(root);
        return Path.Combine(root, runId.ToString("N") + ".mp4");
    }

    public async Task<PublishingMediaInfo> PrepareAsync(PublishingRun run, CancellationToken ct)
    {
        var destination = PathFor(run.RunId);
        var cached = await video.GeneratedVideoOutputs.AsNoTracking().SingleOrDefaultAsync(x => x.ProviderRequestId == run.ProviderRequestId &&
            x.Status == "Ready" && x.DeletedAtUtc == null && x.ExpiresAtUtc > time.GetUtcNow().UtcDateTime, ct)
            ?? throw PublishingCalendar.Error("publishing_output_pending", "Video chưa có trong kho output hoặc đã hết hạn.", 409);
        if (cached.SizeBytes is <= 0 or > MaximumBytes || cached.MimeType != "video/mp4" || cached.Sha256.Length != 64)
            throw PublishingCalendar.Error("publishing_output_invalid", "Video không đúng định dạng MP4 hoặc vượt giới hạn 200 MiB.", 409);
        if (Path.GetFileName(cached.StorageKey) != cached.StorageKey || cached.StorageKey.Contains(':') || cached.StorageKey.Contains('\\'))
            throw PublishingCalendar.Error("publishing_output_invalid", "Mã lưu video không hợp lệ.", 409);
        var cacheRoot = Path.GetFullPath(outputOptions.Value.StorageRoot ?? Path.Combine(AppContext.BaseDirectory, "data", "video-outputs"));
        var source = Path.Combine(cacheRoot, cached.StorageKey); ValidatePath(source);
        if (File.Exists(destination))
        {
            await ValidateAsync(destination, cached.Sha256, cached.SizeBytes, PublishingService.Read<PublishingScheduleInput>(run.InputJson), ct);
            return new(cached.Sha256, cached.SizeBytes);
        }
        var drive = new DriveInfo(Path.GetPathRoot(destination)!);
        if (drive.AvailableFreeSpace < cached.SizeBytes + 256L * 1024 * 1024)
            throw PublishingCalendar.Error("publishing_disk_full", "Kho video của server không còn đủ dung lượng.", 409);
        var part = destination + ".part"; ValidatePath(part);
        // Only this run's deterministic staging file can be replaced on a verified local retry.
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            if (input.Length != cached.SizeBytes) throw PublishingCalendar.Error("publishing_output_changed", "Dung lượng video nguồn đã thay đổi.", 409);
            await input.CopyToAsync(output, ct); await output.FlushAsync(ct);
        }
        await ValidateAsync(part, cached.Sha256, cached.SizeBytes, PublishingService.Read<PublishingScheduleInput>(run.InputJson), ct);
        File.Move(part, destination, false);
        return new(cached.Sha256, cached.SizeBytes);
    }

    public async Task<FileStream> OpenVerifiedAsync(PublishingRun run, CancellationToken ct)
    {
        var path = PathFor(run.RunId); ValidatePath(path);
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        try
        {
            if (file.Length != run.MediaSizeBytes || file.Length is <= 0 or > MaximumBytes ||
                !string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, ct)), run.MediaSha256, StringComparison.OrdinalIgnoreCase))
                throw PublishingCalendar.Error("publishing_media_changed", "Video đã thay đổi sau kiểm tra. Không đăng file này.", 409);
            file.Position = 0; return file;
        }
        catch { await file.DisposeAsync(); throw; }
    }

    private async Task ValidateAsync(string path, string hash, long size, PublishingScheduleInput input, CancellationToken ct)
    {
        ValidatePath(path);
        await using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (file.Length != size || size is <= 0 or > MaximumBytes) throw PublishingCalendar.Error("publishing_media_invalid", "Dung lượng video không hợp lệ.", 409);
            var header = new byte[12]; await file.ReadExactlyAsync(header, ct);
            if (!header.AsSpan(4, 4).SequenceEqual("ftyp"u8)) throw PublishingCalendar.Error("publishing_media_invalid", "Video không có signature MP4 hợp lệ.", 409);
            file.Position = 0;
            if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(file, ct)), hash, StringComparison.OrdinalIgnoreCase))
                throw PublishingCalendar.Error("publishing_media_invalid", "Checksum video không khớp metadata.", 409);
        }
        var tool = Path.GetFullPath(options.Value.FfprobePath!); ValidatePath(tool);
        await using (var toolFile = File.OpenRead(tool))
            if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(toolFile, ct)), options.Value.FfprobeSha256, StringComparison.OrdinalIgnoreCase))
                throw PublishingCalendar.Error("publishing_probe_invalid", "FFprobe không khớp checksum đã cấu hình.", 409);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(45));
        var start = new ProcessStartInfo(tool) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Clear();
        foreach (var key in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP" }) if (Environment.GetEnvironmentVariable(key) is { } value) start.Environment[key] = value;
        foreach (var arg in new[] { "-v", "error", "-show_entries", "format=duration:stream=codec_type,codec_name,width,height,r_frame_rate", "-of", "json", path }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw PublishingCalendar.Error("publishing_probe_failed", "Không khởi động được FFprobe.", 409);
        using var registration = deadline.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var stdout = ReadBoundedAsync(process.StandardOutput, deadline.Token);
        var stderr = ReadBoundedAsync(process.StandardError, deadline.Token);
        await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(deadline.Token));
        if (process.ExitCode != 0) throw PublishingCalendar.Error("publishing_probe_failed", "FFprobe từ chối video thành phẩm.", 409);
        ValidateProbe(await stdout, input);
    }

    internal static void ValidateProbe(string json, PublishingScheduleInput input)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var streams = root.GetProperty("streams").EnumerateArray().ToArray();
        var pictures = streams.Where(x => x.GetProperty("codec_type").GetString() == "video").ToArray();
        var audio = streams.Where(x => x.GetProperty("codec_type").GetString() == "audio").ToArray();
        var duration = double.Parse(root.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        if (pictures.Length != 1 || audio.Length != 1 || streams.Length != 2 || pictures[0].GetProperty("codec_name").GetString() != "h264" ||
            audio[0].GetProperty("codec_name").GetString() != "aac" || !double.IsFinite(duration) || Math.Abs(duration - input.DurationSeconds) > 1)
            throw PublishingCalendar.Error("publishing_media_invalid", "Video cần một luồng H.264, một luồng AAC và thời lượng đúng thiết lập.", 409);
        var width = pictures[0].GetProperty("width").GetInt32(); var height = pictures[0].GetProperty("height").GetInt32();
        var ratio = input.AspectRatio == "9:16" ? 9d / 16 : 16d / 9;
        if (width < 360 || height < 360 || Math.Abs((double)width / height - ratio) > .03)
            throw PublishingCalendar.Error("publishing_media_invalid", "Kích thước hoặc tỷ lệ video không đúng thiết lập.", 409);
        if (input.Targets.Any(x => x.Platform == "Facebook"))
        {
            var rate = pictures[0].TryGetProperty("r_frame_rate", out var fps) ? (fps.GetString() ?? "").Split('/') : [];
            if (input.AspectRatio != "9:16" || width < 540 || height < 960 || duration < 4 || rate.Length != 2 ||
                !double.TryParse(rate[0], CultureInfo.InvariantCulture, out var numerator) || !double.TryParse(rate[1], CultureInfo.InvariantCulture, out var denominator) ||
                !double.IsFinite(numerator) || !double.IsFinite(denominator) || denominator <= 0 || numerator / denominator < 23)
                throw PublishingCalendar.Error("publishing_facebook_media_invalid", "Facebook Reels cần video dọc từ 540×960, ít nhất 4 giây và 23 khung hình/giây.", 409);
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken ct)
    {
        var output = new StringBuilder(); var buffer = new char[2048]; int read;
        while ((read = await reader.ReadAsync(buffer, ct)) > 0)
        { if (output.Length + read > 64 * 1024) throw PublishingCalendar.Error("publishing_probe_invalid", "FFprobe trả về dữ liệu vượt giới hạn.", 409); output.Append(buffer, 0, read); }
        return output.ToString();
    }

    internal static void ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) ||
            path.AsSpan(Path.GetPathRoot(path)!.Length).Contains(':')) throw PublishingCalendar.Error("publishing_path_invalid", "Kho video phải là đường dẫn local hợp lệ.", 409);
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw PublishingCalendar.Error("publishing_path_invalid", "Kho video không được đi qua liên kết filesystem.", 409);
            current = Path.GetDirectoryName(current);
        }
    }
}
