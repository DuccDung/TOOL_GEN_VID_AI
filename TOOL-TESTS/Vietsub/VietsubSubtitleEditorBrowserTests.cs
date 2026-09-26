using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TOOL_TESTS.Vietsub;

[Collection(NativeWindowsCollection.Name)]
public sealed class VietsubSubtitleEditorBrowserTests
{
    [Fact]
    public async Task WebView2_subtitle_editor_preserves_scroll_focus_and_layout()
    {
        var root = Path.Combine(Path.GetTempPath(), "VietsubSubtitleEditorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        try
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "TOOL_GEN_POST_VIDEO.slnx")))
                repository = repository.Parent;
            Assert.NotNull(repository);
            var output = Path.Combine(root, "web");
            var start = new ProcessStartInfo("node")
            {
                WorkingDirectory = Path.Combine(repository.FullName, "TOOL-LOCAL", "Web"),
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "node_modules/vite/bin/vite.js", "build", "--config",
                         "tests/subtitle-editor-browser/vite.config.mjs", "--outDir", output }) start.ArgumentList.Add(argument);
            using (var process = Process.Start(start)!)
            {
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                try { await process.WaitForExitAsync(timeout.Token); }
                catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
                Assert.True(process.ExitCode == 0, (await stdout) + (await stderr));
            }

            var completion = new TaskCompletionSource<List<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var screenshots = Environment.GetEnvironmentVariable("VIDEOMAKER_SUBTITLE_EDITOR_ARTIFACTS");
            var thread = new Thread(() =>
            {
                using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                    Location = new Point(-10_000, -10_000), ClientSize = new Size(1500, 620) };
                using var webView = new WebView2 { Dock = DockStyle.Fill };
                form.Controls.Add(webView);
                using var registration = timeout.Token.Register(() =>
                {
                    completion.TrySetException(new TimeoutException("Subtitle editor probe did not complete."));
                    if (form.IsHandleCreated) form.BeginInvoke(form.Close);
                });
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(root, "profile"));
                        await webView.EnsureCoreWebView2Async(environment);
                        var core = webView.CoreWebView2;
                        core.SetVirtualHostNameToFolderMapping("app.local", output, CoreWebView2HostResourceAccessKind.DenyCors);
                        core.NavigationCompleted += async (_, args) =>
                        {
                            try
                            {
                                Assert.True(args.IsSuccess);
                                await core.CallDevToolsProtocolMethodAsync("Performance.enable", "{}");
                                var results = new List<string>();
                                foreach (var zoom in new[] { 1d, 1.25, 1.5, 2d })
                                {
                                    webView.ZoomFactor = zoom;
                                    var before = await core.CallDevToolsProtocolMethodAsync("Performance.getMetrics", "{}");
                                    var evaluation = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                                        JsonSerializer.Serialize(new { expression = "window.run()", awaitPromise = true, returnByValue = true }));
                                    using var parsed = JsonDocument.Parse(evaluation);
                                    results.Add(parsed.RootElement.GetProperty("result").GetProperty("value").GetRawText());
                                    if (!string.IsNullOrWhiteSpace(screenshots))
                                    {
                                        Directory.CreateDirectory(screenshots);
                                        var after = await core.CallDevToolsProtocolMethodAsync("Performance.getMetrics", "{}");
                                        await File.WriteAllTextAsync(Path.Combine(screenshots, $"metrics-{(int)(zoom * 100)}.json"), JsonSerializer.Serialize(new { before, after, result = results[^1] }));
                                        await using var image = File.Create(Path.Combine(screenshots, $"editor-{(int)(zoom * 100)}.png"));
                                        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                                    }
                                    var notices = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                                        JsonSerializer.Serialize(new { expression = "window.runNotices()", awaitPromise = true, returnByValue = true }));
                                    using var noticeResult = JsonDocument.Parse(notices);
                                    results.Add(noticeResult.RootElement.GetProperty("result").GetProperty("value").GetRawText());
                                    if (!string.IsNullOrWhiteSpace(screenshots))
                                    {
                                        await using var image = File.Create(Path.Combine(screenshots, $"notices-{(int)(zoom * 100)}.png"));
                                        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                                    }
                                    Assert.Equal("true", await core.ExecuteScriptAsync("document.querySelector('.vietsub-editor-recovery .vietsub-notice-close').focus(); !!document.querySelector('.vietsub-editor-recovery')"));
                                    await core.CallDevToolsProtocolMethodAsync("Emulation.setFocusEmulationEnabled", "{\"enabled\":true}");
                                    foreach (var keyType in new[] { "keyDown", "keyUp" })
                                        await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent",
                                            JsonSerializer.Serialize(new { type = keyType, key = "Enter", code = "Enter", windowsVirtualKeyCode = 13,
                                                text = keyType == "keyDown" ? "\r" : "", unmodifiedText = keyType == "keyDown" ? "\r" : "" }));
                                    var keyboard = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                                        JsonSerializer.Serialize(new { expression = "new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve({errors: document.querySelector('.vietsub-editor-recovery') ? ['Enter did not dismiss the focused notice'] : []}))))", awaitPromise = true, returnByValue = true }));
                                    using var keyboardResult = JsonDocument.Parse(keyboard);
                                    results.Add(keyboardResult.RootElement.GetProperty("result").GetProperty("value").GetRawText());
                                    var cloud = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                                        JsonSerializer.Serialize(new { expression = "window.runCloud()", awaitPromise = true, returnByValue = true }));
                                    using var cloudResult = JsonDocument.Parse(cloud);
                                    results.Add(cloudResult.RootElement.GetProperty("result").GetProperty("value").GetRawText());
                                    if (!string.IsNullOrWhiteSpace(screenshots))
                                    {
                                        await using var cloudImage = File.Create(Path.Combine(screenshots, $"cloud-{(int)(zoom * 100)}.png"));
                                        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, cloudImage);
                                    }
                                }
                                completion.TrySetResult(results);
                            }
                            catch (Exception exception) { completion.TrySetException(exception); }
                            finally { form.Close(); }
                        };
                        core.Navigate("https://app.local/index.html");
                    }
                    catch (Exception exception) { completion.TrySetException(exception); form.Close(); }
                };
                Application.Run(form);
            }) { IsBackground = true };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            var results = await completion.Task.WaitAsync(timeout.Token);
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
            Assert.All(results, json =>
            {
                using var result = JsonDocument.Parse(json);
                Assert.True(result.RootElement.GetProperty("errors").GetArrayLength() == 0, json);
            });
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
