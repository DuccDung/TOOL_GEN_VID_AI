using System.Net.Mail;
using TOOL_LOCAL.Authentication;

namespace TOOL_LOCAL;

public sealed class ForgotPasswordForm : Form
{
    private const string AcceptedMessage =
        "Nếu email thuộc một tài khoản hợp lệ, mã OTP sẽ được gửi tới hộp thư. Vui lòng kiểm tra cả thư rác.";

    private readonly AccountSessionManager _sessionManager;
    private readonly AuthTextBox _emailField = new();
    private readonly AuthTextBox _otpField = new();
    private readonly AuthTextBox _newPasswordField = new();
    private readonly AuthTextBox _passwordConfirmationField = new();
    private readonly AuthButton _sendOtpButton = new();
    private readonly AuthButton _resetPasswordButton = new();
    private readonly AuthButton _cancelButton = new();
    private readonly Label _statusLabel = new();
    private bool _otpStepActive;

    public ForgotPasswordForm(AccountSessionManager sessionManager, string? initialEmail)
    {
        _sessionManager = sessionManager;
        InitializeUi(initialEmail);
    }

    private void InitializeUi(string? initialEmail)
    {
        SuspendLayout();
        Text = "VideoMaker - Quên mật khẩu";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(600, 800);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font(AuthTheme.FontFamily, 9f);
        BackColor = AuthTheme.BackgroundTop;

        var background = new AuthBackgroundPanel { Dock = DockStyle.Fill };
        var brand = new BrandHeader
        {
            Location = new Point(135, 18),
            Size = new Size(330, 82)
        };
        var card = new AuthCardPanel
        {
            Location = new Point(82, 108),
            Size = new Size(436, 620)
        };

        var title = CreateCenteredLabel(
            "Khôi phục mật khẩu",
            new Point(43, 22),
            new Size(350, 36),
            17f,
            FontStyle.Bold,
            AuthTheme.TextPrimary);
        var subtitle = CreateCenteredLabel(
            "Nhận OTP qua email và tạo mật khẩu mới.",
            new Point(43, 59),
            new Size(350, 35),
            9f,
            FontStyle.Regular,
            AuthTheme.TextSecondary);

        ConfigureField(_emailField, "Email", "Nhập email của bạn", AuthFieldIcon.Email, new Point(43, 99));
        _emailField.Value = initialEmail?.Trim() ?? string.Empty;

        _sendOtpButton.Text = "Gửi OTP";
        _sendOtpButton.LeadingGlyph = "➜";
        _sendOtpButton.GlyphColor = Color.White;
        _sendOtpButton.Location = new Point(43, 184);
        _sendOtpButton.Size = new Size(350, 44);
        _sendOtpButton.Click += SendOtpButtonOnClick;

        ConfigureField(_otpField, "Mã OTP", "Nhập 6 chữ số trong email", AuthFieldIcon.Lock, new Point(43, 239));
        ConfigureField(_newPasswordField, "Mật khẩu mới", "Nhập mật khẩu mới", AuthFieldIcon.Lock, new Point(43, 318), true);
        ConfigureField(
            _passwordConfirmationField,
            "Nhập lại mật khẩu mới",
            "Nhập lại mật khẩu mới",
            AuthFieldIcon.Lock,
            new Point(43, 397),
            true);

        _resetPasswordButton.Text = "Đổi mật khẩu";
        _resetPasswordButton.LeadingGlyph = "✓";
        _resetPasswordButton.GlyphColor = Color.White;
        _resetPasswordButton.Location = new Point(43, 484);
        _resetPasswordButton.Size = new Size(230, 44);
        _resetPasswordButton.Click += ResetPasswordButtonOnClick;

        _cancelButton.Primary = false;
        _cancelButton.Text = "Hủy";
        _cancelButton.Location = new Point(281, 484);
        _cancelButton.Size = new Size(112, 44);
        _cancelButton.DialogResult = DialogResult.Cancel;

        _statusLabel.Location = new Point(43, 538);
        _statusLabel.Size = new Size(350, 62);
        _statusLabel.Font = new Font(AuthTheme.FontFamily, 8.2f);
        _statusLabel.ForeColor = AuthTheme.TextSecondary;
        _statusLabel.TextAlign = ContentAlignment.TopCenter;
        _statusLabel.AutoEllipsis = true;
        _statusLabel.BackColor = Color.Transparent;

        card.Controls.AddRange([
            title,
            subtitle,
            _emailField,
            _sendOtpButton,
            _otpField,
            _newPasswordField,
            _passwordConfirmationField,
            _resetPasswordButton,
            _cancelButton,
            _statusLabel
        ]);
        background.Controls.AddRange([brand, card]);
        Controls.Add(background);
        CancelButton = _cancelButton;
        SetOtpStepActive(false);
        Shown += (_, _) => _emailField.FocusInput();
        ResumeLayout(false);
    }

