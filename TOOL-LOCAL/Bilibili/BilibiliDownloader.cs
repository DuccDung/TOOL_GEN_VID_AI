using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_LOCAL.Media;

namespace TOOL_LOCAL.Bilibili;

internal interface IBilibiliMediaVerifier
{
    Task RequireReadyAsync(CancellationToken token);
    Task<string> VerifyAsync(string path, CancellationToken token);
}

internal sealed class BilibiliMediaVerifier(IMediaToolPreflightService preflight, FfprobeService probe) : IBilibiliMediaVerifier
{
    public async Task RequireReadyAsync(CancellationToken token) => await preflight.RequireReadyAsync(token);

    public async Task<string> VerifyAsync(string path, CancellationToken token)
    {
        BilibiliFiles.RequireRegularFile(path);
        var size = new FileInfo(path).Length;
        if (size is < 12 or > BilibiliDownloader.MaximumVideoBytes)
            throw new BilibiliException("bilibili_media_size", "Video rỗng hoặc vượt giới hạn 4 GiB mỗi video.");
        var header = new byte[12];
        await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
            await input.ReadExactlyAsync(header, token);
        if (!header.AsSpan(4, 4).SequenceEqual("ftyp"u8))
            throw new BilibiliException("bilibili_media_signature", "Tệp tải về không phải video MP4 hợp lệ.");
        var info = await probe.ProbeAsync(path, token);
        if (!info.HasVideo || !info.HasAudio || info.DurationSeconds <= 0 || info.Width is not > 0 || info.Height is not > 0)
            throw new BilibiliException("bilibili_media_invalid", "Video tải về thiếu hình, âm thanh hoặc thời lượng hợp lệ.");
        return await BilibiliFiles.HashAsync(path, token);
    }
}

