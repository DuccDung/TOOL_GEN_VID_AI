using System.Net.Mail;
using TOOL_LOCAL.Authentication;

namespace TOOL_LOCAL;

public sealed class LoginForm : Form
{
    private readonly AccountSessionManager _sessionManager;
    private readonly AuthTextBox _emailField = new();
    private readonly AuthTextBox _passwordField = new();
    private readonly AuthButton _loginButton = new();
    private readonly AuthButton _googleButton = new();
    private readonly AuthButton _facebookButton = new();
    private readonly CheckBox _rememberCheckBox = new();
    private readonly LinkLabel _forgotPasswordLink = new();
    private readonly LinkLabel _registerLink = new();
    private readonly Label _statusLabel = new();

    public LoginForm(AccountSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
        InitializeUi();
        Shown += RestoreSessionOnShown;
    }

    private void InitializeUi()
    {
        SuspendLayout();
        Text = "VideoMaker - Đăng nhập";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(600, 800);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font(AuthTheme.FontFamily, 9f);
        BackColor = AuthTheme.BackgroundTop;

        var background = new AuthBackgroundPanel { Dock = DockStyle.Fill };
        var brand = new BrandHeader
        {
            Location = new Point(135, 24),
            Size = new Size(330, 82)
        };
        var card = new AuthCardPanel
        {
            Location = new Point(82, 128),
            Size = new Size(436, 574)
        };

        var title = CreateCenteredLabel(
            "Đăng nhập",
            new Point(43, 32),
            new Size(350, 38),
            18f,
            FontStyle.Bold,
            AuthTheme.TextPrimary);
        var subtitle = CreateCenteredLabel(
            "Chào mừng bạn trở lại!",
            new Point(43, 68),
            new Size(350, 22),
            9.3f,
            FontStyle.Regular,
            AuthTheme.TextSecondary);

        ConfigureField(
            _emailField,
            "Email",
            "Nhập email của bạn",
            AuthFieldIcon.Email,
            new Point(43, 99));
        ConfigureField(
            _passwordField,
            "Mật khẩu",
            "Nhập mật khẩu",
            AuthFieldIcon.Lock,
            new Point(43, 178),
            true);

        _rememberCheckBox.Text = "Ghi nhớ đăng nhập";
        _rememberCheckBox.Checked = true;
        _rememberCheckBox.AutoSize = true;
        _rememberCheckBox.Location = new Point(43, 264);
        _rememberCheckBox.Font = new Font(AuthTheme.FontFamily, 8.8f);
        _rememberCheckBox.ForeColor = AuthTheme.TextPrimary;
        _rememberCheckBox.BackColor = Color.Transparent;
        _rememberCheckBox.Cursor = Cursors.Hand;

        ConfigureLink(_forgotPasswordLink, "Quên mật khẩu?", new Point(253, 263), new Size(140, 23));
        _forgotPasswordLink.TextAlign = ContentAlignment.MiddleRight;
        _forgotPasswordLink.LinkClicked += ForgotPasswordLinkOnClicked;

        _loginButton.Text = "Đăng nhập";
        _loginButton.LeadingGlyph = "➜";
        _loginButton.GlyphColor = Color.White;
        _loginButton.Location = new Point(43, 303);
        _loginButton.Size = new Size(350, 46);
        _loginButton.Click += LoginButtonOnClick;

        ConfigureStatusLabel(_statusLabel, new Point(43, 352), new Size(350, 25));

        var divider = new AuthDivider
        {
            Text = "Hoặc đăng nhập với",
            Location = new Point(43, 378),
            Size = new Size(350, 28)
        };

        ConfigureSocialButton(_googleButton, "Đăng nhập với Google", "G", Color.FromArgb(219, 68, 55), 411);
        ConfigureSocialButton(_facebookButton, "Đăng nhập với Facebook", "f", Color.FromArgb(24, 119, 242), 461);
        _googleButton.Click += (_, _) => ShowUnavailableFeature("Đăng nhập Google");
        _facebookButton.Click += (_, _) => ShowUnavailableFeature("Đăng nhập Facebook");

        var accountPrompt = CreateLabel(
            "Chưa có tài khoản?",
            new Point(92, 525),
            new Size(145, 24),
            9f,
            FontStyle.Regular,
            AuthTheme.TextPrimary);
        accountPrompt.TextAlign = ContentAlignment.MiddleRight;
        ConfigureLink(_registerLink, "Đăng ký ngay", new Point(238, 525), new Size(110, 24));
        _registerLink.TextAlign = ContentAlignment.MiddleLeft;
        _registerLink.LinkClicked += RegisterLinkOnClicked;

        card.Controls.AddRange([
            title,
            subtitle,
            _emailField,
            _passwordField,
            _rememberCheckBox,
            _forgotPasswordLink,
            _loginButton,
            _statusLabel,
            divider,
            _googleButton,
            _facebookButton,
            accountPrompt,
            _registerLink
        ]);

        var securityFooter = new SecurityFooter
        {
            Location = new Point(135, 724),
            Size = new Size(330, 28)
        };

        background.Controls.AddRange([brand, card, securityFooter]);
        Controls.Add(background);
        AcceptButton = _loginButton;
        ResumeLayout(false);
    }

