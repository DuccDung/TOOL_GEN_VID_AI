using TOOL_LOCAL.Authentication;

namespace TOOL_LOCAL;

public sealed class RegisterForm : Form
{
    private readonly AccountSessionManager _sessionManager;
    private readonly AuthTextBox _displayNameField = new();
    private readonly AuthTextBox _emailField = new();
    private readonly AuthTextBox _passwordField = new();
    private readonly AuthTextBox _passwordConfirmationField = new();
    private readonly CheckBox _termsCheckBox = new();
    private readonly LinkLabel _agreementLabel = new();
    private readonly AuthButton _registerButton = new();
    private readonly AuthButton _googleButton = new();
    private readonly AuthButton _facebookButton = new();
    private readonly LinkLabel _loginLink = new();
    private readonly Label _statusLabel = new();

    public RegisterForm(AccountSessionManager sessionManager)
    {
        _sessionManager = sessionManager;
        InitializeUi();
    }

    private void InitializeUi()
    {
        SuspendLayout();
        Text = "VideoMaker - Đăng ký";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(540, 780);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font(AuthTheme.FontFamily, 9f);
        BackColor = AuthTheme.BackgroundTop;
        KeyPreview = true;
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        var background = new AuthBackgroundPanel { Dock = DockStyle.Fill };
        var brand = new BrandHeader
        {
            Location = new Point(105, 10),
            Size = new Size(330, 78)
        };
        var card = new AuthCardPanel
        {
            Location = new Point(51, 88),
            Size = new Size(438, 684)
        };

        var title = CreateCenteredLabel(
            "Đăng ký",
            new Point(44, 24),
            new Size(350, 36),
            17f,
            FontStyle.Bold,
            AuthTheme.TextPrimary);
        var subtitle = CreateCenteredLabel(
            "Tạo tài khoản để bắt đầu ngay!",
            new Point(44, 57),
            new Size(350, 22),
            9f,
            FontStyle.Regular,
            AuthTheme.TextSecondary);

        ConfigureField(
            _displayNameField,
            "Họ và tên",
            "Nhập họ và tên",
            AuthFieldIcon.User,
            new Point(44, 85));
        ConfigureField(
            _emailField,
            "Email",
            "Nhập email của bạn",
            AuthFieldIcon.Email,
            new Point(44, 161));
        ConfigureField(
            _passwordField,
            "Mật khẩu",
            "Tối thiểu 10 ký tự",
            AuthFieldIcon.Lock,
            new Point(44, 237),
            true);
        ConfigureField(
            _passwordConfirmationField,
            "Xác nhận mật khẩu",
            "Nhập lại mật khẩu",
            AuthFieldIcon.Lock,
            new Point(44, 313),
            true);

        _termsCheckBox.AutoSize = true;
        _termsCheckBox.Location = new Point(44, 397);
        _termsCheckBox.BackColor = Color.Transparent;
        _termsCheckBox.Cursor = Cursors.Hand;

        ConfigureAgreementLabel();

        _registerButton.Text = "Đăng ký";
        _registerButton.LeadingGlyph = "+";
        _registerButton.GlyphColor = Color.White;
        _registerButton.Location = new Point(44, 428);
        _registerButton.Size = new Size(350, 45);
        _registerButton.Click += RegisterButtonOnClick;

        ConfigureStatusLabel(_statusLabel, new Point(44, 476), new Size(350, 25));

        var divider = new AuthDivider
        {
            Text = "Hoặc đăng ký với",
            Location = new Point(44, 502),
            Size = new Size(350, 28)
        };

        ConfigureSocialButton(_googleButton, "Đăng ký với Google", "G", Color.FromArgb(219, 68, 55), 534);
        ConfigureSocialButton(_facebookButton, "Đăng ký với Facebook", "f", Color.FromArgb(24, 119, 242), 582);
        _googleButton.Click += (_, _) => ShowUnavailableFeature("Đăng ký Google");
        _facebookButton.Click += (_, _) => ShowUnavailableFeature("Đăng ký Facebook");

        var loginPrompt = CreateLabel(
            "Đã có tài khoản?",
            new Point(94, 635),
            new Size(135, 24),
            8.8f,
            FontStyle.Regular,
            AuthTheme.TextPrimary);
        loginPrompt.TextAlign = ContentAlignment.MiddleRight;
        ConfigureLink(_loginLink, "Đăng nhập ngay", new Point(230, 635), new Size(120, 24), 8.8f);
        _loginLink.TextAlign = ContentAlignment.MiddleLeft;
        _loginLink.LinkClicked += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        card.Controls.AddRange([
            title,
            subtitle,
            _displayNameField,
            _emailField,
            _passwordField,
            _passwordConfirmationField,
            _termsCheckBox,
            _agreementLabel,
            _registerButton,
            _statusLabel,
            divider,
            _googleButton,
            _facebookButton,
            loginPrompt,
            _loginLink
        ]);

        background.Controls.AddRange([brand, card]);
        Controls.Add(background);

        AcceptButton = _registerButton;
        Shown += (_, _) => _displayNameField.FocusInput();
        ResumeLayout(false);
    }

