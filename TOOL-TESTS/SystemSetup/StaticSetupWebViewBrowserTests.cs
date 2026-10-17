using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using TOOL_TESTS.Updates;

namespace TOOL_TESTS.SystemSetup;

[Collection(NativeWindowsCollection.Name)]
public sealed class StaticSetupWebViewBrowserTests
{
    [Fact]
    [Trait("Category", "StaticSetupBrowser")]
    public async Task Production_setup_modal_keeps_controls_and_keyboard_focus_accessible_in_real_webview()
    {
        var root = Path.Combine(Path.GetTempPath(), "vm-static-browser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        Form? activeForm = null;
        Process? browser = null;
        Thread? thread = null;
        try
        {
            var repository = new DirectoryInfo(AppContext.BaseDirectory);
            while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "TOOL_GEN_POST_VIDEO.slnx")))
                repository = repository.Parent;
            Assert.NotNull(repository);
            var webRoot = Path.Combine(root, "web");
            var start = new ProcessStartInfo("node")
            {
                WorkingDirectory = Path.Combine(repository.FullName, "TOOL-LOCAL", "Web"),
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var argument in new[] { "node_modules/vite/bin/vite.js", "build", "--config", "tests/static-setup-browser/vite.config.mjs", "--outDir", webRoot })
                start.ArgumentList.Add(argument);
            using (var build = Process.Start(start)!)
            {
                var stdout = build.StandardOutput.ReadToEndAsync();
                var stderr = build.StandardError.ReadToEndAsync();
                try { await build.WaitForExitAsync(timeout.Token); }
                catch { if (!build.HasExited) build.Kill(entireProcessTree: true); throw; }
                Assert.True(build.ExitCode == 0, (await stdout) + (await stderr));
            }
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var artifacts = Environment.GetEnvironmentVariable("VIDEOMAKER_STATIC_SETUP_ARTIFACTS");
            thread = new Thread(() =>
            {
                using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                    Location = new Point(-10_000, -10_000), ClientSize = new Size(1280, 720) };
                activeForm = form;
                using var webView = new WebView2 { Dock = DockStyle.Fill };
                form.Controls.Add(webView);
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(root, "profile"));
                        await webView.EnsureCoreWebView2Async(environment);
                        var core = webView.CoreWebView2;
                        browser = Process.GetProcessById((int)core.BrowserProcessId);
                        core.SetVirtualHostNameToFolderMapping("app.local", webRoot, CoreWebView2HostResourceAccessKind.DenyCors);
                        var navigated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        core.NavigationCompleted += (_, args) =>
                        {
                            if (args.IsSuccess) navigated.TrySetResult();
                            else navigated.TrySetException(new InvalidOperationException("Setup fixture navigation failed."));
                        };
                        core.Navigate("https://app.local/index.html");
                        await navigated.Task.WaitAsync(timeout.Token);
                        while (await core.ExecuteScriptAsync("typeof window.runSetupCase === 'function' && !!document.querySelector('.startup-setup-dialog')") != "true")
                            await Task.Delay(50, timeout.Token);
                        await core.CallDevToolsProtocolMethodAsync("Emulation.setFocusEmulationEnabled", "{\"enabled\":true}");
                        var results = new List<object>();
                        foreach (var size in new[] { new Size(1280, 720), new Size(640, 480), new Size(390, 844) })
                        foreach (var zoom in size.Width == 1280 ? new[] { 1d, 1.25, 1.5, 2d } : new[] { 1d })
                        {
                            form.ClientSize = size;
                            webView.ZoomFactor = zoom;
                            foreach (var scenario in new[] { "missing", "error", "warning" })
                            {
                                var raw = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new
                                { expression = $"window.runSetupCase({JsonSerializer.Serialize(scenario)})", awaitPromise = true, returnByValue = true }));
                                using var parsed = JsonDocument.Parse(raw);
                                Assert.False(parsed.RootElement.TryGetProperty("exceptionDetails", out _), raw);
                                var result = parsed.RootElement.GetProperty("result").GetProperty("value");
                                results.Add(new { width = size.Width, height = size.Height, zoom, scenario, result = result.Clone() });
                                if (!string.IsNullOrWhiteSpace(artifacts))
                                {
                                    Directory.CreateDirectory(artifacts);
                                    await File.WriteAllTextAsync(Path.Combine(artifacts, "browser-layout.json"), JsonSerializer.Serialize(results));
                                    await using var png = File.Create(Path.Combine(artifacts, $"setup-{size.Width}-{(int)(zoom * 100)}-{scenario}.png"));
                                    await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, png);
                                }
                                Assert.Equal(0, result.GetProperty("errors").GetArrayLength());
                                for (var index = 0; index < 8; index++)
                                {
                                    await DispatchKey(core, "Tab", 9, index % 2 == 0 ? 0 : 8);
                                    Assert.Equal("true", await core.ExecuteScriptAsync("!!document.activeElement.closest('.startup-setup-dialog')"));
                                }
                                await DispatchKey(core, "Escape", 27, 0);
                                Assert.Equal("true", await core.ExecuteScriptAsync("!!document.querySelector('.startup-setup-dialog')"));
                            }
                            var ready = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new
                            { expression = "window.runSetupCase('ready')", awaitPromise = true, returnByValue = true }));
                            using var readyResult = JsonDocument.Parse(ready);
                            Assert.Equal(0, readyResult.RootElement.GetProperty("result").GetProperty("value").GetProperty("errors").GetArrayLength());
                        }
                        completion.TrySetResult();
                    }
                    catch (Exception error) { completion.TrySetException(error); }
                    finally { form.Close(); }
                };
                Application.Run(form);
            }) { IsBackground = true, Name = "Static Setup WebView2 Regression" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            await completion.Task.WaitAsync(timeout.Token);
        }
        finally
        {
            if (activeForm is { IsDisposed: false, IsHandleCreated: true })
            { try { activeForm.BeginInvoke(new Action(activeForm.Close)); } catch (InvalidOperationException) { } }
            if (thread is not null) Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
            if (browser is not null)
            {
                if (!browser.HasExited && !browser.WaitForExit(5000)) browser.Kill(entireProcessTree: true);
                browser.Dispose();
            }
            StaticArchiveFixture.DeleteTemp(root);
        }
    }

    private static async Task DispatchKey(CoreWebView2 core, string key, int code, int modifiers)
    {
        foreach (var type in new[] { "keyDown", "keyUp" })
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", JsonSerializer.Serialize(new
            { type, key, code = key, windowsVirtualKeyCode = code, modifiers }));
    }
}
