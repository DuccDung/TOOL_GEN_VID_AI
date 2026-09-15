using System.Text.Json;
using TOOL_LOCAL.Bilibili;
using TOOL_LOCAL.Media;

namespace TOOL_TESTS.Bilibili;

public sealed class BilibiliTests : IDisposable
{
    private const string VideoUrl = "https://www.bilibili.com/video/BV13x41117TL";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "vmb-" + Guid.NewGuid().ToString("N")[..8]);

    public BilibiliTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("https://bilibili.com/video/BV13x41117TL/?spm_id_from=123", VideoUrl)]
    [InlineData("https://www.bilibili.com/video/BV13x41117TL?p=2&tracking=abc", VideoUrl + "?p=2")]
    [InlineData("https://space.bilibili.com/123", "https://space.bilibili.com/123/video")]
    [InlineData("https://space.bilibili.com/123/upload/video?tid=1", "https://space.bilibili.com/123/video")]
    [InlineData("https://b23.tv/Ab123?share=1", "https://b23.tv/Ab123")]
    public void Canonicalizes_supported_links_without_tracking(string input, string expected) => Assert.Equal(expected, BilibiliUrl.Normalize(input));

    [Theory]
    [InlineData("http://www.bilibili.com/video/BV13x41117TL")]
    [InlineData("https://www.bilibili.com.evil.test/video/BV13x41117TL")]
    [InlineData("https://127.0.0.1/video/BV13x41117TL")]
    [InlineData("https://user:secret@www.bilibili.com/video/BV13x41117TL")]
    [InlineData("https://www.bilibili.com:444/video/BV13x41117TL")]
    [InlineData("https://www.bilibili.com/video/BV13x41117TL?p=-1")]
    [InlineData("https://www.bilibili.com/video/BV13x41117TL?p=1&p=2")]
    [InlineData("file:///C:/video.mp4")]
    [InlineData("--exec calc.exe")]
    [InlineData("https://space.bilibili.com/123/favlist")]
    public void Rejects_unsupported_and_unsafe_links(string input) => Assert.Throws<BilibiliException>(() => BilibiliUrl.Normalize(input));

    [Theory]
    [InlineData("https://i0.hdslb.com/bfs/archive/a.jpg", "https://i0.hdslb.com/bfs/archive/a.jpg")]
    [InlineData("http://i1.hdslb.com/bfs/archive/a.jpg?token=x", "https://i1.hdslb.com/bfs/archive/a.jpg")]
    [InlineData("https://i0.hdslb.com.evil.test/a.jpg", null)]
    [InlineData("https://i0.hdslb.com:444/a.jpg", null)]
    [InlineData("http://127.0.0.1/a.jpg", null)]
    public void Only_exposes_allowlisted_thumbnail_hosts(string input, string? expected) => Assert.Equal(expected, BilibiliUrl.Thumbnail(input));

    [Fact]
    public async Task Scan_streams_deduplicates_and_expands_channel_collections()
    {
        var runner = new FakeRunner((arguments, _, output, _) =>
        {
            if (arguments.Last().Contains("/lists/")) output(EntryJson(VideoUrl + "?p=2"));
            else
            {
                output(EntryJson(VideoUrl)); output(EntryJson(VideoUrl));
                output("{\"url\":\"https://space.bilibili.com/123/lists/456?type=season\"}");
            }
            return Task.FromResult(0);
        });
        var entries = new List<BilibiliEntry>();
        Assert.True(await Downloader(runner).ScanAsync("https://space.bilibili.com/123", entries.Add, default));
        Assert.Equal(2, entries.Count);
        Assert.NotEqual(entries[0].Id, entries[1].Id);
        Assert.Equal(2, runner.Calls);
        Assert.All(runner.Arguments, args => { Assert.Contains("--lazy-playlist", args); Assert.DoesNotContain("--playlist-end", args); });
    }

    [Fact]
    public async Task Partial_scan_retains_entries_and_never_claims_complete_after_failure()
    {
        var runner = new FakeRunner((_, _, output, _) => { output(EntryJson(VideoUrl)); throw new BilibiliException("blocked", "Bilibili chặn truy cập."); });
        using var service = Service(runner);
        await Assert.ThrowsAsync<BilibiliException>(() => service.ScanAsync(VideoUrl, default));
        var state = service.Snapshot();
        Assert.Single(state.Entries); Assert.False(state.Scan!.Complete); Assert.Equal("Partial", state.Scan.Status); Assert.Equal("Idle", state.Operation);
    }

    [Fact]
    public async Task Unsupported_entries_mark_scan_incomplete()
    {
        var runner = new FakeRunner((_, _, output, _) => { output("{\"url\":\"https://evil.test/a\"}"); return Task.FromResult(0); });
        Assert.False(await Downloader(runner).ScanAsync(VideoUrl, _ => Assert.Fail("Unexpected video"), default));
    }

    [Fact]
    public async Task Cancellation_releases_operation_and_blocks_a_second_scan_until_finished()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new FakeRunner(async (_, _, output, token) => { output(EntryJson(VideoUrl)); started.SetResult(); await Task.Delay(Timeout.Infinite, token); return 0; });
        using var service = Service(runner);
        var scan = service.ScanAsync(VideoUrl, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAsync<BilibiliException>(() => service.ScanAsync(VideoUrl, default));
        service.Cancel(null);
        await scan.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("Idle", service.Snapshot().Operation);
        Assert.Equal("Cancelled", service.Snapshot().Scan!.Status);
        Assert.Single(service.Snapshot().Entries);
    }

    [Fact]
    public async Task Stale_or_forged_selection_never_starts_downloader()
    {
        var runner = new FakeRunner((_, _, output, _) => { output(EntryJson(VideoUrl)); return Task.FromResult(0); });
        using var service = Service(runner);
        await service.ScanAsync(VideoUrl, default);
        var snapshot = service.Snapshot();
        await Assert.ThrowsAsync<BilibiliException>(() => service.DownloadAsync("old", [snapshot.Entries[0].Id], "best", default));
        await Assert.ThrowsAsync<BilibiliException>(() => service.DownloadAsync(snapshot.Scan!.Id, ["forged"], "best", default));
        Assert.Equal(1, runner.Calls); Assert.Empty(service.Snapshot().Jobs); Assert.Equal("Idle", service.Snapshot().Operation);
    }

    [Fact]
    public async Task Access_is_checked_before_tool_or_network()
    {
        var runner = new FakeRunner((_, _, _, _) => throw new Exception("Must not run"));
        using var service = new BilibiliService(new FakeRuntime(_root), Downloader(runner),
            _ => throw new BilibiliException("license_required", "License required"), _root);
        await Assert.ThrowsAsync<BilibiliException>(() => service.ScanAsync(VideoUrl, default));
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task Download_promotes_only_after_verification_and_never_overwrites_user_file()
    {
        var verified = false;
        var job = Job();
        var target = Path.Combine(_root, BilibiliFiles.SafeName(job.Video.Title, job.Id));
        await File.WriteAllTextAsync(target, "user content");
        var runner = new FakeRunner(async (_, stage, _, token) => { await File.WriteAllTextAsync(Path.Combine(stage, "video.mp4"), "new video", token); return 0; });
        var verifier = new FakeVerifier(async (path, token) => { verified = true; Assert.False(path == target); return await BilibiliFiles.HashAsync(path, token); });
        var downloader = new BilibiliDownloader(new FakeRuntime(_root), runner, verifier, "ffmpeg.exe");
        var result = await downloader.DownloadAsync(job, _root, _ => { }, default);
        Assert.True(verified); Assert.Equal("user content", await File.ReadAllTextAsync(target));
        Assert.Equal("new video", await File.ReadAllTextAsync(Path.Combine(_root, result.FileName)));
    }

    [Fact]
    public async Task Failed_verification_leaves_video_in_staging()
    {
        var runner = new FakeRunner(async (_, stage, _, token) => { await File.WriteAllTextAsync(Path.Combine(stage, "video.mp4"), "bad video", token); return 0; });
        var downloader = new BilibiliDownloader(new FakeRuntime(_root), runner,
            new FakeVerifier((_, _) => throw new BilibiliException("invalid", "invalid")), "ffmpeg.exe");
        await Assert.ThrowsAsync<BilibiliException>(() => downloader.DownloadAsync(Job(), _root, _ => { }, default));
        Assert.Empty(Directory.GetFiles(_root, "*.mp4"));
    }

    [Fact]
    public async Task Failed_job_can_retry_using_same_staging_and_quality()
    {
        var downloads = 0;
        var directories = new List<string>();
        var runner = new FakeRunner(async (args, stage, output, token) =>
        {
            if (args.Contains("--dump-json")) output(EntryJson(VideoUrl));
            else
            {
                directories.Add(stage);
                if (++downloads == 1) return 1;
                await File.WriteAllTextAsync(Path.Combine(stage, "video.mp4"), "video", token);
            }
            return 0;
        });
        using var service = Service(runner);
        await service.ScanAsync(VideoUrl, default);
        var scan = service.Snapshot();
        await service.DownloadAsync(scan.Scan!.Id, [scan.Entries[0].Id], "720", default);
        var failed = Assert.Single(service.Snapshot().Jobs);
        Assert.Equal("Failed", failed.Status);
        await service.RetryAsync(failed.Id, default);
        var completed = Assert.Single(service.Snapshot().Jobs);
        Assert.Equal("Completed", completed.Status); Assert.Equal("720", completed.Quality);
        Assert.Equal(directories[0], directories[1]); Assert.True(File.Exists(Path.Combine(_root, completed.FileName!)));
    }

    [Fact]
    public async Task Cancel_current_job_allows_next_selected_job_to_run()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var runner = new FakeRunner(async (args, stage, output, token) =>
        {
            if (args.Contains("--dump-json")) { output(EntryJson(VideoUrl)); output(EntryJson(VideoUrl + "?p=2")); }
            else if (++count == 1) { started.SetResult(); await Task.Delay(Timeout.Infinite, token); }
            else await File.WriteAllTextAsync(Path.Combine(stage, "video.mp4"), "video", token);
            return 0;
        });
        using var service = Service(runner);
        await service.ScanAsync(VideoUrl, default);
        var scan = service.Snapshot();
        var download = service.DownloadAsync(scan.Scan!.Id, scan.Entries.Select(x => x.Id).ToArray(), "best", default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        service.Cancel(service.Snapshot().Jobs[0].Id);
        await download.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new[] { "Cancelled", "Completed" }, service.Snapshot().Jobs.Select(x => x.Status));
    }

    [Fact]
    public void Download_arguments_disable_plugins_config_and_keep_paths_out_of_shell()
    {
        var args = BilibiliDownloader.BuildDownloadArguments(VideoUrl, "720", _root, "C:\\tools\\ffmpeg.exe");
        Assert.Contains("--ignore-config", args); Assert.Contains("--no-plugin-dirs", args);
        Assert.Contains("--continue", args); Assert.Contains("--no-playlist", args);
        Assert.DoesNotContain("--exec", args); Assert.DoesNotContain("--cookies-from-browser", args);
        Assert.Equal("--", args[^2]); Assert.Equal(VideoUrl, args[^1]);
        Assert.Contains("bv[ext=mp4][height<=720]+ba[ext=m4a]/b[ext=mp4][acodec!=none][height<=720]", args);
    }

    [Theory]
    [InlineData("https://evil.test/yt-dlp.exe")]
    [InlineData("http://github.com/yt-dlp.exe")]
    [InlineData("https://release-assets.githubusercontent.com:8443/a")]
    public void Installer_rejects_untrusted_redirects(string input) => Assert.Throws<BilibiliException>(() => BilibiliRuntime.ValidateDownloadUri(new Uri(input)));

    [Fact]
    public async Task Runtime_does_not_trust_a_present_executable_or_a_ready_marker()
    {
        var directory = Path.Combine(_root, BilibiliRuntime.PinnedVersion);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "yt-dlp.exe"), "untrusted");
        await File.WriteAllTextAsync(Path.Combine(directory, "READY"), "true");
        var runtime = new BilibiliRuntime(_root);
        Assert.False(await runtime.IsReadyAsync(default));
        await Assert.ThrowsAsync<BilibiliException>(() => runtime.RequireAsync(default));
    }

    [Fact]
    public async Task Media_verifier_rejects_html_before_probe()
    {
        var path = Path.Combine(_root, "fake.mp4");
        await File.WriteAllTextAsync(path, "<html>blocked</html>");
        var verifier = new BilibiliMediaVerifier(new FakePreflight(), new FfprobeService("unused", new NoProcessRunner()));
        var error = await Assert.ThrowsAsync<BilibiliException>(() => verifier.VerifyAsync(path, default));
        Assert.Equal("bilibili_media_signature", error.Code);
    }

    [Fact]
    public void Native_and_process_failures_never_expose_paths_or_signed_urls()
    {
        var diagnostics = new BilibiliDiagnostics();
        diagnostics.Accept("ERROR: HTTP Error 403 https://cdn.test/video?secret=abc C:\\Users\\private");
        Assert.DoesNotContain("secret", diagnostics.ToException().Message);
        Assert.DoesNotContain("private", BilibiliService.SafeMessage(new IOException("C:\\Users\\private")));
    }

    [Fact]
    public void Too_large_download_is_reported_even_when_tool_only_prints_a_skip_message()
    {
        var diagnostics = new BilibiliDiagnostics();
        diagnostics.Accept("[download] File is larger than max-filesize (4294967296 bytes). Aborting.");
        Assert.True(diagnostics.HasError);
        Assert.Equal("bilibili_media_size", diagnostics.ToException().Code);
    }

    [Fact]
    public void Cleanup_preserves_files_outside_the_exact_job_staging_folder()
    {
        var jobId = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(_root, ".videomaker-bilibili", jobId);
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "video.mp4.part"), "partial");
        File.WriteAllText(Path.Combine(staging, "user-notes.txt"), "keep");
        File.WriteAllText(Path.Combine(_root, "video.mp4"), "keep final");
        BilibiliFiles.CleanStaging(_root, jobId);
        Assert.False(File.Exists(Path.Combine(staging, "video.mp4.part")));
        Assert.True(File.Exists(Path.Combine(staging, "user-notes.txt")));
        Assert.Equal("keep final", File.ReadAllText(Path.Combine(_root, "video.mp4")));
        BilibiliFiles.CleanStaging(_root, "../");
        Assert.True(File.Exists(Path.Combine(_root, "video.mp4")));
    }

    [Fact]
    public async Task Bridge_rejects_extra_path_fields_and_forged_jobs()
    {
        var runner = new FakeRunner((_, _, _, _) => Task.FromResult(0));
        using var service = Service(runner);
        var messages = new List<string>();
        using var bridge = new BilibiliWebBridge(service, _ => Task.CompletedTask, () => null,
            _ => Assert.Fail("Must not open folder"), messages.Add);
        var request = Guid.NewGuid().ToString();
        Assert.True(await bridge.TryHandleAsync(JsonSerializer.Serialize(new { type = "bilibili.scan", requestId = request,
            payload = new { url = VideoUrl, destinationPath = "C:\\Windows" } }), default));
        Assert.Contains("bilibili.error", messages.Last()); Assert.Equal(0, runner.Calls);
        await bridge.TryHandleAsync(JsonSerializer.Serialize(new { type = "bilibili.folder.open", requestId = request,
            payload = new { jobId = "forged" } }), default);
        Assert.Contains("bilibili_job_missing", messages.Last());
    }

    private static string EntryJson(string url) => JsonSerializer.Serialize(new { url, title = "Video thử nghiệm", duration = 15, thumbnail = "https://i0.hdslb.com/a.jpg", uploader = "Kênh" });
    private static BilibiliDownloadJob Job() => new(Guid.NewGuid().ToString("N"), new("entry", VideoUrl, "CON / Video: 1", null, 15, null), "best", "Queued", 0);
    private BilibiliDownloader Downloader(FakeRunner runner) => new(new FakeRuntime(_root), runner, new FakeVerifier(BilibiliFiles.HashAsync), "ffmpeg.exe");
    private BilibiliService Service(FakeRunner runner) => new(new FakeRuntime(_root), Downloader(runner), _ => Task.CompletedTask, _root);

    private sealed class FakeRuntime(string root) : IBilibiliRuntime
    {
        public string Version => "test";
        public Task<bool> IsReadyAsync(CancellationToken token) => Task.FromResult(true);
        public Task InstallAsync(CancellationToken token) => Task.CompletedTask;
        public Task<string> RequireAsync(CancellationToken token) => Task.FromResult(Path.Combine(root, "yt-dlp.exe"));
    }
    private sealed class FakeVerifier(Func<string, CancellationToken, Task<string>> verify) : IBilibiliMediaVerifier
    {
        public Task RequireReadyAsync(CancellationToken token) => Task.CompletedTask;
        public Task<string> VerifyAsync(string path, CancellationToken token) => verify(path, token);
    }
    private sealed class FakeRunner(Func<IReadOnlyList<string>, string, Action<string>, CancellationToken, Task<int>> action) : IBilibiliProcessRunner
    {
        public int Calls { get; private set; }
        public List<IReadOnlyList<string>> Arguments { get; } = [];
        public Task<int> RunAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory,
            Action<string> output, Action<string> error, TimeSpan timeout, CancellationToken token)
        { Calls++; Arguments.Add(arguments); return action(arguments, workingDirectory, output, token); }
    }
    private sealed class FakePreflight : IMediaToolPreflightService
    {
        public Task<MediaToolStatusSummary> GetStatusAsync(bool force, CancellationToken cancellationToken) => RequireReadyAsync(cancellationToken);
        public Task<MediaToolStatusSummary> RequireReadyAsync(CancellationToken cancellationToken) => Task.FromResult(new MediaToolStatusSummary(true, null, "OK", "1", "1", DateTime.UtcNow));
    }
    private sealed class NoProcessRunner : IExternalProcessRunner
    {
        public Task<ProcessExecutionResult> RunAsync(string executable, IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new Exception("Probe must not be called");
    }
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