    private async void RestoreSessionOnShown(object? sender, EventArgs eventArgs)
    {
        SetBusy(true, "Đang kiểm tra phiên đăng nhập...");
        try
        {
            if (await _sessionManager.TryRestoreAsync())
            {
                CompleteAuthentication();
                return;
            }

            SetBusy(false, string.Empty);
            _emailField.FocusInput();
        }
        catch (HttpRequestException)
        {
            SetBusy(false, "Không thể kết nối tới Account Server.", true);
        }
        catch (TaskCanceledException)
        {
            SetBusy(false, "Kết nối tới Server đã hết thời gian chờ.", true);
        }
    }

    private async void LoginButtonOnClick(object? sender, EventArgs eventArgs)
    {
        if (!ValidateLoginInput())
        {
            return;
        }

        SetBusy(true, "Đang đăng nhập...");
        try
        {
            await _sessionManager.LoginAsync(
                _emailField.Value,
                _passwordField.Value,
                _rememberCheckBox.Checked);
            CompleteAuthentication();
        }
        catch (AccountClientException exception)
        {
            SetBusy(false, exception.Message, true);
            ShowServerErrors(exception.Errors);
            if (exception.Code.Equals("invalid_credentials", StringComparison.OrdinalIgnoreCase))
            {
                _passwordField.ShowError("Email hoặc mật khẩu không đúng.");
                _passwordField.FocusInput();
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            SetBusy(false, GetSafeMessage(exception), true);
        }
    }

    private bool ValidateLoginInput()
    {
        _emailField.ClearError();
        _passwordField.ClearError();
        SetStatus(string.Empty);
        AuthTextBox? firstInvalidField = null;

        var email = _emailField.Value.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            _emailField.ShowError("Vui lòng nhập email.");
            firstInvalidField = _emailField;
        }
        else if (!MailAddress.TryCreate(email, out var address) ||
                 !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            _emailField.ShowError("Email không đúng định dạng.");
            firstInvalidField = _emailField;
        }

        if (string.IsNullOrEmpty(_passwordField.Value))
        {
            _passwordField.ShowError("Vui lòng nhập mật khẩu.");
            firstInvalidField ??= _passwordField;
        }

        firstInvalidField?.FocusInput();
        return firstInvalidField is null;
    }

