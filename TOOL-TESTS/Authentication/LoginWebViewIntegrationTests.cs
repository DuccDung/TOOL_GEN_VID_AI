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
            using var manager = new AccountSessionManager(api, tokenStore, new DeviceIdentityService());
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
