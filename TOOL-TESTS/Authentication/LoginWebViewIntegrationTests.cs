using System.Globalization;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;
using TOOL_LOCAL;
using TOOL_LOCAL.Authentication;
using TOOL_SHARED.Contracts.Authentication;

namespace TOOL_TESTS.Authentication;

[Collection(NativeWindowsCollection.Name)]
public sealed class LoginWebViewIntegrationTests
{
    [Theory]
    [InlineData("auth.register.open")]
    [InlineData("auth.password-reset.open")]
    public async Task AccountDialog_IsDeferredAndDoesNotBlockWebViewCallbacks(string messageType)
    {
        var webRoot = FindBuiltWebRoot();
        var profile = Path.Combine(Path.GetTempPath(), $"vm-login-dialog-{Guid.NewGuid():N}");
        System.Diagnostics.Process? browser = null;
        Form? activeForm = null;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var manager = new AccountSessionManager(new FakeAccountApiClient(), new FakeTokenStore(),
                    new DeviceIdentityService(Path.Combine(profile, "device-id.bin")));
                using var form = new LoginForm(manager, webRoot, profile)
                {
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(-2000, -2000),
                    ShowInTaskbar = false
                };
                activeForm = form;
                Exception? failure = null;
                using var monitor = new System.Windows.Forms.Timer { Interval = 50 };
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        var webView = form.Controls.OfType<WebView2>().Single();
                        for (var attempt = 0; attempt < 150 && webView.CoreWebView2 is null; attempt++)
                            await Task.Delay(100);
                        var core = webView.CoreWebView2 ?? throw new InvalidOperationException("WebView2 không khởi tạo.");
                        browser = System.Diagnostics.Process.GetProcessById((int)core.BrowserProcessId);
                        await WaitForLoginReadyAsync(core);

                        var received = 0;
                        core.WebMessageReceived += (_, args) =>
                        {
                            if (!args.TryGetWebMessageAsString().Contains(messageType, StringComparison.Ordinal)) return;
                            received++;
                        };
                        var deadline = DateTime.UtcNow.AddSeconds(10);
                        monitor.Tick += async (_, _) =>
                        {
                            var owned = form.OwnedForms;
                            if (received < 2 && DateTime.UtcNow < deadline) return;
                            if (owned.Length == 0 && DateTime.UtcNow < deadline) return;
                            monitor.Stop();
                            try
                            {
                                // Both WebMessageReceived callbacks must finish while the modal is still open.
                                // A synchronous ShowDialog in the handler cannot meet this condition.
                                Assert.Equal(2, received);
                                var dialog = Assert.Single(owned);
                                if (messageType == "auth.register.open") Assert.IsType<RegisterForm>(dialog);
                                else Assert.IsType<ForgotPasswordForm>(dialog);
                                Assert.Equal("4", await core.ExecuteScriptAsync("2 + 2").WaitAsync(TimeSpan.FromSeconds(3)));
                                dialog.Close();
                                await WaitForLoginReadyAsync(core);
                                Assert.False(manager.IsAuthenticated);
                            }
                            catch (Exception exception)
                            {
                                failure = exception;
                            }
                            finally
                            {
                                foreach (var dialog in form.OwnedForms) dialog.Close();
                                form.Close();
                            }
                        };
                        monitor.Start();
                        var first = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            type = messageType, requestId = Guid.NewGuid().ToString(), payload = new { email = "user@example.test" }
                        });
                        var second = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            type = messageType, requestId = Guid.NewGuid().ToString(), payload = new { email = "user@example.test" }
                        });
                        // Send actual page-to-host messages without submitting an account to a server.
                        await core.ExecuteScriptAsync($"window.chrome.webview.postMessage(JSON.stringify({first}));" +
                            $"window.chrome.webview.postMessage(JSON.stringify({second}));");
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                        form.Close();
                    }
                };
                Application.Run(form);
                if (failure is not null) completion.TrySetException(failure);
                else
                {
                    Assert.Empty(form.OwnedForms);
                    Assert.False(manager.IsAuthenticated);
                    completion.TrySetResult();
                }
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }) { IsBackground = true, Name = "VideoMaker Account Dialog WebView2 Regression Test" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(45));
        }
        finally
        {
            if (activeForm is { IsDisposed: false, IsHandleCreated: true })
                activeForm.BeginInvoke(new Action(() =>
                {
                    foreach (var dialog in activeForm.OwnedForms) dialog.Close();
                    activeForm.Close();
                }));
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Account dialog UI thread did not stop.");
            if (browser is not null)
            {
                try { await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { browser.Dispose(); }
            }
            if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true);
        }
    }

    private static async Task WaitForLoginReadyAsync(CoreWebView2 core)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            if (await core.ExecuteScriptAsync(
                "Boolean(document.querySelector('#login-email') && !document.querySelector('#login-email').disabled)") == "true") return;
            await Task.Delay(100);
        }

        throw new TimeoutException("Form đăng nhập không sẵn sàng.");
    }

    [Fact]
    public async Task LoginForm_LoadsWebViewAndAuthenticatesThroughTheHost()
    {
        var webRoot = FindBuiltWebRoot();
        var profile = Path.Combine(Path.GetTempPath(), $"vm-login-{Guid.NewGuid():N}");
        System.Diagnostics.Process? browser = null;
        var completion = new TaskCompletionSource<(bool Authenticated, string? Email, int Saves)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Form? activeForm = null;
        var thread = new Thread(() =>
        {
            var tokenStore = new FakeTokenStore();
            var api = new FakeAccountApiClient();
            using var manager = new AccountSessionManager(api, tokenStore,
                new DeviceIdentityService(Path.Combine(profile, "device-id.bin")));
            using var form = new LoginForm(manager, webRoot, profile)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-2000, -2000),
                ShowInTaskbar = false
            };
            activeForm = form;
            form.FormClosed += (_, _) => completion.TrySetResult((
                manager.IsAuthenticated, api.LoginEmail, tokenStore.SaveCount));
            form.Shown += async (_, _) =>
            {
                try
                {
                    var webView = form.Controls.OfType<WebView2>().Single();
                    for (var attempt = 0; attempt < 150 && webView.CoreWebView2 is null; attempt++)
                        await Task.Delay(100);
                    var core = webView.CoreWebView2
                        ?? throw new InvalidOperationException("WebView2 không khởi tạo.");
                    browser = System.Diagnostics.Process.GetProcessById((int)core.BrowserProcessId);
                    for (var attempt = 0; attempt < 150; attempt++)
                    {
                        var ready = await core.ExecuteScriptAsync(
                            "Boolean(document.querySelector('#login-email') && !document.querySelector('#login-email').disabled)");
                        if (ready == "true") break;
                        await Task.Delay(100);
                        if (attempt == 149) throw new TimeoutException("Form đăng nhập không sẵn sàng.");
                    }

                    webView.ZoomFactor = 1;
                    await VerifyContentFitsAsync(core);
                    var baselineWidth = int.Parse(await core.ExecuteScriptAsync("innerWidth"),
                        CultureInfo.InvariantCulture);
                    webView.ZoomFactor = 1.25;
                    await VerifyContentFitsAsync(core, (int)Math.Floor(baselineWidth / 1.15));

                    var screenshotPath = Environment.GetEnvironmentVariable("VIDEOMAKER_LOGIN_SCREENSHOT");
                    if (!string.IsNullOrWhiteSpace(screenshotPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath)!);
                        await using var stream = File.Create(screenshotPath);
                        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                    }

                    await core.ExecuteScriptAsync("""
                        const email = document.querySelector('#login-email');
                        const password = document.querySelector('#login-password');
                        const setValue = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
                        setValue.call(email, 'user@example.test');
                        email.dispatchEvent(new Event('input', { bubbles: true }));
                        setValue.call(password, 'pass1234567');
                        password.dispatchEvent(new Event('input', { bubbles: true }));
                        document.querySelector('form').requestSubmit();
                        """);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                    form.Close();
                }
            };
            Application.Run(form);
        })
        {
            IsBackground = true,
            Name = "VideoMaker Login WebView2 Integration Test"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        try
        {
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(45));
            Assert.True(result.Authenticated);
            Assert.Equal("user@example.test", result.Email);
            Assert.Equal(1, result.Saves);
        }
        finally
        {
            if (activeForm is { IsDisposed: false, IsHandleCreated: true } form)
                form.BeginInvoke(new Action(form.Close));
            Assert.True(thread.Join(TimeSpan.FromSeconds(5)), "Login UI thread did not stop.");
            if (browser is not null)
            {
                // BrowserProcessExited is dispatched on the STA loop, which has ended here.
                // Wait for the owned OS process instead of an event that can no longer dispatch.
                try { await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { browser.Dispose(); }
            }
            if (Directory.Exists(profile)) Directory.Delete(profile, recursive: true);
        }
    }

    private static async Task VerifyContentFitsAsync(CoreWebView2 core, int? maximumViewportWidth = null)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (maximumViewportWidth is not null &&
                int.Parse(await core.ExecuteScriptAsync("innerWidth"), CultureInfo.InvariantCulture) >
                maximumViewportWidth)
            {
                await Task.Delay(100);
                continue;
            }

            var fits = await core.ExecuteScriptAsync("""
                (() => {
                    const canvas = document.querySelector('.login-canvas')?.getBoundingClientRect();
                    const register = document.querySelector('.login-register')?.getBoundingClientRect();
                    const footer = document.querySelector('.login-security-footer')?.getBoundingClientRect();
                    return Boolean(canvas && register && footer &&
                        canvas.left >= -1 && canvas.top >= -1 &&
                        canvas.right <= innerWidth + 1 && canvas.bottom <= innerHeight + 1 &&
                        register.bottom <= innerHeight + 1 && footer.bottom <= innerHeight + 1 &&
                        document.documentElement.scrollWidth <= innerWidth + 1 &&
                        document.documentElement.scrollHeight <= innerHeight + 1);
                })()
                """);
            if (fits == "true") return;
            await Task.Delay(100);
        }

        throw new InvalidOperationException("Giao diện đăng nhập tràn khỏi WebView2.");
    }

    private static string FindBuiltWebRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName,
                   "TOOL_GEN_POST_VIDEO.slnx")))
            directory = directory.Parent;
        if (directory is null) throw new InvalidOperationException("Không tìm thấy repository.");