internal sealed class BilibiliDownloader(
    IBilibiliRuntime runtime, IBilibiliProcessRunner runner, IBilibiliMediaVerifier verifier, string ffmpegPath)
{
    public const long MaximumVideoBytes = 4L * 1024 * 1024 * 1024;
    internal const int MaximumEntries = 20000;
    internal static readonly string[] BaseArguments = [
        "--ignore-config", "--no-plugin-dirs", "--no-cache-dir", "--no-colors", "--encoding", "utf-8",
        "--socket-timeout", "20", "--retries", "2", "--extractor-retries", "2", "--fragment-retries", "2",
        "--sleep-requests", "1.5",
        "--use-extractors", "BiliBili,BilibiliSpaceVideo,BilibiliCollectionList", "--no-js-runtimes"
    ];

    public async Task<bool> ScanAsync(string source, Action<BilibiliEntry> found, CancellationToken token)
    {
        source = BilibiliUrl.Normalize(source);
        if (BilibiliUrl.IsShort(source)) source = await BilibiliRuntime.ResolveShortUrlAsync(source, token);
        var executable = await runtime.RequireAsync(token);
        var sources = new Queue<string>();
        var scanned = new HashSet<string>(StringComparer.Ordinal);
        var videoUrls = new HashSet<string>(StringComparer.Ordinal);
        sources.Enqueue(source);
        var complete = true;
        while (sources.TryDequeue(out var current))
        {
            token.ThrowIfCancellationRequested();
            if (!scanned.Add(current)) continue;
            if (scanned.Count > 1000)
                throw new BilibiliException("bilibili_scan_limit", "Kênh có quá nhiều bộ sưu tập trong một lượt quét. Danh sách chưa đầy đủ.");
            var diagnostics = new BilibiliDiagnostics();
            var arguments = BaseArguments.Concat(new[] {
                "--flat-playlist", "--lazy-playlist", "--dump-json", "--skip-download", "--yes-playlist",
                "--abort-on-error", "--", current }).ToArray();
            var exit = await runner.RunAsync(executable, arguments, Path.GetDirectoryName(executable)!, line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var url = GetString(root, "url");
                string? normalized = null;
                foreach (var candidate in new[] { url, GetString(root, "webpage_url") })
                {
                    try { normalized = BilibiliUrl.Normalize(candidate, allowCollection: true); break; }
                    catch (BilibiliException) { }
                }
                if (normalized is null) { complete = false; return; }
                if (!BilibiliUrl.IsVideo(normalized))
                {
                    if (normalized.Contains("/lists/", StringComparison.Ordinal)) sources.Enqueue(normalized);
                    else complete = false;
                    return;
                }
                if (!videoUrls.Add(normalized)) return;
                if (videoUrls.Count > MaximumEntries)
                    throw new BilibiliException("bilibili_scan_limit", "Đã đạt giới hạn 20.000 video trong một lượt quét. Danh sách chưa đầy đủ.");
                var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant()[..32];
                found(new BilibiliEntry(id, normalized, Limit(GetString(root, "title") ?? "Video Bilibili", 500)!,
                    BilibiliUrl.Thumbnail(GetString(root, "thumbnail")), Duration(root), Limit(GetString(root, "uploader"), 150)));
            }, diagnostics.Accept, TimeSpan.FromMinutes(45), token);
            if (exit != 0) throw diagnostics.ToException();
            if (diagnostics.HasError) complete = false;
        }
        return complete;
    }

    public async Task<(string FileName, string Hash)> DownloadAsync(BilibiliDownloadJob job, string directory,
        Action<BilibiliDownloadJob> progress, CancellationToken token)
    {
        var url = BilibiliUrl.Normalize(job.Video.Url);
        if (!BilibiliUrl.IsVideo(url)) throw new BilibiliException("bilibili_invalid_video", "Mục đã chọn không phải video.");
        var executable = await runtime.RequireAsync(token);
        await verifier.RequireReadyAsync(token);
        directory = BilibiliFiles.RequireLocalDirectory(directory);
        BilibiliFiles.RequireDiskSpace(directory, 256L * 1024 * 1024);
        var stage = BilibiliFiles.RequireLocalDirectory(Path.Combine(directory, ".videomaker-bilibili", job.Id));
        Directory.CreateDirectory(stage);
        var output = Path.Combine(stage, "video.mp4");
        var diagnostics = new BilibiliDiagnostics();
        var arguments = BuildDownloadArguments(url, job.Quality, stage, ResolveFfmpeg(ffmpegPath));
        var exit = await runner.RunAsync(executable, arguments, stage, line =>
        {
            if (line.StartsWith("VM_PROGRESS:", StringComparison.Ordinal))
            {
                using var data = JsonDocument.Parse(line[12..]);
                var value = data.RootElement;
                var bytes = Number(value, "downloaded_bytes") ?? 0;
                if (bytes > MaximumVideoBytes)
                    throw new BilibiliException("bilibili_media_size", "Video vượt giới hạn 4 GiB.");
                BilibiliFiles.RequireDiskSpace(directory, 64L * 1024 * 1024);
                var total = Number(value, "total_bytes") ?? Number(value, "total_bytes_estimate");
                var percent = total is > 0 ? Math.Clamp(bytes * 100 / total.Value, 0, 99) : 0;
                progress(job with { Status = "Downloading", Percent = percent, DownloadedBytes = (long)bytes,
                    BytesPerSecond = Number(value, "speed"), Message = "Đang tải hình và âm thanh…" });
            }
            else if (line.StartsWith("VM_MERGE", StringComparison.Ordinal))
                progress(job with { Status = "Processing", Percent = 99, Message = "Đang ghép hình và âm thanh…" });
        }, diagnostics.Accept, TimeSpan.FromHours(4), token);
        if (exit != 0 || diagnostics.HasError) throw diagnostics.ToException();
        token.ThrowIfCancellationRequested();
        progress(job with { Status = "Verifying", Percent = 99, Message = "Đang kiểm tra video…" });
        var hash = await verifier.VerifyAsync(output, token);
        token.ThrowIfCancellationRequested();
        var name = BilibiliFiles.SafeName(job.Video.Title, job.Id);
        var destination = Path.Combine(directory, name);
        if (File.Exists(destination))
        {
            if ((await BilibiliFiles.HashAsync(destination, token)).Equals(hash, StringComparison.Ordinal))
            {
                BilibiliFiles.CleanStaging(directory, job.Id);
                return (name, hash);
            }
            name = BilibiliFiles.SafeName(job.Video.Title, Guid.NewGuid().ToString("N"));
            destination = Path.Combine(directory, name);
        }
        // Same volume, atomic promotion; never overwrite a user's existing file.
        BilibiliFiles.RequireLocalDirectory(directory);
        File.Move(output, destination, overwrite: false);
        BilibiliFiles.CleanStaging(directory, job.Id);
        return (name, hash);
    }

    private static string ResolveFfmpeg(string path)
    {
        if (Path.IsPathFullyQualified(path)) return path;
        // Development preflight may resolve ffmpeg from PATH. Pass its absolute path to the isolated child.
        var fileName = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? path : path + ".exe";
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(folder.Trim('"'), fileName);
            if (Path.IsPathFullyQualified(candidate) && File.Exists(candidate)) return candidate;
        }
        return path;
    }

    internal static string[] BuildDownloadArguments(string url, string quality, string stage, string ffmpegPath)
    {
        var height = quality switch { "best" => "", "1080" => "[height<=1080]", "720" => "[height<=720]",
            "480" => "[height<=480]", _ => throw new BilibiliException("bilibili_quality_invalid", "Chất lượng tải không hợp lệ.") };
        return BaseArguments.Concat(new[] { "--no-playlist", "--no-simulate", "--newline", "--progress", "--progress-delta", "1",
            "--progress-template", "download:VM_PROGRESS:%(progress)j", "--progress-template", "postprocess:VM_MERGE",
            "--print", "after_move:VM_DONE", "--ffmpeg-location", ffmpegPath,
            "--format", $"bv[ext=mp4]{height}+ba[ext=m4a]/b[ext=mp4][acodec!=none]{height}",
            "--merge-output-format", "mp4", "--remux-video", "mp4", "--max-filesize", "4G",
            "--concurrent-fragments", "1", "--continue", "--no-overwrites", "--windows-filenames",
            "--paths", stage, "--output", "video.%(ext)s", "--", url }).ToArray();
    }

    private static string? GetString(JsonElement value, string name) => value.TryGetProperty(name, out var item)
        && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    private static double? Number(JsonElement value, string name) => value.TryGetProperty(name, out var item)
        && item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
    private static string? Limit(string? value, int length) => value is null ? null : value[..Math.Min(value.Length, length)];
    private static double? Duration(JsonElement value)
    {
        var number = Number(value, "duration");
        if (number is >= 0) return number;
        var text = GetString(value, "duration");
        if (text is null) return null;
        var parts = text.Split(':');
        double result = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) || n < 0) return null;
            result = result * 60 + n;
        }
        return double.IsFinite(result) ? result : null;
    }
}