    private void ShowServerErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (field, fieldErrors) in errors)
        {
            var message = fieldErrors.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (message is null)
            {
                continue;
            }

            if (field.Equals("email", StringComparison.OrdinalIgnoreCase))
            {
                _emailField.ShowError(message);
            }
            else if (field.Equals("password", StringComparison.OrdinalIgnoreCase))
            {
                _passwordField.ShowError(message);
            }
        }
    }

    private void RegisterLinkOnClicked(object? sender, LinkLabelLinkClickedEventArgs eventArgs)
    {
        using var registerForm = new RegisterForm(_sessionManager);
        if (registerForm.ShowDialog(this) == DialogResult.OK)
        {
            CompleteAuthentication();
        }
    }

    private void ForgotPasswordLinkOnClicked(object? sender, LinkLabelLinkClickedEventArgs eventArgs)
    {
        using var forgotPasswordForm = new ForgotPasswordForm(_sessionManager, _emailField.Value);
        forgotPasswordForm.ShowDialog(this);
    }

    private void CompleteAuthentication()
    {
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ShowUnavailableFeature(string feature) =>
        SetStatus($"{feature} chưa được cấu hình.", false);

    private void SetBusy(bool busy, string status, bool error = false)
    {
        _emailField.Enabled = !busy;
        _passwordField.Enabled = !busy;
        _rememberCheckBox.Enabled = !busy;
        _forgotPasswordLink.Enabled = !busy;
        _loginButton.Enabled = !busy;
        _googleButton.Enabled = !busy;
        _facebookButton.Enabled = !busy;
        _registerLink.Enabled = !busy;
        _loginButton.Text = busy ? "Đang xử lý..." : "Đăng nhập";
        SetStatus(status, error);
        UseWaitCursor = busy;
    }

    private void SetStatus(string status, bool error = false)
    {
        _statusLabel.Text = status;
        _statusLabel.ForeColor = error ? AuthTheme.Danger : AuthTheme.TextSecondary;
    }

    private static string GetSafeMessage(Exception exception) => exception switch
    {
        AccountClientException accountException => accountException.Message,
        TaskCanceledException => "Kết nối tới Server đã hết thời gian chờ.",
        _ => "Không thể kết nối tới Account Server."
    };

    private static void ConfigureField(
        AuthTextBox field,
        string label,
        string placeholder,
        AuthFieldIcon icon,
        Point location,
        bool isPassword = false)
    {
        field.LabelText = label;
        field.PlaceholderText = placeholder;
        field.FieldIcon = icon;
        field.IsPassword = isPassword;
        field.Location = location;
        field.Size = new Size(350, 78);
    }

    private static void ConfigureSocialButton(
        AuthButton button,
        string text,
        string glyph,
        Color glyphColor,
        int y)
    {
        button.Primary = false;
        button.Text = text;
        button.LeadingGlyph = glyph;
        button.GlyphColor = glyphColor;
        button.Location = new Point(43, y);
        button.Size = new Size(350, 42);
    }

    private static Label CreateCenteredLabel(
        string text,
        Point location,
        Size size,
        float fontSize,
        FontStyle fontStyle,
        Color color)
    {
        var label = CreateLabel(text, location, size, fontSize, fontStyle, color);
        label.TextAlign = ContentAlignment.MiddleCenter;
        return label;
    }

    private static Label CreateLabel(
        string text,
        Point location,
        Size size,
        float fontSize,
        FontStyle fontStyle,
        Color color) =>
        new()
        {
            Text = text,
            Location = location,
            Size = size,
            Font = new Font(AuthTheme.FontFamily, fontSize, fontStyle),
            ForeColor = color,
            BackColor = Color.Transparent
        };

    private static void ConfigureLink(LinkLabel link, string text, Point location, Size size)
    {
        link.Text = text;
        link.Location = location;
        link.Size = size;
        link.Font = new Font(AuthTheme.FontFamily, 8.8f, FontStyle.Bold);
        link.LinkColor = AuthTheme.Primary;
        link.ActiveLinkColor = AuthTheme.PrimaryDark;
        link.VisitedLinkColor = AuthTheme.Primary;
        link.BackColor = Color.Transparent;
        link.Cursor = Cursors.Hand;
    }

    private static void ConfigureStatusLabel(Label label, Point location, Size size)
    {
        label.Location = location;
        label.Size = size;
        label.Font = new Font(AuthTheme.FontFamily, 8.3f);
        label.ForeColor = AuthTheme.TextSecondary;
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.AutoEllipsis = true;
        label.BackColor = Color.Transparent;
    }
}
