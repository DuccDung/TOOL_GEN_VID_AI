using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TOOL_LOCAL.SystemSetup;

// An installed runtime version is insufficient evidence: create a controller in
// a private STA message loop without showing a window or navigating to a site.
internal static class StaticModelWebViewProbe
{
    internal static async Task<DesktopWebViewStatus> CheckAsync(CancellationToken token)
    {
        var inspected = DesktopWebViewRuntime.Inspect();
        if (!inspected.IsReady) return inspected;
        var result = new TaskCompletionSource<DesktopWebViewStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var context = new ApplicationContext();
                using var host = new Form { ShowInTaskbar = false, Width = 16, Height = 16 };
                using var webView = new WebView2 { Dock = DockStyle.Fill };
                host.Controls.Add(webView);
                _ = host.Handle;
                _ = webView.Handle;
                using var cancelled = token.Register(() =>
                {
                    result.TrySetCanceled(token);
                    try { host.BeginInvoke(new Action(context.ExitThread)); }
                    catch (Exception error) when (error is InvalidOperationException or ObjectDisposedException) { }
                });
                host.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        var userData = Path.Combine(SystemSetupPaths.MetadataRoot, "webview2-probe");
                        SystemSetupPaths.EnsureSafeDirectory(userData);
                        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
                        await webView.EnsureCoreWebView2Async(environment);
                        result.TrySetResult(new("READY", null, "WebView2 đã khởi tạo thành công trên tài khoản Windows này."));
                    }
                    catch (OperationCanceledException) { result.TrySetCanceled(token); }
                    catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
                    {
                        result.TrySetResult(new("CHECK_FAILED", "webview2_initialization_failed",
                            "WebView2 chưa khởi tạo được. Hãy sửa hoặc cài lại Microsoft Edge WebView2 Runtime."));
                    }
                    finally { context.ExitThread(); }
                }));
                Application.Run(context);
            }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
            {
                result.TrySetResult(new("CHECK_FAILED", "webview2_initialization_failed", "Không thể khởi tạo WebView2."));
            }
        }) { IsBackground = true, Name = "StaticModelWebViewProbe" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return await result.Task.WaitAsync(TimeSpan.FromSeconds(60), token);
    }
}