    private async void SendOtpButtonOnClick(object? sender, EventArgs eventArgs)
    {
        if (!ValidateEmail())
        {
            return;
        }

        SetBusy(true, "Đang gửi OTP...", "send");
        try
        {
            await _sessionManager.RequestPasswordResetAsync(_emailField.Value);
            SetOtpStepActive(true);
            SetBusy(false, AcceptedMessage);
            _otpField.FocusInput();
        }
        catch (AccountClientException exception)
        {
            SetBusy(false, exception.Message, error: true);
            ShowServerErrors(exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            SetBusy(
                false,
                exception is TaskCanceledException
                    ? "Kết nối tới Server đã hết thời gian chờ."
                    : "Không thể kết nối tới Account Server.",
                error: true);
        }
    }

    private async void ResetPasswordButtonOnClick(object? sender, EventArgs eventArgs)
    {
        if (!ValidateResetInput())
        {
            return;
        }

        SetBusy(true, "Đang đổi mật khẩu...", "reset");
        try
        {
            await _sessionManager.ResetPasswordAsync(
                _emailField.Value,
                _otpField.Value,
                _newPasswordField.Value);
            MessageBox.Show(
                "Mật khẩu đã được đổi. Tất cả phiên đăng nhập cũ đã bị thu hồi.",
                "Đổi mật khẩu thành công",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (AccountClientException exception)
        {
            SetBusy(false, exception.Message, error: true);
            ShowServerErrors(exception);
            if (exception.Code.Equals("invalid_password_reset_otp", StringComparison.OrdinalIgnoreCase))
            {
                _otpField.ShowError(exception.Message);
                _otpField.FocusInput();
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            SetBusy(
                false,
                exception is TaskCanceledException
                    ? "Kết nối tới Server đã hết thời gian chờ."
                    : "Không thể kết nối tới Account Server.",
                error: true);
        }
    }

    private bool ValidateEmail()
    {
        _emailField.ClearError();
        var email = _emailField.Value.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            _emailField.ShowError("Vui lòng nhập email.");
            _emailField.FocusInput();
            return false;
        }

        if (!MailAddress.TryCreate(email, out var address) ||
            !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            _emailField.ShowError("Email không đúng định dạng.");
            _emailField.FocusInput();
            return false;
        }

        return true;
    }

    private bool ValidateResetInput()
    {
        ClearErrors();
        AuthTextBox? firstInvalidField = null;

        var otp = _otpField.Value.Trim();
        if (otp.Length != 6 || otp.Any(character => character is < '0' or > '9'))
        {
            _otpField.ShowError("OTP phải gồm đúng 6 chữ số.");
            firstInvalidField = _otpField;
        }

        var errors = RegistrationInputValidator.Validate(
            null,
            _emailField.Value,
            _newPasswordField.Value,
            _passwordConfirmationField.Value);
        foreach (var (field, messages) in errors)
        {
            var input = FindField(field);
            var message = messages.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (input is null || message is null)
            {
                continue;
            }

            input.ShowError(message);
            firstInvalidField ??= input;
        }

        firstInvalidField?.FocusInput();
        return firstInvalidField is null;
    }

    private void ShowServerErrors(AccountClientException exception)
    {
        foreach (var (field, messages) in exception.Errors)
        {
            var input = FindField(field);
            var message = messages.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (input is not null && message is not null)
            {
                input.ShowError(message);
            }
        }
    }

    private AuthTextBox? FindField(string field) => field.ToLowerInvariant() switch
    {
        "email" => _emailField,
        "otp" => _otpField,
        "password" or "newpassword" => _newPasswordField,
        "passwordconfirmation" => _passwordConfirmationField,
        _ => null
    };

    private void ClearErrors()
    {
        _emailField.ClearError();
        _otpField.ClearError();
        _newPasswordField.ClearError();
        _passwordConfirmationField.ClearError();
        SetStatus(string.Empty);
    }

    private void SetOtpStepActive(bool active)
    {
        _otpStepActive = active;
        _otpField.Visible = active;
        _newPasswordField.Visible = active;
        _passwordConfirmationField.Visible = active;
        _resetPasswordButton.Visible = active;
        _sendOtpButton.Text = active ? "Gửi lại OTP" : "Gửi OTP";
        AcceptButton = active ? _resetPasswordButton : _sendOtpButton;
    }

    private void SetBusy(bool busy, string status, string? operation = null, bool error = false)
    {
        _emailField.Enabled = !busy;
        _sendOtpButton.Enabled = !busy;
        _otpField.Enabled = !busy && _otpStepActive;
        _newPasswordField.Enabled = !busy && _otpStepActive;
        _passwordConfirmationField.Enabled = !busy && _otpStepActive;
        _resetPasswordButton.Enabled = !busy && _otpStepActive;
        _cancelButton.Enabled = !busy;
        _sendOtpButton.Text = busy && operation == "send"
            ? "Đang gửi..."
            : _otpStepActive ? "Gửi lại OTP" : "Gửi OTP";
        _resetPasswordButton.Text = busy && operation == "reset" ? "Đang đổi..." : "Đổi mật khẩu";
        UseWaitCursor = busy;
        SetStatus(status, error);
    }

    private void SetStatus(string message, bool error = false)
    {
        _statusLabel.Text = message;
        _statusLabel.ForeColor = error ? AuthTheme.Danger : AuthTheme.TextSecondary;
    }

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

    private static Label CreateCenteredLabel(
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
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleCenter
        };
}
