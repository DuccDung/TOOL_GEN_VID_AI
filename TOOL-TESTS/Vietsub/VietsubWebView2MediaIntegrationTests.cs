using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using TOOL_LOCAL.Media;
using TOOL_LOCAL.Vietsub;
using TOOL_LOCAL.Vietsub.Domain;
using TOOL_LOCAL.Vietsub.Media;
using TOOL_LOCAL.Vietsub.Playback;
using TOOL_LOCAL.Vietsub.Storage;

namespace TOOL_TESTS.Vietsub;

public sealed class VietsubWebView2MediaIntegrationTests
{
    [Fact]
    public async Task WebView2_decodes_real_artifacts_through_project_bridge_and_playback_service()
    {
        var testRoot = Path.Combine(
            Path.GetTempPath(),
            "VideoMaker-Vietsub-WebView2-Tests",
            Guid.NewGuid().ToString("N"));
        var webRoot = Path.Combine(testRoot, "web");
        var profileRoot = Path.Combine(testRoot, "profile");
        var workspaceRoot = Path.Combine(testRoot, "workspace");
        Directory.CreateDirectory(webRoot);
        Directory.CreateDirectory(workspaceRoot);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            var ffmpegPath = FindBundledTool("ffmpeg.exe");
            var ffprobePath = FindBundledTool("ffprobe.exe");
            var runner = new ExternalProcessRunner();
            var sourcePath = Path.Combine(testRoot, "source-with-audio.mp4");
            var generation = await runner.RunAsync(
                ffmpegPath,
                [
                    "-hide_banner", "-loglevel", "error",
                    "-f", "lavfi", "-i", "color=c=royalblue:s=160x90:r=12:d=1.2",
                    "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100:duration=1.2",
                    "-map", "0:v:0", "-map", "1:a:0",
                    "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-shortest", "-movflags", "+faststart", "-y", sourcePath
                ],
                TimeSpan.FromSeconds(20),
                timeout.Token);
            Assert.True(generation.ExitCode == 0 && File.Exists(sourcePath), generation.StandardError);

            var paths = new VietsubAppPaths(workspaceRoot);
            var store = new VietsubProjectStore(paths, new VietsubSubtitleStore(paths));
            var organizationId = Guid.NewGuid();
            const string userId = "webview-media-owner";
            var manifest = await store.CreateAsync(
                organizationId,
                userId,
                "WebView2 full path",
                cancellationToken: timeout.Token);
            var preflight = new ReadyMediaPreflight();
            var import = new VietsubMediaImportService(
                paths,
                preflight,
                new FfprobeService(ffprobePath, runner));
            manifest.SourceVideo = await import.ImportAsync(
                manifest,
                sourcePath,
                VietsubMediaImportMode.Copy,
                cancellationToken: timeout.Token);
            Assert.True(manifest.SourceVideo.Metadata.HasVideo);
            Assert.True(manifest.SourceVideo.Metadata.HasAudio);
            manifest.ServerSynchronized = true;
            await store.SaveAsync(manifest, timeout.Token);

            var thumbnails = new VietsubTimelineThumbnailService(
                paths,
                import,
                preflight,
                ffmpegPath,
                runner);
            var waveforms = new VietsubTimelineWaveformService(
                paths,
                import,
                preflight,
                ffmpegPath,
                runner);
            var context = new VietsubUserContext(userId, organizationId);
            var bridgeMessages = new ConcurrentQueue<string>();
            using var bridge = new VietsubWebBridge(
                true,
                bridgeMessages.Enqueue,
                store,
                () => context,
                mediaImportService: import,
                mediaPlaybackService: new VietsubMediaPlaybackService(import, thumbnails, waveforms),
                thumbnailService: thumbnails,
                waveformService: waveforms);

            await bridge.TryHandleAsync(JsonSerializer.Serialize(new
            {
                type = "vietsub.project.open",
                requestId = "open-full-webview-path",
                payload = new { projectId = manifest.ProjectId }
            }), timeout.Token);
            await bridge.TryHandleAsync(JsonSerializer.Serialize(new
            {
                type = "vietsub.timeline.thumbnails.request",
                requestId = "request-full-webview-thumbnail",
                payload = new
                {
                    sourceSha256 = manifest.SourceVideo.Sha256,
                    indices = new[] { 0 }
                }
            }), timeout.Token);
            await bridge.TryHandleAsync(JsonSerializer.Serialize(new
            {
                type = "vietsub.timeline.waveform.request",
                requestId = "request-full-webview-waveform",
                payload = new { sourceSha256 = manifest.SourceVideo.Sha256 }
            }), timeout.Token);
            await WaitUntilAsync(
                () => ContainsMessage(bridgeMessages, "vietsub.timeline.thumbnail.ready")
                    && ContainsMessage(bridgeMessages, "vietsub.timeline.waveform.ready"),
                timeout.Token);
            await bridge.TryHandleAsync(
                """{"type":"vietsub.state.get","requestId":"state-full-webview-path","payload":{}}""",
                timeout.Token);
            var mediaState = ReadLatestMediaState(bridgeMessages);
            Assert.NotNull(mediaState.ThumbnailUrl);
            Assert.NotNull(mediaState.WaveformUrl);

            await File.WriteAllTextAsync(
                Path.Combine(webRoot, "index.html"),
                BuildTestPage(mediaState.ThumbnailUrl!, mediaState.WaveformUrl!),
                timeout.Token);
            var browserResult = await RunWebView2CheckOnStaAsync(
                webRoot,
                workspaceRoot,
                profileRoot,
                bridge,
                timeout.Token);

            Assert.Equal(200, browserResult.ThumbnailStatus);
            Assert.Equal(200, browserResult.WaveformStatus);
            Assert.Equal("none", browserResult.ThumbnailErrorCode);
            Assert.Equal("none", browserResult.WaveformErrorCode);
            Assert.True(browserResult.ThumbnailWidth > 0 && browserResult.ThumbnailHeight > 0);
            Assert.True(browserResult.WaveformWidth > 0 && browserResult.WaveformHeight > 0);
            Assert.Equal(1, browserResult.ThumbnailHandlerCalls);
            Assert.Equal(1, browserResult.WaveformHandlerCalls);
            Assert.Equal(1, browserResult.ThumbnailResponseReceivedCalls);
            Assert.Equal(1, browserResult.WaveformResponseReceivedCalls);

            context = new VietsubUserContext(userId, Guid.NewGuid());
            var wrongOrganization = bridge.TryOpenPlaybackRequest(
                new Uri(mediaState.ThumbnailUrl!),
                "GET",
                null);
            Assert.Equal(403, wrongOrganization.StatusCode);
            Assert.Equal("vietsub_media_session_context_mismatch", wrongOrganization.ErrorCode);
            Assert.Equal(0, wrongOrganization.Content.Length);

            context = new VietsubUserContext(userId, organizationId);
            var thumbnailPath = thumbnails.ResolveArtifactPath(
                manifest.ProjectId,
                manifest.SourceVideo.Sha256,
                mediaState.ThumbnailIndex);
            Assert.NotNull(thumbnailPath);
            File.Delete(thumbnailPath);
            var missing = bridge.TryOpenPlaybackRequest(
                new Uri(mediaState.ThumbnailUrl!),
                "GET",
                null);
            Assert.Equal(404, missing.StatusCode);
            Assert.Equal("vietsub_thumbnail_artifact_missing", missing.ErrorCode);
            Assert.Contains(
                "X-Vietsub-Recovery-Action: regenerate-artifact",
                missing.Headers,
                StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(testRoot);
        }
    }