    private async void RegisterButtonOnClick(object? sender, EventArgs eventArgs)
    {
        ClearErrors();
        var validationErrors = RegistrationInputValidator.Validate(
            _displayNameField.Value,
            _emailField.Value,
            _passwordField.Value,
            _passwordConfirmationField.Value);
        if (validationErrors.Count > 0)
        {
            ShowErrors(validationErrors, "Vui lòng kiểm tra lại thông tin đăng ký.");
            return;
        }

        if (!_termsCheckBox.Checked)
        {
            SetStatus("Vui lòng đồng ý với điều khoản sử dụng.", true);
            _termsCheckBox.Focus();
            return;
        }

        SetBusy(true, "Đang tạo tài khoản...");
        try
        {
            await _sessionManager.RegisterAsync(
                _emailField.Value,
                _passwordField.Value,
                _displayNameField.Value);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (AccountClientException exception)
        {
            SetBusy(false, string.Empty);
            ShowErrors(exception.Errors, FormatServerMessage(exception));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            SetBusy(false, GetSafeMessage(exception), true);
        }
    }

    private void ShowErrors(IReadOnlyDictionary<string, string[]> errors, string fallbackMessage)
    {
        ClearErrors();
        AuthTextBox? firstInvalidField = null;
        var messages = new List<string>();

        foreach (var (field, fieldErrors) in errors)
        {
            var normalizedErrors = fieldErrors
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (normalizedErrors.Length == 0)
            {
                continue;
            }

            messages.AddRange(normalizedErrors);
            var input = FindField(field);
            if (input is not null)
            {
                input.ShowError(normalizedErrors[0]);
                firstInvalidField ??= input;
            }
        }

        SetBusy(false, messages.FirstOrDefault() ?? fallbackMessage, true);
        firstInvalidField?.FocusInput();
    }

    private void ClearErrors()
    {
        _displayNameField.ClearError();
        _emailField.ClearError();
        _passwordField.ClearError();
        _passwordConfirmationField.ClearError();
        SetStatus(string.Empty);
    }

    private AuthTextBox? FindField(string field) => field.ToLowerInvariant() switch
    {
        "displayname" => _displayNameField,
        "email" => _emailField,
        "password" => _passwordField,
        "passwordconfirmation" => _passwordConfirmationField,
        _ => null
    };

    private static string FormatServerMessage(AccountClientException exception) =>
        string.IsNullOrWhiteSpace(exception.TraceId)
            ? exception.Message
            : $"{exception.Message} Mã tra cứu: {exception.TraceId}";

    private void ShowUnavailableFeature(string feature) =>
        SetStatus($"{feature} đang được cập nhật.");

    private void SetBusy(bool busy, string status, bool error = false)
    {
        _displayNameField.Enabled = !busy;
        _emailField.Enabled = !busy;
        _passwordField.Enabled = !busy;
        _passwordConfirmationField.Enabled = !busy;
        _termsCheckBox.Enabled = !busy;
        _agreementLabel.Enabled = !busy;
        _registerButton.Enabled = !busy;
        _googleButton.Enabled = !busy;
        _facebookButton.Enabled = !busy;
        _loginLink.Enabled = !busy;
        _registerButton.Text = busy ? "Đang tạo tài khoản..." : "Đăng ký";
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
        button.Location = new Point(44, y);
        button.Size = new Size(350, 40);
    }

    private void ConfigureAgreementLabel()
    {
        const string text = "Tôi đồng ý với Điều khoản sử dụng và Chính sách bảo mật";
        const string terms = "Điều khoản sử dụng";
        const string privacy = "Chính sách bảo mật";
        _agreementLabel.Text = text;
        _agreementLabel.Location = new Point(67, 394);
        _agreementLabel.Size = new Size(355, 23);
        _agreementLabel.Font = new Font(AuthTheme.FontFamily, 7.8f);
        _agreementLabel.ForeColor = AuthTheme.TextPrimary;
        _agreementLabel.LinkColor = AuthTheme.Primary;
        _agreementLabel.ActiveLinkColor = AuthTheme.PrimaryDark;
        _agreementLabel.VisitedLinkColor = AuthTheme.Primary;
        _agreementLabel.BackColor = Color.Transparent;
        _agreementLabel.TextAlign = ContentAlignment.MiddleLeft;
        _agreementLabel.Cursor = Cursors.Hand;
        _agreementLabel.Links.Clear();
        _agreementLabel.Links.Add(text.IndexOf(terms, StringComparison.Ordinal), terms.Length, terms);
        _agreementLabel.Links.Add(text.IndexOf(privacy, StringComparison.Ordinal), privacy.Length, privacy);
        _agreementLabel.LinkClicked += (_, eventArgs) =>
            ShowUnavailableFeature(eventArgs.Link?.LinkData?.ToString() ?? "Nội dung");
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

    private static void ConfigureLink(LinkLabel link, string text, Point location, Size size, float fontSize)
    {
        link.Text = text;
        link.Location = location;
        link.Size = size;
        link.Font = new Font(AuthTheme.FontFamily, fontSize, FontStyle.Bold);
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
        label.Font = new Font(AuthTheme.FontFamily, 8.2f);
        label.ForeColor = AuthTheme.TextSecondary;
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.AutoEllipsis = true;
        label.BackColor = Color.Transparent;
    }
}
