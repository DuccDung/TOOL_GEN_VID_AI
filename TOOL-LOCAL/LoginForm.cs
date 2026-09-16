using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using TOOL_LOCAL.Authentication;

namespace TOOL_LOCAL;

public sealed class LoginForm : Form
{
    private readonly AccountSessionManager _sessionManager;
    private readonly string _webRoot;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly WebView2 _webView;
    private readonly Panel _loadingPanel;
    private readonly Label _loadingLabel;
    private readonly JsonSerializerOptions _webJson = new(JsonSerializerDefaults.Web);
    private LoginWebViewState _viewState = new(true, "Đang kiểm tra phiên đăng nhập...", false);
    private bool _restoreStarted;
    private bool _operationBusy;

    public LoginForm(AccountSessionManager sessionManager) : this(sessionManager, null)
    {
    }

    internal LoginForm(AccountSessionManager sessionManager, string? webRoot)
    {
        _sessionManager = sessionManager;
        _webRoot = webRoot ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
        Icon = BrandIdentity.WindowIcon;
        Text = "taphoatool - Đăng nhập";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(600, 800);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = AuthTheme.BackgroundTop;

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            Visible = false,
            DefaultBackgroundColor = AuthTheme.BackgroundTop
        };
        _loadingLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Đang khởi tạo giao diện đăng nhập taphoatool...",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(AuthTheme.FontFamily, 10f),
            ForeColor = AuthTheme.TextSecondary
        };
        _loadingPanel = new Panel { Dock = DockStyle.Fill, BackColor = AuthTheme.BackgroundTop };
        _loadingPanel.Controls.Add(_loadingLabel);
        Controls.Add(_webView);
        Controls.Add(_loadingPanel);
        _loadingPanel.BringToFront();

        Shown += InitializeWebViewOnShown;
        FormClosed += (_, _) => _lifetime.Cancel();
    }

    private async void InitializeWebViewOnShown(object? sender, EventArgs eventArgs)
    {
        try
        {
            var loginPath = Path.Combine(_webRoot, "login.html");
            if (!File.Exists(loginPath))
                throw new InvalidOperationException("Không tìm thấy giao diện đăng nhập đã build.");

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ToolGenPostVideo", "LoginWebView2");
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            if (_lifetime.IsCancellationRequested) return;
            await _webView.EnsureCoreWebView2Async(environment);
            if (_lifetime.IsCancellationRequested) return;

            var core = _webView.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(LoginWebMessageContracts.HostName, _webRoot,
                CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.NavigationStarting += (_, args) =>
            {
                if (!LoginWebMessageContracts.IsTrustedPage(args.Uri)) args.Cancel = true;
            };
            core.NavigationCompleted += (_, args) =>
            {
                if (_lifetime.IsCancellationRequested) return;
                if (!args.IsSuccess)
                {
                    ShowStartupError("Không thể tải giao diện đăng nhập WebView2.");
                    return;
                }

                _loadingPanel.Visible = false;
                _webView.Visible = true;
                _webView.BringToFront();
            };
            core.WebMessageReceived += WebViewOnWebMessageReceived;
            var webVersion = File.GetLastWriteTimeUtc(loginPath).Ticks;
            core.Navigate($"https://{LoginWebMessageContracts.HostName}/login.html?v={webVersion}");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowStartupError("Máy tính chưa cài Microsoft Edge WebView2 Runtime.");
        }
        catch (Exception) when (!_lifetime.IsCancellationRequested)
        {
            ShowStartupError("Không thể khởi tạo giao diện đăng nhập WebView2.");
        }
    }

    private async void WebViewOnWebMessageReceived(object? sender,
        CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        string message;
        try
        {
            message = eventArgs.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return;
        }

        if (!LoginWebMessageContracts.TryRead(eventArgs.Source, message,
            out var request) || request is null || _lifetime.IsCancellationRequested)
            return;

        switch (request.Type)
        {
            case "auth.ready":
                PostState(request.RequestId);
                if (!_restoreStarted)
                {
                    _restoreStarted = true;
                    await RestoreSessionAsync(request.RequestId);
                }
                break;
            case "auth.login":
                await LoginAsync(request);
                break;
            case "auth.register.open":
                OpenRegister(request.RequestId);
                break;
            case "auth.password-reset.open":
                OpenPasswordReset(request);
                break;
            case "auth.social.unavailable":
                ShowSocialUnavailable(request);
                break;
        }
    }

    private async Task RestoreSessionAsync(string requestId)
    {
        if (_operationBusy) return;
        _operationBusy = true;
        SetState(new(true, "Đang kiểm tra phiên đăng nhập...", false), requestId);
        try
        {
            if (await _sessionManager.TryRestoreAsync(_lifetime.Token))
            {
                CompleteAuthentication();
                return;
            }

            SetState(new(false, string.Empty, false, FocusField: "email"), requestId);
        }
        catch (AccountClientException exception)
        {
            SetState(new(false, exception.Message, true, FocusField: "email"), requestId);
        }
        catch (HttpRequestException)
        {
            SetState(new(false, "Không thể kết nối tới Account Server.", true,
                FocusField: "email"), requestId);
        }
        catch (TaskCanceledException) when (!_lifetime.IsCancellationRequested)
        {
            SetState(new(false, "Kết nối tới Server đã hết thời gian chờ.", true,
                FocusField: "email"), requestId);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception) when (!_lifetime.IsCancellationRequested)
        {
            SetState(new(false, "Không thể kiểm tra phiên đăng nhập.", true,
                FocusField: "email"), requestId);
        }
        finally
        {
            _operationBusy = false;
        }
    }

    private async Task LoginAsync(LoginWebRequest request)
    {
        if (_operationBusy || !_restoreStarted) return;
        if (!LoginWebMessageContracts.TryReadLogin(request.Payload, out var email,
            out var password, out var rememberMe, out var validationError))
        {
            SetState(validationError ?? new(false, "Thông tin đăng nhập không hợp lệ.", true),
                request.RequestId);
            return;
        }

        _operationBusy = true;
        SetState(new(true, "Đang đăng nhập...", false), request.RequestId);
        try
        {
            await _sessionManager.LoginAsync(email, password, rememberMe, _lifetime.Token);
            CompleteAuthentication();
        }
        catch (AccountClientException exception)
        {
            var emailError = GetFieldError(exception, "email");
            var passwordError = GetFieldError(exception, "password");
            if (exception.Code.Equals("invalid_credentials", StringComparison.OrdinalIgnoreCase))
                passwordError ??= "Email hoặc mật khẩu không đúng.";
            SetState(new(false, exception.Message, true, emailError, passwordError,
                emailError is not null ? "email" : "password"), request.RequestId);
        }
        catch (HttpRequestException)
        {
            SetState(new(false, "Không thể kết nối tới Account Server.", true), request.RequestId);
        }
        catch (TaskCanceledException) when (!_lifetime.IsCancellationRequested)
        {
            SetState(new(false, "Kết nối tới Server đã hết thời gian chờ.", true), request.RequestId);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception) when (!_lifetime.IsCancellationRequested)
        {
            SetState(new(false, "Không thể đăng nhập. Vui lòng thử lại.", true), request.RequestId);
        }
        finally
        {
            _operationBusy = false;
        }
    }

    private void OpenRegister(string requestId)
    {
        if (_operationBusy || !_restoreStarted) return;
        _operationBusy = true;
        try
        {
            using var registerForm = new RegisterForm(_sessionManager);
            if (registerForm.ShowDialog(this) == DialogResult.OK && _sessionManager.Current is not null)
            {
                CompleteAuthentication();
                return;
            }
        }
        finally
        {
            _operationBusy = false;
        }

        SetState(new(false, string.Empty, false), requestId);
    }

    private void OpenPasswordReset(LoginWebRequest request)
    {
        if (_operationBusy || !_restoreStarted) return;
        string? email = null;
        if (LoginWebMessageContracts.TryReadString(request.Payload, "email", 320, out var value))
            email = value.Trim();

        _operationBusy = true;
        try
        {
            using var forgotPasswordForm = new ForgotPasswordForm(_sessionManager, email);
            forgotPasswordForm.ShowDialog(this);
        }
        finally
        {
            _operationBusy = false;
        }

        SetState(new(false, string.Empty, false), request.RequestId);
    }

    private void ShowSocialUnavailable(LoginWebRequest request)
    {
        if (_operationBusy || !_restoreStarted ||
            !LoginWebMessageContracts.TryReadString(request.Payload, "provider", 20,
                out var provider) || provider is not ("Google" or "Facebook"))
            return;
        SetState(new(false, $"Đăng nhập {provider} chưa được cấu hình.", false),
            request.RequestId);
    }

    private void SetState(LoginWebViewState state, string? requestId = null)
    {
        _viewState = state;
        PostState(requestId);
    }

    private void PostState(string? requestId = null)
    {
        if (_lifetime.IsCancellationRequested || _webView.CoreWebView2 is null) return;
        var json = JsonSerializer.Serialize(new
        {
            type = "auth.state",
            requestId = requestId ?? Guid.NewGuid().ToString(),
            payload = _viewState
        }, _webJson);
        _webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void CompleteAuthentication()
    {
        if (_lifetime.IsCancellationRequested || _sessionManager.Current is null) return;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ShowStartupError(string message)
    {
        if (_lifetime.IsCancellationRequested) return;
        _loadingLabel.Text = message;
        MessageBox.Show(this, message, "Đăng nhập chưa sẵn sàng", MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private static string? GetFieldError(AccountClientException exception, string field)
    {
        foreach (var (name, messages) in exception.Errors)
        {
            if (name.Equals(field, StringComparison.OrdinalIgnoreCase))
                return messages.FirstOrDefault(message => !string.IsNullOrWhiteSpace(message));
        }

        return null;
    }
}