    private static Task<BrowserResult> RunWebView2CheckOnStaAsync(
        string webRoot,
        string workspaceRoot,
        string profileRoot,
        VietsubWebBridge bridge,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<BrowserResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var form = new Form
            {
                ClientSize = new Size(320, 200),
                ShowInTaskbar = false,
                Opacity = 0.01,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-10_000, -10_000)
            };
            using var webView = new WebView2 { Dock = DockStyle.Fill };
            form.Controls.Add(webView);
            var statuses = new ConcurrentDictionary<string, (int Status, string ErrorCode)>();
            var handlerCalls = new ConcurrentDictionary<string, int>();
            var responseReceivedCalls = new ConcurrentDictionary<string, int>();
            var headerFailures = new ConcurrentQueue<string>();
            using var cancellationRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    if (form.IsHandleCreated)
                    {
                        form.BeginInvoke(form.Close);
                    }
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or ObjectDisposedException)
                {
                }
            });

            form.Shown += async (_, _) =>
            {
                try
                {
                    var environment = await CoreWebView2Environment.CreateAsync(
                        userDataFolder: profileRoot);
                    await webView.EnsureCoreWebView2Async(environment);
                    var coreWebView = webView.CoreWebView2;
                    coreWebView.SetVirtualHostNameToFolderMapping(
                        "app.local",
                        webRoot,
                        CoreWebView2HostResourceAccessKind.DenyCors);
                    coreWebView.SetVirtualHostNameToFolderMapping(
                        "media.app.local",
                        workspaceRoot,
                        CoreWebView2HostResourceAccessKind.DenyCors);
                    coreWebView.AddWebResourceRequestedFilter(
                        $"https://{VietsubMediaPlaybackService.HostName}/*",
                        CoreWebView2WebResourceContext.All,
                        CoreWebView2WebResourceRequestSourceKinds.All);
                    coreWebView.WebResourceRequested += (_, eventArgs) =>
                    {
                        var uri = new Uri(eventArgs.Request.Uri);
                        var resourceType = VietsubMediaPlaybackService.ClassifyResource(uri);
                        handlerCalls.AddOrUpdate(resourceType, 1, (_, current) => current + 1);
                        var rangeHeader = TOOL_LOCAL.Form1.ReadVietsubRangeHeader(
                            eventArgs.Request.Headers,
                            resourceType,
                            out var rangeHeaderExceptionType);
                        if (rangeHeaderExceptionType is not null)
                        {
                            headerFailures.Enqueue(rangeHeaderExceptionType);
                        }
                        var response = bridge.TryOpenPlaybackRequest(
                            uri,
                            eventArgs.Request.Method,
                            rangeHeader);
                        statuses[resourceType] = (
                            response.StatusCode,
                            response.ErrorCode ?? "none");
                        eventArgs.Response = TOOL_LOCAL.Form1.CreateVietsubWebResourceResponse(
                            coreWebView.Environment,
                            response,
                            Guid.NewGuid().ToString("N"));
                    };
                    coreWebView.WebResourceResponseReceived += (_, eventArgs) =>
                    {
                        var uri = new Uri(eventArgs.Request.Uri);
                        var resourceType = VietsubMediaPlaybackService.ClassifyResource(uri);
                        if (resourceType == VietsubPlaybackResourceTypes.Unknown)
                        {
                            return;
                        }
                        var errorCode = TOOL_LOCAL.Form1.ReadVietsubResponseHeader(
                            eventArgs.Response.Headers,
                            "X-Vietsub-Error-Code");
                        errorCode = string.IsNullOrWhiteSpace(errorCode) ? "none" : errorCode;
                        statuses[resourceType] = (eventArgs.Response.StatusCode, errorCode);
                        responseReceivedCalls.AddOrUpdate(resourceType, 1, (_, current) => current + 1);
                    };
                    coreWebView.WebMessageReceived += (_, eventArgs) =>
                    {
                        var payload = eventArgs.WebMessageAsJson;
                        using var document = JsonDocument.Parse(payload);
                        var root = document.RootElement;
                        if (!string.Equals(root.GetProperty("result").GetString(), "PASS", StringComparison.Ordinal))
                        {
                            completion.TrySetException(new InvalidOperationException(payload));
                        }
                        else
                        {
                            if (headerFailures.TryPeek(out var headerFailure))
                            {
                                completion.TrySetException(new InvalidOperationException(
                                    $"Đọc request header ảnh phát sinh {headerFailure}."));
                                form.BeginInvoke(form.Close);
                                return;
                            }
                            var thumbnailStatus = statuses[VietsubPlaybackResourceTypes.Thumbnail];
                            var waveformStatus = statuses[VietsubPlaybackResourceTypes.Waveform];
                            completion.TrySetResult(new BrowserResult(
                                thumbnailStatus.Status,
                                thumbnailStatus.ErrorCode,
                                root.GetProperty("thumbnailWidth").GetInt32(),
                                root.GetProperty("thumbnailHeight").GetInt32(),
                                waveformStatus.Status,
                                waveformStatus.ErrorCode,
                                root.GetProperty("waveformWidth").GetInt32(),
                                root.GetProperty("waveformHeight").GetInt32(),
                                handlerCalls.GetValueOrDefault(VietsubPlaybackResourceTypes.Thumbnail),
                                handlerCalls.GetValueOrDefault(VietsubPlaybackResourceTypes.Waveform),
                                responseReceivedCalls.GetValueOrDefault(VietsubPlaybackResourceTypes.Thumbnail),
                                responseReceivedCalls.GetValueOrDefault(VietsubPlaybackResourceTypes.Waveform)));
                        }
                        form.BeginInvoke(form.Close);
                    };
                    coreWebView.Navigate("https://app.local/index.html");
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                    form.Close();
                }
            };
            form.FormClosed += (_, _) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetException(new TimeoutException(
                        "WebView2 không hoàn tất full-path media test trong 60 giây."));
                }
            };

            Application.Run(form);
        })
        {
            IsBackground = true,
            Name = "VietsubWebView2FullMediaIntegrationTest"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static MediaState ReadLatestMediaState(IEnumerable<string> messages)
    {
        foreach (var json in messages.Reverse())
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type)
                || type.GetString() != "vietsub.state"
                || !root.TryGetProperty("payload", out var payload)
                || !payload.TryGetProperty("selectedProject", out var project)
                || project.ValueKind != JsonValueKind.Object
                || !project.TryGetProperty("sourceVideo", out var media)
                || media.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var thumbnail = media.GetProperty("timelineThumbnails").EnumerateArray().First();
            return new MediaState(
                thumbnail.GetProperty("url").GetString(),
                thumbnail.GetProperty("index").GetInt32(),
                media.GetProperty("waveformUrl").GetString());
        }
        throw new InvalidOperationException("Bridge không trả state media thật cho trang test.");
    }

    private static bool ContainsMessage(IEnumerable<string> messages, string expectedType) =>
        messages.Any(json =>
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("type", out var type)
                && type.GetString() == expectedType;
        });

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(25, cancellationToken);
        }
    }

    private static string BuildTestPage(string thumbnailUrl, string waveformUrl) => $$"""
        <!doctype html>
        <html>
        <head>
          <meta charset="utf-8">
          <meta http-equiv="Content-Security-Policy" content="default-src 'none'; img-src https://vietsub-media.app.local; script-src 'unsafe-inline'">
        </head>
        <body>
          <img id="thumbnail" crossorigin="anonymous" referrerpolicy="no-referrer">
          <img id="waveform" crossorigin="anonymous" referrerpolicy="no-referrer">
          <script>
            const thumbnail = document.getElementById('thumbnail');
            const waveform = document.getElementById('waveform');
            const loaded = new Set();
            const complete = (name) => {
              loaded.add(name);
              if (loaded.size !== 2) return;
              chrome.webview.postMessage({
                result: 'PASS',
                thumbnailWidth: thumbnail.naturalWidth,
                thumbnailHeight: thumbnail.naturalHeight,
                waveformWidth: waveform.naturalWidth,
                waveformHeight: waveform.naturalHeight
              });
            };
            thumbnail.addEventListener('load', () => complete('thumbnail'));
            waveform.addEventListener('load', () => complete('waveform'));
            thumbnail.addEventListener('error', () => chrome.webview.postMessage({ result: 'FAIL', resource: 'thumbnail' }));
            waveform.addEventListener('error', () => chrome.webview.postMessage({ result: 'FAIL', resource: 'waveform' }));
            thumbnail.src = {{JsonSerializer.Serialize(thumbnailUrl)}};
            waveform.src = {{JsonSerializer.Serialize(waveformUrl)}};
          </script>
        </body>
        </html>
        """;

    private static string FindBundledTool(string fileName)
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            for (var depth = 0; directory is not null && depth < 12; depth++, directory = directory.Parent)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "third_party",
                    "ffmpeg",
                    "win-x64",
                    fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        throw new FileNotFoundException($"Không tìm thấy {fileName} trong bundle FFmpeg kiểm thử.");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class ReadyMediaPreflight : IMediaToolPreflightService
    {
        private static readonly MediaToolStatusSummary Ready = new(
            true,
            null,
            "ready",
            "ffmpeg integration test",
            "ffprobe integration test",
            DateTime.UtcNow);

        public Task<MediaToolStatusSummary> GetStatusAsync(bool force, CancellationToken cancellationToken) =>
            Task.FromResult(Ready);

        public Task<MediaToolStatusSummary> RequireReadyAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Ready);
    }

    private sealed record MediaState(string? ThumbnailUrl, int ThumbnailIndex, string? WaveformUrl);

    private sealed record BrowserResult(
        int ThumbnailStatus,
        string ThumbnailErrorCode,
        int ThumbnailWidth,
        int ThumbnailHeight,
        int WaveformStatus,
        string WaveformErrorCode,
        int WaveformWidth,
        int WaveformHeight,
        int ThumbnailHandlerCalls,
        int WaveformHandlerCalls,
        int ThumbnailResponseReceivedCalls,
        int WaveformResponseReceivedCalls);
}
