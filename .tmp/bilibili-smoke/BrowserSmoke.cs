using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class BrowserSmoke
{
    public static async Task RunAsync()
    {
        var webRoot = Path.GetFullPath("TOOL-LOCAL/Web/dist");
        var artifacts = Path.GetFullPath(".tmp/bilibili-validation/ui");
        Directory.CreateDirectory(artifacts);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                Location = new Point(-10000, -10000), ClientSize = new Size(1440, 900) };
            using var webView = new WebView2 { Dock = DockStyle.Fill };
            form.Controls.Add(webView);
            form.Shown += async (_, _) =>
            {
                try
                {
                    await webView.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(artifacts, "profile")));
                    var core = webView.CoreWebView2;
                    core.SetVirtualHostNameToFolderMapping("app.local", webRoot, CoreWebView2HostResourceAccessKind.DenyCors);
                    core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                    core.WebResourceRequested += (_, e) => { if (new Uri(e.Request.Uri).Host != "app.local") e.Response = core.Environment.CreateWebResourceResponse(Stream.Null, 403, "Blocked", ""); };
                    core.WebMessageReceived += (_, e) =>
                    {
                        using var message = JsonDocument.Parse(e.TryGetWebMessageAsString());
                        var type = message.RootElement.GetProperty("type").GetString();
                        var id = message.RootElement.GetProperty("requestId").GetString();
                        if (type == "app.ready") core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "dashboard.state", payload = Dashboard() }));
                        if (type == "bilibili.state.get")
                        {
                            core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "bilibili.state", payload = State() }));
                            core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "bilibili.ack", requestId = id }));
                        }
                    };
                    core.NavigationCompleted += async (_, _) =>
                    {
                        try
                        {
                            await Task.Delay(500);
                            await core.ExecuteScriptAsync("document.querySelector('button[aria-label=\"Tải video Bilibili\"]').click()");
                            await Task.Delay(500);
                            foreach (var (width, zoom) in new[] { (1440, 1d), (1024, 1d), (1440, 1.25d) })
                            {
                                form.ClientSize = new Size(width, 900); webView.ZoomFactor = zoom;
                                await Task.Delay(200);
                                var result = await core.ExecuteScriptAsync("JSON.stringify({page: !!document.querySelector('.bili-page'), rows: document.querySelectorAll('.bili-video-row').length, overflow: document.documentElement.scrollWidth > innerWidth + 1})");
                                Console.WriteLine($"UI {width}/{zoom}: {result}");
                                await using var output = File.Create(Path.Combine(artifacts, $"bilibili-{width}-{zoom * 100:0}.png"));
                                await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output);
                            }
                            completion.TrySetResult();
                        }
                        catch (Exception error) { completion.TrySetException(error); }
                        finally { form.Close(); }
                    };
                    core.Navigate("https://app.local/index.html");
                }
                catch (Exception error) { completion.TrySetException(error); form.Close(); }
            };
            Application.Run(form);
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(45));
        thread.Join(TimeSpan.FromSeconds(3));
    }

    private static object Dashboard() => new {
        profile = new { userId = "demo", email = "demo@example.test", displayName = "Demo", accountStatus = "Active", roles = Array.Empty<string>() },
        organizations = Array.Empty<object>(), selectedOrganizationId = "org", projects = Array.Empty<object>(), selectedProject = (object?)null,
        models = Array.Empty<object>(), assetLibrary = (object?)null, sceneFirstFrames = Array.Empty<object>(), generationRunning = false,
        providerStatus = new { openAiReady = false, klingReady = false, videoReady = false }, mediaTools = new { ready = true, message = "OK", checkedAtUtc = "" },
        features = new { vietsubEnabled = true, speechSynchronizationEnabled = false, tikTokEnabled = true },
        license = new { hasActiveLicense = true, currentDeviceActivated = true, maxActivatedDevices = 1, activeDeviceCount = 1,
            offlineGraceHours = 0, serverTimeUtc = "2026-09-11T00:00:00Z", heartbeatIntervalSeconds = 300, accessState = "Active", leaseExpiresAtUtc = "2027-09-11T00:00:00Z" }
    };
    private static object State()
    {
        var entries = new[] {
            new { id = "v1", title = "Khám phá thành phố Trùng Khánh về đêm", url = "https://www.bilibili.com/video/BV13x41117TL", durationSeconds = 254, uploader = "Hành trình Trung Hoa" },
            new { id = "v2", title = "Một ngày ở ngôi làng cổ bên sông", url = "https://www.bilibili.com/video/BV13x41117TL?p=2", durationSeconds = 387, uploader = "Hành trình Trung Hoa" },
            new { id = "v3", title = "Ẩm thực đường phố và những câu chuyện đời thường", url = "https://www.bilibili.com/video/BV13x41117TL?p=3", durationSeconds = 425, uploader = "Hành trình Trung Hoa" }
        };
        return new { revision = 3, runtimeReady = true, runtimeVersion = "2026.08.19", operation = "Idle", folderLabel = "Bilibili",
            scan = new { id = "scan", status = "Completed", complete = true, count = 3, message = "Đã quét xong 3 video." }, entries,
            jobs = new[] { new { id = "job", video = entries[0], quality = "720", status = "Completed", percent = 100, downloadedBytes = 12000000, message = "Đã tải và kiểm tra video.", fileName = "Bilibili - Khám phá Trùng Khánh.mp4" } } };
    }
}
