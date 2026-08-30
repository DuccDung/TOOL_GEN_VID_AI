using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;
using TOOL_SERVER.Configuration;

namespace TOOL_SERVER.Authentication;

public sealed class SmtpPasswordResetEmailSender(IOptions<SmtpOptions> optionsAccessor)
    : IPasswordResetEmailSender
{
    private readonly SmtpOptions _options = optionsAccessor.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task SendAsync(
        string recipientEmail,
        string? displayName,
        string otp,
        TimeSpan otpLifetime,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("SMTP is not configured for password reset OTP.");
        }

        var safeName = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(displayName) ? "bạn" : displayName.Trim());
        var safeOtp = WebUtility.HtmlEncode(otp);
        var lifetimeMinutes = Math.Max(1, (int)Math.Ceiling(otpLifetime.TotalMinutes));

        using var message = new MailMessage
        {
            From = new MailAddress(_options.EffectiveFromAddress, _options.FromName, Encoding.UTF8),
            Subject = $"{otp} là mã OTP đặt lại mật khẩu VideoMaker",
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true,
            Body = $$"""
                <!doctype html>
                <html lang="vi">
                <body style="font-family:Arial,sans-serif;color:#172033;line-height:1.6">
                  <p>Xin chào {{safeName}},</p>
                  <p>VideoMaker nhận được yêu cầu đặt lại mật khẩu cho tài khoản của bạn.</p>
                  <p style="margin:24px 0;padding:16px;background:#f3f1ff;border-radius:10px;text-align:center">
                    <span style="display:block;color:#5e6678;font-size:13px">Mã OTP của bạn</span>
                    <strong style="font-size:32px;letter-spacing:8px;color:#5a43d2">{{safeOtp}}</strong>
                  </p>
                  <p>Mã có hiệu lực trong {{lifetimeMinutes}} phút và sẽ bị vô hiệu sau quá nhiều lần nhập sai.</p>
                  <p>Không chia sẻ OTP với bất kỳ ai. Nếu bạn không gửi yêu cầu này, hãy bỏ qua email.</p>
                </body>
                </html>
                """
        };
        message.To.Add(new MailAddress(recipientEmail));

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,
            EnableSsl = _options.UseStartTls,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(_options.User, _options.Pass),
            Timeout = checked(_options.TimeoutSeconds * 1000)
        };

        await client.SendMailAsync(message, cancellationToken);
    }
}