#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var webRoot = Path.Combine(directory.FullName, "TOOL-LOCAL", "bin", configuration,
            "net10.0-windows", "win-x64", "wwwroot");
        if (!File.Exists(Path.Combine(webRoot, "login.html")))
            throw new InvalidOperationException("Build desktop thiếu login.html.");
        return webRoot;
    }

    private sealed class FakeTokenStore : ITokenStore
    {
        public int SaveCount { get; private set; }
        public Task<StoredRefreshToken?> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<StoredRefreshToken?>(null);
        public Task SaveAsync(StoredRefreshToken token, CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeAccountApiClient : IAccountApiClient
    {
        public string? LoginEmail { get; private set; }

        public Task<AuthTokenResponse> LoginAsync(LoginRequest request,
            CancellationToken cancellationToken = default)
        {
            LoginEmail = request.Email;
            return Task.FromResult(new AuthTokenResponse(
                "test-access-token", DateTime.UtcNow.AddHours(1),
                "test-refresh-token", DateTime.UtcNow.AddDays(1),
                Guid.NewGuid(), Guid.NewGuid(),
                new UserProfileResponse("user-001", request.Email, "Test User", "Active", ["User"])));
        }

        public Task<AuthTokenResponse> RegisterAsync(RegisterRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RequestPasswordResetAsync(ForgotPasswordRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ResetPasswordAsync(ResetPasswordRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuthTokenResponse> RefreshAsync(RefreshTokenRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LogoutAsync(string accessToken, LogoutRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