internal sealed class BilibiliDiagnostics
{
    public bool HasError { get; private set; }
    private bool _blocked;
    private bool _login;
    private bool _format;
    private bool _tooLarge;
    public void Accept(string line)
    {
        HasError |= line.Contains("ERROR:", StringComparison.OrdinalIgnoreCase);
        _blocked |= line.Contains("412") || line.Contains("403") || line.Contains("429") || line.Contains("352")
            || line.Contains("blocked", StringComparison.OrdinalIgnoreCase);
        _login |= line.Contains("login", StringComparison.OrdinalIgnoreCase) || line.Contains("premium", StringComparison.OrdinalIgnoreCase);
        _format |= line.Contains("Requested format", StringComparison.OrdinalIgnoreCase);
        _tooLarge |= line.Contains("larger than max-filesize", StringComparison.OrdinalIgnoreCase);
        HasError |= _tooLarge;
        // Deliberately do not retain raw stderr: it may contain signed CDN URLs or local paths.
    }
    public BilibiliException ToException() => _tooLarge
        ? new("bilibili_media_size", "Video vượt giới hạn 4 GiB. Hãy chọn chất lượng thấp hơn.")
        : _blocked
        ? new("bilibili_access_blocked", "Bilibili đang hạn chế truy cập từ mạng này. Hãy chờ rồi thử lại; danh sách quét có thể chưa đầy đủ.")
        : _login ? new("bilibili_login_required", "Video hoặc chất lượng này yêu cầu đăng nhập/quyền truy cập Bilibili. Hiện hỗ trợ video công khai không cần đăng nhập.")
        : _format ? new("bilibili_format_unavailable", "Video chưa có định dạng hình và âm thanh phù hợp. Hãy thử chất lượng khác.")
        : new("bilibili_request_failed", "Không lấy được dữ liệu Bilibili. Video có thể không còn khả dụng hoặc kết nối bị gián đoạn. Hãy thử lại.");
}
