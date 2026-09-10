using TOOL_LOCAL.Bilibili;
using TOOL_LOCAL.Media;

if (args.Contains("--ui")) { await BrowserSmoke.RunAsync(); return; }

var runtime = new BilibiliRuntime();
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
await runtime.InstallAsync(timeout.Token);
Console.WriteLine("Runtime verified: " + await runtime.IsReadyAsync(timeout.Token));
var executable = await runtime.RequireAsync(timeout.Token);
var runner = new BilibiliProcessRunner();
var exit = await runner.RunAsync(executable, ["--ignore-config", "--no-plugin-dirs", "--version"], Path.GetDirectoryName(executable)!,
    line => Console.WriteLine("Version: " + line), _ => { }, TimeSpan.FromSeconds(30), timeout.Token);
Console.WriteLine("Version exit: " + exit);
var toolRoot = Path.GetFullPath("TOOL-LOCAL/bin/Release/net10.0-windows/win-x64/tools/ffmpeg");
var paths = new MediaToolPaths(Path.Combine(toolRoot, "ffmpeg.exe"), Path.Combine(toolRoot, "ffprobe.exe"), toolRoot);
var processes = new ExternalProcessRunner();
var preflight = new MediaToolPreflightService(paths, processes, TimeProvider.System);
var downloader = new BilibiliDownloader(runtime, runner, new BilibiliMediaVerifier(preflight, new FfprobeService(paths.FfprobePath, processes)), paths.FfmpegPath);
var entries = new List<BilibiliEntry>();
if (args.Contains("--channel"))
{
    var done = await downloader.ScanAsync("https://space.bilibili.com/3985676/video", entries.Add, timeout.Token);
    Console.WriteLine($"Full channel: complete={done}; entries={entries.Count}");
    return;
}
try
{
    var complete = await downloader.ScanAsync("https://www.bilibili.com/video/BV13x41117TL", entries.Add, timeout.Token);
    Console.WriteLine($"Video scan: complete={complete}; entries={entries.Count}");
    if (entries.Count > 0)
    {
        var folder = Path.GetFullPath(".tmp/bilibili-smoke/download");
        var result = await downloader.DownloadAsync(new(Guid.NewGuid().ToString("N"), entries[0], "480", "Queued", 0), folder,
            job => Console.WriteLine($"Download: {job.Status} {job.Percent:0}%"), timeout.Token);
        Console.WriteLine("Download verified: " + result.Hash);
    }
}
catch (BilibiliException error) { Console.WriteLine("Video smoke: " + error.Code + " - " + error.Message); }
try
{
    var diagnostic = new BilibiliDiagnostics();
    var count = 0;
    var args2 = BilibiliDownloader.BaseArguments.Concat(new[] { "--flat-playlist", "--dump-json", "--skip-download", "--playlist-end", "2", "--abort-on-error", "--", "https://space.bilibili.com/3985676/video" }).ToArray();
    var code = await runner.RunAsync(executable, args2, Path.GetDirectoryName(executable)!, _ => count++, diagnostic.Accept, TimeSpan.FromSeconds(45), timeout.Token);
    Console.WriteLine($"Channel smoke (2 entries): exit={code}; count={count}");
    if (code != 0) Console.WriteLine(diagnostic.ToException().Message);
}
catch (BilibiliException error) { Console.WriteLine("Channel smoke: " + error.Code); }
